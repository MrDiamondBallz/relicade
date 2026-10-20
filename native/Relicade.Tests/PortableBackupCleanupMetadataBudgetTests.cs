using System.Reflection;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupMetadataBudgetTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Shared_generated_and_persisted_parser_enforces_unchanged_metadata_byte_ceiling(bool allowEmpty)
    {
        // White-box budget boundary control only, not a claim of a 64MiB public
        // history test. Real preview/apply/retry/count/byte limits have separate
        // production API regressions. Avoid writing oversized private fixtures.
        var f = new PortableBackupCleanupFixture(); await f.Generations(3);
        var plan = f.Tx().PreviewRedundantBackups();
        Assert.Throws<BackupCleanupInterruptedException>(() => f.Tx(name =>
        {
            if (name == "BackupCleanupApproved") throw new IOException("synthetic stop");
        }).CleanupRedundantBackups(plan.PlanSha256));
        var path = Path.Combine(f.Tx().OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json");
        var bytes = File.ReadAllBytes(path);
        var parser = typeof(PortableInstallationTransaction).GetMethod("ParseCleanupApproval", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(parser);
        Assert.NotNull(parser.Invoke(f.Tx(), [bytes, path, allowEmpty]));
        var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        var oversized = new byte[64 * 1024 * 1024 + 1]; bytes.CopyTo(oversized, 0);
        var failure = Assert.Throws<TargetInvocationException>(() => parser.Invoke(f.Tx(), [oversized, path, allowEmpty]));
        Assert.Equal("Cleanup approval exceeds its metadata size budget.", Assert.IsType<InvalidDataException>(failure.InnerException).Message);
        Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
        Assert.True(f.Tx().CleanupRedundantBackups(plan.PlanSha256).Completed);
    }
}
