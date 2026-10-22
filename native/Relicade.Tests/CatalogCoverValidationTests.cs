using System.Text.Json;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogCoverValidationTests
{
    [Theory]
    [InlineData("http://cdn2.steamgriddb.com/grid/11111111111111111111111111111111.png")]
    [InlineData("https://evil.test/grid/11111111111111111111111111111111.png")]
    [InlineData("https://cdn2.steamgriddb.com.evil.test/grid/11111111111111111111111111111111.png")]
    [InlineData("https://user@cdn2.steamgriddb.com/grid/11111111111111111111111111111111.png")]
    [InlineData("https://cdn2.steamgriddb.com:443/grid/11111111111111111111111111111111.png")]
    [InlineData("https://cdn2.steamgriddb.com/grid/11111111111111111111111111111111.png?secret=value")]
    [InlineData("https://cdn2.steamgriddb.com/grid/../11111111111111111111111111111111.png")]
    [InlineData("file:///tmp/image.png")]
    [InlineData("/tmp/image.png")]
    [InlineData("<img src=x>")]
    public void Unsupported_remote_reference_is_not_eligible(string url)
    {
        Assert.False(new CatalogCover("entry", url, null, "header").IsValid);
        Assert.Null(CatalogCover.FromEntry(new() { Id = "entry", LibraryArt = new() { Header = url } }));
    }

    [Fact]
    public void Valid_capsule_fallback_retains_exact_identity_and_borrowed_icon_policy()
    {
        var cover = CatalogCover.FromEntry(new() { Id = "entry", Artwork = "borrowed", ArtworkFromGame = true,
            LibraryArt = new() { Header = "https://unsupported.test/header.png", Capsule = CatalogLibraryCoverTests.Header } });
        Assert.Equal(new CatalogCover("entry", CatalogLibraryCoverTests.Header, null, "capsule"), cover);
        Assert.True(cover!.Matches(new() { CatalogEntryId = "entry" }));
        Assert.False(cover.Matches(new() { CatalogEntryId = "Entry" }));
        Assert.False(cover.Matches(new() { CatalogEntryId = "entry", GameIconUrl = "player-edited" }));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"entryId\":\"entry\"}")]
    [InlineData("{\"entryId\":\"entry\",\"url\":\"bad\",\"iconUrl\":null,\"variant\":\"header\"}")]
    [InlineData("{\"entryId\":\"entry\",\"entryId\":\"other\",\"url\":\"bad\",\"iconUrl\":null,\"variant\":\"header\"}")]
    public void Unsupported_metadata_is_ignored_for_display_but_refused_for_mutation(string value)
    {
        using var doc = JsonDocument.Parse("{\"catalogCover\":" + value + "}");
        Assert.Null(CatalogCover.Read(doc.RootElement, false));
        Assert.Throws<JsonException>(() => CatalogCover.Read(doc.RootElement, true));
    }

    [Fact]
    public void Cover_serialization_does_not_persist_runtime_validity()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new CatalogCover("entry", CatalogLibraryCoverTests.Header, null, "header")));
        Assert.False(json.RootElement.TryGetProperty("IsValid", out _));
        Assert.Equal(4, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void Runtime_card_properties_are_excluded_from_the_serializer_contract()
    {
        var properties = JsonSerializerOptions.Default.GetTypeInfo(typeof(GameInfo)).Properties.Where(p => p.Get != null).Select(p => p.Name).ToArray();
        Assert.DoesNotContain("CardImageUrl", properties);
        Assert.DoesNotContain("CardArtworkSource", properties);
    }

    [Fact]
    public async Task Unsupported_saved_cover_refuses_mutation_without_rewriting_library()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-cover-shape-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"apps\":[{\"appName\":\"Synthetic\",\"githubRepo\":\"fixture/game\",\"folderName\":\"Fixture\",\"catalogCover\":{\"futureField\":true}}]}");
            var path = Path.Combine(root, "apps.json"); await File.WriteAllBytesAsync(path, bytes);
            var service = new AppCatalogService(dataDirectory: root);
            await Assert.ThrowsAsync<JsonException>(() => service.LoadLocalAppsForMutationAsync());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Sync_preserves_player_icon_and_copies_cover_with_exact_binding()
    {
        var game = new GameInfo { Name = "Game", CatalogEntryId = "entry", GameIconUrl = "player-icon",
            CatalogSnapshot = new("Game", null, "old-icon", []) };
        Assert.True(CatalogLibrarySync.Apply(game, new() { Id = "entry", Name = "Game", Artwork = CatalogLibraryCoverTests.Icon,
            LibraryArt = new() { Header = CatalogLibraryCoverTests.Header } }));
        Assert.Equal("player-icon", game.GameIconUrl);
        Assert.False(game.CatalogCover!.Matches(game));
        var saved = new GameInfo(); CatalogLibrarySync.CopyTo(game, saved);
        Assert.Equal(game.CatalogCover, saved.CatalogCover);
        Assert.Equal("entry", saved.CatalogEntryId);
        Assert.Equal("player-icon", saved.GameIconUrl);
    }
}
