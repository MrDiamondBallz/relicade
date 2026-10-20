using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupServiceTests(ITestOutputHelper output)
{
    internal static GameManager Manager(PortableBackupCleanupBaselineScope s, bool loaded = false)
    {
        var manager = new GameManager(s.Store, new HttpClient(s.Network), new AppCatalogService(dataDirectory: s.Catalog));
        manager.Games = new ObservableCollection<GameInfo> { s.Game };
        return manager;
    }
    internal static async Task Install(PortableBackupCleanupBaselineScope s, int generations = 4)
    { for (var i = 1; i <= generations; i++) await s.Install("v" + i); }

    [Fact]
    public async Task Ui_manager_without_removal_loader_matches_exact_CLI_and_reclaims_only_approved_files()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s);
        using var manager = Manager(s);
        var before = s.Snapshot(); var plan = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        Assert.Equal(2, plan.Files.Count);
        var cli = await s.Execute(true, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.Equal(0, cli.Code); Assert.Contains("Plan SHA256: " + plan.PlanSha256, cli.Text);
        foreach (var row in plan.Files) Assert.Contains($"{row.OperationId}/{row.BackupRelativePath} -> retained {row.OperationId}/{row.RetainedRelativePath} | {row.Length} bytes | SHA256 {row.Sha256}", cli.Text);
        Assert.Equal(before, s.Snapshot());
        var result = PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256);
        Assert.True(result.Completed); Assert.Equal(plan.Files.Count, result.DeletedFiles); Assert.Equal(plan.ReclaimableBytes, result.ReclaimedBytes);
        var after = s.Snapshot(); var approved = plan.Files.Select(f => Path.GetRelativePath(s.Root, Path.Combine(s.Tx.OperationsPath, f.OperationId, f.BackupRelativePath))).ToHashSet();
        foreach (var row in before) if (approved.Contains(row.Key)) Assert.False(after.ContainsKey(row.Key)); else Assert.Equal(row.Value, after[row.Key]);
        Assert.All(after.Keys.Except(before.Keys), p => Assert.Equal(Path.GetRelativePath(s.Root, Path.Combine(s.Tx.OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json")), p));
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, true), PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256));
        Assert.Empty(PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey).Files);
        await s.Install("v5"); s.Tx.SelectExecutable("fixture.AppImage");
        File.WriteAllText(Path.Combine(s.Live, "LastPlayed.txt"), "CURRENT_SAVE");
        s.Tx.Rollback(); Assert.Equal("CURRENT_SAVE", File.ReadAllText(Path.Combine(s.Live, "LastPlayed.txt")));
        Assert.NotNull(s.Tx.ReadCommitted()); Assert.NotEmpty(PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey).Files);
        Assert.Equal(0, s.Network.Calls);
    }

    [Theory]
    [InlineData("corrupt-library")][InlineData("duplicate-key")][InlineData("missing-key")][InlineData("case-key")]
    [InlineData("name")][InlineData("repository")][InlineData("shared-persisted-only")][InlineData("unknown-format")]
    [InlineData("corrupt-settings")][InlineData("kiosk")][InlineData("manual")][InlineData("package")]
    [InlineData("default-located")][InlineData("changed-apps-root")][InlineData("read-only")][InlineData("unknown-history")]
    public async Task Already_initialized_UI_cannot_bypass_readonly_corrupt_persisted_identity_and_root_policy(string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s);
        using var manager = Manager(s); var key = s.Game.InstanceKey;
        var prior = SettingsStoreProvider.Default;
        switch (kind)
        {
            case "corrupt-library": File.WriteAllText(s.Library, "{broken"); break;
            case "duplicate-key": s.SaveLibrary(s.Game); break;
            case "missing-key": key = "missing"; break;
            case "case-key": key = key.ToUpperInvariant(); break;
            case "name": key = s.Game.Name!; break;
            case "repository": key = s.Game.Repository!; break;
            case "shared-persisted-only": s.SaveLibrary(new GameInfo { Name = "other", Repository = "fixture/other", FolderName = "other", InstallPath = s.Live }); break;
            case "unknown-format": File.WriteAllText(s.Library, "{\"unknown\": []}"); break;
            case "corrupt-settings": File.WriteAllText(s.Settings, "{broken"); break;
            case "kiosk": s.Store.Current.KioskMode = true; break;
            case "manual": s.Game.Repository = null; break;
            case "package": File.WriteAllText(Path.Combine(s.Live, WindowsInstallerService.ReceiptFileName), "conflict"); break;
            case "default-located": s.Game.InstallPath = Path.Combine(s.Root, "located"); Directory.CreateDirectory(s.Game.InstallPath); break;
            case "changed-apps-root": s.Store.Current.AppsPath = Path.Combine(s.Root, "other"); Directory.CreateDirectory(s.Store.Current.AppsPath); break;
            case "read-only": SettingsStoreProvider.Default = new ReadOnlyStore(s.Store.Current); break;
            case "unknown-history": Directory.CreateDirectory(Path.Combine(s.Tx.OperationsPath, "unknown")); break;
        }
        var before = s.Snapshot();
        try { Assert.ThrowsAny<Exception>(() => PortableBackupCleanupService.Preview(manager, key)); Assert.Equal(before, s.Snapshot()); Assert.Equal(0, s.Network.Calls); }
        finally { SettingsStoreProvider.Default = prior; }
    }

    [Theory]
    [InlineData("library")][InlineData("settings")][InlineData("current-save")][InlineData("selected-root")][InlineData("kiosk")]
    public async Task Changed_open_dialog_context_never_confirms_replacement_hash(string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s); using var manager = Manager(s);
        var key = s.Game.InstanceKey; var plan = PortableBackupCleanupService.Preview(manager, key);
        switch (kind)
        {
            case "library": File.AppendAllText(s.Library, " "); break;
            case "settings": File.AppendAllText(s.Settings, " "); break;
            case "current-save": File.WriteAllText(Path.Combine(s.Live, "LastPlayed.txt"), "changed-current-save"); break;
            case "selected-root": s.Game.InstallPath = Path.Combine(s.Root, "unavailable"); break;
            case "kiosk": s.Store.Current.KioskMode = true; break;
        }
        var before = s.Snapshot(); Assert.ThrowsAny<Exception>(() => PortableBackupCleanupService.Apply(manager, key, plan.PlanSha256));
        Assert.Equal(before, s.Snapshot()); Assert.Equal(0, s.Network.Calls);
    }

    [Theory]
    [InlineData("writer")][InlineData("mod")][InlineData("launch")][InlineData("wrong-hash")][InlineData("cancel")]
    public async Task Authority_and_cancel_before_deletion_never_writes_approval(string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s); using var manager = Manager(s);
        var key = s.Game.InstanceKey; var plan = PortableBackupCleanupService.Preview(manager, key);
        if (kind == "launch") File.WriteAllText(Path.Combine(s.Tx.OperationsPath, "launch.json"), "unknown stale lease");
        var before = s.Snapshot();
        using IDisposable? held = kind == "writer" ? s.Tx.AcquireLease() : kind == "mod" ? new FileStream(Path.Combine(s.Live, ".relicade-mod-writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        using var cts = new CancellationTokenSource(); if (kind == "cancel") cts.Cancel();
        Assert.ThrowsAny<Exception>(() => PortableBackupCleanupService.Apply(manager, key, kind == "wrong-hash" ? new string('0',64) : plan.PlanSha256, cts.Token));
        held?.Dispose(); Assert.Equal(before, s.Snapshot()); Assert.Equal(0, s.Network.Calls);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Independent_injected_UI_manager_recovery_state_must_refuse_typed_service_before_any_deletion(bool apply)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s);
        // Global settings are readable, but THIS UI session's injected store is
        // in recovery. No CLI removal loader/snapshot was involved.
        // Historical failing source/XML remain in fresh/. Obtain this otherwise
        // valid hash independently while readable, so apply=true really reaches
        // Apply on the initially read-only manager (not a failing Preview setup).
        using var readable = Manager(s);
        var plan = PortableBackupCleanupService.Preview(readable, s.Game.InstanceKey);
        var store = new RecoveryStore(s.Store.Current) { ReadOnly = true };
        using var manager = new GameManager(store, new HttpClient(s.Network), new AppCatalogService(dataDirectory: s.Catalog));
        manager.Games = new ObservableCollection<GameInfo> { s.Game };
        var games = manager.Games; var current = store.Current; var loads = store.Loads;
        var before = s.Snapshot();
        var error = apply
            ? Assert.Throws<IOException>(() => PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256))
            : Assert.Throws<IOException>(() => PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey));
        Assert.Contains("This library session's settings are in recovery", error.Message);
        output.WriteLine(JsonSerializer.Serialize(new { apply, plan.PlanSha256, before, after = s.Snapshot(), error.Message }));
        Assert.Equal(before, s.Snapshot()); Assert.Same(games, manager.Games); Assert.Same(s.Game, Assert.Single(manager.Games));
        Assert.Same(current, store.Current); Assert.Equal(loads, store.Loads); Assert.Equal(0, store.Saves); Assert.Equal(0, s.Network.Calls);
    }

    [Fact]
    public void Typed_arguments_are_validated_visibly()
    {
        Assert.Throws<ArgumentNullException>(() => PortableBackupCleanupService.Preview(null!, "key"));
        Assert.Throws<ArgumentNullException>(() => PortableBackupCleanupService.Apply(null!, "key", null!));
        Assert.Throws<ArgumentException>(() => PortableBackupCleanupService.Apply(null!, "key", "bad"));
    }

    [Theory]
    [InlineData("before-apply")][InlineData("under-authority")][InlineData("resume")]
    public async Task Injected_recovery_transitions_revalidate_same_manager_and_explicit_resume(string boundary)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s, 5);
        var store = new RecoveryStore(s.Store.Current);
        using var manager = new GameManager(store, new HttpClient(s.Network), new AppCatalogService(dataDirectory: s.Catalog));
        manager.Games = new ObservableCollection<GameInfo> { s.Game };
        var games = manager.Games; var plan = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        var original = SettingsStoreProvider.Default; int loads = 0;
        using var cts = new CancellationTokenSource();
        var first = Path.Combine(s.Tx.OperationsPath, plan.Files[0].OperationId, plan.Files[0].BackupRelativePath);
        try
        {
            if (boundary == "resume")
            {
                SettingsStoreProvider.Default = new LoadHook(s.Store.Current, () => { if (!File.Exists(first)) cts.Cancel(); });
                var interrupted = Assert.Throws<BackupCleanupInterruptedException>(() => PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256, cts.Token));
                Assert.InRange(interrupted.Result.DeletedFiles, 1, plan.Files.Count - 1);
                Assert.False(interrupted.Result.Completed); Assert.Equal(plan.PlanSha256, interrupted.Result.PlanSha256);
                SettingsStoreProvider.Default = original;
            }
            if (boundary == "under-authority")
                SettingsStoreProvider.Default = new LoadHook(s.Store.Current, () => { if (++loads == 2) store.ReadOnly = true; });
            else store.ReadOnly = true;
            var snapshot = s.Snapshot(); var managerLoads = store.Loads;
            var error = Assert.Throws<IOException>(() => PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256));
            Assert.Contains("This library session's settings are in recovery", error.Message);
            if (boundary == "under-authority") Assert.Equal(2, loads); // Prepare succeeded, Core authority callback refused.
            Assert.Equal(snapshot, s.Snapshot()); Assert.Same(games, manager.Games); Assert.Same(s.Game, Assert.Single(manager.Games));
            Assert.Equal(managerLoads, store.Loads); Assert.Equal(0, store.Saves); Assert.Equal(0, s.Network.Calls);
            store.ReadOnly = false; SettingsStoreProvider.Default = original;
            var result = PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256);
            Assert.True(result.Completed); Assert.True(result.DeletedFiles > 0); // readable control, same approved hash
            Assert.Equal(plan.PlanSha256, result.PlanSha256);
        }
        finally { SettingsStoreProvider.Default = original; }
    }

    [Theory]
    [InlineData("replace-instance")][InlineData("changed-root")][InlineData("changed-library")][InlineData("kiosk")]
    public async Task Service_authority_callback_rechecks_live_UI_context_before_approval(string change)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await Install(s);
        using var manager = Manager(s); var plan = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        var original = SettingsStoreProvider.Default; int loads = 0;
        var before = s.Snapshot();
        SettingsStoreProvider.Default = new LoadHook(s.Store.Current, () =>
        {
            if (++loads != 2) return;
            switch (change)
            {
                case "replace-instance": manager.Games = new ObservableCollection<GameInfo> { new() { Name = s.Game.Name, Repository = s.Game.Repository, FolderName = s.Game.FolderName, InstallPath = Path.Combine(s.Root, "other") } }; break;
                case "changed-root": s.Game.InstallPath = Path.Combine(s.Root, "other"); break;
                case "changed-library": File.AppendAllText(s.Library, " "); break;
                case "kiosk": s.Store.Current.KioskMode = true; break;
            }
        });
        try
        {
            Assert.ThrowsAny<IOException>(() => PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256));
            Assert.Equal(2, loads); Assert.Equal(0, s.Network.Calls);
            var after = s.Snapshot();
            foreach (var row in before) if (change != "changed-library" || row.Key != Path.GetRelativePath(s.Root, s.Library)) Assert.Equal(row.Value, after[row.Key]);
            Assert.Empty(after.Keys.Except(before.Keys));
        }
        finally { SettingsStoreProvider.Default = original; }
    }

    internal sealed class RecoveryStore(AppSettings current) : ISettingsStore, ISettingsRecoveryState
    {
        public bool ReadOnly; public int Loads; public int Saves;
        public AppSettings Current => current;
        public AppSettings Load() { Loads++; return current; }
        public void Save(AppSettings settings) { Saves++; throw new Exception("Unexpected injected store save"); }
        public bool IsReadOnly => ReadOnly;
        public Exception? ReadError => ReadOnly ? new IOException("Synthetic injected recovery") : null;
    }
    sealed class LoadHook(AppSettings current, Action onLoad) : ISettingsStore
    {
        public AppSettings Current => current;
        public AppSettings Load() { onLoad(); return current; }
        public void Save(AppSettings settings) => throw new Exception("Unexpected global save");
    }

    sealed class ReadOnlyStore(AppSettings current) : ISettingsStore, ISettingsRecoveryState
    {
        public AppSettings Current => current; public AppSettings Load() => current;
        public bool IsReadOnly => true; public Exception? ReadError => new IOException("Synthetic recovery refusal");
        public void Save(AppSettings settings) => throw new Exception("No save is authorized");
    }
}
