using System.Text;
using System.Text.Json;
using Relicade.Services;
using Relicade.Services.Credentials;
using Relicade.ViewModels;

namespace Relicade.Tests;

public class LegacyCredentialMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "migration-tests", Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(root, "settings.json");
    private static readonly string[] Canaries = ["synthetic-migration-gh-7a81", "synthetic-migration-gl-9b32", "synthetic-migration-cb-4c77"];
    private static string Source => JsonSerializer.Serialize(new Dictionary<string, object?> {
        ["GitHubApiToken"] = Canaries[0], ["gitlabapitoken"] = Canaries[1], ["CodebergApiToken"] = Canaries[2],
        ["SortBy"] = "Name", ["FirstStartup"] = false, ["future"] = new { keep = true, list = new[] { 1, 2 }, text = "unchanged" }
    });
    public LegacyCredentialMigrationTests() => Directory.CreateDirectory(root);
    private (FileSettingsStore Store, Vault Vault) Create(string? json = null)
    {
        File.WriteAllText(PathName, json ?? Source);
        var vault = new Vault();
        return (new FileSettingsStore(PathName, new(vault)), vault);
    }
    private void Unchanged(byte[] bytes, FileSettingsStore store)
    {
        Assert.Equal(bytes, File.ReadAllBytes(PathName));
        Assert.True(store.IsReadOnly);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => f != PathName))
        {
            // A held writer lease is intentionally unreadable; it never contains document bytes.
            if (file.EndsWith(".lock", StringComparison.Ordinal)) { Assert.Equal(0, new FileInfo(file).Length); continue; }
            foreach (var canary in Canaries) Assert.DoesNotContain(canary, File.ReadAllText(file));
        }
    }
    public void Dispose()
    {
        // Retain synthetic active source on precommit refusal for the private artifact scan, not a new copy.
        var sensitive = File.Exists(PathName) && Canaries.Any(c => File.ReadAllText(PathName).Contains(c));
        File.WriteAllText(Path.Combine(root, "retention.json"), JsonSerializer.Serialize(new { deliberatelyRetainedPrecommitSource = sensitive, activeFile = "settings.json" }));
    }
    internal sealed class Vault : ICredentialVault
    {
        internal readonly Dictionary<CredentialProvider, string> Values = new();
        internal readonly List<string> Calls = new();
        internal Action<CredentialProvider>? OnStore;
        internal Exception? Failure;
        internal bool Lie;
        public Task<string?> ReadAsync(CredentialProvider p, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add("read:" + p);
            if (Failure != null) throw Failure;
            return Task.FromResult(Values.GetValueOrDefault(p));
        }
        public Task StoreAsync(CredentialProvider p, string v, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add("store:" + p);
            if (Failure != null) throw Failure;
            if (!Lie) Values[p] = v;
            OnStore?.Invoke(p);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(CredentialProvider p, CancellationToken ct) => throw new InvalidOperationException("Migration must never delete.");
    }

    [Fact]
    public async Task All_three_exact_values_unknown_fields_reopen_export_snapshot_and_permissions()
    {
        var (store, vault) = Create();
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(PathName, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var before = File.ReadAllBytes(PathName); var preview = store.PreviewLegacyCredentialMigration();
        Assert.True(preview.CanMigrate); Assert.Equal(3, preview.ProviderCount);
        Assert.Equal(new[] { "GitHub", "GitLab", "Codeberg" }, preview.Providers);
        Assert.Equal(before, File.ReadAllBytes(PathName)); Assert.DoesNotContain(vault.Calls, c => c.StartsWith("store"));
        foreach (var c in Canaries) Assert.DoesNotContain(c, JsonSerializer.Serialize(preview));
        Assert.DoesNotContain(PathName, JsonSerializer.Serialize(preview));
        Assert.NotEqual(preview.PreviewId, store.PreviewLegacyCredentialMigration().PreviewId);
        preview = store.PreviewLegacyCredentialMigration();
        var result = await store.MigrateLegacyCredentialsAsync(preview.PreviewId, true);
        Assert.True(result.Success); Assert.True(result.SettingsSanitized); Assert.True(result.VaultMayHaveChanged);
        Assert.False(store.IsReadOnly); Assert.False(store.HasLegacyCredentials);
        foreach (var p in Enum.GetValues<CredentialProvider>()) Assert.Equal(Canaries[(int)p], vault.Values[p]);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(PathName));
        Assert.True(doc.RootElement.GetProperty("future").GetProperty("keep").GetBoolean());
        Assert.Equal("Name", doc.RootElement.GetProperty("SortBy").GetString());
        Assert.DoesNotContain(doc.RootElement.EnumerateObject(), p => p.Name.EndsWith("ApiToken", StringComparison.OrdinalIgnoreCase));
        var reopened = new FileSettingsStore(PathName, new(vault));
        Assert.False(reopened.IsReadOnly); Assert.Equal(Canaries[0], reopened.Current.GitHubApiToken);
        File.WriteAllText(Path.Combine(root, "export.json"), JsonSerializer.Serialize(reopened.Current));
        LauncherUpdateBackup.Create(root);
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            foreach (var c in Canaries) Assert.DoesNotContain(c, File.ReadAllText(f));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PathName));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task No_confirmation_or_no_matching_preview_never_touches_vault(bool confirm, bool previewExists)
    {
        var (store, vault) = Create(); var before = File.ReadAllBytes(PathName);
        var id = previewExists ? store.PreviewLegacyCredentialMigration().PreviewId : "not-an-issued-preview";
        vault.Calls.Clear(); var result = await store.MigrateLegacyCredentialsAsync(id, confirm);
        Assert.False(result.Success); Assert.False(result.VaultMayHaveChanged); Assert.Empty(vault.Calls); Unchanged(before, store);
    }

    [Fact]
    public async Task Preview_stale_replaced_or_expired_refuses_before_vault_mutation()
    {
        var (store, vault) = Create(); var preview = store.PreviewLegacyCredentialMigration();
        File.AppendAllText(PathName, " "); var before = File.ReadAllBytes(PathName); vault.Calls.Clear();
        var result = await store.MigrateLegacyCredentialsAsync(preview.PreviewId, true);
        Assert.False(result.Success); Assert.False(result.VaultMayHaveChanged); Assert.Empty(vault.Calls); Unchanged(before, store);
        preview = store.PreviewLegacyCredentialMigration(); var newer = store.PreviewLegacyCredentialMigration();
        Assert.False((await store.MigrateLegacyCredentialsAsync(preview.PreviewId, true)).Success);
        Assert.False((await store.MigrateLegacyCredentialsAsync(newer.PreviewId, true)).Success); // attempted stale execution consumes outstanding preview
    }

    [Theory]
    [InlineData("")] [InlineData("null")] [InlineData("[]")] [InlineData("{broken")]
    [InlineData("{\"GitHubApiToken\":3}")] [InlineData("{\"GitHubApiToken\":{}}")]
    [InlineData("{\"GitHubApiToken\":true}")] [InlineData("{\"GitHubApiToken\":[]}")]
    [InlineData("{\"GitHubApiToken\":\"x\",\"githubapitoken\":\"x\"}")]
    [InlineData("{\"GitHubApiToken\":\"x\",\"GitHubApiToken\":null}")]
    [InlineData("{\"GitHubApiToken\":\"   \"}")]
    [InlineData("{\"GitHubApiToken\":\"x\\n\"}")]
    [InlineData("{\"GitHubApiToken\":\"x\",\"SortBy\":{}}")]
    [InlineData("{\"GitHubApiToken\":\"\\uD800\"}")]
    [InlineData("{\"GitHubApiToken\":\"\\uDC00\"}")]
    [InlineData("{\"future\":\"\\uD800\",\"GitHubApiToken\":\"x\"}")]
    public async Task Invalid_ambiguous_documents_remain_read_only_and_redacted(string json)
    {
        var (store, vault) = Create(json); var before = File.ReadAllBytes(PathName);
        Assert.True(store.IsReadOnly); Assert.NotNull(store.ReadError);
        Assert.False(store.PreviewLegacyCredentialMigration().CanMigrate);
        Assert.False((await store.MigrateLegacyCredentialsAsync("invented", true)).Success);
        Assert.DoesNotContain(vault.Calls, c => c.StartsWith("store")); Unchanged(before, store);
    }

    [Theory]
    [InlineData("oversize")] [InlineData("invalid-utf8")] [InlineData("utf16")] [InlineData("oversize-token")]
    public void Invalid_size_encoding_or_token_is_not_silently_repaired(string kind)
    {
        byte[] bytes = kind switch {
            "oversize" => Encoding.UTF8.GetBytes("{\"future\":\"" + new string('a', 4 * 1024 * 1024) + "\"}"),
            "invalid-utf8" => [123,34,120,34,58,34,255,34,125],
            "utf16" => Encoding.Unicode.GetBytes(Source),
            _ => Encoding.UTF8.GetBytes("{\"GitHubApiToken\":\"" + new string('x',2049) + "\"}")
        };
        File.WriteAllBytes(PathName, bytes); var store = new FileSettingsStore(PathName, new(new Vault()));
        Assert.True(store.IsReadOnly); Assert.False(store.PreviewLegacyCredentialMigration().CanMigrate); Unchanged(bytes, store);
    }

    [Fact]
    public async Task Null_empty_keys_removed_only_after_confirmation_and_exact_whitespace_secret_preserved()
    {
        var (store, vault) = Create("{\"GitHubApiToken\":null,\"GitLabApiToken\":\"\",\"CodebergApiToken\":\" exact value \"}");
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
        Assert.True(result.Success); Assert.Equal(" exact value ", vault.Values[CredentialProvider.Codeberg]);
        Assert.Empty(JsonDocument.Parse(File.ReadAllBytes(PathName)).RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Null_empty_only_keys_are_not_silently_removed_by_normal_save()
    {
        var (store,vault)=Create("{\"GitHubApiToken\":null,\"GitLabApiToken\":\"\",\"future\":true}");
        var before=File.ReadAllBytes(PathName); Assert.True(store.IsReadOnly);
        Assert.Throws<LegacyCredentialCleanupRequiredException>(()=>store.Save(store.Current)); Unchanged(before,store);
        var preview=store.PreviewLegacyCredentialMigration(); Assert.True(preview.CanMigrate); Assert.Equal(0,preview.ProviderCount);
        Assert.True((await store.MigrateLegacyCredentialsAsync(preview.PreviewId,true)).Success);
        Assert.Empty(vault.Values); Assert.Equal("{\"future\":true}",File.ReadAllText(PathName));
    }

    [Fact]
    public async Task Last_provider_conflict_preflights_every_entry_without_any_write()
    {
        var (store, vault) = Create(); vault.Values[CredentialProvider.Codeberg] = "different-existing";
        var before = File.ReadAllBytes(PathName); vault.Calls.Clear();
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
        Assert.False(result.Success); Assert.False(result.VaultMayHaveChanged); Assert.Contains("conflict",result.Message);
        Assert.Equal(3, vault.Calls.Count); Assert.Single(vault.Values); Unchanged(before, store);
    }

    [Fact]
    public async Task Identical_entries_are_independently_read_back_but_never_stored_again()
    {
        var (store, vault) = Create(); foreach (var p in Enum.GetValues<CredentialProvider>()) vault.Values[p] = Canaries[(int)p];
        vault.Calls.Clear(); var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
        Assert.True(result.Success); Assert.False(result.VaultMayHaveChanged);
        Assert.Equal(9, vault.Calls.Count); Assert.All(vault.Calls, c => Assert.StartsWith("read:", c));
    }

    [Theory]
    [InlineData(CredentialVaultFailure.Locked)] [InlineData(CredentialVaultFailure.Denied)] [InlineData(CredentialVaultFailure.Unavailable)]
    public async Task Vault_preflight_failure_preserves_source_and_no_success_events(CredentialVaultFailure failure)
    {
        var (store, vault) = Create(); var model = new SettingsViewModel(store); var changes = 0; var failed = 0;
        model.CredentialsChanged += _ => changes++; model.SaveFailed += _ => failed++;
        var before = File.ReadAllBytes(PathName); vault.Failure = new CredentialVaultUnavailableException(failure);
        var result = await model.MigrateLegacyCredentialsAsync(model.PreviewLegacyCredentialMigration().PreviewId, true);
        Assert.False(result.Success); Assert.False(result.VaultMayHaveChanged); Assert.Equal(0, changes); Assert.Equal(1,failed);
        Assert.False(string.IsNullOrWhiteSpace(model.CredentialMigrationMessage)); Unchanged(before, store);
    }

    [Fact]
    public async Task Readback_mismatch_partial_outcome_retry_is_idempotent_and_session_not_published_on_failure()
    {
        var (store, vault) = Create(); vault.Lie = true; var before = File.ReadAllBytes(PathName);
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
        Assert.False(result.Success); Assert.True(result.VaultMayHaveChanged); Assert.False(result.SettingsSanitized);
        Assert.Equal("", store.Current.GitHubApiToken); Unchanged(before, store);
        vault.Lie = false;
        Assert.True((await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true)).Success);
    }

    [Theory]
    [InlineData("before")] [InlineData("between")] [InlineData("before-replace")] [InlineData("after-replace")]
    public async Task Cancellation_never_erases_precommit_source_or_relabels_committed_scrub(string boundary)
    {
        var (store, vault) = Create(); using var cts = new CancellationTokenSource(); var before = File.ReadAllBytes(PathName);
        if (boundary == "before") cts.Cancel();
        if (boundary == "between") vault.OnStore = _ => cts.Cancel();
        store.CredentialMigrationCheckpoint = p => { if (p == (boundary == "after-replace" ? "AfterReplace" : "BeforeReplace")) cts.Cancel(); };
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true, cts.Token);
        if (boundary == "after-replace") { Assert.True(result.Success); Assert.True(result.SettingsSanitized); }
        else {
            Assert.False(result.Success); Assert.False(result.SettingsSanitized); Assert.Equal(boundary != "before",result.VaultMayHaveChanged); Unchanged(before,store);
            Assert.True((await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true)).Success);
            Assert.Equal(3, vault.Values.Count);
        }
    }

    [Theory]
    [InlineData("before")] [InlineData("after")]
    public async Task Replace_or_reload_failure_reports_actual_commit_boundary(string when)
    {
        var (store, vault) = Create(); var before = File.ReadAllBytes(PathName);
        store.CredentialMigrationCheckpoint = p => { if (p == (when == "before" ? "BeforeReplace" : "AfterReplace")) throw new IOException(Canaries[0]); };
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true);
        Assert.False(result.Success); Assert.True(result.VaultMayHaveChanged); Assert.Equal(when == "after",result.SettingsSanitized);
        Assert.DoesNotContain(Canaries[0],result.Message); Assert.Equal("",store.Current.GitHubApiToken);
        if (when == "before") Unchanged(before,store);
        else Assert.True(new FileSettingsStore(PathName,new(vault)).HasLegacyCredentials == false);
    }

    [Fact]
    public async Task Snapshot_is_refused_before_and_during_partial_migration()
    {
        var (store, vault) = Create(); var before = File.ReadAllBytes(PathName);
        Assert.Throws<LegacyCredentialCleanupRequiredException>(() => LauncherUpdateBackup.Create(root));
        vault.OnStore = _ => { Assert.Throws<LegacyCredentialCleanupRequiredException>(() => LauncherUpdateBackup.Create(root)); throw new IOException(); };
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true);
        Assert.False(result.Success); Assert.True(result.VaultMayHaveChanged); Unchanged(before,store);
    }

    [Fact]
    public async Task Source_changed_during_vault_work_refuses_scrub_no_session_event_and_no_speculative_rollback()
    {
        var (store, vault) = Create(); var model = new SettingsViewModel(store); var successes=0;
        model.CredentialsChanged += _ => successes++;
        vault.OnStore = _ => File.AppendAllText(PathName," ");
        var result = await model.MigrateLegacyCredentialsAsync(model.PreviewLegacyCredentialMigration().PreviewId,true);
        Assert.False(result.Success); Assert.True(result.VaultMayHaveChanged); Assert.False(result.SettingsSanitized);
        Assert.StartsWith(Source,File.ReadAllText(PathName)); Assert.Equal(0,successes); Assert.Equal(3,vault.Values.Count);
        vault.OnStore=null;
        Assert.True((await model.MigrateLegacyCredentialsAsync(model.PreviewLegacyCredentialMigration().PreviewId,true)).Success);
        Assert.Equal(3,successes); Assert.False(model.IsReadOnly);
    }

    [Theory]
    [InlineData(CredentialVaultFailure.Locked)] [InlineData(CredentialVaultFailure.Denied)] [InlineData(CredentialVaultFailure.Unavailable)]
    public async Task Mutating_adapter_failure_keeps_source_and_retry_reuses_equal_values_without_rollback(CredentialVaultFailure failure)
    {
        var (store,vault)=Create(); var before=File.ReadAllBytes(PathName);
        vault.OnStore = p => { if (p == CredentialProvider.GitLab) throw new CredentialVaultUnavailableException(failure); };
        var result=await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true);
        Assert.False(result.Success); Assert.True(result.VaultMayHaveChanged); Assert.Equal(2,vault.Values.Count); Unchanged(before,store);
        Assert.Equal("",store.Current.GitHubApiToken);
        vault.OnStore=null; vault.Calls.Clear();
        Assert.True((await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true)).Success);
        Assert.Equal(new[]{"store:Codeberg"},vault.Calls.Where(c=>c.StartsWith("store:")));
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.UserRead)]
    public async Task Replacement_never_expands_settings_access_permissions(UnixFileMode permissions)
    {
        if (OperatingSystem.IsWindows()) return; // Windows ACL acceptance is still required.
        var (store,_) = Create(); File.SetUnixFileMode(PathName,permissions);
        Assert.True((await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true)).Success);
        Assert.Equal(permissions & (UnixFileMode.UserRead | UnixFileMode.UserWrite),File.GetUnixFileMode(PathName));
    }

    [Fact]
    public async Task Expired_preview_is_not_accepted_and_provider_set_is_reverified_before_scrub()
    {
        var (store, vault) = Create(); var preview = store.PreviewLegacyCredentialMigration(); var before = File.ReadAllBytes(PathName);
        var field = typeof(FileSettingsStore).GetField("_migrationPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var snapshot = field.GetValue(store)!;
        field.SetValue(store, Activator.CreateInstance(snapshot.GetType(), preview.PreviewId, before, Environment.TickCount64 - 301000));
        vault.Calls.Clear();
        Assert.False((await store.MigrateLegacyCredentialsAsync(preview.PreviewId,true)).Success); Assert.Empty(vault.Calls); Unchanged(before,store);
        vault.OnStore = p => { if (p == CredentialProvider.Codeberg) vault.Values[CredentialProvider.GitHub] = "changed-by-other-writer"; };
        var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true);
        Assert.False(result.Success); Assert.True(result.VaultMayHaveChanged); Unchanged(before,store);
        Assert.Equal("changed-by-other-writer",vault.Values[CredentialProvider.GitHub]); // no speculative restore/delete
    }

    [Fact]
    public async Task Actual_replace_permission_failure_keeps_source_exact_and_only_sanitized_temp_bytes()
    {
        if (OperatingSystem.IsWindows()) return; // Windows ACL/native filesystem acceptance remains separate.
        var (store,vault) = Create(); var before = File.ReadAllBytes(PathName);
        var mode = File.GetUnixFileMode(root);
        store.CredentialMigrationCheckpoint = p => { if (p == "BeforeReplace") File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute); };
        LegacyCredentialMigrationResult result;
        try { result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true); }
        finally { File.SetUnixFileMode(root,mode); }
        Assert.False(result.Success); Assert.False(result.SettingsSanitized); Assert.True(result.VaultMayHaveChanged); Unchanged(before,store);
        Assert.Equal(3,vault.Values.Count);
        store.CredentialMigrationCheckpoint=null;
        Assert.True((await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId,true)).Success);
    }

    [Fact]
    public async Task Lease_denial_and_unreadable_source_refuse_without_vault_changes()
    {
        var (store,vault)=Create(); var preview=store.PreviewLegacyCredentialMigration(); var before=File.ReadAllBytes(PathName);
        using(var lease=new FileStream(PathName+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            var result=await store.MigrateLegacyCredentialsAsync(preview.PreviewId,true);
            Assert.False(result.Success); Assert.False(result.VaultMayHaveChanged); Unchanged(before,store);
        }
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(PathName, UnixFileMode.None);
            try { Assert.False(store.PreviewLegacyCredentialMigration().CanMigrate); }
            finally { File.SetUnixFileMode(PathName,UnixFileMode.UserRead|UnixFileMode.UserWrite); }
        }
    }
}
