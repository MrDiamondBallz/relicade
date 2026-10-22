using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class NativeTaglineRenderingTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Theory]
    [InlineData(1100, 760, false)]
    [InlineData(750, 490, false)]
    [InlineData(1100, 760, true)]
    public async Task Product_tagline_is_readable_beneath_the_unchanged_native_wordmark(int width, int height, bool light)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "relicade-tagline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var previous = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Current.PrimaryColor = light ? "#e5e5e5" : "#18181b";
            using var network = new NoNetwork(); using var http = new HttpClient(network);
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            var view = new MainView(RealRenderTestApp.Dependencies(store, manager));
            var window = new Window { Content = view, Width = width, Height = height };
            try
            {
                view.Shell.Mode = MainViewMode.Library;
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                var tagline = view.FindControl<TextBlock>("SidebarTagline")!;
                var suffix = view.FindControl<Image>("SidebarWordmarkTail")!;
                var sidebar = view.FindControl<Border>("SidebarPanel")!;
                Assert.Equal("Old legends. New life.", tagline.Text); Assert.True(tagline.IsEffectivelyVisible);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Native foundation");
                var point = tagline.TranslatePoint(default, sidebar)!.Value;
                var wordmark = suffix.TranslatePoint(default, sidebar)!.Value;
                Assert.InRange(point.X, 0, sidebar.Bounds.Width - tagline.Bounds.Width);
                Assert.True(point.Y >= wordmark.Y + suffix.Bounds.Height);
                Assert.True(tagline.Bounds.Height >= 11); Assert.True(tagline.Bounds.Width > 90);
                Assert.Equal(0, network.Calls);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                Assert.Equal(new PixelSize(width, height), frame.PixelSize);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, $"18-native-tagline-{width}x{height}-light-{light}.png"), FileMode.CreateNew, FileAccess.Write);
                    frame.Save(file);
                }
                output.WriteLine("Actual headless native Library with an empty isolated profile, unchanged logo assets, no external requests and no gameplay/platform acceptance.");
            }
            finally { window.Close(); await view.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    sealed class NoNetwork : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("Unexpected request in native branding render."); }
    }
}
