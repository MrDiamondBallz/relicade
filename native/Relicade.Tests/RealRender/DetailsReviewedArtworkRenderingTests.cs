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
public sealed class DetailsReviewedArtworkRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(1280, 860, false, 100)]
    [InlineData(750, 490, true, 100)]
    [InlineData(1100, 760, false, 200)]
    public async Task Actual_catalog_details_Add_then_Library_keeps_reviewed_portrait(int width, int height, bool light, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-real-art-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previousRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var previousLoader = ImageLoader.AsyncImageLoader;
            using var handler = new CatalogOnly(); using var http = new HttpClient(handler);
            using var loader = new LauncherArtworkLoader(root, http); ImageLoader.AsyncImageLoader = loader;
            var store = new Store(root, light); store.Current.InterfaceScalePercent = percent; var catalog = new AppCatalogService(dataDirectory: root);
            using var manager = new GameManager(store, http, catalog);
            var view = new MainView(RealRenderTestApp.Dependencies(store, manager));
            var window = new Window { Content = view, Width = width, Height = height };
            using var scaling = new DesktopInterfaceScaling(window, new SettingsViewModel(store), () => new Size(3840, 2160));
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
                // A completed image request does not establish positive layout: commit the actual frame first.
                await Capture(window, $"resolution-{width}x{height}-{percent}-{light}");
                Assert.True(image.Bounds.Width > 0 && image.Bounds.Height > 0, $"Unrealized viewport: {image.Bounds}");
                Assert.IsType<Avalonia.Media.Imaging.Bitmap>(image.Source);
                Assert.Contains("afensorm", item.ArtworkSource);
                await Capture(window, $"23-taisei-discover-{width}x{height}-light-{light}-scale-{percent}");
                var details = view.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
                var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void DetailChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                { if (details.Model.Entry != null) resolved.TrySetResult(); }
                details.Model.PropertyChanged += DetailChanged;
                try { browse.OpenDetails(item); await resolved.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { details.Model.PropertyChanged -= DetailChanged; }
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.True(details.IsEffectivelyVisible);
                var detailArt = details.FindControl<Image>("BrowseDetailsArt")!;
                var artHost = details.FindControl<Border>("BrowseDetailsArtHost")!;
                Assert.True(artHost.Bounds.Width > 0 && artHost.Bounds.Height > 0);
                await Capture(window, $"26-details-before-assert-{width}x{height}");
                Assert.Equal(ReviewedGameArtwork.TaiseiUrl, ImageLoader.GetSource(detailArt));
                var loadedArt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ArtChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                { if (e.Property == Image.SourceProperty && detailArt.Source != null) loadedArt.TrySetResult(); }
                detailArt.PropertyChanged += ArtChanged;
                try { if (detailArt.Source != null) loadedArt.TrySetResult(); await loadedArt.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { detailArt.PropertyChanged -= ArtChanged; }
                Assert.NotNull(detailArt.Source);
                Assert.Equal(ReviewedGameArtwork.TaiseiAttribution, ToolTip.GetTip(artHost));
                Assert.Equal(ReviewedGameArtwork.TaiseiAttribution, Avalonia.Automation.AutomationProperties.GetHelpText(artHost));
                var hero = details.FindControl<Image>("BrowseDetailsHero")!;
                Assert.True(hero.IsVisible);
                Assert.Equal(LauncherArtworkLoader.FullSize(ReviewedGameArtwork.TaiseiUrl), ImageLoader.GetSource(hero));
                Assert.Equal(ReviewedGameArtwork.TaiseiAttribution, ToolTip.GetTip(hero));
                await Capture(window, $"26-taisei-details-{width}x{height}-light-{light}-scale-{percent}");
                var open = details.FindControl<Button>("BrowseDetailsOpenLibraryButton")!;
                var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OpenChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                { if (e.Property == Avalonia.Visual.IsVisibleProperty && open.IsVisible) added.TrySetResult(); }
                open.PropertyChanged += OpenChanged;
                try { details.BrowseDetailsAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await added.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { open.PropertyChanged -= OpenChanged; }
                var game = Assert.Single(manager.LibraryApps);
                Assert.False(game.IsInstalled); Assert.Equal(ReviewedGameArtwork.TaiseiUrl, game.CardImageUrl);
                Assert.Equal(1, handler.DetailCalls); Assert.True(handler.UnexpectedCalls == 0, string.Join("; ", handler.Paths));
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(view.FindControl<LibraryView>("LibraryPanel")!.IsEffectivelyVisible);
                await Capture(window, $"23-taisei-library-{width}x{height}-light-{light}-scale-{percent}");
                var saved = Assert.Single(await catalog.LoadLocalAppsForMutationAsync());
                await saved.LoadCatalogCoverAsync(root, false, TestContext.Current.CancellationToken);
                Assert.Equal(ReviewedGameArtwork.TaiseiUrl, saved.CardImageUrl);
                Assert.Equal(1, handler.DetailCalls); Assert.True(handler.UnexpectedCalls == 0, string.Join("; ", handler.Paths)); Assert.Contains("CC BY 4.0", saved.CardArtworkSource);
                // A reused details surface must revoke the portrait and its credit for a different identity.
                view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var other = CatalogOnly.Entry(); other.Id = "different-catalog-entry";
                other.Artwork = ReviewedGameArtwork.TaiseiUrl;
                other.LibraryArt = new() { Hero = "  AVARES://Relicade/Assets/GameArt/taisei-elly-afensorm-cover.png", Header = ReviewedGameArtwork.TaiseiUrl, Capsule = ReviewedGameArtwork.TaiseiUrl, Logo = ReviewedGameArtwork.TaiseiUrl };
                browse.OpenDetails(BrowseItem.FromCatalog(other, new Dictionary<string, string>()));
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Null(ImageLoader.GetSource(detailArt)); Assert.False(hero.IsVisible);
                Assert.Null(ToolTip.GetTip(artHost)); Assert.Null(ToolTip.GetTip(hero));
                Assert.True(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetHelpText(artHost)));
                // Original-game pages have their own upstream artwork; no stale portrait attribution carries across.
                var gameStatus = details.FindControl<TextBlock>("BrowseDetailsGameStatus")!;
                var gameLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void GameChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                { if (e.Property == TextBlock.TextProperty && gameStatus.Text == "No apps play this game yet.") gameLoaded.TrySetResult(); }
                gameStatus.PropertyChanged += GameChanged;
                try { details.OpenGame("synthetic-original", "Synthetic original game"); await gameLoaded.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { gameStatus.PropertyChanged -= GameChanged; }
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Null(ImageLoader.GetSource(detailArt)); Assert.Null(ToolTip.GetTip(artHost));
                Assert.Null(ToolTip.GetTip(hero)); Assert.False(hero.IsVisible);
                using var local = new GameInfo { Name = "Local custom entry", FolderName = "Local", IsCustom = true, GameIconUrl = ReviewedGameArtwork.TaiseiUrl };
                browse.OpenDetails(BrowseItem.FromList(local)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(ReviewedGameArtwork.TaiseiUrl, ImageLoader.GetSource(detailArt));
                Assert.Null(ToolTip.GetTip(artHost)); Assert.False(hero.IsVisible);
                Assert.Equal(0, handler.UnexpectedCalls);
            }
            finally { GamepadFocusChrome.SetKeyboardNavigationActive(false); window.Close(); await view.ShutdownAsync(); ImageLoader.AsyncImageLoader = previousLoader; RelicadePaths.OverrideUserDataRoot = previousRoot; Directory.Delete(root, true); }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    static async Task Capture(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) { Directory.CreateDirectory(output); using var file = new FileStream(Path.Combine(output, "details-route-" + name + ".png"), FileMode.CreateNew); frame.Save(file); }
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
            if (path.EndsWith("/games/synthetic-original")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new QuiverCatalogGameDetail { Game = new() { Slug = "synthetic-original", Title = "Synthetic original game", Artwork = ReviewedGameArtwork.TaiseiUrl, LibraryArt = new() { Hero = ReviewedGameArtwork.TaiseiUrl, Capsule = ReviewedGameArtwork.TaiseiUrl } } }, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
            if (path.EndsWith("/readme")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/reviews")) return new HttpResponseMessage(HttpStatusCode.NotFound);
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
