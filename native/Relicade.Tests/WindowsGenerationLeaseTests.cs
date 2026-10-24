using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class WindowsGenerationLeaseTests
{
    const string LockName = ".relicade-mod-writer.lock";
    const string WorkerName = "Relicade.Tests.WindowsGenerationLeaseTests.Coordination_worker";

    static GameInstallationOptions Options(Action<string>? boundary = null) => new()
    {
        SelectedExecutableRelativePath = "fixture.exe", PreservedDataPaths = ["saves"],
        TransactionBoundary = boundary
    };

    static Task Install(string live, string version, Action<string>? boundary = null) =>
        new PortableInstallationTransaction(live, Options(boundary)).InstallAsync(stage =>
        {
            File.WriteAllText(Path.Combine(stage, "fixture.exe"), "NONEXECUTABLE synthetic " + version);
            File.WriteAllText(Path.Combine(stage, "alternate.exe"), "NONEXECUTABLE alternate " + version);
            return Task.CompletedTask;
        }, version);

    static Process Start(string root, string mode, string[]? paths = null, string? point = null)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(psi.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(WindowsGenerationLeaseTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", WorkerName })
            psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_WINDOWS_LEASE_ROOT"] = root;
        psi.Environment["RELICADE_WINDOWS_LEASE_MODE"] = mode;
        psi.Environment["RELICADE_WINDOWS_LEASE_PATHS"] = JsonSerializer.Serialize(paths ?? []);
        psi.Environment["RELICADE_WINDOWS_LEASE_POINT"] = point;
        return Process.Start(psi)!;
    }

    static void Success(Process child, string root, string label)
    {
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.True(child.WaitForExit(40000), "Owned lease contender did not finish.");
            var text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.Combine(root, "coord"));
            File.WriteAllText(Path.Combine(root, "coord", label + "-" + child.Id + ".log"), text);
            Assert.True(child.ExitCode == 0, text);
        }
        finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(); } }
    }

    static async Task Ready(Process child, string root)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "ready")))
        {
            if (child.HasExited) throw new IOException("Owned worker exited: " + await child.StandardOutput.ReadToEndAsync() + await child.StandardError.ReadToEndAsync());
            if (elapsed.Elapsed > TimeSpan.FromSeconds(40)) throw new TimeoutException("Owned lease checkpoint did not arrive.");
            await Task.Delay(15);
        }
    }

    static void ContendAt(string root, string operation, string point)
    {
        var live = Path.Combine(root, "live");
        var paths = new[] { live, Path.Combine(operation, "candidate"), Path.Combine(operation, "previous") }
            .Where(Directory.Exists).Select(path => Path.Combine(path, LockName)).ToArray();
        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.True(File.Exists(path), "Occupied generation lacks its legacy mod identity: " + path));
        var existed = Directory.Exists(live);
        using var child = Start(root, "contend", paths);
        Success(child, root, point);
        Assert.Equal(existed, Directory.Exists(live)); // No contender recreates a missing live generation.
    }

    [Fact]
    public async Task Long_generation_paths_activate_and_recover_without_shortening_the_installation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises native Windows hard-link and identity calls beyond MAX_PATH.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-long-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, new string('a', 70), new string('b', 70), new string('c', 70), "long installation é", "live");
        Assert.True(Path.Combine(live, LockName).Length > 300);
        await Install(live, "old");
        Directory.CreateDirectory(Path.Combine(live, "saves")); File.WriteAllText(Path.Combine(live, "saves", "slot"), "long-path save");
        await Install(live, "new");
        await Assert.ThrowsAsync<IOException>(() => Install(live, "interrupted", point =>
        { if (point == "CandidateRenamed") throw new IOException("Synthetic reporting failure after both renames."); }));
        var manifest = new PortableInstallationTransaction(live).ReadCommitted()!;
        Assert.Equal("new", manifest.SelectedVersion);
        Assert.Equal("NONEXECUTABLE synthetic new", File.ReadAllText(Path.Combine(live, "fixture.exe")));
        Assert.Equal("long-path save", File.ReadAllText(Path.Combine(live, "saves", "slot")));
    }

    [Theory]
    [InlineData(@"C:\folder\file", @"\\?\C:\folder\file")]
    [InlineData(@"C:\folder\..\other\file", @"\\?\C:\other\file")]
    [InlineData(@"\\server\share\folder\file", @"\\?\UNC\server\share\folder\file")]
    [InlineData(@"\\?\C:\folder\file", @"\\?\C:\folder\file")]
    [InlineData(@"\\?\UNC\server\share\file", @"\\?\UNC\server\share\file")]
    public void Native_paths_preserve_normalized_drive_UNC_and_existing_extended_names(string path, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path namespace semantics.");
        Assert.Equal(expected, WindowsGenerationModLease.NativePath(path));
    }

    [Fact]
    public void Win32_device_prefix_is_not_reinterpreted_as_a_UNC_server()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows device namespace semantics.");
        Assert.Throws<IOException>(() => WindowsGenerationModLease.NativePath(@"\\.\C:\folder\file"));
    }

    [Fact]
    public async Task Both_commit_renames_preserve_independent_legacy_and_current_writer_exclusion()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows generation rename and file-sharing rules.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-lease-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); await Install(live, "old");
        Directory.CreateDirectory(Path.Combine(live, "saves")); File.WriteAllText(Path.Combine(live, "saves", "slot"), "current save");
        var tx = new PortableInstallationTransaction(live); var seen = new List<string>(); string? operation = null;
        await Install(live, "new", point =>
        {
            if (point is not ("BeforePreviousRename" or "PreviousRenamed" or "BeforeCandidateRename" or "CandidateRenamed")) return;
            operation ??= Directory.GetDirectories(tx.OperationsPath).Single(path => Directory.Exists(Path.Combine(path, "candidate")));
            ContendAt(root, operation, point); seen.Add(point);
        });
        Assert.Equal(new[] { "BeforePreviousRename", "PreviousRenamed", "BeforeCandidateRename", "CandidateRenamed" }, seen);
        Assert.Equal("new", tx.ReadCommitted()!.SelectedVersion);
        Assert.Equal("current save", File.ReadAllText(Path.Combine(live, "saves", "slot")));
    }

    [Theory]
    [InlineData("selection")][InlineData("rollback")][InlineData("confirmed-rollback")]
    public async Task Other_generation_mutations_preserve_current_saves_and_cross_process_exclusion(string mutation)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows executable selection and both rollback paths.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-mutation-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); await Install(live, "old"); await Install(live, "new");
        Directory.CreateDirectory(Path.Combine(live, "saves")); File.WriteAllText(Path.Combine(live, "saves", "slot"), "latest save");
        var plain = new PortableInstallationTransaction(live); var seen = new List<string>();
        string? operation = null;
        var tx = new PortableInstallationTransaction(live, Options(point =>
        {
            if (point is not ("BeforePreviousRename" or "PreviousRenamed" or "BeforeCandidateRename" or "CandidateRenamed")) return;
            operation ??= Directory.GetDirectories(plain.OperationsPath).Single(path => Directory.Exists(Path.Combine(path, "candidate")));
            ContendAt(root, operation, point); seen.Add(point);
        }));
        if (mutation == "selection") tx.SelectExecutable("alternate.exe");
        else if (mutation == "rollback") tx.Rollback();
        else
        {
            var preview = PortableRollbackCoreTests.Preview(tx);
            Assert.True(PortableRollbackCoreTests.Apply(tx, preview.PlanSha256).Completed);
        }
        Assert.Equal(4, seen.Count);
        Assert.Equal(mutation == "selection" ? "new" : "old", plain.ReadCommitted()!.SelectedVersion);
        Assert.Equal(mutation == "selection" ? "alternate.exe" : "fixture.exe", plain.ReadCommitted()!.SelectedExecutable);
        Assert.Equal("latest save", File.ReadAllText(Path.Combine(live, "saves", "slot")));
    }

    [Fact]
    public void Unmanaged_removal_can_move_the_whole_target_without_releasing_legacy_exclusion()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows unmanaged removal's held alias lease.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-removal-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); var removed = Path.Combine(root, "removed"); Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, LockName), ""); File.WriteAllText(Path.Combine(live, "keep"), "retained user bytes");
        using (new PortableInstallationTransaction(live).AcquireUnmanagedRemovalLease())
        {
            Directory.Move(live, removed);
            using var child = Start(root, "contend", [Path.Combine(removed, LockName)]);
            Success(child, root, "removal"); Assert.False(Directory.Exists(live));
            Assert.Equal("retained user bytes", File.ReadAllText(Path.Combine(removed, "keep")));
        }
    }

    [Theory]
    [InlineData("PreviousRenamed")]
    [InlineData("CandidateRenamed")]
    public async Task Killed_owner_recovery_preserves_exclusion_during_every_required_rename(string point)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows killed-owner alias leases and recovery renames.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-recover-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); await Install(live, "old");
        Directory.CreateDirectory(Path.Combine(live, "saves")); File.WriteAllText(Path.Combine(live, "saves", "slot"), "current save");
        using (var child = Start(root, "pause", point: point))
        {
            try { await Ready(child, root); child.Kill(true); await child.WaitForExitAsync(); Assert.NotEqual(0, child.ExitCode); }
            finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
        }
        var plain = new PortableInstallationTransaction(live);
        var operation = Directory.GetDirectories(plain.OperationsPath).Single(path =>
            File.Exists(Path.Combine(path, "journal.json")) && File.ReadAllText(Path.Combine(path, "journal.json")).Contains("\"new\"", StringComparison.Ordinal));
        var seen = new List<string>();
        var recovering = new PortableInstallationTransaction(live, Options(stage =>
        {
            if (stage is not ("BeforeRecoveryCandidateRename" or "RecoveryCandidateRenamed" or "BeforeRecoveryPreviousRename" or "RecoveryPreviousRenamed")) return;
            ContendAt(root, operation, stage); seen.Add(stage);
        }));
        recovering.Recover();
        Assert.Contains("BeforeRecoveryPreviousRename", seen); Assert.Contains("RecoveryPreviousRenamed", seen);
        if (point == "CandidateRenamed") Assert.Contains("RecoveryCandidateRenamed", seen);
        Assert.Equal("old", plain.ReadCommitted()!.SelectedVersion);
        Assert.Equal("current save", File.ReadAllText(Path.Combine(live, "saves", "slot")));
    }

    [Fact]
    public async Task Legacy_writer_first_refuses_before_new_payload_is_staged()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows legacy-first file-sharing refusal.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-legacy-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); await Install(live, "old");
        var before = File.ReadAllBytes(Path.Combine(live, "fixture.exe")); var staged = false;
        using var child = Start(root, "legacy");
        try
        {
            await Ready(child, root);
            await Assert.ThrowsAnyAsync<IOException>(() => Install(live, "new", _ => staged = true));
            Assert.False(staged); Assert.Equal(before, File.ReadAllBytes(Path.Combine(live, "fixture.exe")));
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }

    [Fact]
    public void Nonempty_legacy_lock_refuses_without_changing_its_bytes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows lock-file identity validation.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-nonempty-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); Directory.CreateDirectory(live);
        var path = Path.Combine(live, LockName); File.WriteAllText(path, "retained unexpected evidence");
        Assert.ThrowsAny<IOException>(() => { using var held = WindowsGenerationModLease.Open(path, FileMode.Open, root, [live]); });
        Assert.Equal("retained unexpected evidence", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("empty-file")][InlineData("nonempty-file")][InlineData("directory")]
    public void Existing_alias_with_wrong_identity_is_retained_and_refused(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows hard-link identity collision refusal.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-collision-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); Directory.CreateDirectory(live);
        var path = Path.Combine(live, LockName); File.WriteAllText(path, "");
        string alias;
        using (var held = WindowsGenerationModLease.Open(path, FileMode.Open, root, [live])) alias = held.Name;
        var retained = alias + ".test-retained-" + Guid.NewGuid().ToString("N");
        File.Move(alias, retained);
        var directory = kind == "directory";
        var contents = kind == "empty-file" ? "" : "collision evidence";
        try
        {
            if (directory) Directory.CreateDirectory(alias); else File.WriteAllText(alias, contents);
            Assert.ThrowsAny<IOException>(() => { using var held = WindowsGenerationModLease.Open(path, FileMode.Open, root, [live]); });
            Assert.True(File.Exists(retained)); Assert.Equal("", File.ReadAllText(path));
            if (directory) Assert.True(Directory.Exists(alias)); else Assert.Equal(contents, File.ReadAllText(alias));
        }
        finally
        {
            // Remove only this test's synthetic collision; restore its original
            // retained alias so the test does not poison later filesystem IDs.
            if (directory) Directory.Delete(alias); else File.Delete(alias);
            File.Move(retained, alias);
        }
    }

    [Fact]
    public void No_coordination_location_outside_protected_trees_refuses_without_payload_changes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exercises Windows safe coordination placement.");
        var root = Path.Combine(Path.GetTempPath(), "windows-generation-placement-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live"); Directory.CreateDirectory(live);
        var path = Path.Combine(live, LockName); File.WriteAllText(path, "");
        Assert.ThrowsAny<IOException>(() => { using var held = WindowsGenerationModLease.Open(path, FileMode.Open, root, [Path.GetTempPath()]); });
        Assert.Equal(new[] { path }, Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.Equal("", File.ReadAllText(path));
    }

    [Fact]
    public async Task Coordination_worker()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only independent-process helper.");
        var root = Environment.GetEnvironmentVariable("RELICADE_WINDOWS_LEASE_ROOT");
        Assert.SkipWhen(root == null, "Runs only as an owned independent-process helper; standalone invocation is not acceptance.");
        var live = Path.Combine(root!, "live");
        switch (Environment.GetEnvironmentVariable("RELICADE_WINDOWS_LEASE_MODE"))
        {
            case "contend":
                var paths = JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("RELICADE_WINDOWS_LEASE_PATHS")!)!;
                foreach (var path in paths)
                {
                    var error = Assert.ThrowsAny<IOException>(() => { using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
                    Assert.Contains(error.HResult & 0xffff, new[] { 32, 33 });
                }
                var writer = Assert.ThrowsAny<IOException>(() => { using var held = new PortableInstallationTransaction(live).AcquireLease(); });
                Assert.Contains(writer.HResult & 0xffff, new[] { 32, 33 });
                var currentMod = Assert.ThrowsAny<IOException>(() => { using var held = ModInstallTransaction.Acquire(live); });
                Assert.Contains(currentMod.HResult & 0xffff, new[] { 32, 33 });
                break;
            case "legacy":
                using (new FileStream(Path.Combine(live, LockName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                { File.WriteAllText(Path.Combine(root!, "ready"), "legacy held"); Thread.Sleep(Timeout.Infinite); }
                throw new IOException("The paused worker must be killed by its owner.");
            case "pause":
                var point = Environment.GetEnvironmentVariable("RELICADE_WINDOWS_LEASE_POINT");
                await Install(live, "new", stage =>
                { if (stage == point) { File.WriteAllText(Path.Combine(root!, "ready"), stage); Thread.Sleep(Timeout.Infinite); } });
                throw new IOException("The paused worker must not silently complete.");
            default: throw new InvalidOperationException("Unknown owned worker mode.");
        }
    }
}
