using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PortableInstallationRuntimeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-runtime-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "live");
    string Source => Path.Combine(root, "fixture.exe");
    public PortableInstallationRuntimeTests() { Directory.CreateDirectory(root); GitHubApiCache.Initialize(Path.Combine(root, "cache")); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    GameInfo Game() => new() { FolderName = "live", Name = "Synthetic portable", Repository = "fixture/" + Guid.NewGuid().ToString("N") };
    async Task Initial()
    {
        File.WriteAllText(Source, "old"); await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.exe", "old"); File.WriteAllText(Source, "new");
    }
    [Theory]
    [InlineData("old")][InlineData("new")]
    public async Task Status_recovers_before_reading_version_and_never_invents_a_version(string expected)
    {
        await Initial();
        var point = expected == "old" ? "PreviousRenamed" : "Committed";
        await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.exe", "new", new() { TransactionBoundary = p => { if (p == point || p == "Recovering") throw new IOException("retained boundary"); } }));
        var game = Game(); using var client = new HttpClient(new Handler([], false));
        await GameStatusService.CheckStatusAsync(game, client, root, checkRemoteVersion: false, applyCachedRelease: false);
        Assert.Null(game.PortableRecoveryError); Assert.Equal(expected, game.InstalledVersion); Assert.True(game.IsInstalled); Assert.False(game.CanManageMods);
        Assert.Equal(expected, File.ReadAllText(Path.Combine(Live, "version.txt")));
    }
    [Theory]
    [InlineData("corrupt")][InlineData("unknown")]
    public async Task Recovery_error_visibly_blocks_status_launch_and_install_without_game_execution(string fault)
    {
        await Initial();
        if (fault == "corrupt") File.WriteAllText(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName), "{invalid");
        else File.WriteAllText(Path.Combine(Live, "fixture.exe"), "unknown bytes");
        var game = Game(); game.Status = GameStatus.Installed; game.InstalledVersion = "old";
        using var client = new HttpClient(new Handler([], false));
        await GameStatusService.CheckStatusAsync(game, client, root, checkRemoteVersion: false, applyCachedRelease: false);
        Assert.NotNull(game.PortableRecoveryError); Assert.Contains("Recovery needed", game.StatusText);
        Assert.False(game.IsInstalled); Assert.False(game.CanLaunch); Assert.False(game.CanDownload); Assert.False(game.CanUpdate);
        Assert.Equal(GameStatus.NeedsExecutable, game.Status); Assert.False(await GameLaunchService.LaunchAsync(game, root));
        var dialogs = new Dialogs(); Assert.False(await game.PerformActionAsync(client, root, new(), dialogs)); Assert.Contains("Recovery needed", dialogs.Error);
    }
    static byte[] Zip(string text)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "fixture.exe", "payload/data.txt" }) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(text); }
        return stream.ToArray();
    }
    GitHubRelease Release(string version, byte[] payload) => new() { tag_name = version, assets = [new GitHubAsset { name = "fixture-windows.zip", browser_download_url = "https://fixture.invalid/payload.zip", digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload)) }] };
    [Theory]
    [InlineData("success")][InlineData("network")][InlineData("checksum")][InlineData("extract")][InlineData("cancel")]
    [InlineData("stream-io")][InlineData("stream-cancel")]
    public async Task Download_entry_installs_then_updates_or_preserves_exact_committed_generation(string fault)
    {
        var first = Zip("old"); var game = Game(); var dialogs = new Dialogs();
        using (var client = new HttpClient(new Handler(first, false)))
            await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, Release("old", first), new() { Platform = TestPlatforms.ForWindowsPayload }, GameStatus.NotInstalled, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease);
        Assert.Null(dialogs.Error); Assert.True(game.IsInstalled); Assert.Equal("old", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        var manifest = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        var second = fault == "extract" ? "not an archive"u8.ToArray() : Zip("new"); var selected = Release("new", fault == "checksum" ? Zip("different") : second);
        using (var client = new HttpClient(new Handler(second, fault == "network", fault == "cancel", fault.StartsWith("stream-") ? fault : null)))
            await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, selected, new() { Platform = TestPlatforms.ForWindowsPayload }, GameStatus.UpdateAvailable, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease);
        Assert.Equal(fault == "success" ? "new" : "old", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        Assert.Equal(fault == "success" ? "new" : "old", File.ReadAllText(Path.Combine(Live, "fixture.exe")));
        if (fault != "success") { Assert.NotNull(dialogs.Error); Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName))); }
        else Assert.Null(dialogs.Error);
        Assert.True(game.IsInstalled); Assert.Null(game.PortableRecoveryError);
        Assert.False(game.IsPortableCommitInProgress); Assert.False(game.IsInstallIndeterminate);
    }
    [Fact]
    public void Noninterruptible_commit_projection_is_explicit_and_does_not_override_recovery()
    {
        var game = Game(); game.Status = GameStatus.Installing; game.IsPortableCommitInProgress = true;
        Assert.Contains("Finishing safely", game.StatusText); Assert.Equal("Finishing safely", game.ButtonText);
        game.PortableRecoveryError = "Recovery needed: synthetic retained evidence";
        Assert.StartsWith("Recovery needed", game.StatusText); Assert.False(game.CanLaunch);
        game.IsPortableCommitInProgress = false; Assert.StartsWith("Recovery needed", game.StatusText);
    }
    [Fact]
    public async Task Direct_shortcut_cannot_bypass_managed_recovery_or_lifecycle()
    {
        await Initial(); var game = Game(); game.SelectedExecutable = Path.Combine(Live, "fixture.exe");
        var manifest = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => GameShortcutLaunch.PrepareAsync(game, root, new()));
        Assert.Contains("running-game lease", ex.Message); Assert.False(File.Exists(Path.Combine(Live, "selected_executable.txt")));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
    }
    [Fact]
    public async Task Uncertain_prelaunch_receipt_blocks_mutation_after_lease_release()
    {
        await Initial(); var lifetime = PortableInstallationLifetime.Begin(Live); lifetime.PreserveUncertain();
        var ex = await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.exe", "new"));
        Assert.Contains("interrupted", ex.Message); Assert.True(File.Exists(Path.Combine(new PortableInstallationTransaction(Live).OperationsPath, "launch.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Live, "fixture.exe")));
    }
    static Process Child(string root, string mode)
    {
        var exe = Environment.ProcessPath!; var p = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) p.ArgumentList.Add(typeof(PortableInstallationRuntimeTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.PortableInstallationRuntimeTests.Lifetime_process_control" }) p.ArgumentList.Add(arg);
        p.Environment["RELICADE_LIFETIME_FIXTURE_ROOT"] = root; p.Environment["RELICADE_LIFETIME_FIXTURE_MODE"] = mode; return Process.Start(p)!;
    }
    [Fact]
    public async Task Lifetime_process_control()
    {
        var target = Environment.GetEnvironmentVariable("RELICADE_LIFETIME_FIXTURE_ROOT"); if (target == null) return;
        var mode = Environment.GetEnvironmentVariable("RELICADE_LIFETIME_FIXTURE_MODE");
        if (mode == "tracked")
        {
            File.WriteAllText(Path.Combine(target, "tracked-ready"), "ready");
            using var barrier = new ManualResetEventSlim(); barrier.Wait(); return;
        }
        using var lifetime = PortableInstallationLifetime.Begin(Path.Combine(target, "live"));
        using var child = Child(target, "tracked"); lifetime.Attach(child);
        File.WriteAllText(Path.Combine(target, "tracked-pid"), child.Id.ToString());
        using var held = new ManualResetEventSlim(); held.Wait(); await Task.CompletedTask;
    }
    [Fact]
    public async Task Actual_tracked_process_blocks_update_even_after_owning_launcher_is_killed()
    {
        await Initial(); using var owner = Child(root, "owner"); int? tracked = null;
        try
        {
            var timer = Stopwatch.StartNew(); while ((!File.Exists(Path.Combine(root, "tracked-pid")) || !File.Exists(Path.Combine(root, "tracked-ready"))) && !owner.HasExited && timer.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(20);
            Assert.False(owner.HasExited); Assert.True(File.Exists(Path.Combine(root, "tracked-pid"))); tracked = int.Parse(File.ReadAllText(Path.Combine(root, "tracked-pid")));
            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.exe", "new"));
            owner.Kill(entireProcessTree: false); await owner.WaitForExitAsync();
            using var nativeTracked = Process.GetProcessById(tracked.Value); Assert.False(nativeTracked.HasExited, "The independent tracked control must still be alive after its launcher exits");
            var ex = await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.exe", "new"));
            Assert.Contains("running", ex.Message); Assert.Equal("old", File.ReadAllText(Path.Combine(Live, "fixture.exe")));
            var beforeMod = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
            var modError = Assert.Throws<IOException>(() => new Relicade.Services.Mods.InstalledModsStore().Save(Live, new()));
            Assert.Contains("running", modError.Message);
            Assert.False(File.Exists(Path.Combine(Live, Relicade.Services.Mods.InstalledModsStore.SidecarFileName)));
            Assert.Equal(beforeMod, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        }
        finally
        {
            if (!owner.HasExited) { owner.Kill(true); await owner.WaitForExitAsync(); }
            if (tracked != null) { using var p = Process.GetProcessById(tracked.Value); p.Kill(true); await p.WaitForExitAsync(); }
        }
        await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.exe", "new"); Assert.Equal("new", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
    }
    [Theory]
    [InlineData("../escape")][InlineData("/absolute")][InlineData("C:/drive")][InlineData("nested/game")]
    public async Task Download_cannot_choose_roots_outside_explicit_game_folder_authority(string folder)
    {
        var game = Game(); game.FolderName = folder; var dialogs = new Dialogs();
        using var client = new HttpClient(new Handler([], true));
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, Release("v1", []), new(), GameStatus.NotInstalled, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease);
        Assert.Contains("path authority", dialogs.Error); Assert.False(Directory.Exists(Path.Combine(root, PortableInstallationTransaction.DirectoryName)));
    }
    sealed class FailAfterPrefix(byte[] bytes, bool cancel) : MemoryStream(bytes)
    {
        bool first = true;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!first) return ValueTask.FromException<int>(cancel ? new OperationCanceledException("Controlled streamed cancellation", cancellationToken) : new IOException("Controlled disconnect after real streamed bytes"));
            first = false; return ValueTask.FromResult(Read(buffer.Span[..Math.Min(buffer.Length, Math.Max(1, bytes.Length / 2))]));
        }
    }
    sealed class Handler(byte[] bytes, bool network, bool cancel = false, string? streamFault = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (network) throw new HttpRequestException("Controlled network failure; no request sent");
            if (cancel) throw new OperationCanceledException("Controlled cancelled streaming boundary", cancellationToken);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = streamFault == null ? new ByteArrayContent(bytes) : new StreamContent(new FailAfterPrefix(bytes, streamFault == "stream-cancel")) });
        }
    }
    sealed class Dialogs : IGameDownloadDialogs
    {
        public string? Error;
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(true);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string gamePath, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) => Task.FromResult<LinuxWindowsRunnerConfig?>(null);
        public Task ShowRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string title) { Error = message; return Task.CompletedTask; }
        public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) => Task.FromResult(true);
    }
}
