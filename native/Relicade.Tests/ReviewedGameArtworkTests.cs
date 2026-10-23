using Relicade.Services;
using Avalonia.Media.Imaging;
using AsyncImageLoader;
using Avalonia.Headless.XUnit;
using Relicade.Models;
using Relicade.ViewModels;
namespace Relicade.Tests;
public sealed class ReviewedGameArtworkTests
{
    internal const string EntryId = "k177mxt12q61yv9fsvf6wvh1pn8f4n2n";
    internal const string Image = "avares://Relicade/Assets/GameArt/taisei-elly-afensorm-cover.png";
    internal static QuiverCatalogApp Entry() => new() { Id = EntryId, Slug = "taisei-project", Name = "Taisei Project", ProjectName = "Taisei Project",
        ProjectType = "games", Launcher = new() { FolderName = "Taisei" } };
    [Fact]
    public void Exact_catalog_game_has_reviewed_art_without_a_remote_art_hint()
    {
        var entry = Entry();
        Assert.Equal(Image, BrowseItem.FromCatalog(entry, new Dictionary<string,string>()).ImageUrl);
        var cover = CatalogCover.FromEntry(entry);
        Assert.NotNull(cover); Assert.Equal(Image, cover.Url);
    }
    [Theory]
    [InlineData(Image)]
    [InlineData("  " + Image)]
    [InlineData("AVARES://Relicade/Assets/GameArt/taisei-elly-afensorm-cover.png")]
    public void Other_game_cannot_claim_reviewed_asset_by_supplying_its_resource_url(string supplied)
    {
        var entry = Entry(); entry.Id = "another-game"; entry.LibraryArt = new() { Header = supplied };
        Assert.Null(CatalogCover.FromEntry(entry));
        Assert.NotEqual(Image, BrowseItem.FromCatalog(entry, new Dictionary<string,string>()).ImageUrl);
    }
    [AvaloniaFact]
    public async Task Actual_catalog_add_save_reload_keeps_credited_art_offline_without_provider_requests()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-reviewed-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var previous = ImageLoader.AsyncImageLoader;
        var handler = new RejectNetwork(); using var http = new HttpClient(handler);
        using var loader = new LauncherArtworkLoader(root, http); ImageLoader.AsyncImageLoader = loader;
        var store = new Store(root); var catalog = new AppCatalogService(dataDirectory: root);
        using var manager = new GameManager(store, http, catalog); await using var session = new LauncherSession();
        try
        {
            var external = QuiverCatalogMapping.ToGameInfo(catalog, Entry(), new() { Provider = "github", Repository = "taisei-project/taisei" });
            var service = new LibraryAddService(manager, new SettingsViewModel(store), session);
            var result = await service.AddAsync(external);
            Assert.Equal(LibraryAddOutcome.Added, result.Outcome); Assert.Equal(Image, result.App!.CardImageUrl);
            Assert.Contains("afensorm", result.App.CardArtworkSource); Assert.Contains("CC BY 4.0", result.App.CardArtworkSource);
            Assert.False(result.App.IsInstalled); Assert.Equal(0, handler.ImageCalls);
            var loaded = Assert.Single(await catalog.LoadLocalAppsForMutationAsync());
            await loaded.LoadCatalogCoverAsync(root, false, TestContext.Current.CancellationToken);
            Assert.Equal(Image, loaded.CardImageUrl); Assert.Equal(0, handler.ImageCalls);
            Assert.Empty(Directory.Exists(Path.Combine(root, "Images")) ? Directory.GetFiles(Path.Combine(root, "Images")) : []);
            loaded.Repository = "another/game";
            Assert.Equal(loaded.IconUrl, loaded.CardImageUrl); Assert.Empty(loaded.CardArtworkSource);
        }
        finally { await session.DisposeAsync(); ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
        Assert.Equal(0, handler.ImageCalls);
    }
    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("identity")]
    [InlineData("repository")]
    [InlineData("icon")]
    [InlineData("custom")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task Late_or_failed_embedded_result_never_replaces_changed_player_art(string change)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-reviewed-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var previous = ImageLoader.AsyncImageLoader;
        var pending = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fake = new PendingImages(pending.Task); ImageLoader.AsyncImageLoader = fake;
        using var cancellation = new CancellationTokenSource();
        using var bitmap = ReviewedGameArtwork.LoadTaisei();
        using var game = new GameInfo { CatalogEntryId = EntryId, Repository = "taisei-project/taisei", CatalogCover = CatalogCover.FromEntry(Entry()) };
        try
        {
            var loading = game.LoadCatalogCoverAsync(root, false, cancellation.Token);
            Assert.Equal(1, fake.Calls);
            if (change == "cancel") cancellation.Cancel();
            if (change == "identity") game.CatalogEntryId = "another";
            if (change == "repository") game.Repository = "another/game";
            if (change == "icon") game.GameIconUrl = "player-edited";
            if (change == "custom") { var file = Path.Combine(root, "player.png"); bitmap.Save(file); game.CustomIconPath = file; }
            if (change == "corrupt") pending.SetException(new InvalidDataException("Rejected embedded representation"));
            else pending.SetResult(change == "missing" ? null : bitmap);
            if (change == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading); else await loading;
            Assert.Equal(game.IconUrl, game.CardImageUrl); Assert.Empty(game.CardArtworkSource);
        }
        finally { ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("other", "taisei-project")]
    [InlineData(EntryId, "other")]
    [InlineData(EntryId, "TAISEI-PROJECT")]
    public void Reviewed_art_requires_exact_catalog_identity(string id, string slug)
    {
        var entry = Entry(); entry.Id = id; entry.Slug = slug;
        Assert.Null(ReviewedGameArtwork.ForEntry(entry)); Assert.Null(CatalogCover.FromEntry(entry));
    }
    sealed class PendingImages(Task<Bitmap?> task) : IAsyncImageLoader
    {
        public int Calls;
        public Task<Bitmap?> ProvideImageAsync(string url) { Calls++; Assert.Equal(Image, url); return task; }
        public void Dispose() { }
    }
    sealed class RejectNetwork : HttpMessageHandler
    {
        public int Calls, ImageCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; if (!(request.RequestUri!.AbsolutePath.EndsWith("/release-status") || request.RequestUri.AbsolutePath.EndsWith("/apps") || request.RequestUri.AbsolutePath.EndsWith("/apps/taisei-project/releases") || request.RequestUri.AbsolutePath.EndsWith("/repos/taisei-project/taisei/releases"))) ImageCalls++; throw new InvalidOperationException("Artwork fixture forbids external requests."); }
    }
    sealed class Store(string root) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true,
            AppsPath = Path.Combine(root, "Apps"), AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings value) { }
    }
}
