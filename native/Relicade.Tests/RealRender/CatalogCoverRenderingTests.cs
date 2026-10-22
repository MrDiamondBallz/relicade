using System.Runtime.InteropServices;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class CatalogCoverRenderer : IAsyncDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(RealRenderTestApp), AvaloniaTestIsolationLevel.PerAssembly);
    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

public sealed class CatalogCoverRenderingTests(CatalogCoverRenderer renderer, ITestOutputHelper output) : IClassFixture<CatalogCoverRenderer>
{
    const string Source = "https://cdn2.steamgriddb.com/grid/11111111111111111111111111111111.png";
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Native_library_renders_cached_synthetic_cover_in_each_layout(bool grid, bool compact)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-cover-render-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var oldRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var oldLoader = ImageLoader.AsyncImageLoader;
            using var network = new Offline(); using var http = new HttpClient(network);
            using var loader = new LauncherArtworkLoader(Path.Combine(root, "Images"), http); ImageLoader.AsyncImageLoader = loader;
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps"); store.Current.UseGridView = grid; store.Current.GridCompactCards = compact;
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: store.Current.AppsPath));
            var view = new MainView(new() { SettingsStore = store, GameManager = manager, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
            var window = new Window { Content = view, Width = 1100, Height = 760 };
            try
            {
                var cached = LauncherArtworkLoader.CachedPath(root, Source); Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                using (var tile = new WriteableBitmap(new PixelSize(192, 128), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque))
                {
                    using (var pixels = tile.Lock())
                    {
                        var bytes = new byte[pixels.RowBytes * 128];
                        for (var y = 0; y < 128; y++) for (var x = 0; x < 192; x++)
                        {
                            var p = y * pixels.RowBytes + x * 4; var stripe = (x / 24 + y / 24) % 2 == 0;
                            bytes[p] = (byte)(stripe ? 165 : 55); bytes[p + 1] = (byte)(40 + y); bytes[p + 2] = (byte)(stripe ? 30 + x : 220); bytes[p + 3] = 255;
                        }
                        Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
                    }
                    using var file = File.Create(cached); tile.Save(file);
                }
                var game = new GameInfo { Name = "Synthetic image fixture", Project = "Not game artwork or an installed game", CatalogEntryId = "synthetic-entry",
                    CatalogCover = new("synthetic-entry", Source, null, "header") };
                await game.LoadCatalogCoverAsync(root, false, CancellationToken.None);
                Assert.Equal(cached, game.CardImageUrl);
                manager.SetCatalogAppsAndFilter([game], store.Current); view.Shell.Mode = MainViewMode.Library;
                var library = view.FindControl<LibraryView>("LibraryPanel")!; library.UpdateEmptyState(true);
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var art = Assert.Single(library.GetVisualDescendants().OfType<AdvancedImage>().Where(i => i.IsEffectivelyVisible && ReferenceEquals(i.DataContext, game)));
                Assert.Equal(cached, art.Source); Assert.NotNull(art.CurrentImage);
                Assert.Contains(Source, Assert.IsType<string>(ToolTip.GetTip(art)));
                Assert.True(art.Bounds.Width > 0 && art.Bounds.Height > 0);
                Assert.True(library.FindGameActionButton(game)!.IsEffectivelyVisible);
                var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(1100, 760), frame.PixelSize);
                using (var locked = frame.Lock())
                {
                    var pixels = new byte[locked.RowBytes * 760]; Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
                    var colors = new HashSet<int>(); var opaque = 0;
                    for (var i = 0; i < pixels.Length; i += 4) { colors.Add(BitConverter.ToInt32(pixels, i)); if (pixels[i + 3] > 0) opaque++; }
                    Assert.True(colors.Count > 32); Assert.True(opaque > 1100 * 760 / 2);
                }
                Assert.Equal(0, network.Calls);
                var folder = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(folder))
                { Directory.CreateDirectory(folder); using var file = new FileStream(Path.Combine(folder, $"12-library-cover-{(grid ? compact ? "compact" : "grid" : "list")}.png"), FileMode.CreateNew, FileAccess.Write); frame.Save(file); }
                output.WriteLine("Actual native headless library with procedural synthetic image fixture, not game art or an installed game. Local cache and injected offline transport only.");
            }
            finally { window.Close(); await view.ShutdownAsync(); ImageLoader.AsyncImageLoader = oldLoader; RelicadePaths.OverrideUserDataRoot = oldRoot; Directory.Delete(root, true); }
            return 0;
        }, CancellationToken.None);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_decoder_refuses_truncated_image_without_changing_cache_or_contacting_provider(bool plausibleHeader)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-corrupt-cover-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var previous = ImageLoader.AsyncImageLoader;
            using var network = new Offline(); using var http = new HttpClient(network);
            using var loader = new LauncherArtworkLoader(Path.Combine(root, "Images"), http); ImageLoader.AsyncImageLoader = loader;
            try
            {
                var path = LauncherArtworkLoader.CachedPath(root, Source); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                byte[] bytes = plausibleHeader ? [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0] : [1, 2, 3]; File.WriteAllBytes(path, bytes);
                using var game = new GameInfo { Name = "Corrupt synthetic cache", CatalogEntryId = "entry", CatalogCover = new("entry", Source, null, "header") };
                await game.LoadCatalogCoverAsync(root, true, CancellationToken.None);
                Assert.Equal(game.IconUrl, game.CardImageUrl); Assert.Empty(game.CardArtworkSource);
                Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(0, network.Calls);
            }
            finally { ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
            return 0;
        }, CancellationToken.None);
    }
    sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    sealed class Offline : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; throw new InvalidOperationException("Unexpected external image request in synthetic cover render."); }
    }
}
