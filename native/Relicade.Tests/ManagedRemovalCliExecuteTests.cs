using System.Text.Json;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// Full production public Execute; the absent-manager seam changes ONLY its HTTP
// transport, not manager creation, library parsing, leases, or removal routing.
public sealed class ManagedRemovalCliExecuteTests(ITestOutputHelper output)
{
    sealed class Scope : IDisposable
    {
        readonly CredentialVaultTestScope vault = new();
        readonly ISettingsStore previous = SettingsStoreProvider.Default;
        readonly string? oldRoot = RelicadePaths.OverrideUserDataRoot;
        public ManagedRemovalFixture F { get; } = new();
        public ManagedRemovalLibraryTests.Handler Network { get; } = new();
        public string Catalog => Path.Combine(F.Root, "catalog");
        public string Library => Path.Combine(Catalog, "apps.json");
        public string Settings => Path.Combine(Catalog, "settings.json");
        public FileSettingsStore Store { get; }
        public GameInfo Game { get; } = new() { Name = "Synthetic execute removal", Repository = "fixture/removal", FolderName = "live", GameIconUrl = "https://example.invalid/never-art.png", AutoUpdate = true };
        public List<string> Calls { get; } = [];
        public GameManager? Manager;
        public Scope()
        {
            Directory.CreateDirectory(Catalog); Directory.CreateDirectory(Path.Combine(Catalog, "Cache"));
            // Unchanged GameManager/RelicadePaths constructor creates these empty
            // top-level application dirs, even with a custom AppsPath. Account for
            // them explicitly; generation/history/settings/library remain exact.
            Directory.CreateDirectory(Path.Combine(Catalog, "Apps"));
            RelicadePaths.OverrideUserDataRoot = Catalog;
            File.WriteAllText(Settings, JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(F.Root, "Apps") }));
            Store = new FileSettingsStore(Settings); SettingsStoreProvider.Default = Store;
        }
        public void WriteLibrary(params GameInfo[] others) => File.WriteAllText(Library,
            JsonSerializer.Serialize(new { apps = new[] { Game }.Concat(others).Select(AppCatalogService.SerializeApp) }));
        public CLIHandler Cli(bool injected, Action<string>? mutation = null)
        {
            if (injected) Manager = new GameManager(Store, new HttpClient(Network), new AppCatalogService(dataDirectory: Catalog));
            return new CLIHandler(Manager, moveToTrash: path =>
            {
                Calls.Add(path);
                foreach (var root in new[] { F.Live, Game.GetInstallPath(Path.Combine(F.Root, "Apps")) }.Distinct())
                    Assert.ThrowsAny<IOException>(() => { using var l = new PortableInstallationTransaction(root).AcquireLease(); });
                mutation?.Invoke(path);
            }) { RemovalHttpClient = new HttpClient(Network) };
        }
        public void Dispose()
        {
            Manager?.Dispose(); SettingsStoreProvider.Default = previous;
            RelicadePaths.OverrideUserDataRoot = oldRoot; vault.Dispose();
        }
    }
    async Task<(int Code, string Text)> Execute(Scope s, CLIHandler cli, string command = "--uninstall", string? name = null)
    {
        var old = Console.Out; using var text = new StringWriter();
        int code;
        try { Console.SetOut(text); code = await cli.Execute([command, name ?? s.Game.Name!]); }
        finally { Console.SetOut(old); }
        Assert.Equal(0, s.Network.Calls);
        Assert.DoesNotContain("Launcher Version:", text.ToString());
        output.WriteLine($"command={command}; absent_manager={s.Manager == null}; application_HTTP_requests={s.Network.Calls}; exit={code}; output={text}");
        return (code, text.ToString());
    }
    public static IEnumerable<object[]> RefusalCases()
    {
        foreach (var injected in new[] { false, true })
        foreach (var command in new[] { "-x", "--uninstall" })
        foreach (var kind in new[] { "managed", "located", "stale-location", "owned-located", "missing-live", "missing-marker", "unknown-history", "failed-recovery", "writer", "running", "MSI-conflict", "Flatpak-conflict" })
            yield return [injected, command, kind];
    }
    [Theory, MemberData(nameof(RefusalCases))]
    public async Task Full_command_refuses_before_recovery_status_or_network(bool injected, string command, string kind)
    {
        using var s = new Scope(); var f = s.F;
        if (kind is "writer" or "running") Directory.CreateDirectory(f.Live);
        else { await f.Install(); await f.Install("new"); }
        if (kind == "located") s.Game.InstallPath = f.Live;
        if (kind is "stale-location" or "owned-located")
        {
            s.Game.InstallPath = Path.Combine(f.Root, "external"); Directory.CreateDirectory(s.Game.InstallPath);
            using (new PortableInstallationTransaction(s.Game.InstallPath).AcquireLease()) { }
            if (kind == "owned-located")
            {
                await ManagedRemovalFixture.InstallAt(s.Game.InstallPath, "external");
                // Keep default managed too: both slots independently own history.
            }
        }
        if (kind == "missing-live") Directory.Move(f.Live, Path.Combine(f.Root, "retained-live"));
        if (kind == "missing-marker") File.Delete(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
        if (kind == "unknown-history") { File.Delete(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)); File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "unknown"), "keep uncertain"); }
        if (kind == "failed-recovery")
            await Assert.ThrowsAsync<IOException>(() => ManagedRemovalFixture.InstallAt(f.Live, "failed", p =>
            { if (p is "PreviousRenamed" or "Recovering") throw new IOException("synthetic failed recovery; never resolve for removal"); }));
        if (kind == "MSI-conflict") WindowsInstallerService.WriteReceipt(f.Live, new("Synthetic", "old", Path.Combine(f.Live, "fixture.exe")));
        if (kind == "Flatpak-conflict") File.WriteAllText(Path.Combine(f.Live, FlatpakService.ReceiptFileName), "never parse/dispatched conflict");
        // An unrelated live-gap app detects any accidental all-app status/recovery load.
        var sibling = Path.Combine(f.Root, "Apps", "other"); await ManagedRemovalFixture.InstallAt(sibling, "sibling");
        Directory.Move(sibling, Path.Combine(f.Root, "retained-other"));
        s.WriteLibrary(new GameInfo() { Name = "Other", FolderName = "other", Repository = "fixture/other", AutoUpdate = true, GameIconUrl = "https://example.invalid/other.png" });
        using IDisposable? lease = kind == "writer" ? f.Tx.AcquireLease() : kind == "running" ? PortableInstallationLifetime.Begin(f.Live) : null;
        var cli = s.Cli(injected); var before = f.Snapshot();
        var result = await Execute(s, cli, command);
        Assert.NotEqual(0, result.Code); Assert.Contains("ERROR:", result.Text);
        Assert.DoesNotContain("successfully", result.Text); Assert.DoesNotContain("is not installed", result.Text); Assert.Empty(s.Calls);
        Assert.Equal(before, f.Snapshot()); Assert.False(Directory.Exists(sibling));
        if (kind == "missing-live") Assert.False(Directory.Exists(f.Live));
        if (s.Manager != null)
        {
            Assert.All(s.Manager.Games, g => { Assert.Equal(GameStatus.NotInstalled, g.Status); Assert.False(g.IsManagedPortable); Assert.Null(g.PortableRecoveryError); });
            Assert.False(s.Manager.HasLoadedLibrary);
        }
    }
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var injected in new[] { false, true })
        foreach (var kind in new[] { "settings-corrupt", "settings-legacy", "settings-type", "library-missing", "library-corrupt", "library-unknown", "library-partial", "duplicate-instance", "duplicate-property", "ambiguous-name", "relative-location", "traversal", "linked-library", "denied-library", "denied-settings", "relative-apps", "missing-apps" })
            yield return [injected, kind];
    }
    [Theory, MemberData(nameof(InvalidCases))]
    public async Task Unknown_corrupt_ambiguous_and_unsafe_local_input_fails_without_repair(bool injected, string kind)
    {
        using var s = new Scope(); Directory.CreateDirectory(s.F.Live); File.WriteAllText(Path.Combine(s.F.Live, "keep"), "synthetic unmanaged keep"); s.WriteLibrary();
        var cli = s.Cli(injected); // ensure settings can become corrupt after construction too
        var settings = JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(s.F.Root, "Apps") });
        switch (kind)
        {
            case "settings-corrupt": File.WriteAllText(s.Settings, "{broken"); break;
            case "settings-legacy": File.WriteAllText(s.Settings, "{\"GitHubApiToken\":\"synthetic-unused\"}"); break;
            case "settings-type": File.WriteAllText(s.Settings, "{\"AppsPath\":42}"); break;
            case "library-missing": File.Move(s.Library, Path.Combine(s.Catalog, "games.json")); break;
            case "library-corrupt": File.WriteAllText(s.Library, "{broken"); break;
            case "library-unknown": File.WriteAllText(s.Library, "{\"schemaVersion\":999,\"apps\":[]}"); break;
            case "library-partial": File.WriteAllText(s.Library, "{\"apps\":[{\"name\":\"Synthetic execute removal\",\"folderName\":\"live\"},42]}"); break;
            case "duplicate-instance": File.WriteAllText(s.Library, "{\"apps\":[{\"name\":\"A\",\"folderName\":\"live\"},{\"name\":\"B\",\"folderName\":\"live\"}]}"); break;
            case "duplicate-property": File.WriteAllText(s.Library, "{\"apps\":[{\"name\":\"A\",\"folderName\":\"live\",\"folderName\":\"other\"}]}"); break;
            case "ambiguous-name": s.WriteLibrary(new GameInfo() { Name = s.Game.Name, FolderName = "other", Repository = "fixture/other" }); break;
            case "relative-location": s.Game.InstallPath = "relative"; s.WriteLibrary(); break;
            case "traversal": s.Game.FolderName = "../catalog"; s.WriteLibrary(); break;
            case "linked-library": File.Move(s.Library, Path.Combine(s.Catalog, "original.json")); File.CreateSymbolicLink(s.Library, Path.Combine(s.Catalog, "original.json")); break;
            case "relative-apps": File.WriteAllText(s.Settings, settings.Replace(Path.Combine(s.F.Root, "Apps"), "relative")); break;
            case "missing-apps": Directory.Move(Path.Combine(s.F.Root, "Apps"), Path.Combine(s.F.Root, "retained-apps")); break;
        }
        var deny = kind == "denied-library" ? s.Library : kind == "denied-settings" ? s.Settings : null;
        var before = s.F.Snapshot(); UnixFileMode? mode = null;
        if (deny != null) { mode = File.GetUnixFileMode(deny); File.SetUnixFileMode(deny, UnixFileMode.None); }
        try
        {
            var result = await Execute(s, cli); Assert.NotEqual(0, result.Code); Assert.Contains("ERROR:", result.Text); Assert.Empty(s.Calls);
            if (deny != null) Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(deny));
        }
        finally { if (mode != null) File.SetUnixFileMode(deny!, mode.Value); }
        Assert.Equal(before, s.F.Snapshot());
    }
    [Theory]
    [InlineData(false, false, false)][InlineData(true, false, false)][InlineData(false, true, false)][InlineData(true, true, false)]
    [InlineData(false, false, true)][InlineData(true, false, true)][InlineData(false, true, true)][InlineData(true, true, true)]
    public async Task Allowed_unmanaged_exact_callback_stays_leased_and_errors_are_honest(bool injected, bool located, bool fail)
    {
        using var s = new Scope(); Directory.CreateDirectory(s.F.Live);
        File.WriteAllText(Path.Combine(s.F.Live, "fixture.exe"), "NONEXECUTABLE"); File.WriteAllText(Path.Combine(s.F.Live, "other"), "keep");
        File.WriteAllText(Path.Combine(s.F.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        if (located) s.Game.InstallPath = s.F.Live; s.WriteLibrary();
        var cli = s.Cli(injected, path =>
        {
            if (fail) throw new IOException("synthetic Execute callback failure");
            if (located) File.Move(path, Path.Combine(s.F.Root, "benign-trash")); else Directory.Move(path, Path.Combine(s.F.Root, "benign-trash"));
        });
        var before = s.F.Snapshot(); var result = await Execute(s, cli, "-x");
        Assert.Equal(new[] { located ? Path.Combine(s.F.Live, "fixture.exe") : s.F.Live }, s.Calls);
        Assert.Equal(fail ? 1 : 0, result.Code);
        if (fail) { Assert.Equal(before, s.F.Snapshot()); Assert.DoesNotContain("successfully", result.Text); Assert.Contains("callback failure", result.Text); }
        else
        {
            Assert.DoesNotContain("ERROR:", result.Text);
            if (located) { Assert.True(File.Exists(Path.Combine(s.F.Live, "other"))); using var d = JsonDocument.Parse(File.ReadAllBytes(s.Library)); Assert.Equal(JsonValueKind.Null, d.RootElement.GetProperty("apps")[0].GetProperty("installPath").ValueKind); }
            else Assert.False(Directory.Exists(s.F.Live));
        }
    }
    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)][InlineData(true, true)]
    public async Task Forward_nonsecret_settings_fields_keep_existing_schema_semantics_and_bytes(bool injected, bool managed)
    {
        using var s = new Scope();
        if (managed) await s.F.Install();
        s.WriteLibrary();
        // Existing settings parser deliberately preserves unknown nonsecret fields.
        // Do not silently change its shared schema in a removal-only correction.
        var settings = managed ? File.ReadAllText(s.Settings).TrimEnd()[..^1] + ",\"FutureNonsecretSetting\":true}"
            : "{\"FutureNonsecretSetting\":true}";
        File.WriteAllText(s.Settings, settings);
        if (!managed)
        {
            // The default (not custom) missing slot may create ONLY its stable
            // lock artifacts. Initialize those identities before exact byte snapshot.
            using var stable = new PortableInstallationTransaction(Path.Combine(s.Catalog, "Apps", "live")).AcquireLease();
        }
        var cli = s.Cli(injected); var before = s.F.Snapshot(); var result = await Execute(s, cli);
        Assert.Equal(managed ? 1 : 0, result.Code); // unknown-only defaults: factual missing-game no-op, never deletion
        Assert.Empty(s.Calls); Assert.Equal(before, s.F.Snapshot());
    }

    [Theory]
    [InlineData(false, "MSI")][InlineData(true, "MSI")]
    [InlineData(false, "bad-Flatpak")][InlineData(true, "bad-Flatpak")]
    [InlineData(false, "shared")][InlineData(true, "shared")]
    public async Task Platform_receipts_and_shared_linked_metadata_remain_truthful(bool injected, string kind)
    {
        using var s = new Scope(); Directory.CreateDirectory(s.F.Live); s.Game.InstallPath = s.F.Live;
        File.WriteAllText(Path.Combine(s.F.Live, "fixture.exe"), "NONEXECUTABLE");
        File.WriteAllText(Path.Combine(s.F.Live, "version.txt"), "old");
        if (kind == "MSI") WindowsInstallerService.WriteReceipt(s.F.Live, new("Synthetic", "old", Path.Combine(s.F.Live, "fixture.exe")));
        if (kind == "bad-Flatpak") File.WriteAllText(Path.Combine(s.F.Live, FlatpakService.ReceiptFileName), "invalid synthetic receipt");
        if (kind == "shared") s.WriteLibrary(new GameInfo { Name = "Other", FolderName = "other", Repository = "fixture/other", InstallPath = s.F.Live });
        else s.WriteLibrary();
        var cli = s.Cli(injected); var before = s.F.Snapshot(); var result = await Execute(s, cli);
        Assert.Empty(s.Calls);
        if (kind == "shared")
        {
            Assert.Equal(0, result.Code); Assert.Contains("other files were kept", result.Text);
            Assert.Equal("old", File.ReadAllText(Path.Combine(s.F.Live, "version.txt")));
            Assert.Equal("NONEXECUTABLE", File.ReadAllText(Path.Combine(s.F.Live, "fixture.exe")));
            using var doc = JsonDocument.Parse(File.ReadAllBytes(s.Library));
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("apps")[0].GetProperty("installPath").ValueKind);
            Assert.Equal(s.F.Live, doc.RootElement.GetProperty("apps")[1].GetProperty("installPath").GetString());
        }
        else { Assert.NotEqual(0, result.Code); Assert.Equal(before, s.F.Snapshot()); }
    }
    [Theory]
    [InlineData(false, "array")][InlineData(true, "array")][InlineData(false, "legacy")][InlineData(true, "legacy")]
    [InlineData(false, "empty")][InlineData(true, "empty")]
    public async Task Supported_local_forms_are_read_only_not_migrated_or_backed_up(bool injected, string form)
    {
        using var s = new Scope(); s.Game.Repository = null; s.WriteLibrary();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(s.Library)))
        {
            var array = doc.RootElement.GetProperty("apps").GetRawText();
            File.WriteAllText(s.Library, form == "array" ? array : form == "legacy" ? "{\"custom\":" + array + "}" : "{\"apps\":[]}");
        }
        var cli = s.Cli(injected); var before = s.F.Snapshot(); var result = await Execute(s, cli);
        Assert.Equal(form == "empty" ? 1 : 0, result.Code); Assert.Empty(s.Calls); Assert.Equal(before, s.F.Snapshot());
        Assert.False(Directory.Exists(Path.Combine(s.Catalog, "Backups"))); Assert.False(File.Exists(s.Library + ".lock"));
    }
    [Fact]
    public async Task Only_documented_empty_application_directories_may_be_initialized_before_refusal()
    {
        using var s = new Scope(); await s.F.Install(); s.WriteLibrary();
        Directory.Delete(Path.Combine(s.Catalog, "Apps")); Directory.Delete(Path.Combine(s.Catalog, "Cache"));
        var before = s.F.Snapshot(); var result = await Execute(s, s.Cli(false));
        Assert.NotEqual(0, result.Code); Assert.Empty(s.Calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(s.Catalog, "Apps")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(s.Catalog, "Cache")));
        var after = s.F.Snapshot().Where(r => r is not ("dir catalog/Apps" or "dir catalog/Cache")).ToArray();
        Assert.Equal(before, after);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Empty_manually_managed_library_and_missing_unmanaged_game_are_not_manufactured(bool injected)
    {
        using var s = new Scope(); s.Game.Repository = null; s.WriteLibrary(); var before = s.F.Snapshot();
        var result = await Execute(s, s.Cli(injected)); Assert.Equal(0, result.Code); Assert.Contains("is not installed", result.Text); Assert.Empty(s.Calls);
        Assert.Equal(before, s.F.Snapshot());
    }
}
