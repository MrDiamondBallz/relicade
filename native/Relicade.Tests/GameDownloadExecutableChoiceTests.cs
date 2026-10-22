using System.IO.Compression;
using System.Net;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class GameDownloadExecutableChoiceTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "download-choice-" + Guid.NewGuid().ToString("N"));
    public GameDownloadExecutableChoiceTests() { Directory.CreateDirectory(root); GitHubApiCache.Initialize(Path.Combine(root, "cache")); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task Player_can_choose_between_staged_launch_files_before_first_activation()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "game.x86_64", "editor.x86_64" })
            { using var file = zip.CreateEntry(name).Open(); file.Write(PortableInstallationFixtures.NativePayload("owned synthetic " + name)); }
        var handler = new Handler(archive.ToArray()); using var client = new HttpClient(handler);
        var game = new GameInfo { Name = "Synthetic multi-target package", Repository = "fixture/game", FolderName = "Game" };
        var dialogs = new Dialogs();
        var release = new GitHubRelease { tag_name = "v1", assets = [new GitHubAsset { name = "game-linux-x64.zip", browser_download_url = "https://example.invalid/package.zip" }] };
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, release,
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease);
        Assert.True(dialogs.Error == null, dialogs.Error);
        Assert.Equal(1, dialogs.Choices); Assert.Equal(1, handler.Calls);
        Assert.Equal(GameStatus.Installed, game.Status);
        var manifest = new PortableInstallationTransaction(game.GetInstallPath(root)).ReadCommitted();
        Assert.NotNull(manifest); Assert.Equal("game.x86_64", manifest.SelectedExecutable);
        Assert.False(File.Exists(Path.Combine(game.GetInstallPath(root), "LastPlayed.txt")));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Declining_or_automatic_installation_does_not_activate_or_show_an_error(bool automatic)
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "game.x86_64", "editor.x86_64" })
            { using var file = zip.CreateEntry(name).Open(); file.Write(PortableInstallationFixtures.NativePayload("owned synthetic " + name)); }
        var handler = new Handler(archive.ToArray()); using var client = new HttpClient(handler);
        var game = new GameInfo { Name = "Synthetic cancelled package", Repository = "fixture/game", FolderName = "Game" };
        var dialogs = new Dialogs { Decline = true };
        var release = new GitHubRelease { tag_name = "v1", assets = [new GitHubAsset { name = "game-linux-x64.zip", browser_download_url = "https://example.invalid/package.zip" }] };
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, release,
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled,
            automatic ? new AutomaticGameDownloadDialogs(dialogs) : dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease);
        Assert.Null(dialogs.Error); Assert.Equal(automatic ? 0 : 1, dialogs.Choices); Assert.Equal(1, handler.Calls);
        Assert.Equal(GameStatus.NotInstalled, game.Status);
        Assert.False(File.Exists(Path.Combine(game.GetInstallPath(root), PortableInstallationTransaction.ManifestFileName)));
    }

    sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Assert.Equal("example.invalid", request.RequestUri!.Host); Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
    sealed class Dialogs : IGameDownloadDialogs
    {
        public int Choices; public string? Error; public bool Decline;
        public Task<string?> ChoosePortableExecutableAsync(string appName, IReadOnlyList<string> candidates, CancellationToken token)
        { Choices++; Assert.Equal(new[] { "editor.x86_64", "game.x86_64" }, candidates); return Task.FromResult<string?>(Decline ? null : "game.x86_64"); }
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(false);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string p, LinuxWindowsRunnerConfig? c = null, bool isInstall = true) => throw new Exception("Unexpected runner configuration");
        public Task ShowRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowErrorAsync(string m, string t) { Error = t + ": " + m; return Task.CompletedTask; }
        public Task<bool> ConfirmUnverifiedReleaseAsync(string a, string v, ReleaseCheck c) => Task.FromResult(true);
    }
}
