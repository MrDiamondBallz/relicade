using System.Text.RegularExpressions;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupCaseAuthorityTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Case_distinct_existing_default_and_located_roots_refuse_before_approval(bool injected)
    {
        Assert.True(OperatingSystem.IsLinux());
        using var s = new PortableBackupCleanupBaselineScope();
        for (var i = 1; i <= 3; i++) await s.Install("default-" + i);
        var located = Path.Combine(s.Root, "Apps", "LIVE");
        s.Game.InstallPath = located; s.SaveLibrary();
        var tx = await InstallLocated(located);
        Assert.NotEqual(s.Tx.InstallKey, tx.InstallKey);
        var before = s.Snapshot();
        var result = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.NotEqual(0, result.Code);
        Assert.Equal(0, s.Network.Calls); Assert.Equal(before, s.Snapshot());
        Assert.Empty(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Case_distinct_absent_default_writer_excludes_confirmed_cleanup(bool injected)
    {
        Assert.True(OperatingSystem.IsLinux());
        using var s = new PortableBackupCleanupBaselineScope();
        var located = Path.Combine(s.Root, "Apps", "LIVE");
        s.Game.InstallPath = located; s.SaveLibrary();
        var tx = await InstallLocated(located);
        Assert.NotEqual(s.Tx.InstallKey, tx.InstallKey);
        Assert.False(Directory.Exists(s.Live));
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.Equal(0, preview.Code);
        var hash = PlanHash(preview.Text);
        // The entire synthetic tree includes retained witnesses, metadata,
        // operations, library and settings, not just the located live payload.
        var before = s.Snapshot();
        using (s.Tx.AcquireLease())
        {
            var result = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash]);
            Assert.NotEqual(0, result.Code);
            Assert.Equal(before, s.Snapshot());
            Assert.Empty(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
        }
        Assert.Equal(0, s.Network.Calls); Assert.False(Directory.Exists(s.Live));
        // Releasing the actual alternate authority permits the same confirmed
        // plan, proving this is exclusion rather than a blanket located no-op.
        var apply = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash]);
        Assert.Equal(0, apply.Code); Assert.Single(Directory.GetFiles(tx.OperationsPath, "backup-cleanup-*.json"));
        Assert.Equal("located-3", tx.ReadCommitted()!.SelectedVersion);
        Assert.Empty(tx.PreviewRedundantBackups().Files); Assert.Equal(0, s.Network.Calls);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Ordinary_located_root_remains_functional_with_absent_default(bool injected)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        var located = Path.Combine(s.Root, "located"); s.Game.InstallPath = located; s.SaveLibrary();
        var tx = await InstallLocated(located);
        var candidates = tx.PreviewRedundantBackups().Files;
        Assert.Single(candidates); Assert.False(Directory.Exists(s.Live));
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.Equal(0, preview.Code);
        var apply = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", PlanHash(preview.Text)]);
        Assert.Equal(0, apply.Code);
        foreach (var file in candidates)
        {
            var op = Path.Combine(tx.OperationsPath, file.OperationId);
            Assert.False(File.Exists(Path.Combine(op, file.BackupRelativePath)));
            Assert.True(File.Exists(Path.Combine(op, file.RetainedRelativePath)));
        }
        Assert.Empty(tx.PreviewRedundantBackups().Files);
        Assert.Equal("located-3", tx.ReadCommitted()!.SelectedVersion);
        Assert.Equal(0, s.Network.Calls); Assert.False(Directory.Exists(s.Live));
    }

    static string PlanHash(string text)
    {
        var match = Regex.Match(text, @"Plan SHA256: ([a-f0-9]{64})");
        Assert.True(match.Success, text); return match.Groups[1].Value;
    }

    static async Task<PortableInstallationTransaction> InstallLocated(string path)
    {
        var tx = new PortableInstallationTransaction(path);
        for (var i = 1; i <= 3; i++)
        {
            var version = "located-" + i;
            await tx.InstallAsync(p => { File.WriteAllBytes(Path.Combine(p, "fixture.AppImage"), PortableInstallationFixtures.NativePayload(version)); return Task.CompletedTask; }, version);
        }
        return tx;
    }
}
