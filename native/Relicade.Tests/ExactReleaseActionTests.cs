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

public sealed class ExactReleaseActionTests
{
    const string Target = "v1.0.0-prerelease+37", Other = "v1.0.0-prerelease+39", Asset = "game-windows.zip";

    [AvaloniaTheory]
    [InlineData("reinstall")]
    [InlineData("unverified")]
    public async Task Named_release_actions_choose_the_exact_build_before_numeric_aliases(string action)
        => await Exercise(action, "exact");

    [AvaloniaTheory]
    [InlineData("reinstall", "missing")][InlineData("unverified", "missing")]
    [InlineData("reinstall", "case")][InlineData("unverified", "case")]
    [InlineData("reinstall", "legacy-single")][InlineData("unverified", "legacy-single")]
    [InlineData("reinstall", "legacy-ambiguous")][InlineData("unverified", "legacy-ambiguous")]
    [InlineData("reinstall", "exact-before-alias")][InlineData("unverified", "exact-before-alias")]
    [InlineData("reinstall", "declined")][InlineData("unverified", "declined")]
    [InlineData("reinstall", "unknown-followed")]
    [InlineData("reinstall", "unknown-label")][InlineData("reinstall", "unknown-zero")]
    [InlineData("reinstall", "unknown-vzero")][InlineData("reinstall", "zero-build")]
    [InlineData("reinstall", "sentinel-exact")][InlineData("reinstall", "sentinel-alias")]
    [InlineData("reinstall", "zero-build-missing")]
    public async Task Named_actions_preserve_target_identity_and_existing_files(string action, string scenario)
        => await Exercise(action, scenario);

