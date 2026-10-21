using System.Net;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Relicade.Services;

namespace Relicade.Tests;

public class NativeBrandingTests
{
    [AvaloniaTheory]
    [InlineData(1280, false)] [InlineData(750, false)]
    [InlineData(1280, true)] [InlineData(750, true)]
    public async Task Native_name_uses_one_oversized_orange_initial_and_complete_suffix(int width, bool light)
    {
        var previous = RelicadePaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "relicade-brand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        RelicadePaths.OverrideUserDataRoot = root;
        using var client = new HttpClient(new NoNetwork());
        var store = new FileSettingsStore();
        store.Current.FirstStartup = false;
        store.Current.LocalFirstCatalogMigrationComplete = true;
        store.Current.AppsPath = Path.Combine(root, "Apps");
        store.Current.PrimaryColor = light ? "#e5e5e5" : "#18181b";
        using var manager = new GameManager(store, client);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager,
            InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Width = width, Height = 720, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var panel = view.FindControl<StackPanel>("SidebarLogoPanel")!;
            var initial = view.FindControl<Image>("SidebarLogoImage")!;
            var suffix = view.FindControl<Image>("SidebarWordmarkTail")!;
            panel.GetVisualDescendants().OfType<Image>().Should().Equal(initial, suffix);
            initial.Source.Should().BeOfType<Bitmap>().Which.PixelSize.Should().Be(new PixelSize(256, 256));
            suffix.Source.Should().BeOfType<Bitmap>().Which.PixelSize.Should().Be(new PixelSize(606, 104),
                "the native suffix must not bind the eight-letter full RELICADE bitmap");
            AutomationProperties.GetName(suffix).Should().Be("Relicade");
            var sidebar = view.FindControl<Border>("SidebarPanel")!;
            var point = suffix.TranslatePoint(default, sidebar)!.Value;
            point.X.Should().BeGreaterThan(0);
            (point.X + suffix.Bounds.Width).Should().BeLessThanOrEqualTo(sidebar.Bounds.Width);
            // The orange path occupies 144/256 of its padded square; cap height is 156/256.
            (initial.Bounds.Width * 156 / 256).Should().BeGreaterThan(suffix.Bounds.Width * 104 / 606,
                "the established orange initial retains its larger emphasis");
            var split = view.FindControl<SplitView>("MainSplitView")!;
            view.SettingsModel.DesktopSidebarCollapsed = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            split.IsPaneOpen.Should().BeFalse(); sidebar.IsHitTestVisible.Should().BeFalse();
            view.SettingsModel.DesktopSidebarCollapsed = false;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            split.IsPaneOpen.Should().BeTrue();
            suffix.Source.Should().NotBeNull();
        }
        finally
        {
            window.Close(); await view.ShutdownAsync();
            RelicadePaths.OverrideUserDataRoot = previous;
            Directory.Delete(root, true);
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
