using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupBudgetTests
{
    [Theory]
    [InlineData("file-count", false)][InlineData("file-count", true)]
    [InlineData("bytes", false)][InlineData("bytes", true)]
    public async Task Oversized_aggregate_plan_refuses_before_preview_or_publication_and_default_retry_works(string kind, bool applyDirectly)
    {
        var (root, ordinary, bytes) = await History(kind == "bytes" ? 65536 : 4096);
        var plan = ordinary.PreviewRedundantBackups();
        Assert.Equal(20, plan.Files.Count); Assert.Equal(20L * bytes, plan.ReclaimableBytes);
        var bounded = new PortableInstallationTransaction(Path.Combine(root, "live"), kind == "file-count"
            ? new GameInstallationOptions { MaximumFileCount = 18 }
            : new GameInstallationOptions { MaximumExpandedBytes = 1024 * 1024 });
        var before = PortableBackupCleanupFixture.Snapshot(root);
        // InvalidDataException is not derived from IOException. The public
        // contract is refusal before ANY receipt/write, then a precise schema
        // validation error, not an interrupted cleanup after publication.
        var error = Record.Exception(() =>
        {
            if (applyDirectly) bounded.CleanupRedundantBackups(plan.PlanSha256);
            else bounded.PreviewRedundantBackups();
        });
        Assert.NotNull(error);
        Assert.Empty(Directory.GetFiles(ordinary.OperationsPath, "backup-cleanup-*"));
        Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(root));
        Assert.IsType<InvalidDataException>(error);
        var retry = ordinary.PreviewRedundantBackups(); Assert.Equal(plan.PlanSha256, retry.PlanSha256);
        var result = ordinary.CleanupRedundantBackups(retry.PlanSha256);
        Assert.True(result.Completed); Assert.Equal(20, result.DeletedFiles); Assert.Equal(20L * bytes, result.ReclaimedBytes);
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, true), ordinary.CleanupRedundantBackups(plan.PlanSha256));
        Assert.Empty(ordinary.PreviewRedundantBackups().Files);
    }

    [Theory]
    [InlineData("file-count")][InlineData("bytes")]
    public async Task Exact_aggregate_budget_is_accepted_and_same_limits_can_resume_and_repeat(string kind)
    {
        var (root, ordinary, bytes) = await History(65536);
        var bounded = new PortableInstallationTransaction(Path.Combine(root, "live"), kind == "file-count"
            ? new GameInstallationOptions { MaximumFileCount = 20 }
            : new GameInstallationOptions { MaximumExpandedBytes = 20L * bytes });
        var plan = bounded.PreviewRedundantBackups(); Assert.Equal(20, plan.Files.Count);
        var interrupted = new PortableInstallationTransaction(Path.Combine(root, "live"), new GameInstallationOptions
        {
            MaximumFileCount = kind == "file-count" ? 20 : GameInstallationOptions.Default.MaximumFileCount,
            MaximumExpandedBytes = kind == "bytes" ? 20L * bytes : GameInstallationOptions.Default.MaximumExpandedBytes,
            TransactionBoundary = name => { if (name == "BackupCleanupApproved") throw new IOException("synthetic interruption"); }
        });
        var failure = Assert.Throws<BackupCleanupInterruptedException>(() => interrupted.CleanupRedundantBackups(plan.PlanSha256));
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, false), failure.Result);
        Assert.Equal(plan.PlanSha256, bounded.PreviewRedundantBackups().PlanSha256);
        var result = bounded.CleanupRedundantBackups(plan.PlanSha256);
        Assert.True(result.Completed); Assert.Equal(20, result.DeletedFiles); Assert.Equal(20L * bytes, result.ReclaimedBytes);
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, true), bounded.CleanupRedundantBackups(plan.PlanSha256));
        Assert.Empty(bounded.PreviewRedundantBackups().Files); Assert.NotNull(ordinary.ReadCommitted());
    }

    static async Task<(string Root, PortableInstallationTransaction Tx, int Bytes)> History(int bytes)
    {
        var root = Path.Combine(Path.GetTempPath(), "bc-budget-" + Guid.NewGuid().ToString("N"));
        var tx = new PortableInstallationTransaction(Path.Combine(root, "live"), new GameInstallationOptions { SelectedExecutableRelativePath = "fixture.AppImage" });
        for (var g = 1; g <= 4; g++)
        {
            var generation = g;
            await tx.InstallAsync(p =>
            {
                for (var f = 0; f < 10; f++)
                {
                    var content = new byte[bytes]; PortableInstallationFixtures.NativePayload("g" + generation + "f" + f).CopyTo(content, 0);
                    File.WriteAllBytes(Path.Combine(p, f == 0 ? "fixture.AppImage" : "payload-" + f + ".bin"), content);
                }
                return Task.CompletedTask;
            }, "generation-" + g);
        }
        return (root, tx, bytes);
    }
}
