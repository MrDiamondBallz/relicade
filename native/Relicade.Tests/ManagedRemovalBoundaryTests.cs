using Relicade.Core.Services;
using Relicade.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class ManagedRemovalBoundaryTests
{
    [Theory]
    [InlineData("healthy")][InlineData("multi")][InlineData("rollback")]
    [InlineData("missing-marker")][InlineData("corrupt-marker")][InlineData("unknown-marker")]
    [InlineData("missing-journal")][InlineData("corrupt-journal")][InlineData("unknown-operation")]
    [InlineData("unknown-history-file")][InlineData("missing-live")][InlineData("prePrepared")]
    [InlineData("Prepared")][InlineData("Applying")][InlineData("failed-recovery")]
    [InlineData("history-link")][InlineData("live-link")][InlineData("marker-directory")]
    [InlineData("uncertain-launch")][InlineData("stale-launch")][InlineData("mod-staging")]
    public async Task Managed_or_uncertain_evidence_refuses_direct_removal_without_mutating_any_bytes(string fault)
    {
        using var f = new ManagedRemovalFixture();
        var realGeneration = fault is "healthy" or "multi" or "rollback" or "missing-marker" or "missing-journal" or "corrupt-journal" or "missing-live" or "Prepared" or "Applying" or "failed-recovery";
        if (realGeneration) await f.Install();
        else { Directory.CreateDirectory(f.Live); File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE unmanaged fixture"); }
        if (fault is "multi" or "rollback") { await f.Install("new"); if (fault == "rollback") f.Tx.Rollback(); }
        var marker = Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName);
        var journal = Directory.GetFiles(f.Tx.OperationsPath, "journal.json", SearchOption.AllDirectories).FirstOrDefault() ?? "";
        switch (fault)
        {
            case "missing-marker": File.Delete(marker); break;
            case "corrupt-marker": File.WriteAllText(marker, "{broken"); break;
            case "unknown-marker": File.WriteAllText(marker, "{\"schemaVersion\":999}"); break;
            case "missing-journal": File.Delete(journal); break;
            case "corrupt-journal": File.WriteAllText(journal, "not JSON"); break;
            case "unknown-operation": Directory.CreateDirectory(Path.Combine(f.Tx.OperationsPath, "unknown")); break;
            case "unknown-history-file": File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "unknown"), "retained"); break;
            case "missing-live": Directory.Move(f.Live, Path.Combine(f.Root, "retained-live")); break;
            case "prePrepared": Directory.CreateDirectory(Path.Combine(f.Tx.OperationsPath, Guid.NewGuid().ToString("N"), "candidate")); File.Delete(marker); break;
            case "Prepared": case "Applying": case "failed-recovery":
                File.WriteAllText(journal, "{\"phase\":\"" + fault + "\"}"); File.Delete(marker); break;
            case "history-link": Directory.CreateSymbolicLink(Path.Combine(f.Tx.OperationsPath, "link"), f.Live); break;
            case "live-link": File.CreateSymbolicLink(Path.Combine(f.Live, "link"), Path.Combine(f.Root, "sibling-canary")); break;
            case "marker-directory": File.Delete(marker); Directory.CreateDirectory(marker); break;
            case "uncertain-launch": File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "launch.json"), "{bad"); break;
            case "stale-launch": File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "launch.json"), "{\"schemaVersion\":1,\"state\":\"Running\",\"processId\":2147483647,\"startIdentity\":\"old\"}"); break;
            case "mod-staging": Directory.CreateDirectory(Path.Combine(f.Live, ".relicade-mod-staging")); break;
        }
        var before = f.Snapshot(); var callbacks = new List<string>();
        var error = Record.Exception(() => LocatedInstallRemoval.Remove(f.Live, false, callbacks.Add));
        Assert.True(error is IOException or InvalidDataException, error?.ToString() ?? "Removal unexpectedly succeeded"); Assert.Empty(callbacks); Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData("marker")][InlineData("history")][InlineData("live")]
    public async Task Permission_uncertainty_with_zero_capabilities_is_not_absence(string target)
    {
        Assert.Contains("CapEff:\t0000000000000000", File.ReadAllText("/proc/self/status"));
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName), "{synthetic unreadable ownership}");
        if (target != "marker") File.Delete(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
        File.WriteAllText(Path.Combine(f.Live, "version.txt"), "unmanaged; permission uncertainty must still refuse");
        var path = target == "marker" ? Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName) : target == "history" ? f.Tx.OperationsPath : f.Live;
        var before = f.Snapshot(); var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.ThrowsAny<Exception>(() => LocatedInstallRemoval.Remove(f.Live, false, _ => throw new Exception("Unexpected mutation")));
            // Prove the permission fault really affects this caps0 process.
            if (target == "marker") Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(path));
            else Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(path));
        }
        finally { File.SetUnixFileMode(path, mode); }
        Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData("writer")][InlineData("G05")][InlineData("legacy-mod")][InlineData("lifetime")]
    public void Held_writer_or_running_lifetime_refuses_before_callback(string held)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "version.txt"), "unmanaged");
        using IDisposable lease = held switch
        {
            "writer" => f.Tx.AcquireLease(), "G05" => ModInstallTransaction.Acquire(f.Live),
            "legacy-mod" => new FileStream(Path.Combine(f.Live, ".relicade-mod-writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
            _ => PortableInstallationLifetime.Begin(f.Live)
        };
        var before = f.Snapshot();
        Assert.ThrowsAny<IOException>(() => LocatedInstallRemoval.Remove(f.Live, false, _ => throw new Exception("Unexpected mutation")));
        Assert.Equal(before, f.Snapshot());
        Assert.Equal("unmanaged", File.ReadAllText(Path.Combine(f.Live, "version.txt")));
    }
    [Fact]
    public void Missing_truly_unmanaged_root_is_not_recreated_by_removal_lease()
    {
        using var f = new ManagedRemovalFixture(); Assert.False(Directory.Exists(f.Live));
        var before = f.Snapshot();
        using (f.Tx.AcquireUnmanagedRemovalLease())
        {
            Assert.False(Directory.Exists(f.Live));
            Assert.ThrowsAny<IOException>(() => { using var writer = f.Tx.AcquireLease(); });
            Assert.Equal(before, f.Snapshot());
        }
        Assert.Equal(before, f.Snapshot()); using var reopened = f.Tx.AcquireLease();
    }
    [Fact]
    public async Task Actual_failed_recovery_refuses_removal_before_reopen_changes_any_generation()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        await Assert.ThrowsAsync<IOException>(() => ManagedRemovalFixture.InstallAt(f.Live, "new", point =>
        { if (point is "PreviousRenamed" or "Recovering") throw new IOException("Synthetic real recovery-boundary failure"); }));
        Assert.False(Directory.Exists(f.Live)); var before = f.Snapshot();
        Assert.ThrowsAny<IOException>(() => LocatedInstallRemoval.Remove(f.Live, false, _ => throw new Exception("No callback")));
        Assert.Equal(before, f.Snapshot()); Assert.False(Directory.Exists(f.Live));
        f.Tx.Recover(); Assert.Equal("old", f.Tx.ReadCommitted()!.SelectedVersion);
        Assert.Equal("current save old", File.ReadAllText(Path.Combine(f.Live, "saves", "slot")));
    }
    [Fact]
    public void Truly_unmanaged_lease_excludes_writers_through_callback_and_preserves_lock_identity()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE");
        File.WriteAllText(Path.Combine(f.Live, "other"), "keep");
        File.WriteAllText(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        var seen = new List<string>();
        LocatedInstallRemoval.Remove(f.Live, false, path =>
        {
            Assert.ThrowsAny<IOException>(() => { using var writer = f.Tx.AcquireLease(); });
            seen.Add(path); File.Move(path, Path.Combine(f.Root, "benign-trash"));
        });
        Assert.Equal(new[] { Path.Combine(f.Live, "fixture.exe") }, seen);
        Assert.True(File.Exists(Path.Combine(f.Root, "benign-trash"))); Assert.True(File.Exists(Path.Combine(f.Live, "other")));
        Assert.False(File.Exists(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk)));
        using var allowed = f.Tx.AcquireUnmanagedRemovalLease();
        Assert.True(File.Exists(Path.Combine(f.Tx.OperationsPath, "writer.lock")));
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Shared_and_throwing_unmanaged_callbacks_keep_metadata_and_other_files(bool shared)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE");
        File.WriteAllText(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        File.WriteAllText(Path.Combine(f.Live, "selected_executable.txt"), "fixture.exe");
        var before = f.Snapshot(); var calls = 0;
        var error = Record.Exception(() => LocatedInstallRemoval.Remove(f.Live, shared, _ => { calls++; throw new IOException("injected callback failure"); }));
        Assert.Equal(shared ? 0 : 1, calls); Assert.Equal(before, f.Snapshot());
        if (!shared) Assert.IsType<IOException>(error); else Assert.Null(error);
    }
}
