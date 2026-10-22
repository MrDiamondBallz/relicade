using Relicade.Models;
using Relicade.Core.Services;
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
    public async Task Managed_folder_rename_is_refused_before_metadata_or_payload_changes(bool writerHeld)
    {
        File.Delete(Path.Combine(OldPath, "payload")); // Remove only this fixture constructor's unmanaged placeholder.
        var tx = new PortableInstallationTransaction(OldPath, new() { PreservedDataPaths = ["saves"] });
        await tx.InstallAsync(stage =>
        {
            File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("old"));
            return Task.CompletedTask;
        }, "old");
        Directory.CreateDirectory(Path.Combine(OldPath, "saves"));
        File.WriteAllText(Path.Combine(OldPath, "saves", "slot.dat"), "synthetic save");
        var committed = tx.ReadCommitted()!;
        var settingsBytes = File.ReadAllBytes(Path.Combine(root, "settings.json"));
        var libraryBytes = File.ReadAllBytes(manager.CatalogService.AppsConfigPath);
        using var held = writerHeld ? tx.AcquireLease() : null;
        var saved = await Edit();
        var relocatedError = Directory.Exists(NewPath)
            ? Record.Exception(() => new PortableInstallationTransaction(NewPath).ReadCommitted()) : null;
        Console.WriteLine($"Save={saved}; old exists={Directory.Exists(OldPath)}; new exists={Directory.Exists(NewPath)}; relocated read={relocatedError?.GetType().Name}: {relocatedError?.Message}");
        Assert.False(saved);
        Assert.True(Directory.Exists(OldPath));
        Assert.False(Directory.Exists(NewPath));
        Assert.Equal(settingsBytes, File.ReadAllBytes(Path.Combine(root, "settings.json")));
        Assert.Equal(libraryBytes, File.ReadAllBytes(manager.CatalogService.AppsConfigPath));
        Assert.Equal("synthetic save", File.ReadAllText(Path.Combine(OldPath, "saves", "slot.dat")));
        held?.Dispose();
        Assert.Equal(committed.OperationId, tx.ReadCommitted()!.OperationId);
        Assert.Contains("managed", Assert.Single(notices).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_presentation_edit_preserves_payload_and_does_not_prepare_missing_markers(bool manual)
    {
        File.Delete(Path.Combine(OldPath, "payload"));
        if (manual) original.Repository = "";
        original.FilesToAdd = ["portable.txt"];
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        var tx = new PortableInstallationTransaction(OldPath);
        await tx.InstallAsync(stage =>
        {
            File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("old"));
            return Task.CompletedTask;
        }, "old");
        var manifest = File.ReadAllBytes(Path.Combine(OldPath, PortableInstallationTransaction.ManifestFileName));
        var draft = new AppEntryDraft("New title", original.Repository, "github", "Old", "", "", "My title", "favorite",
            "portable.txt", "", "", "", false, manual, original.InstanceKey, original.Repository);
        Assert.True(await new AppEntryService(manager, settings).SaveAsync(draft, notices.Add,
            _ => throw new Exception("Must not open folder"), CancellationToken.None));
        var saved = Assert.Single(await manager.CatalogService.LoadLocalAppsAsync());
        Assert.Equal("My title", saved.CustomDisplayName);
        Assert.Equal("New title", saved.Name);
        Assert.Contains("favorite", saved.Tags);
        Assert.False(File.Exists(Path.Combine(OldPath, "portable.txt")));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(OldPath, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal("old", tx.ReadCommitted()!.SelectedVersion);
        Assert.Contains("updated", Assert.Single(notices).Title, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("provider")]
    [InlineData("manual")]
    [InlineData("markers")]
    [InlineData("release-filter")]
    [InlineData("mods")]
    [InlineData("mods-case")]
    public async Task Managed_setup_edits_are_refused_without_changing_metadata_or_payload(string change)
    {
        if (change == "mods-case")
        {
            original.ModsPath = "Mods";
            await manager.CatalogService.SaveLocalAppsAsync([original]);
        }
        File.Delete(Path.Combine(OldPath, "payload"));
        var tx = new PortableInstallationTransaction(OldPath);
        await tx.InstallAsync(stage =>
        {
            File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("old"));
            return Task.CompletedTask;
        }, "old");
        var manifest = File.ReadAllBytes(Path.Combine(OldPath, PortableInstallationTransaction.ManifestFileName));
        var library = File.ReadAllBytes(manager.CatalogService.AppsConfigPath);
        var draft = new AppEntryDraft("Fixture", original.Repository, "github", "Old", "", "", "", "",
            "", "", "", "", false, false, original.InstanceKey, original.Repository);
        draft = change switch
        {
            "repository" => draft with { Repository = "fixture/other" },
            "provider" => draft with { RepositorySource = "gitlab" },
            "manual" => draft with { ManuallyManaged = true },
            "markers" => draft with { FilesToAdd = "new.txt" },
            "release-filter" => draft with { ReleaseAssetFilter = "*.zip" },
            "mods" or "mods-case" => draft with { ModsPath = "mods" },
            _ => throw new ArgumentException(change)
        };
        Assert.False(await new AppEntryService(manager, settings).SaveAsync(draft, notices.Add,
            _ => throw new Exception("Must not open folder"), CancellationToken.None));
        Assert.Equal(library, File.ReadAllBytes(manager.CatalogService.AppsConfigPath));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(OldPath, PortableInstallationTransaction.ManifestFileName)));
        Assert.False(File.Exists(Path.Combine(OldPath, "new.txt")));
        Assert.Equal("old", tx.ReadCommitted()!.SelectedVersion);
        Assert.Contains("not supported", Assert.Single(notices).Message);
    }

    [Fact]
    public async Task Initial_install_writer_blocks_entry_move_before_a_manifest_exists()
    {
        var tx = new PortableInstallationTransaction(OldPath);
        using var writer = tx.AcquireLease();
        Assert.False(PortableInstallationTransaction.HasEvidence(OldPath));
        Assert.False(await Edit());
        await AssertOld();
        Assert.Contains("authority", Assert.Single(notices).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Destination_writer_or_retained_history_prevents_unmanaged_move(bool retainedHistory)
    {
        var destination = new PortableInstallationTransaction(NewPath);
        if (retainedHistory)
        {
            await destination.InstallAsync(stage =>
            {
                File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("destination"));
                return Task.CompletedTask;
            }, "destination");
            // Synthetic missing generation: retained outside-root history must still count.
            Directory.Delete(NewPath, true);
            Assert.True(PortableInstallationTransaction.HasEvidence(NewPath));
        }
        using var writer = retainedHistory ? null : destination.AcquireLease();
        Assert.False(await Edit());
        await AssertOld();
        Assert.True(Directory.Exists(destination.OperationsPath));
        Assert.Contains("authority", Assert.Single(notices).Message);
    }

    [Fact]
    public async Task Linked_managed_presentation_edit_uses_actual_root_not_unrelated_default_folder()
    {
        original.InstallPath = NewPath;
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        var actual = new PortableInstallationTransaction(NewPath);
        await actual.InstallAsync(stage =>
        {
            File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("linked"));
            return Task.CompletedTask;
        }, "linked");
        using var unrelated = new PortableInstallationTransaction(OldPath).AcquireLease();
        var draft = new AppEntryDraft("Linked title", original.Repository, "github", "Old", "", "", "", "",
            "", "", "", "", false, false, original.InstanceKey, original.Repository);
        Assert.True(await new AppEntryService(manager, settings).SaveAsync(draft, notices.Add,
            _ => throw new Exception("Must not open folder"), CancellationToken.None));
        Assert.Equal("Linked title", Assert.Single(await manager.CatalogService.LoadLocalAppsAsync()).Name);
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(OldPath, "payload")));
        Assert.Equal("linked", actual.ReadCommitted()!.SelectedVersion);
    }

    [Fact]
    public async Task Linked_unmanaged_folder_change_does_not_move_unrelated_default_folder()
    {
        var linked = Path.Combine(root, "linked");
        Directory.CreateDirectory(linked);
        File.WriteAllText(Path.Combine(linked, "data"), "linked data");
        original.InstallPath = linked;
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        Assert.False(await Edit());
        await AssertOld();
        Assert.Equal("linked data", File.ReadAllText(Path.Combine(linked, "data")));
        Assert.Contains("Linked", Assert.Single(notices).Message);
    }

    [Fact]
    public async Task Explicit_default_install_path_cannot_be_left_pointing_at_a_moved_folder()
    {
        original.InstallPath = OldPath;
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        Assert.False(await Edit());
        await AssertOld();
        Assert.Equal(OldPath, Assert.Single(await manager.CatalogService.LoadLocalAppsAsync()).InstallPath);
    }

    [Fact]
    public async Task Uncertain_launch_receipt_blocks_unmanaged_folder_move_without_receipt_cleanup()
    {
        using (var launch = PortableInstallationLifetime.Begin(OldPath)) launch.PreserveUncertain();
        var receipt = Path.Combine(new PortableInstallationTransaction(OldPath).OperationsPath, "launch.json");
        var bytes = File.ReadAllBytes(receipt);
        Assert.False(PortableInstallationTransaction.HasEvidence(OldPath));
        Assert.False(await Edit());
        await AssertOld();
        Assert.Equal(bytes, File.ReadAllBytes(receipt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unmanaged_presentation_edit_retains_launch_receipt_and_does_not_prepare_files(bool manual)
    {
        if (manual) original.Repository = "";
        original.FilesToAdd = ["portable.txt"];
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        using (var launch = PortableInstallationLifetime.Begin(OldPath)) launch.PreserveUncertain();
        var receipt = Path.Combine(new PortableInstallationTransaction(OldPath).OperationsPath, "launch.json");
        var bytes = File.ReadAllBytes(receipt);
        var draft = new AppEntryDraft("New title", original.Repository, "github", "Old", "", "", "", "",
            "portable.txt", "", "", "", false, manual, original.InstanceKey, original.Repository);
        Assert.True(await new AppEntryService(manager, settings).SaveAsync(draft, notices.Add,
            _ => throw new Exception("Must not open folder"), CancellationToken.None));
        Assert.Equal("New title", Assert.Single(await manager.CatalogService.LoadLocalAppsAsync()).Name);
        Assert.Equal(bytes, File.ReadAllBytes(receipt));
        Assert.Equal(new[] { "payload" }, Directory.GetFiles(OldPath).Select(Path.GetFileName).ToArray());
        Assert.Equal("old-working", File.ReadAllText(Path.Combine(OldPath, "payload")));
    }

    [Theory]
    [InlineData("managed", false)]
    [InlineData("managed", true)]
    [InlineData("managed-writer", false)]
    [InlineData("managed-writer", true)]
    [InlineData("initial-writer", false)]
    [InlineData("initial-writer", true)]
    public async Task Nested_install_path_refuses_before_creating_child_history(string state, bool presentationOnly)
    {
        var parent = Path.Combine(root, "Apps", "Parent");
        var tx = new PortableInstallationTransaction(parent, new() { PreservedDataPaths = ["saves"] });
        if (state != "initial-writer")
            await tx.InstallAsync(stage =>
            {
                File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("parent"));
                return Task.CompletedTask;
            }, "parent");
        using var writer = state.EndsWith("writer", StringComparison.Ordinal) ? tx.AcquireLease() : null;
        var nested = Path.Combine(parent, "saves");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "save.dat"), "retained save");
        original.InstallPath = nested;
        await manager.CatalogService.SaveLocalAppsAsync([original]);
        var library = File.ReadAllBytes(manager.CatalogService.AppsConfigPath);
        var draft = new AppEntryDraft("New title", original.Repository, "github", "Old", "", "", "", "",
            presentationOnly ? "" : "new-marker", "", "", "", false, false, original.InstanceKey, original.Repository);
        Assert.False(await new AppEntryService(manager, settings).SaveAsync(draft, notices.Add,
            _ => throw new Exception("Must not open folder"), CancellationToken.None));
        Assert.Equal(library, File.ReadAllBytes(manager.CatalogService.AppsConfigPath));
        Assert.Equal("retained save", File.ReadAllText(Path.Combine(nested, "save.dat")));
        Assert.False(File.Exists(Path.Combine(nested, "new-marker")));
        Assert.False(Directory.Exists(Path.Combine(parent, ".relicade-game-transactions")));
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
