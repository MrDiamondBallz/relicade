using System.Net;
using System.Text.Json;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.Models;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class ReviewedArtworkRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(1280, 860, false)]
    [InlineData(750, 490, true)]
    public async Task Actual_catalog_Add_then_Open_in_Library_keeps_reviewed_portrait(int width, int height, bool light)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-real-art-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previousRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var previousLoader = ImageLoader.AsyncImageLoader;
            using var handler = new CatalogOnly(); using var http = new HttpClient(handler);
            using var loader = new LauncherArtworkLoader(root, http); ImageLoader.AsyncImageLoader = loader;
            var store = new Store(root, light); var catalog = new AppCatalogService(dataDirectory: root);
            using var manager = new GameManager(store, http, catalog);
            var view = new MainView(RealRenderTestApp.Dependencies(store, manager));
            var window = new Window { Content = view, Width = width, Height = height };
            try
            {
                Assert.False(Telemetry.Current.Available);
                var browse = view.FindControl<BrowseView>("BrowsePanel")!;
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Loaded(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                { if (!browse.Model.IsLoading && browse.Model.Items.Count == 1) ready.TrySetResult(); }
                browse.Model.PropertyChanged += Loaded;
                window.Show(); Dispatcher.UIThread.RunJobs();
                view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                browse.Model.PropertyChanged -= Loaded;
                var item = Assert.Single(browse.Model.Items);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); window.UpdateLayout();
                GamepadFocusChrome.SetKeyboardNavigationActive(true);
                browse.Navigation.SelectInitial(bringIntoView: true);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); window.UpdateLayout();
                var card = Assert.Single(browse.GetVisualDescendants().OfType<BrowseCard>());
                var image = card.FindControl<Image>("BrowseCardArt")!;
                var imageReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
                { if (e.Property == Image.SourceProperty && image.Source != null) imageReady.TrySetResult(); }
                image.PropertyChanged += Changed;
                try { if (image.Source != null) imageReady.TrySetResult(); await imageReady.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { image.PropertyChanged -= Changed; }
                Assert.Contains("afensorm", item.ArtworkSource);
                await Capture(window, $"23-taisei-discover-{width}x{height}-light-{light}");
                var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void StateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                { if (!item.IsAdding && item.InLibrary) added.TrySetResult(); }
                item.PropertyChanged += StateChanged;
                try
                {
                    card.FindControl<Button>("BrowseCardAddButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await added.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { item.PropertyChanged -= StateChanged; }
                var game = Assert.Single(manager.LibraryApps);
                Assert.False(game.IsInstalled); Assert.Equal(ReviewedGameArtwork.TaiseiUrl, game.CardImageUrl);
                Assert.Equal(1, handler.DetailCalls); Assert.True(handler.UnexpectedCalls == 0, string.Join("; ", handler.Paths));
                card.FindControl<Button>("BrowseCardAddButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(view.FindControl<LibraryView>("LibraryPanel")!.IsEffectivelyVisible);
                await Capture(window, $"23-taisei-library-{width}x{height}-light-{light}");
                var saved = Assert.Single(await catalog.LoadLocalAppsForMutationAsync());
                await saved.LoadCatalogCoverAsync(root, false, TestContext.Current.CancellationToken);
                Assert.Equal(ReviewedGameArtwork.TaiseiUrl, saved.CardImageUrl);
                Assert.Equal(1, handler.DetailCalls); Assert.True(handler.UnexpectedCalls == 0, string.Join("; ", handler.Paths)); Assert.Contains("CC BY 4.0", saved.CardArtworkSource);
            }
            finally { GamepadFocusChrome.SetKeyboardNavigationActive(false); window.Close(); await view.ShutdownAsync(); ImageLoader.AsyncImageLoader = previousLoader; RelicadePaths.OverrideUserDataRoot = previousRoot; Directory.Delete(root, true); }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task Actual_decoder_full_size_and_thumbnail_are_bounded_local_resources()
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
        var root = Path.Combine(Path.GetTempPath(), "relicade-reviewed-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var previous = ImageLoader.AsyncImageLoader;
        using var network = new RejectImagesNetwork(); using var http = new HttpClient(network);
        using var loader = new LauncherArtworkLoader(root, http); ImageLoader.AsyncImageLoader = loader;
        try
        {
            using var full = await loader.ProvideImageAsync(LauncherArtworkLoader.FullSize(ReviewedGameArtwork.TaiseiUrl)!);
            Assert.NotNull(full); Assert.Equal(new Avalonia.PixelSize(960, 540), full.PixelSize);
            var thumbnail = await loader.ProvideImageAsync(ReviewedGameArtwork.TaiseiUrl); Assert.NotNull(thumbnail);
            Assert.InRange(thumbnail.PixelSize.Width, 1, 960); Assert.InRange(thumbnail.PixelSize.Height, 1, 540);
            using var game = new GameInfo { CatalogEntryId = ReviewedGameArtwork.TaiseiEntryId, Repository = "taisei-project/taisei", CatalogCover = CatalogCover.FromEntry(CatalogOnly.Entry()) };
            var custom = Path.Combine(root, "custom.png"); full.Save(custom); game.CustomIconPath = custom;
            await game.LoadCatalogCoverAsync(root, false, TestContext.Current.CancellationToken);
            Assert.Equal(custom, game.CardImageUrl); Assert.Empty(game.CardArtworkSource);
            Assert.Equal(0, network.Calls);
            Assert.Equal(new[] { custom }, Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
        return 0;
        }, TestContext.Current.CancellationToken);
    }
    sealed class RejectImagesNetwork : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No network allowed in the actual decoder fixture."); }
    }
    static async Task Capture(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) { Directory.CreateDirectory(output); using var file = new FileStream(Path.Combine(output, name + ".png"), FileMode.CreateNew); frame.Save(file); }
    }
    sealed class CatalogOnly : HttpMessageHandler
    {
        public int DetailCalls, UnexpectedCalls;
        public List<string> Paths { get; } = [];
        internal static QuiverCatalogApp Entry() => new() { Id = ReviewedGameArtwork.TaiseiEntryId, Slug = "taisei-project", Name = "Taisei Project", ProjectName = "Taisei Project", ProjectType = "games", Description = "A free and open-source bullet hell game.", Launcher = new() { FolderName = "Taisei" } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object value;
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            if (path.EndsWith("/release-status")) { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Cancelled catalog fixture unexpectedly resumed."); }
            if (path.EndsWith("/apps/taisei-project")) { DetailCalls++; value = new QuiverCatalogDetail { Entry = Entry(), Project = new() { Provider = "github", Repository = "taisei-project/taisei" } }; }
            else if (path.EndsWith("/facets")) value = new QuiverCatalogFacets { Total = 1 };
            else if (path.EndsWith("/apps")) value = new QuiverCatalogPage<QuiverCatalogApp>([Entry()], null, true);
            else { UnexpectedCalls++; throw new InvalidOperationException("Unexpected fixture request: " + path); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        }
    }
    sealed class Store(string root, bool light) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false, AppsPath = Path.Combine(root, "Apps"), PrimaryColor = light ? "#e5e5e5" : "#18181b" };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
