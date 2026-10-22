using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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

public sealed class UpdateOutcomeTests
{
    [AvaloniaTheory]
    [InlineData("missing-digest", false)][InlineData("missing-digest", true)]
    [InlineData("declined", false)][InlineData("declined", true)]
    [InlineData("network-failure", false)][InlineData("network-failure", true)]
    [InlineData("cancelled", false)][InlineData("cancelled", true)]
    [InlineData("installed", false)][InlineData("installed", true)]
    [InlineData("post-commit-error", false)][InlineData("post-commit-error", true)]
    [InlineData("closed-after-activation", false)][InlineData("closed-after-activation", true)]
    public Task Update_outcome_and_preferences_require_confirmed_completion(string outcome, bool priorPin) => VerifyOutcome(outcome, priorPin, false);

    [AvaloniaTheory]
    [InlineData("missing-digest", false)][InlineData("missing-digest", true)]
    [InlineData("declined", false)][InlineData("declined", true)]
    [InlineData("network-failure", false)][InlineData("network-failure", true)]
    [InlineData("cancelled", false)][InlineData("cancelled", true)]
    [InlineData("installed", false)][InlineData("installed", true)]
    [InlineData("post-commit-error", false)][InlineData("post-commit-error", true)]
    [InlineData("closed-after-activation", false)][InlineData("closed-after-activation", true)]
    public Task Automatic_update_requires_confirmed_completion(string outcome, bool priorPin) => VerifyOutcome(outcome, priorPin, true);

    [AvaloniaTheory]
    [InlineData("changed-preferences", false)][InlineData("changed-preferences", true)]
    [InlineData("changed-preferences-failure", false)][InlineData("changed-preferences-failure", true)]
    [InlineData("cleared-skip", false)][InlineData("cleared-skip", true)]
    public Task Independent_preferences_during_transfer_remain_authoritative(string outcome, bool priorPin) => VerifyOutcome(outcome, priorPin, false);

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public Task Already_current_update_does_not_count_or_clear_preferences(bool priorPin) => VerifyOutcome("already-current", priorPin, true);

