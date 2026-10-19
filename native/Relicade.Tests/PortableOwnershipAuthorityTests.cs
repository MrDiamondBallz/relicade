using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PortableOwnershipAuthorityTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-authority-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "live");
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    static async Task Install(string root, string version, Action<string>? boundary = null)
    {
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, "fixture.zip");
        using (var f = File.Create(zip))
        using (var z = new ZipArchive(f, ZipArchiveMode.Create))
        { using var w = new StreamWriter(z.CreateEntry("game.exe").Open()); w.Write("NONEXECUTABLE SYNTHETIC " + version); }
        await GameInstallationService.InstallOrUpdateGameAsync(zip, Path.Combine(root, "live"), "fixture.zip", version,
            options: new GameInstallationOptions { PreservedDataPaths = ["saves"], TransactionBoundary = boundary });
    }
    static string[] Snapshot(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(path, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))
        .Order(StringComparer.Ordinal).ToArray();
    async Task Missing(string state)
    {
        await Install(root, "old");
        Directory.CreateDirectory(Path.Combine(Live, "saves")); File.WriteAllText(Path.Combine(Live, "saves", "save.dat"), "CURRENT SAVE");
        File.WriteAllText(Path.Combine(root, "sibling.dat"), "SIBLING");
        if (state == "updated") await Install(root, "new");
        if (state == "rolled-back") { await Install(root, "new"); new PortableInstallationTransaction(Live).Rollback(); }
        if (state == "recovered-old")
            await Assert.ThrowsAsync<IOException>(() => Install(root, "new", p => { if (p == "CandidateRenamed") throw new IOException("synthetic interruption"); }));
        Assert.NotNull(new PortableInstallationTransaction(Live).ReadCommitted());
        File.Delete(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
    }
    [Theory]
    [InlineData("fresh")][InlineData("updated")][InlineData("rolled-back")][InlineData("recovered-old")]
    public async Task Missing_manifest_never_downgrades_known_owned_generation_to_unmanaged(string state)
    {
        await Missing(state); var before = Snapshot(root); var tx = new PortableInstallationTransaction(Live);
        Assert.Throws<IOException>(() => tx.ReadCommitted());
        Assert.Throws<IOException>(() => tx.Recover());
        Assert.Throws<IOException>(() => PortableInstallationLifetime.Begin(Live));
        Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        var game = new GameInfo { Name = "synthetic", FolderName = "live" };
        using var http = new HttpClient();
        await GameStatusService.CheckStatusAsync(game, http, root, checkRemoteVersion: false, applyCachedRelease: false);
        Assert.NotNull(game.PortableRecoveryError); Assert.False(game.CanLaunch);
        Assert.False(await GameLaunchService.LaunchAsync(game, root));
        Assert.NotNull(game.PortableRecoveryError); Assert.False(game.CanLaunch);
        var staged = false;
        await Assert.ThrowsAsync<IOException>(() => tx.InstallAsync(_ => { staged = true; return Task.CompletedTask; }, "next"));
        Assert.False(staged);
        Assert.Throws<IOException>(() => tx.SelectExecutable("game.exe"));
        Assert.Throws<IOException>(() => tx.Rollback());
        Assert.Equal(before, Snapshot(root));
    }
    [Theory]
    [InlineData("unmanaged")][InlineData("pre-prepared")][InlineData("resolved-fresh")]
    public async Task Genuinely_unmanaged_or_abandoned_fresh_staging_remains_distinct(string state)
    {
        Directory.CreateDirectory(Live);
        if (state != "unmanaged")
            await Assert.ThrowsAsync<IOException>(() => Install(root, "old", p =>
            { if (p == (state == "pre-prepared" ? "Staged" : "Prepared")) throw new IOException("abandoned fresh staging"); }));
        var tx = new PortableInstallationTransaction(Live);
        Assert.Null(tx.ReadCommitted()); tx.Recover();
        using (PortableInstallationLifetime.Begin(Live)) Assert.True(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        using var http = new HttpClient(); var game = new GameInfo { Name = "synthetic", FolderName = "live" };
        await GameStatusService.CheckStatusAsync(game, http, root, checkRemoteVersion: false, applyCachedRelease: false);
        Assert.Null(game.PortableRecoveryError); Assert.False(game.IsManagedPortable);
        await Install(root, "next"); Assert.Equal("next", tx.ReadCommitted()!.SelectedVersion);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Missing_manifest_and_denied_ownership_journal_cannot_hide_evidence(bool denyDirectory)
    {
        if (!OperatingSystem.IsLinux()) return; // real DAC denial, not native-platform acceptance
        await Missing("fresh"); var tx = new PortableInstallationTransaction(Live);
        var journal = Directory.GetFiles(tx.OperationsPath, "journal.json", SearchOption.AllDirectories).Single();
        var denied = denyDirectory ? Path.GetDirectoryName(journal)! : journal;
        var prior = File.GetUnixFileMode(denied); var before = Snapshot(root);
        try
        {
            File.SetUnixFileMode(denied, UnixFileMode.None);
            Assert.IsType<UnauthorizedAccessException>(Record.Exception(() => File.ReadAllBytes(journal)));
            Assert.NotNull(Record.Exception(() => tx.ReadCommitted()));
            Assert.NotNull(Record.Exception(() => tx.Recover()));
            Assert.NotNull(Record.Exception(() => PortableInstallationLifetime.Begin(Live)));
            Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
            using var http = new HttpClient(); var game = new GameInfo { Name = "synthetic", FolderName = "live" };
            await GameStatusService.CheckStatusAsync(game, http, root, checkRemoteVersion: false, applyCachedRelease: false);
            Assert.NotNull(game.PortableRecoveryError); Assert.False(game.CanLaunch);
        }
        finally { File.SetUnixFileMode(denied, prior); }
        Assert.Equal(before, Snapshot(root));
    }
    [Fact]
    public async Task Independent_reopened_process_refuses_missing_owned_manifest_and_preserves_receipts_data_and_evidence()
    {
        await Missing("updated"); var before = Snapshot(root);
        using var child = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        child.StartInfo.ArgumentList.Add(typeof(PortableOwnershipAuthorityTests).Assembly.Location);
        child.StartInfo.ArgumentList.Add("-noLogo"); child.StartInfo.ArgumentList.Add("-noColor");
        child.StartInfo.ArgumentList.Add("-method"); child.StartInfo.ArgumentList.Add("Relicade.Tests.PortableOwnershipAuthorityTests.Independent_authority_worker");
        child.StartInfo.Environment["RELICADE_AUTHORITY_FIXTURE"] = root;
        child.Start(); var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(child.ExitCode == 0, await output + await error); Assert.Equal(before, Snapshot(root));
    }
    [Fact]
    public async Task Independent_authority_worker()
    {
        var path = Environment.GetEnvironmentVariable("RELICADE_AUTHORITY_FIXTURE"); if (path == null) return;
        var live = Path.Combine(path, "live"); var tx = new PortableInstallationTransaction(live);
        Assert.Throws<IOException>(() => tx.ReadCommitted()); Assert.Throws<IOException>(() => tx.Recover());
        Assert.Throws<IOException>(() => PortableInstallationLifetime.Begin(live));
        Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        using var http = new HttpClient(); var game = new GameInfo { Name = "synthetic", FolderName = "live" };
        await GameStatusService.CheckStatusAsync(game, http, path, checkRemoteVersion: false, applyCachedRelease: false);
        Assert.NotNull(game.PortableRecoveryError); Assert.False(game.CanLaunch);
    }
}
