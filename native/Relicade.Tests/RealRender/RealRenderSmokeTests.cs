using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

/// <summary>CPU-rendered native views with synthetic state, never a platform/game acceptance test.</summary>
[Collection(SharedCatalogRenderer.CollectionName)]
public class RealRenderSmokeTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Fact]
    public async Task Real_renderer_captures_native_settings_pixels()
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var window = new Window { Width = 400, Height = 720, Content = new SettingsView() };
            try
            {
                window.Show();
                using var frame = await ReadyFrame(window);
                AssertPixels(frame, 400, 720);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
            return 0;
        }, CancellationToken.None);
        output.WriteLine("real-render settings probe closed; shared application/dispatcher remains owned by collection");
    }

    [Fact]
    public void Controlled_composition_disables_devices_without_changing_production_defaults()
    {
        var defaults = new MainViewDependencies();
        Assert.True(defaults.EnableInput);
        Assert.True(defaults.EnableMusic);
        Assert.True(defaults.InitializeOnOpen);
        var store = new Store();
        using var http = new HttpClient(new OfflineCatalog());
        using var manager = new GameManager(store, http);
        var controlled = RealRenderTestApp.Dependencies(store, manager);
        Assert.Same(store, controlled.SettingsStore);
        Assert.Same(manager, controlled.GameManager);
        Assert.False(controlled.EnableInput);
        Assert.False(controlled.EnableMusic);
        Assert.False(controlled.InitializeOnOpen);
    }

    [Fact]
    public async Task Native_shell_transitions_library_offline_catalog_settings_and_drains_close()
    {
        {
            await renderer.Session.Dispatch<int>(async () =>
            {
                var store = new Store();
                var offline = new OfflineCatalog();
                using var http = new HttpClient(offline);
                using var manager = new GameManager(store, http);
                var dependencies = RealRenderTestApp.Dependencies(store, manager);
                // Fail before constructing anything if the controlled precondition regresses.
                Assert.False(dependencies.EnableInput);
                Assert.False(dependencies.EnableMusic);
                Assert.False(dependencies.InitializeOnOpen);
                var view = new MainView(dependencies);
                var window = new Window { Content = view, Width = 1280, Height = 720, Title = "Relicade" };
                using var host = new DesktopHostController(window, view);
                var closed = false;
                window.Closed += (_, _) => closed = true;
                var hashes = new List<string>();
                try
                {
                    AssertNoDeviceService(view);
                    window.Show();
                    Assert.Equal(MainViewMode.Library, view.Shell.Mode);
                    Assert.False(view.Shell.SettingsOpen);
                    Visible(view, "LibraryNavButton");
                    Visible(view, "BrowseNavButton");
                    Visible(view, "SettingsButton");
                    hashes.Add(await Capture(window, "01-library-empty"));
                    Assert.Equal(0, offline.Requests);

                    Click(view, "BrowseNavButton");
                    var browse = view.FindControl<BrowseView>("BrowsePanel")!;
                    // The injected handler completes synchronously: no real HTTP/DNS, no sleeps.
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.Equal(MainViewMode.Browse, view.Shell.Mode);
                    Assert.False(browse.Model.IsLoading);
                    Assert.Contains("Couldn't reach", browse.Model.Status);
                    Assert.True(offline.Requests > 0);
                    Visible(browse, "BrowseSearchTextBox");
                    Visible(browse, "BrowseRetryButton");
                    hashes.Add(await Capture(window, "02-catalog-offline"));
                    var requests = offline.Requests;
                    Click(browse, "BrowseRetryButton");
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.True(offline.Requests > requests);
                    Assert.False(browse.Model.IsLoading);
                    Assert.Contains("Couldn't reach", browse.Model.Status);

                    Click(view, "SettingsButton");
                    Assert.True(view.Shell.SettingsOpen);
                    var settings = view.FindControl<SettingsView>("SettingsPanel")!;
                    // This checkbox belongs to Appearance, not the initially selected General tab.
                    var tabs = settings.FindControl<TabControl>("SettingsTabControl")!;
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Appearance"));
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Visible(settings, "ShowOSTopBarCheckBox");
                    hashes.Add(await Capture(window, "03-settings"));
                    Assert.Equal(3, hashes.Distinct().Count());
                    AssertNoDeviceService(view);
                    output.WriteLine($"actual native transitions library -> catalog error -> retry -> settings; synthetic failed HTTP requests={offline.Requests}");
                }
                finally
                {
                    window.Close();
                    await view.ShutdownAsync();
                    await view.ShutdownAsync();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(closed);
                    Assert.False(window.IsVisible);
                    Assert.Null(GamepadModalDialogNavigation.Instance.ResolveKeyboardAction);
                    output.WriteLine("window Closed observed; ShutdownAsync drained twice (idempotent); input callback detached; caller-owned manager/http/host disposed on scope exit");
                }
                return 0;
            }, CancellationToken.None);
        }
        output.WriteLine("native shell drained and closed; shared renderer remains owned by collection");
    }

    private static void AssertNoDeviceService(MainView view)
    {
        var input = (LauncherInputController)typeof(MainView).GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        Assert.Null(input.Service);
        if (OperatingSystem.IsLinux())
            Assert.DoesNotContain("libSDL3", File.ReadAllText("/proc/self/maps"), StringComparison.OrdinalIgnoreCase);
    }

    private static Control Visible(Control scope, string name)
    {
        var control = scope.FindControl<Control>(name);
        Assert.NotNull(control);
        Assert.True(control.IsEffectivelyVisible, name);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, name);
        return control;
    }

    private static void Click(Control scope, string name)
    {
        var button = (Button)Visible(scope, name);
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<WriteableBitmap> ReadyFrame(Window window)
    {
        // A bounded compositor/layout barrier, not sleep or retry-until-green.
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        await commit.WaitAsync(TimeSpan.FromSeconds(10));
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return frame;
    }

    private async Task<string> Capture(Window window, string name)
    {
        using var frame = await ReadyFrame(window);
        var pixels = AssertPixels(frame, 1280, 720);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
        var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, name + ".png"));
        }
        output.WriteLine($"{name}: {frame.PixelSize.Width}x{frame.PixelSize.Height}; real CPU framebuffer sha256={hash}; synthetic empty library/settings and controlled offline catalog");
        return hash;
    }

    private static byte[] AssertPixels(WriteableBitmap frame, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), frame.PixelSize);
        using var buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes * height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        // Require significant nontransparent/nonuniform content, not a blank layout-only frame.
        var colors = new HashSet<int>();
        var opaque = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * buffer.RowBytes + x * 4;
            colors.Add(BitConverter.ToInt32(pixels, offset));
            if (pixels[offset + 3] > 0) opaque++;
        }
        Assert.True(colors.Count > 32, $"Only {colors.Count} colors: nonblank rendering required.");
        Assert.True(opaque > width * height / 2, "Opaque native UI pixels required.");
        return pixels;
    }

    private sealed class OfflineCatalog : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Synthetic offline catalog fixture")
            });
        }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new()
        {
            FirstStartup = false, EnableGamepadInput = false, BackgroundUpdateCheckEnabled = false,
            AppsPath = Path.Combine(Path.GetTempPath(), "synthetic-render-library"), CloseToTray = false
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
