using System.Diagnostics;
using System.Security.Cryptography;
using Relicade.Services;
using Relicade.Services.Credentials;

namespace Relicade.Tests;

public sealed class LegacyCredentialMigrationWriterTests
{
    private static Process Child(string root, string mode)
    {
        var exe = Environment.ProcessPath!;
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(LegacyCredentialMigrationWriterTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.LegacyCredentialMigrationWriterTests.Process_control_entry" }) start.ArgumentList.Add(arg);
        start.Environment["RELICADE_CREDENTIAL_MIGRATION_ROOT"] = root;
        start.Environment["RELICADE_CREDENTIAL_MIGRATION_MODE"] = mode;
        return Process.Start(start)!;
    }

    private static async Task Success(Process child)
    {
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await child.WaitForExitAsync(deadline.Token);
        Assert.True(child.ExitCode == 0, await stdout + await stderr);
        Assert.Contains("CONTROL_EXECUTED:", await stdout);
        Console.WriteLine(await stdout);
    }

    [Fact]
    public async Task Process_control_entry()
    {
        var root = Environment.GetEnvironmentVariable("RELICADE_CREDENTIAL_MIGRATION_ROOT");
        if (root == null) return; // Helper entry only: parent test, not this no-op, proves separate-process execution.
        var mode = Environment.GetEnvironmentVariable("RELICADE_CREDENTIAL_MIGRATION_MODE")!;
        var path = Path.Combine(root, "settings.json");
        var vault = new LegacyCredentialMigrationTests.Vault();
        var store = new FileSettingsStore(path, new(vault));
        var before = File.ReadAllBytes(path);
        if (mode == "hold")
        {
            store.CredentialMigrationCheckpoint = p => {
                if (p != "BeforeReplace") return;
                File.WriteAllText(Path.Combine(root, "barrier"), "all providers verified; sanitized temp only");
                using var wait = new ManualResetEventSlim(); wait.Wait();
            };
            await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
            throw new InvalidOperationException("Hold control must be terminated by parent.");
        }
        else if (mode == "refuse")
        {
            vault.Calls.Clear();
            var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
            Assert.False(result.Success); Assert.False(result.VaultMayHaveChanged); Assert.Empty(vault.Calls);
            Assert.Throws<LegacyCredentialCleanupRequiredException>(() => store.Save(store.Current));
            Assert.Throws<LegacyCredentialCleanupRequiredException>(() => LauncherUpdateBackup.Create(root));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        else if (mode == "finish")
        {
            var result = await store.MigrateLegacyCredentialsAsync(store.PreviewLegacyCredentialMigration().PreviewId, true);
            Assert.True(result.Success); Assert.True(result.SettingsSanitized); Assert.Single(vault.Values);
            Assert.False(new FileSettingsStore(path, new(vault)).IsReadOnly);
            Assert.DoesNotContain("ApiToken", File.ReadAllText(path));
        }
        else if (mode == "save")
        {
            Assert.False(store.IsReadOnly);
            store.Current.SortBy = "Name"; store.Save(store.Current);
            Assert.Equal("Name", new FileSettingsStore(path, new(vault)).Current.SortBy);
        }
        else throw new InvalidOperationException(mode);
        File.WriteAllText(Path.Combine(root, mode + ".executed"), "verified independent process");
        Console.WriteLine("CONTROL_EXECUTED:" + mode + "; source SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public async Task Independent_migration_save_snapshot_refuse_held_writer_then_kill_reopen_retry_and_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "migration-process", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{\"GitHubApiToken\":\"synthetic-migration-process-canary\",\"future\":{\"keep\":true}}");
        var before = File.ReadAllBytes(path);
        using var holding = Child(root, "hold");
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "barrier")) && !holding.HasExited && timer.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(20);
            Assert.False(holding.HasExited); Assert.True(File.Exists(Path.Combine(root, "barrier")));
            using (var refusing = Child(root, "refuse")) await Success(refusing);
            Assert.True(File.Exists(Path.Combine(root, "refuse.executed")));
            Assert.Equal(before, File.ReadAllBytes(path));
            foreach (var f in Directory.GetFiles(root, "*.tmp")) Assert.DoesNotContain("synthetic-migration-process-canary", File.ReadAllText(f));
            holding.Kill(true); await holding.WaitForExitAsync();
            Assert.Equal(before, File.ReadAllBytes(path));
            using (var finish = Child(root, "finish")) await Success(finish);
            Assert.Contains("future", File.ReadAllText(path));
            Assert.DoesNotContain("synthetic-migration-process-canary", File.ReadAllText(path));
            using (var save = Child(root, "save")) await Success(save);
            Assert.True(File.Exists(Path.Combine(root, "finish.executed"))); Assert.True(File.Exists(Path.Combine(root, "save.executed")));
            Console.WriteLine("Stable settings lease held through provider readback and sanitized staging; separate writers/snapshot refused; kill left exact original; independent retry/reopen/save succeeded.");
        }
        finally { if (!holding.HasExited) { holding.Kill(true); await holding.WaitForExitAsync(); } }
    }
}
