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
public sealed class DetailsHeaderContrastRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(750, 490, true, 100, true, ThemeTextMode.Automatic)]
    [InlineData(750, 490, true, 100, false, ThemeTextMode.Automatic)]
    [InlineData(1100, 760, true, 200, true, ThemeTextMode.Dark)]
    [InlineData(1100, 760, false, 200, false, ThemeTextMode.Light)]
    [InlineData(1280, 860, true, 100, true, ThemeTextMode.Dark, "#d4f1e8")]
    [InlineData(1280, 860, false, 100, false, ThemeTextMode.Light, "#1e3a5f")]
    [InlineData(1280, 860, true, 100, true, ThemeTextMode.Automatic, null, true)]
    [InlineData(1280, 860, false, 100, true, ThemeTextMode.Automatic, null, true)]
    public async Task Header_text_has_actual_pixel_contrast(int width, int height, bool light, int percent, bool portrait, ThemeTextMode textMode, string? customColor = null, bool measureOverview = false)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-real-art-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previousRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var previousLoader = ImageLoader.AsyncImageLoader;
            using var handler = new CatalogOnly(); using var http = new HttpClient(handler);
            using var loader = new LauncherArtworkLoader(root, http); ImageLoader.AsyncImageLoader = loader;
            var store = new Store(root, light); store.Current.InterfaceScalePercent = percent; if (customColor != null) store.Current.PrimaryColor = customColor; var catalog = new AppCatalogService(dataDirectory: root);
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
                await CommitFrame(window);
                Assert.True(image.Bounds.Width > 0 && image.Bounds.Height > 0, $"Unrealized viewport: {image.Bounds}");
                Assert.IsType<Avalonia.Media.Imaging.Bitmap>(image.Source);
                Assert.Contains("afensorm", item.ArtworkSource);
                await CommitFrame(window);
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
                await CommitFrame(window);
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
                await CommitFrame(window);
                var heroReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void HeroChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                { if (e.Property == Image.SourceProperty && hero.Source != null) heroReady.TrySetResult(); }
                hero.PropertyChanged += HeroChanged;
                try { if (hero.Source != null) heroReady.TrySetResult(); await heroReady.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { hero.PropertyChanged -= HeroChanged; }
                // Apply the saved accessibility preference through the live settings path.
                view.SettingsModel.ThemeTextModeIndex = (int)textMode;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(textMode, store.Current.ThemeTextMode);
                if (!portrait)
                {
                    var other = CatalogOnly.Entry(); other.Id = "other-entry";
                    browse.OpenDetails(BrowseItem.FromCatalog(other, new Dictionary<string, string>()));
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Assert.False(hero.IsVisible);
                }
                var kind = details.FindControl<TextBlock>("BrowseDetailsKind")!;
                var title = details.FindControl<TextBlock>("BrowseDetailsTitle")!;
                var tagline = details.FindControl<TextBlock>("BrowseDetailsTagline")!;
                Assert.Equal("STANDALONE GAME", kind.Text);
                Assert.False(string.IsNullOrWhiteSpace(title.Text)); Assert.False(string.IsNullOrWhiteSpace(tagline.Text));
                var label = $"{width}-{height}-{light}-{percent}-{portrait}-{textMode}-{customColor ?? "default"}-{measureOverview}";
                TextBlock[] overviewText = [];
                if (measureOverview)
                {
                    var readme = details.FindControl<ItemsControl>("BrowseDetailsReadme")!;
                    var description = Assert.Single(readme.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == CatalogOnly.Entry().Description);
                    var about = details.FindControl<Border>("BrowseDetailsAbout")!;
                    overviewText = new[] { description }.Concat(about.GetVisualDescendants().OfType<TextBlock>()).ToArray();
                    Assert.Equal(10, overviewText.Length);
                    Assert.All(overviewText, t => Assert.False(string.IsNullOrWhiteSpace(t.Text)));
                }
                using var normal = await Frame(window);
                Save(normal, "normal-" + label);
                title.Opacity = 0; tagline.Opacity = 0; kind.Opacity = 0;
                foreach (var text in overviewText) text.Opacity = 0;
                using var background = await Frame(window);
                title.Opacity = 1; tagline.Opacity = 1; kind.Opacity = 1;
                foreach (var text in overviewText) text.Opacity = 1;
                // This private measurement frame removes only glyphs, preserving the exact composed backdrop.
                Save(background, "measurement-background-" + label);
                CheckContrast(window, title, normal, background, 3);
                CheckContrast(window, tagline, normal, background, 4.5);
                CheckContrast(window, kind, normal, background, 4.5);
                foreach (var text in overviewText) CheckContrast(window, text, normal, background, 4.5, requireOpaqueCore: false);
                Assert.Equal(0, handler.UnexpectedCalls);

            }
            finally { GamepadFocusChrome.SetKeyboardNavigationActive(false); window.Close(); await view.ShutdownAsync(); ImageLoader.AsyncImageLoader = previousLoader; RelicadePaths.OverrideUserDataRoot = previousRoot; Directory.Delete(root, true); }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    static async Task CommitFrame(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
    }
    internal static async Task<Avalonia.Media.Imaging.WriteableBitmap> Frame(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
        return Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(window.CaptureRenderedFrame());
    }
    static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var file = new FileStream(Path.Combine(output, "header-contrast-" + name + ".png"), FileMode.CreateNew); frame.Save(file);
    }
    internal static void CheckContrast(Window window, TextBlock text, Avalonia.Media.Imaging.WriteableBitmap normal, Avalonia.Media.Imaging.WriteableBitmap background, double required, bool requireOpaqueCore = true)
    {
        Assert.True(text.IsEffectivelyVisible && text.Bounds.Width > 0 && text.Bounds.Height > 0);
        var point = text.TranslatePoint(default, window); Assert.NotNull(point);
        var end = text.TranslatePoint(new Point(text.Bounds.Width, text.Bounds.Height), window); Assert.NotNull(end);
        Assert.True(point.Value.X >= 0 && point.Value.Y >= 0 && end.Value.X <= window.Bounds.Width && end.Value.Y <= window.Bounds.Height);
        var color = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(text.Foreground).Color;
        using var n = normal.Lock(); using var b = background.Lock();
        Assert.Equal(Avalonia.Platform.PixelFormat.Rgba8888, n.Format); Assert.Equal(n.Format, b.Format);
        var nb = new byte[n.RowBytes * normal.PixelSize.Height]; var bb = new byte[b.RowBytes * background.PixelSize.Height];
        System.Runtime.InteropServices.Marshal.Copy(n.Address, nb, 0, nb.Length); System.Runtime.InteropServices.Marshal.Copy(b.Address, bb, 0, bb.Length);
        var sx = normal.PixelSize.Width / window.Bounds.Width; var sy = normal.PixelSize.Height / window.Bounds.Height;
        int glyphs = 0; double lowest = double.MaxValue;
        for (var y = (int)Math.Ceiling(point.Value.Y * sy); y < (int)(end.Value.Y * sy); y++)
        for (var x = (int)Math.Ceiling(point.Value.X * sx); x < (int)(end.Value.X * sx); x++)
        {
            var i = y * n.RowBytes + x * 4; var j = y * b.RowBytes + x * 4;
            // Sample actually drawn opaque glyph cores, not antialiased edges or nominal brush contrast.
            if (requireOpaqueCore && Math.Abs(nb[i] - color.R) + Math.Abs(nb[i+1] - color.G) + Math.Abs(nb[i+2] - color.B) > 12) continue;
            if (Math.Abs(nb[i] - bb[j]) + Math.Abs(nb[i+1] - bb[j+1]) + Math.Abs(nb[i+2] - bb[j+2]) < 6) continue;
            // Small labels may have no opaque core: use their solid brush at actually changed glyph pixels.
            var l1 = requireOpaqueCore ? L(nb[i], nb[i+1], nb[i+2]) : L(color.R, color.G, color.B); var l2 = L(bb[j], bb[j+1], bb[j+2]);
            lowest = Math.Min(lowest, (Math.Max(l1,l2)+.05)/(Math.Min(l1,l2)+.05)); glyphs++;
        }
        Assert.True(glyphs >= 10, $"{text.Name ?? text.Text}: only {glyphs} visible core pixels.");
        Assert.True(lowest >= required, $"{text.Name ?? text.Text}: actual glyph/background contrast {lowest:F3}, required {required}; {glyphs} pixels.");
    }
    static double L(byte r, byte g, byte b)
    {
        static double C(byte v) { var c=v/255d; return c<=.04045 ? c/12.92 : Math.Pow((c+.055)/1.055,2.4); }
        return .2126*C(r)+.7152*C(g)+.0722*C(b);
    }
    sealed class CatalogOnly : HttpMessageHandler
    {
        public int DetailCalls, UnexpectedCalls;
        public List<string> Paths { get; } = [];
        internal static QuiverCatalogApp Entry() => new() { Id = ReviewedGameArtwork.TaiseiEntryId, Slug = "taisei-project", Name = "Taisei Project", ProjectName = "Taisei Project", ProjectType = "game", Description = "A free and open-source bullet hell game.", Launcher = new() { FolderName = "Taisei" } };
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
