using System.Diagnostics;
using System.Text.RegularExpressions;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupAncestorAuthorityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)][InlineData(true, false)]
    [InlineData(false, true)][InlineData(true, true)]
    public async Task Located_preview_and_confirmed_apply_share_released_common_ancestor_writer_and_mod(bool injected, bool approvedFirst)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        var tx = await Located(s);
        string? hash = null;
        if (approvedFirst) hash = Hash((await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey])).Text);
        var ancestor = new PortableInstallationTransaction(s.Root);
        using (ancestor.AcquireLease()) { }
        var stableWriter = Path.Combine(ancestor.OperationsPath, "writer.lock");
        var created = File.GetCreationTimeUtc(stableWriter);
        var mod = Path.Combine(s.Root, ".relicade-mod-writer.lock"); File.WriteAllBytes(mod, []);
        var plan = tx.PreviewRedundantBackups(); Assert.Single(plan.Files);
        // Preview may establish the documented alternate stable writer identity.
        // Establish it before the full snapshot so equality still covers ALL bytes.
        using (s.Tx.AcquireLease()) { }
        var before = s.Snapshot();
        var result = await s.Execute(injected, approvedFirst
            ? ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash!]
            : ["--preview-portable-backups", s.Game.InstanceKey]);
        output.WriteLine($"injected={injected}; apply={approvedFirst}; code={result.Code}; {result.Text}");
        Assert.Equal(0, result.Code);
        if (!approvedFirst)
        {
            Assert.Equal(before, s.Snapshot());
            var apply = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", Hash(result.Text)]);
            Assert.Equal(0, apply.Code);
        }
        var removed = plan.Files.Select(f => Path.GetRelativePath(s.Root, Path.Combine(tx.OperationsPath, f.OperationId, f.BackupRelativePath))).ToHashSet();
        var after = s.Snapshot();
        Assert.Equal(removed.Order(), before.Keys.Except(after.Keys).Order());
        foreach (var row in before.Where(r => !removed.Contains(r.Key))) Assert.Equal(row.Value, after[row.Key]);
        Assert.Single(after.Keys.Except(before.Keys));
        Assert.Single(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
        Assert.Equal(created, File.GetCreationTimeUtc(stableWriter)); Assert.Empty(File.ReadAllBytes(stableWriter));
        Assert.Empty(File.ReadAllBytes(mod)); Assert.False(Directory.Exists(s.Live));
        Assert.Equal("located-3", tx.ReadCommitted()!.SelectedVersion); Assert.Empty(tx.PreviewRedundantBackups().Files);
        using (ancestor.AcquireLease()) { }
        Assert.Equal(0, s.Network.Calls);
    }

    [Theory]
    [InlineData(false, "ancestor-writer")][InlineData(true, "ancestor-writer")]
    [InlineData(false, "ancestor-mod")][InlineData(true, "ancestor-mod")]
    [InlineData(false, "default-writer")][InlineData(true, "default-writer")]
    [InlineData(false, "selected-writer")][InlineData(true, "selected-writer")]
    [InlineData(false, "selected-mod")][InlineData(true, "selected-mod")]
    public async Task Real_other_process_authority_blocks_preview_and_apply_then_release_permits_same_plan(bool injected, string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); var tx = await Located(s);
        var ancestor = new PortableInstallationTransaction(s.Root); using (ancestor.AcquireLease()) { }
        File.WriteAllBytes(Path.Combine(s.Root, ".relicade-mod-writer.lock"), []);
        File.WriteAllBytes(Path.Combine(s.Game.InstallPath!, ".relicade-mod-writer.lock"), []);
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.Equal(0, preview.Code); var hash = Hash(preview.Text); var before = s.Snapshot();
        var lockPath = kind switch
        {
            "ancestor-writer" => Path.Combine(ancestor.OperationsPath, "writer.lock"),
            "ancestor-mod" => Path.Combine(s.Root, ".relicade-mod-writer.lock"),
            "default-writer" => Path.Combine(s.Tx.OperationsPath, "writer.lock"),
            "selected-writer" => Path.Combine(tx.OperationsPath, "writer.lock"),
            _ => Path.Combine(s.Game.InstallPath!, ".relicade-mod-writer.lock")
        };
        var coord = Path.Combine(s.Root, "coord"); Directory.CreateDirectory(coord);
        using var worker = StartWorker(coord, lockPath);
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(coord, "ready")))
            {
                Assert.False(worker.HasExited); Assert.True(timer.Elapsed < TimeSpan.FromSeconds(40)); await Task.Delay(15);
            }
            var deniedPreview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
            var deniedApply = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash]);
            Assert.NotEqual(0, deniedPreview.Code); Assert.NotEqual(0, deniedApply.Code);
            Assert.Empty(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
            File.WriteAllText(Path.Combine(coord, "release"), "release own worker");
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            var text = await worker.StandardOutput.ReadToEndAsync() + await worker.StandardError.ReadToEndAsync();
            File.WriteAllText(Path.Combine(coord, "worker-output.txt"), text); Assert.Equal(0, worker.ExitCode);
            output.WriteLine($"actual other-process {kind}; pid={worker.Id}; preview/apply refused; joined exit={worker.ExitCode}");
        }
        finally { if (!worker.HasExited) { worker.Kill(true); await worker.WaitForExitAsync(); } }
        var after = s.Snapshot(); foreach (var row in before) Assert.Equal(row.Value, after[row.Key]);
        var apply = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash]);
        Assert.Equal(0, apply.Code); Assert.Empty(tx.PreviewRedundantBackups().Files); Assert.Equal(0, s.Network.Calls);
    }

    [Theory]
    [InlineData(false, false)][InlineData(true, false)]
    [InlineData(false, true)][InlineData(true, true)]
    public async Task Unknown_or_managed_common_ancestor_refuses_without_repair_or_approval(bool injected, bool managed)
    {
        using var s = new PortableBackupCleanupBaselineScope(); var tx = await Located(s);
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]); Assert.Equal(0, preview.Code);
        var ancestor = new PortableInstallationTransaction(s.Root); using (ancestor.AcquireLease()) { }
        var marker = managed ? Path.Combine(s.Root, PortableInstallationTransaction.ManifestFileName) : Path.Combine(ancestor.OperationsPath, "unknown.json");
        File.WriteAllText(marker, "unknown authority is not discarded"); var before = s.Snapshot();
        foreach (var args in new[] { new[] { "--preview-portable-backups", s.Game.InstanceKey }, new[] { "--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", Hash(preview.Text) } })
            Assert.NotEqual(0, (await s.Execute(injected, args)).Code);
        Assert.Equal(before, s.Snapshot()); Assert.Equal("unknown authority is not discarded", File.ReadAllText(marker));
        Assert.Empty(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json")); Assert.Equal(0, s.Network.Calls);
    }

    [Fact]
    public async Task Authority_coordination_worker_has_substantive_ordinary_entry()
    {
        var coord = Environment.GetEnvironmentVariable("RELICADE_BC_ANCESTOR_COORD");
        if (coord == null)
        {
            using var s = new PortableBackupCleanupBaselineScope(); var tx = await Located(s);
            var plan = tx.PreviewRedundantBackups(); Assert.Single(plan.Files);
            Assert.Equal(1, tx.CleanupRedundantBackups(plan.PlanSha256).DeletedFiles); return;
        }
        using var lease = new FileStream(Environment.GetEnvironmentVariable("RELICADE_BC_ANCESTOR_LOCK")!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        File.WriteAllText(Path.Combine(coord, "ready"), "real FileShare.None held");
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(coord, "release"))) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(60)); await Task.Delay(15); }
        Assert.Equal(0, lease.Length);
    }
    static Process StartWorker(string coord, string lockPath)
    {
        var home = Path.Combine(coord, "home"); Directory.CreateDirectory(home);
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        // dotnet needs the DLL; direct apphost/VSTest workers must not receive it.
        if (Path.GetFileNameWithoutExtension(psi.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(PortableBackupCleanupAncestorAuthorityTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.PortableBackupCleanupAncestorAuthorityTests.Authority_coordination_worker_has_substantive_ordinary_entry", "-xml", Path.Combine(coord, "worker.xml") }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_BC_ANCESTOR_COORD"] = coord; psi.Environment["RELICADE_BC_ANCESTOR_LOCK"] = lockPath;
        psi.Environment["HOME"] = home; psi.Environment["DOTNET_CLI_HOME"] = home;
        return Process.Start(psi)!;
    }
    static string Hash(string text) { var m = Regex.Match(text, @"Plan SHA256: ([a-f0-9]{64})"); Assert.True(m.Success, text); return m.Groups[1].Value; }
    static async Task<PortableInstallationTransaction> Located(PortableBackupCleanupBaselineScope s)
    {
        var root = Path.Combine(s.Root, "located"); s.Game.InstallPath = root; s.SaveLibrary(); var tx = new PortableInstallationTransaction(root);
        for (var i = 1; i <= 3; i++) { var v = "located-" + i; await tx.InstallAsync(p => { File.WriteAllBytes(Path.Combine(p, "fixture.AppImage"), PortableInstallationFixtures.NativePayload(v)); return Task.CompletedTask; }, v); }
        return tx;
    }
}
