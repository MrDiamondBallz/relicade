using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class PortableWriterCoordinationTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-coordination-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "live");
    public PortableWriterCoordinationTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    static async Task Install(string root, string version, Action<string>? boundary = null, bool nested = false)
    {
        var zipPath = Path.Combine(root, "fixture.zip");
        using (var f = File.Create(zipPath))
        using (var zip = new ZipArchive(f, ZipArchiveMode.Create))
            foreach (var name in nested ? new[] { "tool-a.exe", "tool-b.exe", "bin/game.exe" } : new[] { "game.exe", "data.txt" })
            { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write("NONEXECUTABLE synthetic " + version); }
        await GameInstallationService.InstallOrUpdateGameAsync(zipPath, Path.Combine(root, "live"), "fixture.zip", version,
            new() { TransactionBoundary = boundary, PreservedDataPaths = ["saves"], SelectedExecutableRelativePath = nested ? "bin/game.exe" : null });
    }
    async Task Initial()
    {
        await Install(root, "old"); Directory.CreateDirectory(Path.Combine(Live, "saves"));
        File.WriteAllText(Path.Combine(Live, "saves", "slot.dat"), "current save");
        File.WriteAllText(Path.Combine(root, "sibling.txt"), "sibling");
    }
    static Dictionary<string, string> Snapshot(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetFileName(p) != ".relicade-mod-writer.lock" && Path.GetFileName(p) != "writer.lock")
        .ToDictionary(p => Path.GetRelativePath(path, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    static Process Child(string root, string mode)
    {
        var exe = Environment.ProcessPath!; var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(PortableWriterCoordinationTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.PortableWriterCoordinationTests.Process_control_entry" }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_COORDINATION_ROOT"] = root; psi.Environment["RELICADE_COORDINATION_MODE"] = mode;
        return Process.Start(psi)!;
    }
    static async Task Success(Process child)
    {
        var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await child.WaitForExitAsync(deadline.Token);
        Assert.True(child.ExitCode == 0, await output + await error);
        Console.WriteLine(await output);
    }
    async Task Barrier(Process child)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "barrier")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(20);
        Assert.False(child.HasExited); Assert.True(File.Exists(Path.Combine(root, "barrier")));
    }
    [Fact]
    public async Task Process_control_entry()
    {
        var target = Environment.GetEnvironmentVariable("RELICADE_COORDINATION_ROOT"); if (target == null) return;
        var mode = Environment.GetEnvironmentVariable("RELICADE_COORDINATION_MODE")!; var live = Path.Combine(target, "live");
        void Pause() { File.WriteAllText(Path.Combine(target, "barrier"), mode); using var wait = new ManualResetEventSlim(); wait.Wait(); }
        if (mode.StartsWith("pause:")) await Install(target, "new", p => { if (p == mode[6..]) Pause(); });
        else if (mode == "hold-mod") { using var lease = ModInstallTransaction.Acquire(live); Pause(); }
        else if (mode == "refuse-mod")
        {
            var before = Snapshot(target); var existed = Directory.Exists(live);
            Assert.ThrowsAny<IOException>(() => { using var lease = ModInstallTransaction.Acquire(live); });
            Assert.ThrowsAny<IOException>(() => new InstalledModsStore().Save(live, new()));
            Assert.ThrowsAny<IOException>(() => new InstalledModsStore().Load(live));
            var service = new ModInstallService(new ModProviderRegistry(Array.Empty<IModProvider>()));
            var package = new ModPackage { ProviderId = "synthetic", SourceKey = "synthetic", Owner = "synthetic", Name = "one", Id = "one", FullName = "synthetic/one" };
            Assert.ThrowsAny<IOException>(() => service.Uninstall(live, "mods", "synthetic", "one"));
            Assert.ThrowsAny<IOException>(() => service.UninstallMatching(live, "mods", package));
            Assert.ThrowsAny<IOException>(() => service.UninstallMatchingFile(live, "mods", package, "one"));
            Assert.ThrowsAny<IOException>(() => service.UninstallMatchingRecord(live, "mods", package, new()));
            Assert.Equal(existed, Directory.Exists(live)); Assert.Equal(before.OrderBy(e => e.Key), Snapshot(target).OrderBy(e => e.Key));
            Console.WriteLine("Independent G05 acquisition/save/load refused without changing generation or evidence.");
        }
        else if (mode == "refuse-game")
        {
            var before = Snapshot(live);
            await Assert.ThrowsAsync<IOException>(() => Install(target, "new"));
            Assert.Equal(before.OrderBy(e => e.Key), Snapshot(live).OrderBy(e => e.Key));
        }
        else if (mode == "reopen")
        {
            Assert.Equal("old", new PortableInstallationTransaction(live).ReadCommitted()!.SelectedVersion);
            Assert.Equal("NONEXECUTABLE synthetic old", File.ReadAllText(Path.Combine(live, "game.exe")));
            Assert.Equal("current save", File.ReadAllText(Path.Combine(live, "saves", "slot.dat")));
            Assert.Equal("sibling", File.ReadAllText(Path.Combine(target, "sibling.txt")));
            Assert.False(File.Exists(Path.Combine(live, InstalledModsStore.SidecarFileName)));
            Console.WriteLine("Independent reopen: complete old generation + exact current save/sibling; no mod sidecar.");
        }
        else throw new InvalidOperationException(mode);
    }
    [Theory]
    [InlineData("PreviousRenamed")][InlineData("CandidateRenamed")][InlineData("BeforeCommitted")]
    public async Task Independent_mod_process_cannot_recreate_or_mutate_game_during_activation_and_kill(string boundary)
    {
        await Initial(); using var game = Child(root, "pause:" + boundary);
        try
        {
            await Barrier(game); using (var mod = Child(root, "refuse-mod")) await Success(mod);
            game.Kill(true); await game.WaitForExitAsync();
            // A killed writer leaves an unresolved journal. Refusal must precede root creation,
            // even now that the OS lock was released; a separate reopen can still recover.
            using (var mod = Child(root, "refuse-mod")) await Success(mod);
            using var reopen = Child(root, "reopen"); await Success(reopen);
        }
        finally { if (!game.HasExited) { game.Kill(true); await game.WaitForExitAsync(); } }
    }
    [Theory]
    [InlineData("PreviousRenamed")][InlineData("CandidateRenamed")]
    public async Task Independent_mod_process_refuses_retained_ordinary_failure_then_separate_reopen_recovers(string boundary)
    {
        await Initial();
        await Assert.ThrowsAsync<IOException>(() => Install(root, "new", p => { if (p == boundary || p == "Recovering") throw new IOException("retained ordinary failure"); }));
        using (var mod = Child(root, "refuse-mod")) await Success(mod);
        using var reopen = Child(root, "reopen"); await Success(reopen);
    }
    [Fact]
    public async Task Independent_game_process_is_excluded_by_mod_lease_and_kill_releases_stable_lock()
    {
        await Initial(); using var mod = Child(root, "hold-mod");
        try
        {
            await Barrier(mod); using (var game = Child(root, "refuse-game")) await Success(game);
            mod.Kill(true); await mod.WaitForExitAsync();
            await Install(root, "new"); Assert.Equal("new", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        }
        finally { if (!mod.HasExited) { mod.Kill(true); await mod.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task Same_process_cooperative_mod_is_refused_in_gap_without_breaking_game_commit()
    {
        await Initial(); await Install(root, "new", p =>
        {
            if (p != "PreviousRenamed") return;
            Assert.ThrowsAny<IOException>(() => { using var mod = ModInstallTransaction.Acquire(Live); });
            Assert.False(Directory.Exists(Live));
        });
        Assert.Equal("new", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
    }
    [Theory]
    [InlineData("corrupt")][InlineData("missing")][InlineData("permission")]
    public async Task Malformed_missing_or_unreadable_recovery_metadata_refuses_mod_without_changing_evidence(string fault)
    {
        await Initial();
        await Assert.ThrowsAsync<IOException>(() => Install(root, "new", p => { if (p is "CandidateRenamed" or "Recovering") throw new IOException("retained failure"); }));
        var tx = new PortableInstallationTransaction(Live);
        var journal = Directory.GetFiles(tx.OperationsPath, "journal.json", SearchOption.AllDirectories).Single(p => File.ReadAllText(p).Contains("\"Applying\""));
        if (fault == "corrupt") File.WriteAllText(journal, "{broken");
        if (fault == "missing") File.Delete(journal);
        var before = Snapshot(root); UnixFileMode mode = 0;
        try
        {
            if (fault == "permission" && !OperatingSystem.IsWindows()) { mode = File.GetUnixFileMode(journal); File.SetUnixFileMode(journal, UnixFileMode.None); }
            Assert.NotNull(Record.Exception(() => new InstalledModsStore().Save(Live, new())));
            Assert.False(File.Exists(Path.Combine(Live, InstalledModsStore.SidecarFileName)));
        }
        finally { if (fault == "permission" && !OperatingSystem.IsWindows()) File.SetUnixFileMode(journal, mode); }
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(root).OrderBy(e => e.Key));
    }
    [Theory]
    [InlineData("payload")][InlineData("manifest")][InlineData("missing-target")][InlineData("version")]
    public async Task Unknown_committed_generation_blocks_mod_and_launch_without_substituting_helpers(string fault)
    {
        await Install(root, "old", nested: true);
        var selected = Path.Combine(Live, "bin", "game.exe");
        if (fault == "payload") File.WriteAllText(selected, "unknown changed bytes");
        if (fault == "missing-target") File.Delete(selected);
        if (fault == "manifest") File.WriteAllText(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName), "{invalid");
        if (fault == "version") File.WriteAllText(Path.Combine(Live, "version.txt"), "unknown");
        var before = Snapshot(root);
        Assert.NotNull(Record.Exception(() => new InstalledModsStore().Save(Live, new())));
        var game = new GameInfo { Name = "synthetic", FolderName = "live", SelectedExecutable = selected };
        Assert.False(await GameLaunchService.LaunchAsync(game, root));
        Assert.DoesNotContain(game.SelectedExecutable, new[] { Path.Combine(Live, "tool-a.exe"), Path.Combine(Live, "tool-b.exe") });
        Assert.False(File.Exists(Path.Combine(Live, "LastPlayed.txt")));
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(root).OrderBy(e => e.Key));
    }
    [Fact]
    public async Task Interrupted_untracked_launch_receipt_blocks_mod_after_OS_lease_is_released()
    {
        await Initial(); var lifetime = PortableInstallationLifetime.Begin(Live); lifetime.PreserveUncertain();
        var before = Snapshot(root);
        var ex = Assert.Throws<IOException>(() => new InstalledModsStore().Save(Live, new()));
        Assert.Contains("interrupted", ex.Message);
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(root).OrderBy(e => e.Key));
    }
    [Fact]
    public async Task Committed_nested_target_is_kept_when_root_helpers_exist_without_running_any_payload()
    {
        if (!OperatingSystem.IsLinux()) return; // Native launch acceptance is separate.
        await Install(root, "old", nested: true);
        var game = new GameInfo { Name = "synthetic", FolderName = "live" };
        Assert.False(await GameLaunchService.LaunchAsync(game, root)); // No runner exists in isolated HOME.
        Assert.Equal(Path.Combine(Live, "bin", "game.exe"), game.SelectedExecutable);
        Assert.Contains(game.SelectedExecutable, game.AvailableExecutables);
        Assert.False(File.Exists(Path.Combine(Live, "LastPlayed.txt")));
    }
}
