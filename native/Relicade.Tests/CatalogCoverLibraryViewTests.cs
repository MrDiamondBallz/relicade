using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class CatalogCoverLibraryViewTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Actual_library_cards_display_bound_cover_and_preserve_primary_navigation(bool grid, bool compact)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-cover-view-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps"); store.Current.UseGridView = grid; store.Current.GridCompactCards = compact;
        var previous = ImageLoader.AsyncImageLoader;
        using var bitmap = new Bitmap(new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7S8AAAAASUVORK5CYII=")));
        using var loader = new FakeImage(bitmap); ImageLoader.AsyncImageLoader = loader;
        using var network = new Offline(); using var http = new HttpClient(network);
        using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: store.Current.AppsPath));
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        await using var session = new LauncherSession();
        try
        {
            var game = new GameInfo { Name = "Synthetic cover fixture", Project = "No game installed", CatalogEntryId = "entry", GameIconUrl = CatalogLibraryCoverTests.Icon,
                CatalogCover = new("entry", CatalogLibraryCoverTests.Header, CatalogLibraryCoverTests.Icon, "header") };
            await game.LoadCatalogCoverAsync(root, true, TestContext.Current.CancellationToken);
            manager.SetCatalogAppsAndFilter([game], store.Current); view.Shell.Mode = MainViewMode.Library;
            var library = view.FindControl<LibraryView>("LibraryPanel")!; library.UpdateEmptyState(true);
            GameInfo? dispatched = null;
            library.Navigation = new LibraryNavigation(library, (IFeatureNavigationHost)view, session, () => true, () => true, () => false, _ => { }, selected => { dispatched = selected; return Task.CompletedTask; });
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var cover = Assert.Single(library.GetVisualDescendants().OfType<AdvancedImage>().Where(i => i.IsEffectivelyVisible && ReferenceEquals(i.DataContext, game)));
            Assert.Equal(CatalogLibraryCoverTests.Header, cover.Source);
            Assert.Contains(CatalogLibraryCoverTests.Header, Assert.IsType<string>(ToolTip.GetTip(cover)));
            Assert.NotNull(cover.CurrentImage);
            Assert.Same(bitmap, cover.CurrentImage.GetType().GetProperty("ImageImplementation")!.GetValue(cover.CurrentImage)); Assert.True(cover.Bounds.Width > 0 && cover.Bounds.Height > 0);
            Assert.True(library.FindGameActionButton(game)!.IsEffectivelyVisible);
            library.Navigation.ApplyLibraryGamepadSelection(0); Assert.True(library.Navigation.Confirm()); Assert.Same(game, dispatched);
            game.CatalogEntryId = "other-entry"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(game.IconUrl, cover.Source); Assert.Equal("", ToolTip.GetTip(cover)); Assert.Equal(0, network.Calls);
        }
        finally { window.Close(); await view.ShutdownAsync(); ImageLoader.AsyncImageLoader = previous; GamepadFocusChrome.SetKeyboardNavigationActive(false); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task Actual_add_persists_cover_without_waiting_for_image_download_and_close_blocks_late_publish()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-cover-add-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var previousRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
        var previous = ImageLoader.AsyncImageLoader;
        using var loader = new PendingImage(); ImageLoader.AsyncImageLoader = loader;
        using var network = new Offline(); using var http = new HttpClient(network);
        var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
        using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
        await using var session = new LauncherSession();
        manager.UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask();
        var service = new LibraryAddService(manager, new SettingsViewModel(store), session);
        try
        {
            var external = new GameInfo { Name = "Synthetic cover Add", Repository = "fixture/game", RepositorySource = "github", FolderName = "Fixture", CatalogEntryId = "entry",
                CatalogCover = new("entry", CatalogLibraryCoverTests.Header, null, "header") };
            var result = await service.AddAsync(external).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(LibraryAddOutcome.Added, result.Outcome); Assert.NotNull(result.App);
            Assert.Equal(CatalogLibraryCoverTests.Header, await loader.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var saved = Assert.Single(await new AppCatalogService(dataDirectory: root).LoadLocalAppsForMutationAsync());
            Assert.Equal(external.CatalogCover, saved.CatalogCover);
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            loader.Complete(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(result.App.IconUrl, result.App.CardImageUrl); Assert.Empty(result.App.CardArtworkSource);
        }
        finally { loader.Complete(); await session.DisposeAsync(); ImageLoader.AsyncImageLoader = previous; RelicadePaths.OverrideUserDataRoot = previousRoot; Directory.Delete(root, true); }
    }
    private sealed class PendingImage : IAsyncImageLoader
    {
        public TaskCompletionSource<string> Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Bitmap?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Bitmap?> ProvideImageAsync(string url) { Requested.TrySetResult(url); return _result.Task; }
        public void Complete() => _result.TrySetResult(null);
        public void Dispose() => Complete();
    }
    private sealed class FakeImage(Bitmap image) : IAsyncImageLoader
    {
        public Task<Bitmap?> ProvideImageAsync(string url) => Task.FromResult<Bitmap?>(image);
        public void Dispose() { }
    }
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class Offline : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; throw new InvalidOperationException("Unexpected external request in synthetic cover view."); }
    }
}
