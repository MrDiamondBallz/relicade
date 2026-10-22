using System.Text.Json;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogLibraryCoverTests
{
    internal const string Header = "https://cdn2.steamgriddb.com/grid/11111111111111111111111111111111.png";
    internal const string Icon = "https://cdn2.steamgriddb.com/icon/22222222222222222222222222222222.png";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_cover_survives_add_save_and_reload_without_replacing_app_icon(bool borrowedIcon)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new AppCatalogService(dataDirectory: root);
            var entry = new QuiverCatalogApp
            {
                Id = "entry-fixture", Name = "Synthetic game", ProjectName = "Synthetic port", Slug = "fixture",
                Artwork = Icon, ArtworkFromGame = borrowedIcon, LibraryArt = new() { Header = Header },
                Launcher = new() { FolderName = "Fixture" }
            };
            var external = QuiverCatalogMapping.ToGameInfo(catalog, entry, new() { Provider = "github", Repository = "fixture/game" });
            var planned = LibraryAddService.PlanAdd([], external, false);
            Assert.Equal(LibraryAddOutcome.Added, planned.Outcome);
            Assert.Equal(borrowedIcon ? null : Icon, planned.App!.GameIconUrl);
            await catalog.SaveLocalAppsAsync(planned.Library);
            using (var first = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "apps.json"))))
            {
                var cover = first.RootElement.GetProperty("apps")[0].GetProperty("catalogCover");
                Assert.Equal(Header, cover.GetProperty("url").GetString());
                Assert.Equal("entry-fixture", cover.GetProperty("entryId").GetString());
                Assert.Equal(borrowedIcon ? null : Icon, cover.GetProperty("iconUrl").GetString());
            }
            var loaded = await catalog.LoadLocalAppsForMutationAsync();
            Assert.Equal(borrowedIcon ? null : Icon, Assert.Single(loaded).GameIconUrl);
            await catalog.SaveLocalAppsAsync(loaded);
            using var second = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "apps.json")));
            Assert.Equal(Header, second.RootElement.GetProperty("apps")[0].GetProperty("catalogCover").GetProperty("url").GetString());
        }
        finally { Directory.Delete(root, true); }
    }
}
