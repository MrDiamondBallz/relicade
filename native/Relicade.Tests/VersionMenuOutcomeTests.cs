using System.Net;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class VersionMenuOutcomeTests
{
    [AvaloniaTheory]
    [InlineData("declined", false)]
    [InlineData("declined", true)]
    [InlineData("network-failure", false)]
    [InlineData("network-failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("cancelled", true)]
    [InlineData("installed", false)]
    [InlineData("installed", true)]
    [InlineData("already-installed", false)]
    [InlineData("already-installed", true)]
    [InlineData("post-commit-error", false)]
    [InlineData("post-commit-error", true)]
    public async Task Version_preferences_require_a_confirmed_chosen_installation(string outcome, bool priorPin)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-menu-outcome-" + Guid.NewGuid().ToString("N"));
        var previous = RelicadePaths.OverrideUserDataRoot; var previousSettings = SettingsStoreProvider.Default;
        Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.Platform = TargetOS.Windows; store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Save(store.Current); SettingsStoreProvider.Default = store;
            await using var session = new LauncherSession();
            Task? drain = null; var downloads = 0;
            using var client = new HttpClient(new Handler(request =>
            {
                if (request.RequestUri!.Host == "api.quiverlauncher.com") return new(HttpStatusCode.NotFound);
                request.RequestUri.Host.Should().Be("example.test"); downloads++;
                if (outcome == "cancelled")
                {
                    drain = session.DisposeAsync().AsTask();
                    throw new OperationCanceledException(session.Token);
                }
                if (outcome is "installed" or "post-commit-error")
                {
                    using var zipBytes = new MemoryStream();
                    using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, true))
                    using (var file = zip.CreateEntry("game.exe").Open()) file.Write([0x4d, 0x5a, 0x90, 0]);
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes.ToArray()) };
                }
                throw new HttpRequestException("Synthetic connection failure.");
            }));
            using var manager = new GameManager(store, client, new AppCatalogService(dataDirectory: root));
            const string chosen = "v1.0.0-prerelease+37";
            var installed = outcome == "already-installed" ? chosen : "v1.0.0-prerelease+39";
            var game = new GameInfo { Name = "Menu fixture", FolderName = "game", Repository = "fixture/" + Guid.NewGuid().ToString("N"),
                GameManager = manager, CatalogEntryId = outcome == "declined" ? "menu-fixture" : null,
                InstalledVersion = installed, LatestVersion = installed, PreferredVersion = priorPin ? installed : null, Status = GameStatus.Installed };
            var folder = game.GetInstallPath(manager.GamesFolder);
            var original = Path.Combine(root, "original.exe");
            File.WriteAllText(original, "Original synthetic bytes; never executed.");
            await GameInstallationService.InstallOrUpdateGameAsync(original, folder, "game.exe", installed);
            await manager.CatalogService.SaveLocalAppsAsync([game]);
            var injectedPostCommitFailure = false;
            manager.PropertyChanged += (_, args) =>
            {
                if (outcome == "post-commit-error" && !injectedPostCommitFailure && args.PropertyName == nameof(GameManager.Games) &&
                    File.ReadAllText(Path.Combine(folder, "version.txt")).Trim() == chosen)
                { injectedPostCommitFailure = true; throw new IOException("Synthetic notification failure after activation."); }
            };
            var settings = new SettingsViewModel(store);
            var controller = new LibraryLaunchController(manager, settings, session, new LibraryPersistenceService(manager),
                (_, anchor) => anchor, (_, _) => throw new Exception("Unexpected picker"),
                (_, _) => throw new Exception("Unexpected controller prompt"), _ => { }, () => { }, _ => { }, () => 900);
            var release = new GitHubRelease { tag_name = chosen, assets = [new() { name = "game-windows.zip", browser_download_url = "https://example.test/game-windows.zip" }] };
            await controller.ShowReleaseDownloadSelectionMenuAsync(new Button(), game, release, chosen, installed);
            if (drain != null) await drain;
            downloads.Should().Be(outcome is "declined" or "already-installed" ? 0 : 1);
            var activated = outcome is "installed" or "post-commit-error";
            var actualVersion = activated ? chosen : installed;
            File.ReadAllText(Path.Combine(folder, "version.txt")).Trim().Should().Be(actualVersion);
            if (activated) File.ReadAllBytes(Path.Combine(folder, "game.exe")).Should().Equal([0x4d, 0x5a, 0x90, 0]);
            else File.ReadAllText(Path.Combine(folder, "game.exe")).Should().Be("Original synthetic bytes; never executed.");
            game.InstalledVersion.Should().Be(actualVersion);
            injectedPostCommitFailure.Should().Be(outcome == "post-commit-error");
            var confirmed = outcome is "installed" or "already-installed";
            var expectedPin = confirmed ? chosen : priorPin ? installed : null;
            game.PreferredVersion.Should().Be(expectedPin,
                "only the positively completed chosen installation authorizes a new version preference");
            var saved = (await manager.CatalogService.LoadLocalAppsAsync()).Should().ContainSingle().Subject;
            saved.PreferredVersion.Should().Be(expectedPin);
        }
        finally { SettingsStoreProvider.Default = previousSettings; RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
