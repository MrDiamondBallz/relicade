using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class LibraryRevealRenderingTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Fact]
    public async Task Actual_library_reveal_renders_its_filtered_target_and_explanation()
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "library-reveal-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previous = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
            var network = new Offline(); using var http = new HttpClient(network);
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            var view = new MainView(RealRenderTestApp.Dependencies(store, manager));
            var window = new Window { Content = view, Width = 1280, Height = 720 };
            try
            {
                window.Show(); Dispatcher.UIThread.RunJobs();
                var app = new GameInfo { Name = "Synthetic local fixture, no game installed", FolderName = "Fixture", IsCustom = true };
                await (Task)typeof(MainView).GetMethod("AddFromBrowseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [app, "fixture", null])!;
                var entry = Assert.Single(manager.LibraryApps); entry.Status = GameStatus.NotInstalled;
                var filters = view.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!;
                var installed = filters.FindControl<Button>("HideNonInstalledButton")!;
                installed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("selected", installed.Classes);
                var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void SearchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (view.Library.SearchText == "unmatched fixture query") applied.TrySetResult();
                }
                view.Library.PropertyChanged += SearchChanged;
                var search = view.FindControl<LibraryToolbarView>("LibraryToolbar")!.FindControl<TextBox>("LibrarySearchTextBox")!;
                search.Text = "unmatched fixture query";
                await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
                view.Library.PropertyChanged -= SearchChanged;
                Assert.Empty(manager.Games);
                view.Shell.Mode = MainViewMode.Browse;
                typeof(MainView).GetMethod("OpenBrowseDetails", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(BrowseItem), typeof(bool)])!.Invoke(view, [BrowseItem.FromList(app), false]);
                var details = view.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
                details.FindControl<Button>("BrowseDetailsOpenLibraryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                var notice = view.FindControl<LibraryView>("LibraryPanel")!.FindControl<TextBlock>("LibraryRevealNotice")!;
                Assert.True(notice.IsEffectivelyVisible); Assert.True(notice.Bounds.Height > 0);
                var point = notice.TranslatePoint(default, window); Assert.NotNull(point);
                Assert.InRange(point.Value.Y, 0, 720 - notice.Bounds.Height);
                Assert.Same(entry, Assert.Single(manager.Games)); Assert.Equal(0, network.Calls);
                Assert.Contains("selected", installed.Classes); Assert.Equal("unmatched fixture query", search.Text);
                var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(1280, 720), frame.PixelSize);
                using var locked = frame.Lock(); var bytes = new byte[locked.RowBytes * 720]; Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
                var colors = new HashSet<int>(); for (var i = 0; i < bytes.Length; i += 4) colors.Add(BitConverter.ToInt32(bytes, i));
                Assert.True(colors.Count > 32);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, "07-library-filtered-target-reveal.png"), FileMode.CreateNew, FileAccess.Write); frame.Save(file);
                }
                output.WriteLine("Actual native library and filter exception notice, 1280x720 CPU framebuffer. Synthetic uninstalled local entry, no game artwork or external request. Not desktop, hardware or gameplay acceptance.");
            }
            finally
            {
                window.Close(); await view.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = previous;
                Directory.Delete(root, true);
            }
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
        { Calls++; throw new InvalidOperationException("Unexpected network request in native library reveal fixture."); }
    }
}
