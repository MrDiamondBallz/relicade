using System.Reflection;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class ManagedRemovalCliTests
{
    public static async Task<(int Code, string Output)> Remove(ManagedRemovalLibraryTests.Harness h, Action<string>? mutate = null)
    {
        var cli = new CLIHandler(h.Manager, moveToTrash: path =>
        {
            h.Callbacks.Add(path);
            Assert.ThrowsAny<IOException>(() => { using var writer = h.Fixture.Tx.AcquireLease(); });
            if (h.Game.InstallPath != null && Path.GetFullPath(h.Game.InstallPath) != h.Fixture.Live)
                Assert.ThrowsAny<IOException>(() => { using var writer = new PortableInstallationTransaction(h.Game.InstallPath).AcquireLease(); });
            mutate?.Invoke(path);
        });
        var old = Console.Out; using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var code = await (Task<int>)typeof(CLIHandler).GetMethod("UninstallGame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cli, [h.Game.Name])!;
            return (code, output.ToString());
        }
        finally { Console.SetOut(old); }
    }
    static async Task AssertRefusal(ManagedRemovalLibraryTests.Harness h)
    {
        var f = h.Fixture; var before = f.Snapshot(); var status = h.Game.Status; var location = h.Game.InstallPath;
        var result = await Remove(h);
        Assert.NotEqual(0, result.Code); Assert.Contains("ERROR:", result.Output);
        Assert.DoesNotContain("uninstalled successfully", result.Output); Assert.DoesNotContain("is not installed", result.Output);
        Assert.Empty(h.Callbacks); Assert.Equal(0, h.Network.Calls); Assert.Equal(0, h.Settings.Saves);
        Assert.Equal(status, h.Game.Status); Assert.Equal(location, h.Game.InstallPath); Assert.Equal("fixture.exe", h.Game.SelectedExecutable);
        Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData(false, false, false)][InlineData(false, true, false)][InlineData(true, false, false)][InlineData(true, true, false)]
    [InlineData(false, false, true)][InlineData(false, true, true)][InlineData(true, false, true)][InlineData(true, true, true)]
    public async Task Managed_multiversion_current_saves_refuse_regardless_stale_flags(bool located, bool flag, bool staleStatus)
    {
        using var f = new ManagedRemovalFixture(); await f.Install(); await f.Install("new");
        await using var h = new ManagedRemovalLibraryTests.Harness(f, located); await h.Save();
        h.Game.IsManagedPortable = flag; h.Game.IsLoading = true; if (staleStatus) h.Game.Status = GameStatus.NotInstalled;
        await AssertRefusal(h); Assert.True(h.Game.IsLoading);
    }
    [Theory]
    [InlineData("MSI", "default")][InlineData("Flatpak", "default")]
    [InlineData("MSI", "located")][InlineData("Flatpak", "located")]
    [InlineData("MSI", "stale-location")][InlineData("Flatpak", "stale-location")]
    [InlineData("MSI", "owned-located")][InlineData("Flatpak", "owned-located")]
    public async Task Both_authority_roots_precede_OS_receipts(string receipt, string route)
    {
        using var f = new ManagedRemovalFixture();
        if (route == "owned-located")
        {
            Directory.CreateDirectory(f.Live); await ManagedRemovalFixture.InstallAt(Path.Combine(f.Root, "external"), "old");
        }
        else await f.Install();
        await using var h = new ManagedRemovalLibraryTests.Harness(f, route != "default");
        if (route is "stale-location" or "owned-located")
        {
            h.Game.InstallPath = Path.Combine(f.Root, "external"); Directory.CreateDirectory(h.Game.InstallPath);
            using var initialize = new PortableInstallationTransaction(h.Game.InstallPath).AcquireLease();
        }
        var path = h.Game.GetInstallPath(h.Manager.GamesFolder);
        if (receipt == "MSI") WindowsInstallerService.WriteReceipt(path, new("Linked", "old", Path.Combine(path, "fixture.exe")));
        else File.WriteAllText(Path.Combine(path, FlatpakService.ReceiptFileName), "synthetic conflict; must never be parsed/dispatched");
        await h.Save(); await AssertRefusal(h);
    }
    [Theory]
    [InlineData("missing-marker")][InlineData("corrupt-marker")][InlineData("unknown-marker")]
    [InlineData("missing-journal")][InlineData("corrupt-journal")][InlineData("unknown-operation")]
    [InlineData("missing-live")][InlineData("prePrepared")][InlineData("failed-recovery")]
    [InlineData("link")][InlineData("rollback")][InlineData("stale-launch")]
    public async Task Recovery_history_and_uncertainty_refuse_without_resolution(string fault)
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        var marker = Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName);
        var journal = Directory.GetFiles(f.Tx.OperationsPath, "journal.json", SearchOption.AllDirectories).Single();
        switch (fault)
        {
            case "missing-marker": File.Delete(marker); break;
            case "corrupt-marker": File.WriteAllText(marker, "{broken"); break;
            case "unknown-marker": File.WriteAllText(marker, "{\"schemaVersion\":999}"); break;
            case "missing-journal": File.Delete(journal); File.Delete(marker); break;
            case "corrupt-journal": File.WriteAllText(journal, "broken"); File.Delete(marker); break;
            case "unknown-operation": File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "unknown"), "keep"); File.Delete(marker); break;
            case "missing-live": Directory.Move(f.Live, Path.Combine(f.Root, "retained-live")); break;
            case "prePrepared": Directory.CreateDirectory(Path.Combine(f.Tx.OperationsPath, Guid.NewGuid().ToString("N"), "candidate")); File.Delete(marker); break;
            case "failed-recovery":
                await Assert.ThrowsAsync<IOException>(() => ManagedRemovalFixture.InstallAt(f.Live, "new", p =>
                { if (p is "PreviousRenamed" or "Recovering") throw new IOException("synthetic recovery failure"); })); break;
            case "link": File.CreateSymbolicLink(Path.Combine(f.Live, "link"), Path.Combine(f.Root, "sibling-canary")); break;
            case "rollback": await f.Install("new"); f.Tx.Rollback(); break;
            case "stale-launch": File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "launch.json"), "{broken uncertain lifetime}"); break;
        }
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); await h.Save(); await AssertRefusal(h);
    }
    [Theory]
    [InlineData("writer")][InlineData("G05")][InlineData("lifetime")][InlineData("legacy-mod")]
    public async Task Held_writer_running_or_mod_domain_refuses(string held)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); await h.Save();
        using IDisposable lease = held switch
        {
            "writer" => f.Tx.AcquireLease(), "G05" => ModInstallTransaction.Acquire(f.Live),
            "legacy-mod" => new FileStream(Path.Combine(f.Live, ".relicade-mod-writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
            _ => PortableInstallationLifetime.Begin(f.Live)
        };
        await AssertRefusal(h);
    }
    [Theory]
    [InlineData("marker")][InlineData("history")][InlineData("live")]
    public async Task Caps0_permission_uncertainty_is_visible_refusal(string target)
    {
        Assert.Contains("CapEff:\t0000000000000000", File.ReadAllText("/proc/self/status"));
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        var marker = Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName);
        if (target == "marker") File.WriteAllText(marker, "uncertain");
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); await h.Save();
        var before = f.Snapshot(); var path = target == "marker" ? marker : target == "history" ? f.Tx.OperationsPath : f.Live;
        var mode = File.GetUnixFileMode(path); File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var result = await Remove(h); Assert.NotEqual(0, result.Code); Assert.Contains("ERROR:", result.Output); Assert.Empty(h.Callbacks);
            if (target == "marker") Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(path));
            else Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(path));
        }
        finally { File.SetUnixFileMode(path, mode); }
        Assert.Equal(before, f.Snapshot()); Assert.Equal(0, h.Network.Calls); Assert.Equal(GameStatus.Installed, h.Game.Status); Assert.Equal(f.Live, h.Game.InstallPath);
    }
    [Theory]
    [InlineData(false, false, false)][InlineData(true, false, false)][InlineData(false, true, false)][InlineData(true, true, false)]
    [InlineData(false, false, true)][InlineData(true, false, true)][InlineData(false, true, true)][InlineData(true, true, true)]
    public async Task Allowed_unmanaged_exact_callback_under_lease_and_failure_is_honest(bool located, bool fail, bool manual)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE"); File.WriteAllText(Path.Combine(f.Live, "other"), "keep");
        File.WriteAllText(Path.Combine(f.Live, "version.txt"), "old");
        File.WriteAllText(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        await using var h = new ManagedRemovalLibraryTests.Harness(f, located); if (manual) h.Game.Repository = null;
        h.Game.IsManagedPortable = true; await h.Save(); var before = f.Snapshot();
        var result = await Remove(h, path =>
        {
            if (fail) throw new IOException("benign injected CLI callback failure");
            if (located) File.Move(path, Path.Combine(f.Root, "benign-trash")); else Directory.Move(path, Path.Combine(f.Root, "benign-trash"));
        });
        Assert.Equal(new[] { located ? Path.Combine(f.Live, "fixture.exe") : f.Live }, h.Callbacks); Assert.Equal(0, h.Network.Calls);
        if (fail)
        {
            Assert.NotEqual(0, result.Code); Assert.Contains("benign injected CLI", result.Output); Assert.DoesNotContain("successfully", result.Output);
            Assert.Equal(before, f.Snapshot()); Assert.Equal(GameStatus.Installed, h.Game.Status); Assert.Equal(located ? f.Live : null, h.Game.InstallPath);
        }
        else
        {
            Assert.Equal(0, result.Code); Assert.DoesNotContain("ERROR:", result.Output); Assert.True(File.Exists(Path.Combine(f.Root, "sibling-canary")));
            Assert.Null(h.Game.InstallPath); Assert.True(File.Exists(Path.Combine(f.Root, "benign-trash")) || Directory.Exists(Path.Combine(f.Root, "benign-trash")));
            if (located) { Assert.Contains("other files were kept", result.Output); Assert.True(File.Exists(Path.Combine(f.Live, "other"))); Assert.False(File.Exists(Path.Combine(f.Live, "version.txt"))); }
            else Assert.False(Directory.Exists(f.Live));
            Assert.Null(Assert.Single(await h.Catalog.LoadLocalAppsAsync()).InstallPath);
        }
        using var released = f.Tx.AcquireUnmanagedRemovalLease();
    }
    [Fact]
    public async Task Shared_linked_folder_only_detaches_link_without_deleting_other_apps()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE"); File.WriteAllText(Path.Combine(f.Live, "version.txt"), "old");
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); await h.Save();
        h.Manager.Games.Add(new() { Name = "Other", FolderName = "other", Repository = "fixture/other", InstallPath = f.Live });
        var bytes = Directory.GetFiles(f.Live).ToDictionary(p => p, File.ReadAllText); var result = await Remove(h);
        Assert.Equal(0, result.Code); Assert.Contains("other files were kept", result.Output); Assert.Empty(h.Callbacks); Assert.Null(h.Game.InstallPath);
        Assert.Equal(bytes.OrderBy(p => p.Key), Directory.GetFiles(f.Live).ToDictionary(p => p, File.ReadAllText).OrderBy(p => p.Key)); Assert.Equal(0, h.Network.Calls);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Missing_unmanaged_folder_distinguishes_noop_from_failed_removal(bool notInstalled)
    {
        using var f = new ManagedRemovalFixture(); await using var h = new ManagedRemovalLibraryTests.Harness(f, false); await h.Save();
        if (notInstalled) h.Game.Status = GameStatus.NotInstalled;
        var status = h.Game.Status; var before = f.Snapshot(); var result = await Remove(h);
        Assert.Equal(notInstalled ? 0 : 1, result.Code); Assert.Equal(before, f.Snapshot()); Assert.Equal(status, h.Game.Status); Assert.Empty(h.Callbacks); Assert.Equal(0, h.Network.Calls);
        Assert.DoesNotContain("uninstalled successfully", result.Output);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Stale_NotInstalled_does_not_skip_real_unmanaged_mutation(bool located)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE");
        File.WriteAllText(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        await using var h = new ManagedRemovalLibraryTests.Harness(f, located); h.Game.Status = GameStatus.NotInstalled; await h.Save();
        var result = await Remove(h, path =>
        {
            if (located) File.Move(path, Path.Combine(f.Root, "benign-trash"));
            else Directory.Move(path, Path.Combine(f.Root, "benign-trash"));
        });
        Assert.Equal(0, result.Code); Assert.Single(h.Callbacks); Assert.DoesNotContain("is not installed", result.Output); Assert.Equal(0, h.Network.Calls);
    }
    [Fact]
    public async Task Distinct_default_and_located_slots_stay_locked_through_exact_file_callback()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true);
        var external = Path.Combine(f.Root, "external"); h.Game.InstallPath = external; Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(f.Live, "default-canary"), "keep default");
        File.WriteAllText(Path.Combine(external, "fixture.exe"), "NONEXECUTABLE"); File.WriteAllText(Path.Combine(external, "other"), "keep linked sibling");
        File.WriteAllText(Path.Combine(external, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        await h.Save(); var result = await Remove(h, path => File.Move(path, Path.Combine(f.Root, "benign-trash")));
        Assert.Equal(0, result.Code); Assert.Equal(new[] { Path.Combine(external, "fixture.exe") }, h.Callbacks);
        Assert.Equal("keep default", File.ReadAllText(Path.Combine(f.Live, "default-canary")));
        Assert.Equal("keep linked sibling", File.ReadAllText(Path.Combine(external, "other"))); Assert.Equal(0, h.Network.Calls);
        using var defaultLease = f.Tx.AcquireUnmanagedRemovalLease();
        using var locatedLease = new PortableInstallationTransaction(external).AcquireUnmanagedRemovalLease();
    }
    [Fact]
    public async Task Corrupt_unmanaged_Flatpak_receipt_stays_visible_without_executing_package_helper()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, FlatpakService.ReceiptFileName), "invalid synthetic JSON; parsing must fail before any OS dispatch");
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); await h.Save();
        await AssertRefusal(h); // No receipt, metadata or location cleanup on parse failure.
    }
    [Fact]
    public async Task Unmanaged_MSI_keeps_OS_owned_bytes_and_never_dispatches_native_helper()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        WindowsInstallerService.WriteReceipt(f.Live, new("Linked", "old", Path.Combine(f.Live, "fixture.exe")));
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); await h.Save(); var before = f.Snapshot(); var result = await Remove(h);
        Assert.Equal(1, result.Code); Assert.Contains("Windows Settings", result.Output); Assert.Empty(h.Callbacks); Assert.Equal(before, f.Snapshot()); Assert.Equal(0, h.Network.Calls);
    }
}
