using System.Diagnostics;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupAlternateAuthorityTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Located_default_alternate_authority_cannot_be_a_FIFO(bool injected)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        var located = Path.Combine(s.Root, "located"); s.Game.InstallPath = located; s.SaveLibrary();
        var tx = new PortableInstallationTransaction(located);
        for (var i = 1; i <= 3; i++)
        {
            var version = "v" + i;
            await tx.InstallAsync(p => { File.WriteAllBytes(Path.Combine(p, "fixture.AppImage"), PortableInstallationFixtures.NativePayload(version)); return Task.CompletedTask; }, version);
        }
        using (s.Tx.AcquireLease()) { }
        var writer = Path.Combine(s.Tx.OperationsPath, "writer.lock"); File.Delete(writer);
        using (var fifo = Process.Start("/usr/bin/mkfifo", writer)!) { fifo.WaitForExit(); Assert.Equal(0, fifo.ExitCode); }
        var result = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.NotEqual(0, result.Code); Assert.Equal(0, s.Network.Calls);
        Assert.Empty(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
    }
}