    private static async Task Exercise(string action, string scenario)
    {
        var requested = scenario switch { "legacy-single" => "1.0", "legacy-ambiguous" => "01", "exact-before-alias" => "1", "zero-build" or "zero-build-missing" => "v0.0.0+37", "sentinel-exact" => "0.0.0", "sentinel-alias" => "v0.0.0", _ => Target };
        string[] tags = scenario switch
        {
            "sentinel-exact" or "sentinel-alias" => [Other, "0.0.0"],
            "zero-build-missing" => ["v0.0.0+39"],
            "zero-build" => ["v0.0.0+39", requested],
            "missing" => [Other], "case" => [Target.ToUpperInvariant()],
            "legacy-single" => ["v1.0"], "legacy-ambiguous" or "exact-before-alias" => ["v1", "1"],
            _ => [Other, Target]
        };
        string? expected = scenario switch
        {
            "missing" or "case" or "legacy-ambiguous" or "declined" or "zero-build-missing" => null,
            "legacy-single" => "v1.0", "exact-before-alias" => "1", "zero-build" => requested, "sentinel-exact" or "sentinel-alias" => "0.0.0", _ => Target
        };
        var root = Path.Combine(Path.GetTempPath(), "relicade-release-action-" + Guid.NewGuid().ToString("N"));
        var previous = RelicadePaths.OverrideUserDataRoot; var previousSettings = SettingsStoreProvider.Default;
        Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.Platform = TargetOS.Windows; store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Save(store.Current); SettingsStoreProvider.Default = store;
            await using var session = new LauncherSession();
            var notices = new List<(string Text, string Title)>();
            var downloads = new List<string>(); var unexpected = new List<string>(); var repository = "fixture/" + Guid.NewGuid().ToString("N");
            var payloads = tags.ToDictionary(tag => tag, tag => Archive(tag == expected ? (byte)0x37 : (byte)0x39), StringComparer.Ordinal);
            object History(string tag) => new { version = tag, state = scenario == "declined" && tag == requested ? "unverified" : "verified", reasons = Array.Empty<string>(),
                assets = new[] { new { filename = Asset, url = $"https://files.example/{tag}/{Asset}",
                    checksum = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payloads[tag])) } } };
            using var client = new HttpClient(new Handler(request =>
            {
                var uri = request.RequestUri!;
                if (uri.Host == "api.quiverlauncher.com")
                {
                    object[] items = uri.AbsolutePath switch
                    {
                        "/api/v1/release-status" => [new { id = "selector-fixture", slug = "selector-fixture", provider = "github", repository,
                            verified = new { version = tags[0] }, latestUpstream = new { version = requested } }],
                        "/api/v1/apps/selector-fixture/release-history" => tags.Select(History).ToArray(),
                        "/api/v1/apps" => [new { id = "selector-fixture", slug = "selector-fixture", launcher = new { folderName = "game" } }],
                        _ => Unexpected(uri.AbsolutePath)
                    };
                    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items, isDone = true })) };
                }
                if (uri.Host != "files.example") { unexpected.Add(uri.ToString()); throw new IOException("Unexpected synthetic transport destination."); }
                var tag = Uri.UnescapeDataString(uri.AbsolutePath.Split('/')[1]); downloads.Add(tag);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payloads[tag]) };
            }));
            using var manager = new GameManager(store, client, new AppCatalogService(dataDirectory: root));
            object[] Unexpected(string path) { unexpected.Add(path); throw new IOException("Unexpected synthetic catalog request: " + path); }
            var installed = scenario switch
            {
                "unknown-label" => "Unknown", "unknown-zero" => "0.0.0", "unknown-vzero" => "v0.0.0",
                _ => action == "reinstall" && scenario != "unknown-followed" ? requested : "v0.9.0"
            };
            var game = new GameInfo { Name = "Release action fixture", FolderName = "game", Repository = repository,
                CatalogEntryId = "selector-fixture", GameManager = manager, InstalledVersion = installed,
                LatestVersion = tags[0], CatalogUnverifiedVersion = action == "unverified" ? requested : null, Status = GameStatus.Installed };
            var folder = game.GetInstallPath(manager.GamesFolder); var original = Path.Combine(root, "original.exe");
            File.WriteAllBytes(original, [0x4d, 0x5a, 0x01]);
            await GameInstallationService.InstallOrUpdateGameAsync(original, folder, "game.exe", installed);
            await manager.CatalogService.SaveLocalAppsAsync([game]);
            if (scenario.StartsWith("unknown-", StringComparison.Ordinal)) game.PreferredVersion = requested;
            if (scenario == "unknown-followed") game.InstalledVersion = null;
            var controller = new LibraryLaunchController(manager, new SettingsViewModel(store), session, new LibraryPersistenceService(manager),
                (_, anchor) => anchor, (_, _) => throw new Exception("Unexpected picker"),
                (message, title) => { notices.Add((message, title)); return Task.CompletedTask; }, _ => { }, () => { }, _ => { }, () => 900);
            if (action == "reinstall") await controller.HandleReinstallAsync(new Button(), game);
            else await controller.HandleInstallUnverifiedAsync(new Button(), game);
            unexpected.Should().BeEmpty();
            if (scenario is "missing" or "case" or "legacy-ambiguous" or "zero-build-missing")
            {
                notices.Should().ContainSingle(); notices[0].Text.Should().Contain(requested);
            }
            else notices.Should().BeEmpty();
            if (expected == null) downloads.Should().BeEmpty();
            else downloads.Should().Equal(new[] { expected }, because: "a named action must select only its exact or unique legacy target");
            File.ReadAllText(Path.Combine(folder, "version.txt")).Trim().Should().Be(expected ?? installed);
            File.ReadAllBytes(Path.Combine(folder, "game.exe")).Should().Equal(new byte[] { 0x4d, 0x5a, expected == null ? (byte)0x01 : (byte)0x37 });
            game.InstalledVersion.Should().Be(expected ?? installed);
        }
        finally { SettingsStoreProvider.Default = previousSettings; RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }
    static byte[] Archive(byte value)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var file = zip.CreateEntry("game.exe").Open()) file.Write([0x4d, 0x5a, value]);
        return bytes.ToArray();
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
