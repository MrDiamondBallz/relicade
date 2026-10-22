using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class PreservationSelectionEntryTests : IDisposable
{
    private readonly string? previousRoot = RelicadePaths.OverrideUserDataRoot;
    private readonly string root = Path.Combine(Path.GetTempPath(), "entry-preservation-" + Guid.NewGuid().ToString("N"));
    private readonly FileSettingsStore store;
    private readonly FaultStore faults;
    private readonly GameManager manager;
    private readonly SettingsViewModel settings;
    private readonly GameInfo original = new() { Name = "Fixture", Repository = "fixture/old", FolderName = "Old", IsCustom = true };
    private readonly List<EntryNotice> notices = [];
    private string OldPath => Path.Combine(root, "Apps", "Old");
    private string NewPath => Path.Combine(root, "Apps", "New");
    public PreservationSelectionEntryTests()
    {
        Directory.CreateDirectory(root);
        RelicadePaths.OverrideUserDataRoot = root;
        store = new FileSettingsStore(Path.Combine(root, "settings.json"));
        store.Current.AppsPath = Path.Combine(root, "Apps");
        store.Current.UserAppTags["fixture/old"] = ["retained"];
        store.Save(store.Current);
        faults = new FaultStore(store);
        settings = new SettingsViewModel(faults);
        manager = new GameManager(faults);
        Directory.CreateDirectory(OldPath);
        File.WriteAllText(Path.Combine(OldPath, "payload"), "old-working");
        manager.CatalogService.SaveLocalAppsAsync([original]).GetAwaiter().GetResult();
    }
    public void Dispose()
    {
        manager.Dispose();
        RelicadePaths.OverrideUserDataRoot = previousRoot;
        Directory.Delete(root, true);
    }
    private sealed class FaultStore(FileSettingsStore inner) : ISettingsStore
    {
        public Action? BeforeSave;
        public AppSettings Current => inner.Current;
        public AppSettings Load() => inner.Load();
        public void Save(AppSettings value) { BeforeSave?.Invoke(); inner.Save(value); }
    }
    private Task<bool> Edit(CancellationToken token = default, bool manual = false, string filesToAdd = "") => new AppEntryService(manager, settings).SaveAsync(
        new("Edited", "fixture/new", "github", "New", "", "", "", "", filesToAdd, "", "", "", false, manual,
            original.InstanceKey, original.Repository), notices.Add, _ => throw new Exception("Must not open folder"), token);
    private async Task AssertOld()
    {
        Assert.Equal("Old", Assert.Single(await new AppCatalogService(dataDirectory: root).LoadLocalAppsAsync()).FolderName);
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(OldPath, "payload")));
        Assert.False(Directory.Exists(NewPath));
        Assert.Contains("fixture/old", new FileSettingsStore(Path.Combine(root, "settings.json")).Load().UserAppTags.Keys);
        Assert.Contains("fixture/old", settings.Current.UserAppTags.Keys);
        Assert.DoesNotContain(notices, n => n.Message.Contains("success", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_marker_list_reports_preservation_for_hosted_and_manual_entries(bool manual)
    {
        if (manual) original.Repository = "";
        original.FilesToAdd = ["portable.txt"];
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        File.WriteAllText(Path.Combine(OldPath, "portable.txt"), "synthetic user configuration");
        Assert.False(await Edit(manual: manual, filesToAdd: "new.txt"));
        Assert.Contains("saved", Assert.Single(notices).Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preserved", notices[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("portable.txt", notices[0].Message);
        Assert.Equal("synthetic user configuration", File.ReadAllText(Path.Combine(NewPath, "portable.txt")));
        Assert.False(File.Exists(Path.Combine(NewPath, "new.txt")));
        var saved = Assert.Single(await manager.CatalogService.LoadLocalAppsAsync());
        Assert.Equal("New", saved.FolderName);
        Assert.Equal(new[] { "new.txt" }, saved.FilesToAdd);
    }

    [Fact]
    public async Task Failed_rename_preserves_original_identity_settings_and_unrelated_target()
    {
        Directory.CreateDirectory(NewPath);
        File.WriteAllText(Path.Combine(NewPath, "unrelated"), "not-owned");
        Assert.False(await Edit());
        Assert.Single(notices);
        Assert.Equal("not-owned", File.ReadAllText(Path.Combine(NewPath, "unrelated")));
        Directory.Delete(NewPath, true);
        await AssertOld();
    }

    [Fact]
    public async Task Settings_save_failure_compensates_rename_and_preserves_live_and_reopened_metadata()
    {
        faults.BeforeSave = () => throw new IOException("Controlled settings write failure");
        Assert.False(await Edit());
        await AssertOld();
        faults.BeforeSave = null;
        notices.Clear();
        Assert.True(await Edit());
        Assert.Equal("New", Assert.Single(await manager.CatalogService.LoadLocalAppsAsync()).FolderName);
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(NewPath, "payload")));
        Assert.False(Directory.Exists(OldPath));
        Assert.Single(notices);
    }

    [Fact]
    public async Task Library_commit_failure_restores_settings_and_folder_without_overwriting_obstruction()
    {
        // A writeable lock file but an obstructed same-directory atomic replacement.
        Directory.CreateDirectory(manager.CatalogService.AppsConfigPath + ".unused");
        faults.BeforeSave = () =>
        {
            // Introduce a damaged backup only after the old library has loaded.
            Directory.CreateDirectory(manager.CatalogService.LibraryBackupDirectory);
            foreach (var file in Directory.GetFiles(manager.CatalogService.LibraryBackupDirectory, "*.json"))
                File.WriteAllText(file, "corrupt-backup-control");
            faults.BeforeSave = null;
        };
        Assert.False(await Edit());
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manager.CatalogService.AppsConfigPath));
        Assert.Contains("Old", document.RootElement.ToString());
        // Explicit external repair of the synthetic backup permits a genuine reopen.
        foreach (var file in Directory.GetFiles(manager.CatalogService.LibraryBackupDirectory, "*.json")) File.Delete(file);
        Assert.Equal("Old", Assert.Single(await new AppCatalogService(dataDirectory: root).LoadLocalAppsAsync()).FolderName);
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(OldPath, "payload")));
        Assert.False(Directory.Exists(NewPath));
        Assert.Contains("fixture/old", new FileSettingsStore(Path.Combine(root, "settings.json")).Load().UserAppTags.Keys);
        Assert.DoesNotContain(notices, n => n.Message.Contains("success", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Denied_rename_preserves_payload_and_can_retry_after_permission_repair()
    {
        if (OperatingSystem.IsWindows()) return;
        var apps = Path.GetDirectoryName(OldPath)!;
        var mode = File.GetUnixFileMode(apps);
        try
        {
            File.SetUnixFileMode(apps, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(apps, "control"), "denied"));
            Assert.False(await Edit());
            Assert.Single(notices);
            await AssertOld();
        }
        finally { File.SetUnixFileMode(apps, mode); }
        notices.Clear();
        Assert.True(await Edit());
        Assert.Equal("New", Assert.Single(await manager.CatalogService.LoadLocalAppsAsync()).FolderName);
    }

    [Fact]
    public async Task Failed_compensation_is_explicit_and_retains_recovery_record_and_both_payloads()
    {
        faults.BeforeSave = () =>
        {
            Directory.CreateDirectory(OldPath);
            File.WriteAllText(Path.Combine(OldPath, "external"), "do-not-overwrite");
            throw new IOException("Controlled commit failure with obstructed compensation");
        };
        Assert.False(await Edit());
        Assert.Equal("do-not-overwrite", File.ReadAllText(Path.Combine(OldPath, "external")));
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(NewPath, "payload")));
        Assert.Contains("recovery", Assert.Single(notices).Title, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(root, "entry-recovery-*.json"));
        Assert.Equal("Old", Assert.Single(await manager.CatalogService.LoadLocalAppsAsync()).FolderName);
    }

    [Fact]
    public async Task Failed_settings_restoration_is_explicit_even_when_folder_compensation_succeeds()
    {
        var calls = 0;
        faults.BeforeSave = () =>
        {
            if (++calls > 1) throw new IOException("Controlled restoration failure");
            foreach (var file in Directory.GetFiles(manager.CatalogService.LibraryBackupDirectory, "*.json"))
                File.WriteAllText(file, "damaged-backup");
        };
        Assert.False(await Edit());
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(OldPath, "payload")));
        Assert.False(Directory.Exists(NewPath));
        Assert.Contains("recovery", Assert.Single(notices).Title, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(root, "entry-recovery-*.json"));
        Assert.Contains("fixture/new", new FileSettingsStore(Path.Combine(root, "settings.json")).Load().UserAppTags.Keys);
        Assert.Contains("fixture/old", settings.Current.UserAppTags.Keys);
    }

    [Fact]
    public async Task Committed_entry_with_failed_preparation_is_not_reported_as_a_rollback_or_success()
    {
        if (OperatingSystem.IsWindows()) return;
        faults.BeforeSave = () => File.SetUnixFileMode(NewPath, UnixFileMode.None);
        try
        {
            Assert.False(await Edit(manual: true));
            Assert.Contains("saved", Assert.Single(notices).Title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("preparation", notices[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("New", Assert.Single(await new AppCatalogService(dataDirectory: root).LoadLocalAppsAsync()).FolderName);
        }
        finally { if (Directory.Exists(NewPath)) File.SetUnixFileMode(NewPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        Assert.False(Directory.Exists(OldPath));
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(NewPath, "payload")));
    }

    [Fact]
    public async Task Cancellation_before_mutation_preserves_both_stores_and_payload()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Edit(cancel.Token));
        await AssertOld();
    }
}
