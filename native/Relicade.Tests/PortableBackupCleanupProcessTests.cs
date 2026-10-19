using System.Diagnostics;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupProcessTests(ITestOutputHelper output)
{
    const string WorkerMethod = "Relicade.Tests.PortableBackupCleanupProcessTests.Coordination_worker_executes_real_cleanup_not_a_noop";
    static Process Start(string root, string mode, string plan)
    {
        var coord = Path.Combine(root, "coord"); Directory.CreateDirectory(coord);
        var home = Path.Combine(coord, "home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(typeof(PortableBackupCleanupProcessTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", WorkerMethod, "-xml", Path.Combine(coord, "worker-" + Guid.NewGuid().ToString("N") + ".xml") }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_BC_WORKER_ROOT"] = root; psi.Environment["RELICADE_BC_WORKER_MODE"] = mode; psi.Environment["RELICADE_BC_WORKER_PLAN"] = plan;
        psi.Environment["HOME"] = home; psi.Environment["DOTNET_CLI_HOME"] = home;
        return Process.Start(psi)!;
    }
    static async Task Ready(Process worker, string root)
    {
        var until = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "coord/ready")))
        {
            if (worker.HasExited) throw new Exception("Worker exited before checkpoint: " + await worker.StandardOutput.ReadToEndAsync() + await worker.StandardError.ReadToEndAsync());
            if (until.Elapsed > TimeSpan.FromSeconds(40)) { worker.Kill(true); throw new TimeoutException("No real worker checkpoint"); }
            await Task.Delay(15);
        }
    }
    [Theory]
    [InlineData("BackupCleanupApproved")][InlineData("BackupCleanupDeleted")][InlineData("BackupCleanupCompleted")]
    public async Task Actual_kill_after_durable_approval_or_deletion_then_separate_process_resume(string boundary)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(); var tx = f.Tx(); var plan = tx.PreviewRedundantBackups();
        var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        using (var child = Start(f.Root, boundary, plan.PlanSha256))
        {
            try
            {
                await Ready(child, f.Root);
                var receipt = Path.Combine(tx.OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json"); Assert.True(File.Exists(receipt));
                Assert.ThrowsAny<IOException>(() => tx.PreviewRedundantBackups());
                child.Kill(true); await child.WaitForExitAsync(); Assert.NotEqual(0, child.ExitCode);
                output.WriteLine($"actual SIGKILL own coordination worker pid={child.Id}; durable checkpoint={boundary}; exit={child.ExitCode}; source/receipt retained");
                File.WriteAllText(Path.Combine(f.Root, "coord/killed-output.txt"), await child.StandardOutput.ReadToEndAsync() + await child.StandardError.ReadToEndAsync());
            }
            finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
        }
        var priorAbsent = plan.Files.Count(r => !File.Exists(Path.Combine(tx.OperationsPath, r.OperationId, r.BackupRelativePath)));
        Assert.Equal(boundary == "BackupCleanupApproved" ? 0 : boundary == "BackupCleanupDeleted" ? 1 : plan.Files.Count, priorAbsent);
        using (var resumed = Start(f.Root, "resume", plan.PlanSha256))
        {
            var text = await resumed.StandardOutput.ReadToEndAsync(); var error = await resumed.StandardError.ReadToEndAsync(); await resumed.WaitForExitAsync();
            File.WriteAllText(Path.Combine(f.Root, "coord/resume-output.txt"), text + error); Assert.Equal(0, resumed.ExitCode);
        }
        var resumeResult = System.Text.Json.JsonSerializer.Deserialize<BackupCleanupResult>(File.ReadAllText(Path.Combine(f.Root, "coord/resume-result.json")))!;
        Assert.Equal(plan.Files.Count - priorAbsent, resumeResult.DeletedFiles);
        Assert.Equal(plan.Files.Skip(priorAbsent).Sum(r => r.Length), resumeResult.ReclaimedBytes);
        f.OnlyApprovedRemoved(before, plan);
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, true), tx.CleanupRedundantBackups(plan.PlanSha256));
        Assert.Equal("v4", tx.ReadCommitted()!.SelectedVersion); Assert.Empty(tx.PreviewRedundantBackups().Files);
        f.Save("CURRENT_PROCESS_SAVE"); tx.Rollback(); Assert.StartsWith("CURRENT_PROCESS_SAVE", File.ReadAllText(Path.Combine(f.Live, "saves/nested/slot.sav")));
    }
    [Theory]
    [InlineData("hold-writer")][InlineData("hold-mod")]
    public async Task Independent_process_writer_and_legacy_mod_authority_refuse_without_deletion(string mode)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3); var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        using var child = Start(f.Root, mode, "");
        try
        {
            await Ready(child, f.Root);
            Assert.ThrowsAny<IOException>(() => f.Tx().PreviewRedundantBackups());
            child.Kill(true); await child.WaitForExitAsync(); Assert.NotEqual(0, child.ExitCode);
            Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
        Assert.NotEmpty(f.Tx().PreviewRedundantBackups().Files);
    }
    [Fact]
    public async Task Coordination_worker_executes_real_cleanup_not_a_noop()
    {
        var root = Environment.GetEnvironmentVariable("RELICADE_BC_WORKER_ROOT");
        if (root == null)
        {
            // The ordinary suite invocation is substantive too; never a green
            // worker early-return. Child invocations use only owned synthetic state.
            var f = new PortableBackupCleanupFixture(); await f.Generations(3); var p = f.Tx().PreviewRedundantBackups();
            Assert.Equal(3, f.Tx().CleanupRedundantBackups(p.PlanSha256).DeletedFiles); return;
        }
        var mode = Environment.GetEnvironmentVariable("RELICADE_BC_WORKER_MODE")!;
        var live = Path.Combine(root, "live"); var ready = Path.Combine(root, "coord/ready");
        void Hold() { File.WriteAllText(ready, "actual held checkpoint " + mode); Thread.Sleep(Timeout.Infinite); }
        if (mode == "hold-writer") { using var lease = new PortableInstallationTransaction(live).AcquireLease(); Hold(); throw new Exception("Never silently succeed held worker"); }
        if (mode == "hold-mod") { using var lease = new FileStream(Path.Combine(live, ".relicade-mod-writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); Hold(); throw new Exception("Never silently succeed held worker"); }
        var tx = new PortableInstallationTransaction(live, PortableBackupCleanupFixture.Options(p => { if (p == mode) Hold(); }));
        var plan = Environment.GetEnvironmentVariable("RELICADE_BC_WORKER_PLAN")!;
        var result = tx.CleanupRedundantBackups(plan); Assert.True(result.Completed);
        var next = tx.CleanupRedundantBackups(plan); Assert.Equal(0, next.DeletedFiles); Assert.True(next.Completed);
        File.WriteAllText(Path.Combine(root, "coord/resume-result.json"), System.Text.Json.JsonSerializer.Serialize(result));
        output.WriteLine($"actual separate-process cleanup and idempotent replay: {result}");
    }
}
