using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class FreedoomInstallRenderingTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false, 180)][InlineData(true, false, 180)][InlineData(true, true, 180)]
    [InlineData(false, false, 120)][InlineData(true, false, 120)][InlineData(true, true, 120)]
    public async Task Eligible_uninstalled_card_shows_setup_without_covering_art_or_primary_download(bool grid, bool compact, int cardSize)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "freedoom-card-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previous = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Current.SlotSize = cardSize; store.Current.ListRowHeight = cardSize == 120 ? 72 : 180;
            store.Current.UseGridView = grid; store.Current.GridCompactCards = compact;
            var network = new Offline(); using var http = new HttpClient(network);
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            var view = new MainView(RealRenderTestApp.Dependencies(store, manager));
            var window = new Window { Content = view, Width = 1100, Height = 760 };
            try
            {
                var app = new GameInfo { Name = "Synthetic UZDoom, no engine installed", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.NotInstalled };
                manager.SetCatalogAppsAndFilter([app], store.Current); view.Shell.Mode = MainViewMode.Library;
                var library = view.FindControl<LibraryView>("LibraryPanel")!; library.UpdateEmptyState(true);
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                var action = Assert.Single(library.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("freedoom-action") && b.IsEffectivelyVisible));
                Assert.Equal("Set up Freedoom Phase 2…", Avalonia.Automation.AutomationProperties.GetName(action));
                Assert.True(action.Focusable); Assert.True(action.Bounds.Height >= 44);
                var primary = library.FindGameActionButton(app)!; Assert.True(primary.IsEffectivelyVisible);
                var art = library.FindGameCardBorder(app)!;
                var point = action.TranslatePoint(default, window)!.Value;
                var artPoint = art.TranslatePoint(default, window)!.Value;
                Assert.True(point.Y >= artPoint.Y + art.Bounds.Height);
                Assert.InRange(point.X, 0, window.Width - action.Bounds.Width);
                Assert.InRange(point.Y, 0, window.Height - action.Bounds.Height);
                Assert.Equal(0, network.Calls);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(1100, 760), frame.PixelSize);
                using var locked = frame.Lock(); var bytes = new byte[locked.RowBytes * 760]; Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
                var colors = new HashSet<int>(); for (var i = 0; i < bytes.Length; i += 4) colors.Add(BitConverter.ToInt32(bytes, i)); Assert.True(colors.Count > 32);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, $"15-freedoom-first-setup-{(grid ? compact ? "compact" : "grid" : "list")}-{cardSize}.png"), FileMode.CreateNew, FileAccess.Write); frame.Save(file);
                }
                output.WriteLine("Actual native library CPU render with a synthetic uninstalled entry and built-in placeholder artwork. No installed engine, acquired execution, external request or gameplay certification.");
            }
            finally { window.Close(); await view.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
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
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("Unexpected network in native library action render."); }
    }
}
