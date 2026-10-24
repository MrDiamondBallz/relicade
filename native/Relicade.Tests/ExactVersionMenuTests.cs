using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class ExactVersionMenuTests
{
    [AvaloniaTheory]
    [InlineData(false, false, "states")][InlineData(false, true, "states")]
    [InlineData(true, false, "states")][InlineData(true, true, "states")]
    [InlineData(false, false, "identity")][InlineData(false, true, "identity")]
    [InlineData(true, false, "identity")][InlineData(true, true, "identity")]
    [InlineData(false, false, "duplicate-blocked")][InlineData(true, false, "duplicate-blocked")]
    [InlineData(false, false, "duplicate-unverified")][InlineData(true, false, "duplicate-unverified")]
    public async Task Version_menu_keeps_case_distinct_release_badges_separate(bool cached, bool reversed, string assertion)
    {
        const string selected = "v1.0.0+Build", other = "v1.0.0+build";
        var root = Path.Combine(Path.GetTempPath(), "relicade-menu-identity-" + Guid.NewGuid().ToString("N"));
        var previous = RelicadePaths.OverrideUserDataRoot; var previousSettings = SettingsStoreProvider.Default;
        Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
        Window? window = null; ContextMenu? menu = null;
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.AppsPath = Path.Combine(root, "Apps"); store.Save(store.Current); SettingsStoreProvider.Default = store;
            await using var session = new LauncherSession();
            var unexpected = new List<string>(); var repository = "fixture/" + Guid.NewGuid().ToString("N");
            var duplicate = assertion.StartsWith("duplicate-", StringComparison.Ordinal);
            string[] tags = duplicate ? [selected, selected] : reversed ? [other, selected] : [selected, other];
            using var client = new HttpClient(new Handler(request =>
            {
                var uri = request.RequestUri!;
                if (uri.Host != "api.quiverlauncher.com") { unexpected.Add(uri.ToString()); throw new IOException("Unexpected transport destination."); }
                object[] items = uri.AbsolutePath switch
                {
                    "/api/v1/release-status" => [new { id = "menu-fixture", slug = "menu-fixture", provider = "github", repository, verified = new { version = other } }],
                    "/api/v1/apps" => [new { id = "menu-fixture", slug = "menu-fixture", launcher = new { folderName = "game" } }],
                    "/api/v1/apps/menu-fixture/release-history" => tags.Select((tag, index) => (object)new { version = tag, state = duplicate ? index == 0 ? "verified" : assertion == "duplicate-blocked" ? "blocked" : "unverified" : tag == selected ? "blocked" : "verified", reasons = Array.Empty<string>(),
                        assets = new[] { new { filename = "game.zip", url = "https://files.example/game.zip", checksum = "sha256:" + new string('a', 64) } } }).ToArray(),
                    _ => Unexpected(uri.ToString())
                };
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items, isDone = true })) };
            }));
            object[] Unexpected(string path) { unexpected.Add(path); throw new IOException("Unexpected catalog request."); }
            using var manager = new GameManager(store, client, new AppCatalogService(dataDirectory: root));
            var game = new GameInfo { Name = "Menu fixture", FolderName = "game", Repository = repository, CatalogEntryId = "menu-fixture", GameManager = manager,
                InstalledVersion = selected, LatestVersion = selected, PreferredVersion = selected, Status = GameStatus.Installed };
            var button = new Button { Content = "Versions" }; window = new Window { Content = button, Width = 900, Height = 700 }; window.Show();
            var controller = new LibraryLaunchController(manager, new SettingsViewModel(store), session, new LibraryPersistenceService(manager),
                (_, anchor) => anchor, (anchor, opened) => { menu = opened; GamepadContextMenuNavigation.Attach(opened); opened.Open(anchor); },
                (message, title) => throw new Exception(title + ": " + message), _ => { }, () => { }, _ => { }, () => 900);
            if (cached)
            {
                await game.FetchReleasesAsync(client, session.Token);
                var snapshot = manager.CatalogReleases.Cached(game)!.Value;
                controller.ShowVersionSelectionMenu(button, game, snapshot.Releases, snapshot.States);
            }
            else await controller.HandleChangeVersionAsync(button, game);
            menu.Should().NotBeNull(); menu!.IsOpen.Should().BeTrue(); unexpected.Should().BeEmpty();
            var headers = menu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString() ?? "").ToArray();
            if (duplicate)
            {
                var matching = headers.Where(x => x.StartsWith(selected + " (", StringComparison.Ordinal)).ToArray();
                matching.Should().HaveCount(2);
                foreach (var header in matching)
                    header.Should().Contain(assertion == "duplicate-blocked" ? "Blocked" : "Unverified").And.NotContain("Verified");
                return;
            }
            var selectedHeader = headers.Single(x => x.StartsWith(selected + " (", StringComparison.Ordinal));
            var otherHeader = headers.Single(x => x.StartsWith(other + " (", StringComparison.Ordinal));
            if (assertion == "states")
            {
                selectedHeader.Should().Contain("Blocked").And.NotContain("Verified");
                otherHeader.Should().Contain("Verified").And.NotContain("Blocked");
            }
            else
            {
                selectedHeader.Should().Contain("Latest").And.Contain("Installed").And.Contain("Preferred");
                otherHeader.Should().NotContain("Latest").And.NotContain("Installed").And.NotContain("Preferred");
            }
        }
        finally { menu?.Close(); window?.Close(); SettingsStoreProvider.Default = previousSettings; RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }
    [AvaloniaTheory]
    [InlineData("unique-alias", "Latest")]
    [InlineData("unique-alias", "Installed")]
    [InlineData("unique-alias", "Preferred")]
    [InlineData("ambiguous-alias", "Latest")]
    [InlineData("ambiguous-alias", "Installed")]
    [InlineData("ambiguous-alias", "Preferred")]
    [InlineData("exact-before-alias", "Latest")]
    [InlineData("exact-before-alias", "Installed")]
    [InlineData("exact-before-alias", "Preferred")]
    [InlineData("missing-build", "Latest")]
    [InlineData("missing-build", "Installed")]
    [InlineData("missing-build", "Preferred")]
    [InlineData("case-exact", "Latest")]
    [InlineData("case-exact", "Installed")]
    [InlineData("case-exact", "Preferred")]
    [InlineData("case-ambiguous", "Latest")]
    [InlineData("case-ambiguous", "Installed")]
    [InlineData("case-ambiguous", "Preferred")]
    [InlineData("empty", "Latest")]
    [InlineData("empty", "Installed")]
    [InlineData("empty", "Preferred")]
    public async Task Version_markers_use_exact_or_unique_legacy_identity(string scenario, string marker)
    {
        string? target = scenario switch { "unique-alias" => "1.0", "ambiguous-alias" => "01", "exact-before-alias" => "1",
            "missing-build" => "v1.0+Build", "case-exact" => "Release", "case-ambiguous" => "RELEASE", _ => null };
        string[] tags = scenario switch { "unique-alias" => ["v1.0"], "ambiguous-alias" or "exact-before-alias" => ["v1", "1"],
            "case-exact" or "case-ambiguous" => ["Release", "release"], _ => ["v1.0+Other"] };
        var expected = scenario switch { "unique-alias" => "v1.0", "exact-before-alias" => "1", "case-exact" => "Release", _ => null };
        var root = Path.Combine(Path.GetTempPath(), "relicade-menu-marker-" + Guid.NewGuid().ToString("N"));
        var previous = RelicadePaths.OverrideUserDataRoot; var previousSettings = SettingsStoreProvider.Default;
        Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
        Window? window = null; ContextMenu? menu = null;
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json")); store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Save(store.Current); SettingsStoreProvider.Default = store;
            await using var session = new LauncherSession();
            var requests = 0;
            using var client = new HttpClient(new Handler(_ => { requests++; throw new IOException("This menu must not request data."); }));
            using var manager = new GameManager(store, client, new AppCatalogService(dataDirectory: root));
            var game = new GameInfo { Name = "Marker fixture", LatestVersion = marker == "Latest" ? target : "9.0",
                InstalledVersion = marker == "Installed" ? target : "8.0", PreferredVersion = marker == "Preferred" ? target : null };
            var releases = tags.Select(tag => new GitHubRelease { tag_name = tag }).ToArray();
            var anchor = new Button { Content = "Versions" }; window = new Window { Content = anchor, Width = 900, Height = 700 }; window.Show();
            var controller = new LibraryLaunchController(manager, new SettingsViewModel(store), session, new LibraryPersistenceService(manager),
                (_, control) => control, (control, opened) => { menu = opened; GamepadContextMenuNavigation.Attach(opened); opened.Open(control); },
                (text, title) => throw new Exception(title + ": " + text), _ => { }, () => { }, _ => { }, () => 900);
            controller.ShowVersionSelectionMenu(anchor, game, releases);
            menu!.IsOpen.Should().BeTrue(); requests.Should().Be(0);
            foreach (var tag in tags)
            {
                var row = menu.Items.OfType<MenuItem>().Single(x => x.Header?.ToString() == tag ||
                    x.Header?.ToString()?.StartsWith(tag + " (", StringComparison.Ordinal) == true);
                var header = row.Header!.ToString()!;
                if (tag == expected) header.Should().Contain(marker);
                else header.Should().NotContain(marker);
                row.Classes.Contains("accent").Should().Be(marker == "Latest" && tag == expected);
            }
        }
        finally { menu?.Close(); window?.Close(); SettingsStoreProvider.Default = previousSettings; RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
