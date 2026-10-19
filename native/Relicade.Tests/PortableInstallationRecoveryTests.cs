using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Relicade.Core.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class PortableInstallationRecoveryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-recovery-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "live");
    string Source => Path.Combine(root, "candidate.zip");
    GameInstallationOptions Options(Action<string>? boundary = null) => new()
    {
        PreservedDataPaths = ["saves", "config", "owned-data", "LastPlayed.txt"],
        ReleaseIdentity = "synthetic-release", AssetIdentity = "synthetic-asset", TransactionBoundary = boundary,
        InstalledBuildText = "fixture.zip\n" + new string('a', 64) + "\n"
    };
    public PortableInstallationRecoveryTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    void Zip(string version)
    {
        using var stream = File.Create(Source); using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, bytes) in new[] { ("fixture.exe", version), ("payload/data.txt", version), (version == "old" ? "obsolete.txt" : "added.txt", version) })
        { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(bytes); }
    }
    async Task Initial()
    {
        Zip("old"); await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "old", Options());
        Directory.CreateDirectory(Path.Combine(Live, "saves")); File.WriteAllText(Path.Combine(Live, "saves", "slot.dat"), "current-save");
        Directory.CreateDirectory(Path.Combine(Live, "owned-data")); File.WriteAllText(Path.Combine(Live, "owned-data", "original.bin"), "synthetic original");
        File.WriteAllText(Path.Combine(root, "sibling.txt"), "sibling");
        var hashes = new[] { "live/saves/slot.dat", "live/owned-data/original.bin", "sibling.txt" }.ToDictionary(p => p, p => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, p)))));
        File.WriteAllText(Path.Combine(root, "expected-data.json"), JsonSerializer.Serialize(hashes)); Zip("new");
    }
    string CurrentJournal => Path.Combine(new PortableInstallationTransaction(Live).OperationsPath,
        JsonNode.Parse(File.ReadAllText(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)))!["operationId"]!.GetValue<string>(), "journal.json");
    string ActiveJournal => Directory.GetFiles(new PortableInstallationTransaction(Live).OperationsPath, "journal.json", SearchOption.AllDirectories)
        .Single(p => JsonNode.Parse(File.ReadAllText(p))!["phase"]!.GetValue<string>() != "Committed" && JsonNode.Parse(File.ReadAllText(p))!["resolution"] == null);
    void Verify(string expected)
    {
        var m = new PortableInstallationTransaction(Live).ReadCommitted(); Assert.NotNull(m);
        Assert.Equal(expected, m.SelectedVersion); Assert.Equal(expected, File.ReadAllText(Path.Combine(Live, "version.txt")));
        Assert.Equal(expected, File.ReadAllText(Path.Combine(Live, "fixture.exe"))); Assert.Equal(expected, File.ReadAllText(Path.Combine(Live, "payload", "data.txt")));
        Assert.Equal(expected == "old", File.Exists(Path.Combine(Live, "obsolete.txt"))); Assert.Equal(expected == "new", File.Exists(Path.Combine(Live, "added.txt")));
        Assert.Equal("current-save", File.ReadAllText(Path.Combine(Live, "saves", "slot.dat")));
        Assert.Equal("synthetic original", File.ReadAllText(Path.Combine(Live, "owned-data", "original.bin"))); Assert.Equal("sibling", File.ReadAllText(Path.Combine(root, "sibling.txt")));
    }
    [Fact]
    public async Task Real_install_update_and_offline_rollback_preserve_current_data()
    {
        await Initial();
        await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options()); Verify("new");
        File.WriteAllText(Path.Combine(Live, "saves", "slot.dat"), "newer-save"); File.Delete(Source);
        new PortableInstallationTransaction(Live).Rollback();
        Assert.Equal("old", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        Assert.Equal("newer-save", File.ReadAllText(Path.Combine(Live, "saves", "slot.dat")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Live, "fixture.exe")));
        Assert.Equal("sibling", File.ReadAllText(Path.Combine(root, "sibling.txt")));
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(CurrentJournal)!, "previous")));
    }
    [Theory]
    [InlineData("Extracted")][InlineData("Staged")][InlineData("Prepared")][InlineData("Applying")]
    [InlineData("BeforeVersionMetadata")][InlineData("VersionMetadata")][InlineData("BeforeBuildMetadata")][InlineData("BuildMetadata")]
    [InlineData("BeforeCandidateManifest")][InlineData("CandidateManifest")][InlineData("BackedUp")][InlineData("BeforePrepared")][InlineData("BeforeApplying")]
    [InlineData("BeforePreviousRename")][InlineData("PreviousRenamed")][InlineData("BeforeCandidateRename")]
    [InlineData("CandidateRenamed")][InlineData("BeforeCommitted")]
    public async Task Every_precommit_fault_recovers_exact_old_generation(string point)
    {
        await Initial(); var manifest = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p == point) throw new IOException("Synthetic boundary failure"); })));
        Verify("old"); Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
    }
    [Fact]
    public async Task Postcommit_error_reports_real_committed_generation()
    {
        await Initial(); var ex = await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p == "Committed") throw new IOException("reporting failed"); })));
        Assert.Contains("committed successfully", ex.Message); Verify("new");
    }
    [Theory]
    [InlineData("Extracted", "old")][InlineData("Staged", "old")][InlineData("Prepared", "old")]
    [InlineData("Applying", "new")][InlineData("PreviousRenamed", "new")][InlineData("CandidateRenamed", "new")]
    public async Task Cancellation_is_honest_before_and_during_noninterruptible_commit(string point, string expected)
    {
        await Initial(); using var cts = new CancellationTokenSource();
        var task = GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p == point) cts.Cancel(); }), cts.Token);
        if (expected == "old") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); else await task;
        Verify(expected);
    }
    [Fact]
    public async Task Failed_recovery_keeps_both_generations_and_retry_recovers()
    {
        await Initial();
        var ex = await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p is "CandidateRenamed" or "Recovering") throw new IOException("Synthetic rollback denial"); })));
        Assert.Contains("recovery is needed", ex.Message); var journal = ActiveJournal;
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(journal)!, "previous")));
        Verify("old");
    }
    [Theory]
    [InlineData("external")][InlineData("corrupt")][InlineData("path")][InlineData("schema")][InlineData("case")][InlineData("root")][InlineData("missing")][InlineData("backup")]
    public async Task Invalid_recovery_records_or_unknown_changes_are_retained_fail_closed(string fault)
    {
        await Initial();
        await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p is "CandidateRenamed" or "Recovering") throw new IOException("retain evidence"); })));
        var journal = ActiveJournal; var json = JsonNode.Parse(File.ReadAllText(journal))!;
        switch (fault)
        {
            case "external": File.WriteAllText(Path.Combine(Live, "fixture.exe"), "external unknown bytes"); break;
            case "corrupt": File.WriteAllText(journal, "{broken"); break;
            case "path": json["previousPath"] = "../outside"; break;
            case "schema": json["schemaVersion"] = 2; break;
            case "case": json["candidatePath"] = "Candidate"; break;
            case "root": json["installKey"] = new string('0', 64); break;
            case "missing": json["candidateFiles"] = new JsonArray(); break;
            case "backup": File.WriteAllText(Path.Combine(Path.GetDirectoryName(journal)!, "previous", "fixture.exe"), "unknown backup bytes"); break;
        }
        if (fault is not ("external" or "corrupt" or "backup")) File.WriteAllText(journal, json.ToJsonString());
        var before = File.ReadAllBytes(journal); var payload = File.ReadAllBytes(Path.Combine(Live, "fixture.exe"));
        var error = Record.Exception(() => new PortableInstallationTransaction(Live).ReadCommitted());
        if (fault is "external" or "backup") Assert.IsType<IOException>(error); else Assert.IsType<InvalidDataException>(error);
        Assert.Equal(before, File.ReadAllBytes(journal)); Assert.Equal(payload, File.ReadAllBytes(Path.Combine(Live, "fixture.exe")));
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(journal)!, "previous")));
    }
    [Fact]
    public async Task Real_permission_denial_during_activation_is_recoverable_without_DAC_bypass()
    {
        if (OperatingSystem.IsWindows()) return; await Initial(); var mode = File.GetUnixFileMode(root);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p == "BeforePreviousRename") File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute); })));
            Assert.Equal("old", File.ReadAllText(Path.Combine(Live, "fixture.exe")));
        }
        finally { File.SetUnixFileMode(root, mode); }
        Verify("old");
    }
    [Theory]
    [InlineData("unknown-file")][InlineData("payload-change")][InlineData("data-change")][InlineData("space")][InlineData("link")][InlineData("mod-state")]
    public async Task Unsafe_updates_preserve_old_payload_and_external_state(string fault)
    {
        await Initial();
        if (fault == "unknown-file") File.WriteAllText(Path.Combine(Live, "unknown.txt"), "external");
        if (fault == "payload-change") File.WriteAllText(Path.Combine(Live, "fixture.exe"), "external");
        if (fault == "link") Directory.CreateSymbolicLink(Path.Combine(Live, "linked"), root);
        // External/noncooperative software can still leave incompatible mod state.
        // Source-owned G05 Save must refuse this managed generation before mutation.
        if (fault == "mod-state") File.WriteAllText(InstalledModsStore.GetSidecarPath(Live), "{\"mods\":[]}");
        var options = fault == "space" ? new GameInstallationOptions { MaximumExpandedBytes = 1 } : Options(p => { if (p == "Staged" && fault == "data-change") File.WriteAllText(Path.Combine(Live, "saves", "slot.dat"), "external"); });
        var error = await Record.ExceptionAsync(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", options));
        if (fault == "link") Assert.IsType<InvalidDataException>(error); else Assert.IsType<IOException>(error);
        Assert.Equal(fault == "payload-change" ? "external" : "old", File.ReadAllText(Path.Combine(Live, "fixture.exe")));
        Assert.Equal(fault == "data-change" ? "external" : "current-save", File.ReadAllText(Path.Combine(Live, "saves", "slot.dat")));
        if (fault == "mod-state") Assert.Equal("{\"mods\":[]}", File.ReadAllText(InstalledModsStore.GetSidecarPath(Live)));
    }
    static Process Child(string root, string mode)
    {
        var exe = Environment.ProcessPath!; var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(PortableInstallationRecoveryTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.PortableInstallationRecoveryTests.Process_control_entry" }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_PORTABLE_FIXTURE_ROOT"] = root; psi.Environment["RELICADE_PORTABLE_FIXTURE_MODE"] = mode; return Process.Start(psi)!;
    }
    [Fact]
    public async Task Process_control_entry()
    {
        var target = Environment.GetEnvironmentVariable("RELICADE_PORTABLE_FIXTURE_ROOT"); if (target == null) return;
        var mode = Environment.GetEnvironmentVariable("RELICADE_PORTABLE_FIXTURE_MODE")!; var live = Path.Combine(target, "live");
        if (mode.StartsWith("reopen:"))
        {
            var m = new PortableInstallationTransaction(live).ReadCommitted()!; var expected = mode[7..];
            Assert.Equal(expected, m.SelectedVersion); Assert.Equal(expected, File.ReadAllText(Path.Combine(live, "fixture.exe")));
            Assert.Equal(expected, File.ReadAllText(Path.Combine(live, "payload", "data.txt"))); Assert.Equal(expected, File.ReadAllText(Path.Combine(live, "version.txt")));
            Assert.Equal("current-save", File.ReadAllText(Path.Combine(live, "saves", "slot.dat"))); Assert.Equal("synthetic original", File.ReadAllText(Path.Combine(live, "owned-data", "original.bin")));
            Assert.Equal("sibling", File.ReadAllText(Path.Combine(target, "sibling.txt")));
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(target, "expected-data.json")))!;
            foreach (var e in hashes) Assert.Equal(e.Value, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(target, e.Key)))));
            Console.WriteLine("Independent reopen complete: " + expected + "; exact save/owned-data/sibling SHA256 bindings checked=" + hashes.Count); return;
        }
        var point = mode.StartsWith("rollback:") ? mode[9..] : mode.StartsWith("recover:") ? mode[8..] : mode;
        var options = Options(p =>
        {
            if (p != point) return; File.WriteAllText(Path.Combine(target, "reached.txt"), p); using var barrier = new ManualResetEventSlim(); barrier.Wait();
        });
        if (mode.StartsWith("recover:")) new PortableInstallationTransaction(live, options).ReadCommitted();
        else if (mode.StartsWith("rollback:")) new PortableInstallationTransaction(live, options).Rollback();
        else await GameInstallationService.InstallOrUpdateGameAsync(Path.Combine(target, "candidate.zip"), live, "payload.zip", "new", options);
    }
    [Theory]
    [InlineData("Extracted", "old")][InlineData("Staged", "old")][InlineData("Prepared", "old")][InlineData("Applying", "old")]
    [InlineData("BeforeVersionMetadata", "old")][InlineData("VersionMetadata", "old")][InlineData("BeforeBuildMetadata", "old")][InlineData("BuildMetadata", "old")]
    [InlineData("BeforeCandidateManifest", "old")][InlineData("CandidateManifest", "old")][InlineData("BackedUp", "old")][InlineData("BeforePrepared", "old")][InlineData("BeforeApplying", "old")]
    [InlineData("BeforePreviousRename", "old")][InlineData("PreviousRenamed", "old")][InlineData("BeforeCandidateRename", "old")]
    [InlineData("CandidateRenamed", "old")][InlineData("BeforeCommitted", "old")][InlineData("Committed", "new")]
    public async Task Kill_and_independent_reopen_at_every_activation_boundary(string point, string expected)
    {
        await Initial(); var before = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        using var child = Child(root, point);
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "reached.txt")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(20);
            Assert.False(child.HasExited, "Writer exited before the deterministic barrier"); Assert.True(File.Exists(Path.Combine(root, "reached.txt")));
            Assert.Throws<IOException>(() => new PortableInstallationTransaction(Live).ReadCommitted());
            if (Directory.Exists(Live) && point != "PreviousRenamed" && point != "BeforeCandidateRename")
                Assert.Throws<IOException>(() => new InstalledModsStore().Load(Live));
            child.Kill(true); await child.WaitForExitAsync();
            using var reopen = Child(root, "reopen:" + expected);
            var output = await reopen.StandardOutput.ReadToEndAsync(); var error = await reopen.StandardError.ReadToEndAsync(); await reopen.WaitForExitAsync();
            Assert.True(reopen.ExitCode == 0, output + error); Verify(expected);
            if (expected == "old") Assert.Equal(before, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
            Console.WriteLine($"writer killed at {point}, exit={child.ExitCode}; separate reopened process={reopen.ExitCode}; complete {expected}; user/sibling hashes unchanged");
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    public static IEnumerable<object[]> RollbackBoundaries => new[] { "Staged", "BeforeVersionMetadata", "VersionMetadata", "BeforeBuildMetadata", "BuildMetadata", "BeforeCandidateManifest", "CandidateManifest", "BackedUp", "BeforePrepared", "Prepared", "BeforeApplying", "Applying", "BeforePreviousRename", "PreviousRenamed", "BeforeCandidateRename", "CandidateRenamed", "BeforeCommitted", "Committed" }.Select(p => new object[] { p });
    [Theory]
    [InlineData("BeforeRecoveryCandidateRename")][InlineData("RecoveryCandidateRenamed")][InlineData("BeforeRecoveryPreviousRename")]
    [InlineData("RecoveryPreviousRenamed")][InlineData("BeforeRecoveryResolved")][InlineData("RecoveryResolved")]
    public async Task Recovery_itself_can_be_killed_at_each_rename_and_resolution_boundary(string point)
    {
        await Initial();
        await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options(p => { if (p is "CandidateRenamed" or "Recovering") throw new IOException("Retain incomplete activation"); })));
        using var child = Child(root, "recover:" + point);
        try
        {
            var timer = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(root, "reached.txt")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(20);
            Assert.False(child.HasExited); Assert.True(File.Exists(Path.Combine(root, "reached.txt")));
            Assert.Throws<IOException>(() => new PortableInstallationTransaction(Live).ReadCommitted());
            if (Directory.Exists(Live)) Assert.Throws<IOException>(() => new Relicade.Services.Mods.InstalledModsStore().Load(Live));
            child.Kill(true); await child.WaitForExitAsync();
            using var reopen = Child(root, "reopen:old"); var output = await reopen.StandardOutput.ReadToEndAsync(); var error = await reopen.StandardError.ReadToEndAsync(); await reopen.WaitForExitAsync();
            Assert.True(reopen.ExitCode == 0, output + error); Verify("old");
            Console.WriteLine($"Recovery killed at {point}; independent retry reopened exact old generation, user/sibling SHA256 unchanged");
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    [Theory]
    [MemberData(nameof(RollbackBoundaries))]
    public async Task Offline_rollback_kill_and_independent_reopen_retains_complete_generation(string point)
    {
        await Initial(); await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "payload.zip", "new", Options()); File.Delete(Source);
        var expected = point == "Committed" ? "old" : "new";
        using var child = Child(root, "rollback:" + point);
        try
        {
            var timer = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(root, "reached.txt")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(20);
            Assert.False(child.HasExited); Assert.True(File.Exists(Path.Combine(root, "reached.txt")));
            child.Kill(true); await child.WaitForExitAsync();
            using var reopen = Child(root, "reopen:" + expected);
            var output = await reopen.StandardOutput.ReadToEndAsync(); var error = await reopen.StandardError.ReadToEndAsync(); await reopen.WaitForExitAsync();
            Assert.True(reopen.ExitCode == 0, output + error); Verify(expected); Assert.False(File.Exists(Source));
            Console.WriteLine($"Offline rollback killed at {point}; separate reopen={reopen.ExitCode}, coherent {expected}; current save/owned-data/sibling bytes unchanged");
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
}