    private static async Task VerifyOutcome(string outcome, bool priorPin, bool automatic)
    {
        const string chosen = "2.0", skipped = "1.5", assetName = "game-windows.zip";
        var installed = outcome == "already-current" ? chosen : "1.0";
        var root = Path.Combine(Path.GetTempPath(), "relicade-update-outcome-" + Guid.NewGuid().ToString("N"));
        var previous = RelicadePaths.OverrideUserDataRoot; var previousSettings = SettingsStoreProvider.Default;
        Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.Platform = TargetOS.Windows; store.Current.AppsPath = Path.Combine(root, "Apps"); store.Save(store.Current); SettingsStoreProvider.Default = store;
            await using var session = new LauncherSession();
            Task? drain = null; var downloads = 0; var notices = new List<string>(); var unexpected = new List<string>();
            var repository = "fixture/" + Guid.NewGuid().ToString("N"); var payload = Archive();
            GameInfo? activeGame = null; GameManager? activeManager = null;
            using var client = new HttpClient(new Handler(async request =>
            {
                var uri = request.RequestUri!;
                if (uri.Host == "api.quiverlauncher.com")
                {
                    object[] items = uri.AbsolutePath switch
                    {
                        "/api/v1/release-status" => [new { id = "update-fixture", slug = "update-fixture", provider = "github", repository, verified = new { version = chosen } }],
                        "/api/v1/apps" => [new { id = "update-fixture", slug = "update-fixture", launcher = new { folderName = "game" } }],
                        "/api/v1/apps/update-fixture/release-history" => [new { version = chosen, state = outcome == "declined" ? "unverified" : "verified", reasons = Array.Empty<string>(),
                            assets = new[] { new { filename = assetName, url = "https://files.example/game-windows.zip",
                                checksum = outcome == "missing-digest" ? null : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload)) } } }],
                        _ => Unexpected(uri.ToString())
                    };
                    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items, isDone = true })) };
                }
                if (uri.Host != "files.example") { unexpected.Add(uri.ToString()); throw new IOException("Unexpected request."); }
                downloads++;
                if (outcome is "changed-preferences" or "changed-preferences-failure" or "cleared-skip")
                {
                    activeGame!.SetVersionPreferences("4.0", outcome == "cleared-skip" ? null : "5.0");
                    await activeManager!.CatalogService.SaveLocalAppsAsync([activeGame]);
                    if (outcome == "changed-preferences-failure") throw new HttpRequestException("Synthetic failure after an independent preference edit.");
                }
                if (outcome == "network-failure") throw new HttpRequestException("Synthetic download failure.");
                if (outcome == "cancelled") { drain = session.DisposeAsync().AsTask(); throw new OperationCanceledException(session.Token); }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            }));
            object[] Unexpected(string path) { unexpected.Add(path); throw new IOException("Unexpected catalog path."); }
            using var manager = new GameManager(store, client, new AppCatalogService(dataDirectory: root));
            var game = new GameInfo { Name = "Update fixture", FolderName = "game", Repository = repository, CatalogEntryId = "update-fixture", GameManager = manager,
                InstalledVersion = installed, LatestVersion = "3.0", PreferredVersion = priorPin ? chosen : null, SkippedUpdateVersion = skipped, Status = GameStatus.Installed };
            activeGame = game; activeManager = manager;
            var folder = game.GetInstallPath(manager.GamesFolder); var original = Path.Combine(root, "original.exe");
            File.WriteAllBytes(original, [0x4d, 0x5a, 1, 0]);
            await GameInstallationService.InstallOrUpdateGameAsync(original, folder, "game.exe", installed);
            await manager.CatalogService.SaveLocalAppsAsync([game]);
            var before = (await manager.CatalogService.LoadLocalAppsAsync()).Should().ContainSingle().Subject;
            before.SkippedUpdateVersion.Should().Be(skipped);
            before.PreferredVersion.Should().Be(priorPin ? chosen : null);
            game.SkippedUpdateVersion.Should().Be(skipped);
            game.PreferredVersion.Should().Be(priorPin ? chosen : null);
            var activationObserved = false;
            manager.PropertyChanged += (_, args) =>
            {
                if (!activationObserved && args.PropertyName == nameof(GameManager.Games) && File.ReadAllText(Path.Combine(folder, "version.txt")).Trim() == chosen)
                {
                    activationObserved = true;
                    if (outcome == "post-commit-error") throw new IOException("Synthetic notification failure after activation.");
                    if (outcome == "closed-after-activation") drain = session.DisposeAsync().AsTask();
                }
            };
            var controller = new LibraryLaunchController(manager, new SettingsViewModel(store), session, new LibraryPersistenceService(manager),
                (_, anchor) => anchor, (_, _) => throw new Exception("Unexpected picker"), (text, _) => { notices.Add(text); return Task.CompletedTask; },
                _ => { }, () => { }, _ => { }, () => 900);
            var result = await controller.HandleUpdateNowAsync(new Button(), game, preferAutoPlatform: automatic, allowAssetPicker: !automatic, interactive: !automatic && outcome != "missing-digest");
            if (drain != null) await drain;
            unexpected.Should().BeEmpty();
            downloads.Should().Be(outcome is "missing-digest" or "declined" or "already-current" ? 0 : 1);
            var activated = outcome is "installed" or "post-commit-error" or "closed-after-activation" or "changed-preferences" or "cleared-skip";
            File.ReadAllText(Path.Combine(folder, "version.txt")).Trim().Should().Be(activated ? chosen : installed);
            File.ReadAllBytes(Path.Combine(folder, "game.exe")).Should().Equal(activated ? new byte[] { 0x4d, 0x5a, 2, 0 } : [0x4d, 0x5a, 1, 0]);
            game.InstalledVersion.Should().Be(activated ? chosen : installed);
            var confirmed = outcome is "installed" or "changed-preferences" or "cleared-skip";
            var changedPreference = outcome is "changed-preferences" or "changed-preferences-failure" or "cleared-skip";
            var expectedSkip = changedPreference ? outcome == "cleared-skip" ? null : "5.0" : confirmed ? null : skipped;
            var saved = (await manager.CatalogService.LoadLocalAppsAsync()).Should().ContainSingle().Subject;
            saved.SkippedUpdateVersion.Should().Be(expectedSkip);
            game.SkippedUpdateVersion.Should().Be(expectedSkip);
            var expectedPin = changedPreference ? "4.0" : priorPin && !confirmed ? chosen : null;
            saved.PreferredVersion.Should().Be(expectedPin); game.PreferredVersion.Should().Be(expectedPin);
            result.Should().Be(confirmed, "refusal, failure and cancelled participation are not an acknowledged update");
            if (priorPin && !confirmed && !changedPreference)
            {
                game.LatestVersion = "3.0";
                game.LatestVersion = chosen;
                game.PreferredVersion.Should().BeNull("temporary preference retention must end with the operation");
            }
        }
        finally { SettingsStoreProvider.Default = previousSettings; RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }
    static byte[] Archive()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("game.exe"); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var file = entry.Open(); file.Write([0x4d, 0x5a, 2, 0]);
        }
        return bytes.ToArray();
    }
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return respond(request); }
    }
}
