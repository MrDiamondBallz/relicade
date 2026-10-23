using System.Runtime.InteropServices;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class DiscoverVisualRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(1280, 860, false, 100, false)]
    [InlineData(750, 490, false, 100, false)]
    [InlineData(1100, 760, false, 200, false)]
    [InlineData(1280, 860, true, 100, false)]
    [InlineData(750, 490, true, 100, false)]
    [InlineData(1100, 760, true, 200, false)]
    [InlineData(1280, 860, false, 100, true)]
    [InlineData(750, 490, true, 100, true)]
    public async Task Discover_preserves_catalog_controls_and_bound_synthetic_art(int width, int height, bool light, int scale, bool tabs)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-discover-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previousRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var previousLoader = ImageLoader.AsyncImageLoader;
            using var tile = new WriteableBitmap(new PixelSize(480, 270), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var pixels = tile.Lock())
            {
                var bytes = new byte[pixels.RowBytes * 270];
                for (var y = 0; y < 270; y++) for (var x = 0; x < 480; x++)
                {
                    var p = y * pixels.RowBytes + x * 4; var stripe = (x / 60 + y / 45) % 2 == 0;
                    bytes[p] = (byte)(stripe ? 60 : 110); bytes[p + 1] = (byte)(40 + y / 2);
                    bytes[p + 2] = (byte)(stripe ? 230 : 70); bytes[p + 3] = 255;
                }
                Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
            }
            var loader = new FixtureImages(tile); ImageLoader.AsyncImageLoader = loader;
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Current.PrimaryColor = light ? "#e5e5e5" : "#18181b"; store.Current.InterfaceScalePercent = scale;
            if (tabs) store.Current.CustomAppListLocation = Path.Combine(root, "synthetic-app-list.json");
            using var network = new NoNetwork(); using var http = new HttpClient(network);
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            var view = new MainView(RealRenderTestApp.Dependencies(store, manager));
            var window = new Window { Content = view, Width = width, Height = height };
            using var scaling = new DesktopInterfaceScaling(window, new SettingsViewModel(store), () => new Size(3840, 2160));
            try
            {
                var browse = view.FindControl<BrowseView>("BrowsePanel")!;
                for (var i = 0; i < 6; i++)
                {
                    var app = new QuiverCatalogApp { Id = "fixture-" + i, Name = "Synthetic project " + (i + 1), ProjectType = "ports",
                        Description = "Synthetic art fixture, not an installed game", SupportedOS = ["linux", "windows"],
                        LibraryArt = new() { Header = "https://fixture.invalid/cover-" + i + ".png" },
                        Launcher = new() { FolderName = "fixture-" + i }, Tags = ["Synthetic image fixture"] };
                    var item = BrowseItem.FromCatalog(app, new Dictionary<string, string>()); item.InLibrary = i == 1;
                    browse.Model.Items.Add(item);
                }
                window.Show(); Dispatcher.UIThread.RunJobs();
                view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                browse.Model.Items[1].InLibrary = true;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                if (scale == 200 || height < 600)
                {
                    GamepadFocusChrome.SetKeyboardNavigationActive(true);
                    browse.Navigation.SelectInitial(bringIntoView: true);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); window.UpdateLayout();
                }
                var cards = browse.GetVisualDescendants().OfType<BrowseCard>().ToArray(); Assert.NotEmpty(cards);
                var art = cards[0].FindControl<Image>("BrowseCardArt")!;
                // The lazy card can be built by the selection scroll. Await the
                // actual binding result, not an assumed number of render ticks.
                var imageReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                { if (e.Property == Image.SourceProperty && art.Source != null) imageReady.TrySetResult(); }
                art.PropertyChanged += ImageChanged;
                try
                {
                    if (art.Source != null) imageReady.TrySetResult();
                    await imageReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { art.PropertyChanged -= ImageChanged; }
                Assert.Same(tile, art.Source); Assert.True(art.Bounds.Width > 0); Assert.True(art.Bounds.Height >= 180);
                Assert.True(cards[0].FindControl<Button>("BrowseCardAddButton")!.Bounds.Height >= 44);
                Assert.Equal("Find your next replay.", browse.FindControl<TextBlock>("DiscoverEditorialTitle")!.Text);
                Assert.Contains("https://fixture.invalid/cover-0.png", loader.Requests);
                foreach (var control in new Control[] { browse.BrowseSearchTextBox, browse.BrowseTypeComboBox, browse.BrowsePlatformComboBox,
                    browse.BrowseConsoleComboBox, browse.BrowseAiComboBox, browse.BrowseSortComboBox })
                    Assert.True(control.IsEffectivelyVisible);
                Assert.Equal(6, browse.Model.Items.Count); Assert.Equal(0, network.Calls);
                Assert.Equal(tabs, browse.BrowseSourceTabs.IsVisible);
                if (tabs)
                {
                    Assert.True(browse.BrowseCatalogTabButton.IsEffectivelyVisible);
                    Assert.True(browse.BrowseCustomListTabButton.IsEffectivelyVisible);
                    foreach (var sourceButton in new[] { browse.BrowseCatalogTabButton, browse.BrowseCustomListTabButton })
                    {
                        var position = sourceButton.TranslatePoint(default, browse.BrowseToolbarPanel)!.Value;
                        Assert.InRange(position.X, 0, browse.BrowseToolbarPanel.Bounds.Width - sourceButton.Bounds.Width);
                    }
                }
                var action = cards[0].FindControl<Button>("BrowseCardAddButton")!;
                var actionBounds = new Rect(action.Bounds.Size).TransformToAABB(action.TransformToVisual(window)!.Value);
                Assert.True(actionBounds.Y >= 0 && actionBounds.Bottom <= window.Bounds.Height);
                var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    using var file = new FileStream(Path.Combine(output, $"21-discover-{width}x{height}-light-{light}-scale-{scale}-tabs-{tabs}.png"), FileMode.CreateNew, FileAccess.Write);
                    frame.Save(file);
                }
            }
            finally { GamepadFocusChrome.SetKeyboardNavigationActive(false); window.Close(); await view.ShutdownAsync(); ImageLoader.AsyncImageLoader = previousLoader; RelicadePaths.OverrideUserDataRoot = previousRoot; Directory.Delete(root, true); }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    sealed class FixtureImages(Bitmap image) : IAsyncImageLoader
    {
        public List<string> Requests { get; } = [];
        public Task<Bitmap?> ProvideImageAsync(string url) { Requests.Add(url); return Task.FromResult<Bitmap?>(image); }
        public void Dispose() { }
    }
    sealed class NoNetwork : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No external request is permitted in the synthetic Discover render."); }
    }
    sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
