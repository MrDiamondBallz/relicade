using System.Diagnostics;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupAuthorityTests
{
    [Theory]
    [InlineData("ancestor-fifo-writer")][InlineData("ancestor-fifo-mod")][InlineData("case-aliased-authority")]
    public async Task Unknown_outside_authority_refuses_before_snapshot_or_approval(string kind)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3); var tx = f.Tx();
        if (kind == "case-aliased-authority")
        {
            Assert.NotEqual(tx.InstallKey, tx.InstallKey.ToUpperInvariant());
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(tx.OperationsPath)!, tx.InstallKey.ToUpperInvariant()));
        }
        else
        {
            string path;
            if (kind == "ancestor-fifo-writer")
            {
                var ancestor = new PortableInstallationTransaction(f.Root);
                using (ancestor.AcquireLease()) { }
                path = Path.Combine(ancestor.OperationsPath, "writer.lock"); File.Delete(path);
            }
            else path = Path.Combine(f.Root, ".relicade-mod-writer.lock");
            using var fifo = Process.Start("/usr/bin/mkfifo", path)!; fifo.WaitForExit(); Assert.Equal(0, fifo.ExitCode);
        }
        // Do not read a special file in the fixture snapshot itself. Every actual
        // duplicate target remains present; no approval may be published.
        Assert.ThrowsAny<IOException>(() => tx.PreviewRedundantBackups());
        Assert.Empty(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
    }
}
