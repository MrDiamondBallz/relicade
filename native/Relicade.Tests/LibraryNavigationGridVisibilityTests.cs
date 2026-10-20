using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public class LibraryNavigationGridVisibilityTests
{
    [AvaloniaTheory]
    [InlineData(false, false, 20)] [InlineData(true, false, 20)] [InlineData(true, false, 1)] [InlineData(true, false, 64)] [InlineData(true, false, 110)] [InlineData(true, false, 500)] [InlineData(true, true, 20)]
    public async Task Large_library_prepares_all_cards_before_scrolling(bool grid, bool compact, int priorHeight)
    {
        // Exercise the existing public lazy-card measure cache, not private reflection.
        foreach (var group in new[] { "grid-art", "grid-text" })
        {
            var seed = new LazyCardContent { Group = group, Content = new Border { Height = priorHeight } };
            var primeWindow = new Window { Content = seed, Width = 240, Height = 600 };
            primeWindow.Show();
            seed.ApplyTemplate();
            primeWindow.UpdateLayout();
            primeWindow.Close();
        }
        await using var fixture = new Fixture();
        fixture.View.SettingsModel.UseGridView = grid;
        fixture.View.SettingsModel.GridCompactCards = compact;
        await fixture.Manager.CatalogService.SaveLocalAppsAsync(Enumerable.Range(0, 150).Select(i => new GameInfo
            { Name = $"App {i:D3}", Repository = "fixture/app" + i, FolderName = "App" + i }).ToList());
        var window = new Window { Width = 1200, Height = 800, Content = fixture.View };
        window.Show(); window.UpdateLayout();
        var startup = fixture.View.InitializeGamesAsync();
        try
        {
            await fixture.Network.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            fixture.View.Library.IsInitialLoading.Should().BeFalse();
            var items = fixture.Library.GetActiveGamesItemsControl()!;
            items.Items.Count.Should().Be(150);
            items.GetRealizedContainers().Count().Should().Be(150,
                "all cards are prepared up front so scrolling does not load cards on demand");
            var lastContainer = items.ContainerFromIndex(149);
            lastContainer.Should().NotBeNull();
            var last = fixture.Manager.Games.Last();
            fixture.Library.Navigation.ApplyLibraryGamepadSelection(149);
            // Drain the posted selection and its induced layout, not a geometry-based retry.
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            items.ContainerFromIndex(149).Should().BeSameAs(lastContainer);
            fixture.Library.FindGameCardRoot(last).Should().NotBeNull();
            last.IsGamepadFocused.Should().BeTrue();
            fixture.Library.FindGameOptionsButton(last).Should().NotBeNull();
            var scroll = fixture.Library.FindControl<ScrollViewer>("LibraryContentPanel")!;
            scroll.Offset.Y.Should().BeGreaterThan(0);
            var selectedCard = fixture.Library.FindGameCardRoot(last)!;
            var position = selectedCard.TranslatePoint(default, scroll)!.Value;
            position.Y.Should().BeLessThan(scroll.Bounds.Height);
            (position.Y + selectedCard.Bounds.Height).Should().BeGreaterThan(0);

            // A filter rebuild must discard recycled cards for removed apps.
            await fixture.View.Library.SearchAsync("App 000", CancellationToken.None, debounce: false);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            window.UpdateLayout();
            items.Items.Count.Should().Be(1);
            items.ContainerFromIndex(0)!.DataContext.Should().BeSameAs(fixture.Manager.Games[0]);
            fixture.Library.FindGameCardRoot(last).Should().BeNull();
        }
        finally
        {
            window.Close();
            await fixture.View.ShutdownAsync(); await startup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Queued_selection_scroll_uses_latest_exact_card_and_never_restores_revoked_work(bool grid, bool compact)
    {
        await using var fixture = new Fixture();
        fixture.View.SettingsModel.UseGridView = grid;
        fixture.View.SettingsModel.GridCompactCards = compact;
        await fixture.Manager.CatalogService.SaveLocalAppsAsync(Enumerable.Range(0, 150).Select(i => new GameInfo
            { Name = $"App {i:D3}", Repository = "fixture/app" + i, FolderName = "App" + i }).ToList());
        var window = new Window { Width = 1200, Height = 800, Content = fixture.View };
        window.Show(); window.UpdateLayout();
        var startup = fixture.View.InitializeGamesAsync();
        await using var session = new LauncherSession();
        var host = new NavigationHost();
        var allowed = true;
        var navigation = new LibraryNavigation(fixture.Library, host, session, () => true, () => allowed,
            () => false, _ => { }, _ => throw new Exception("No app may launch"));
        void Drain() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        try
        {
            await fixture.Network.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Drain();
            fixture.Manager.Games.Count.Should().Be(150);
            var scroll = fixture.Library.FindControl<ScrollViewer>("LibraryContentPanel")!;
            void Reset() { scroll.Offset = default; Drain(); }
            var last = fixture.Manager.Games[149];

            // A later no-scroll request for the SAME highlighted object revokes the queued scroll.
            navigation.ApplyLibraryGamepadSelection(149);
            navigation.ApplyLibraryGamepadSelection(149, stealFocus: false, bringIntoView: false);
            Drain(); scroll.Offset.Y.Should().Be(0);
            last.IsGamepadFocused.Should().BeTrue();

            navigation.ApplyLibraryGamepadSelection(149);
            navigation.ApplyLibraryGamepadSelection(0);
            Drain(); scroll.Offset.Y.Should().Be(0);
            host.Navigation.LibrarySelectedIndex.Should().Be(0);

            navigation.ApplyLibraryGamepadSelection(149);
            allowed = false; // overlay opened before the queued callback executes
            Drain(); scroll.Offset.Y.Should().Be(0);
            allowed = true;

            navigation.ApplyLibraryGamepadSelection(149);
            fixture.Manager.Games.Move(149, 0); // same object no longer occupies captured index
            Drain(); scroll.Offset.Y.Should().Be(0);
            fixture.Manager.Games.Move(0, 149); Drain();

            navigation.ApplyLibraryGamepadSelection(149);
            fixture.Manager.Games.RemoveAt(149); // filtered/rebuilt collection
            Drain(); scroll.Offset.Y.Should().Be(0);
            fixture.Manager.Games.Add(last); Drain();

            // Positive queued latest-selection control, including materialized options content.
            navigation.ApplyLibraryGamepadSelection(0);
            navigation.ApplyLibraryGamepadSelection(149);
            Drain();
            var card = fixture.Library.FindGameCardRoot(last)!;
            card.TranslatePoint(default, scroll)!.Value.Y.Should().BeLessThan(scroll.Bounds.Height);
            (card.TranslatePoint(default, scroll)!.Value.Y + card.Bounds.Height).Should().BeGreaterThan(0);
            fixture.Library.FindGameOptionsButton(last).Should().NotBeNull();
            host.Navigation.LibrarySelectedIndex.Should().Be(149);
            Reset();

            navigation.ApplyLibraryGamepadSelection(149);
            await session.DisposeAsync();
            Drain(); scroll.Offset.Y.Should().Be(0);
            navigation.Confirm().Should().BeFalse();
        }
        finally
        {
            window.Close();
            await fixture.View.ShutdownAsync(); await startup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class NavigationHost : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.Library;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) => false;
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }

    private sealed class BlockedNetwork : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string? _previous = RelicadePaths.OverrideUserDataRoot;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "relicade-loading-" + Guid.NewGuid().ToString("N"));
        public BlockedNetwork Network { get; } = new();
        public HttpClient Client { get; }
        public FileSettingsStore Store { get; }
        public GameManager Manager { get; }
        public MainView View { get; }
        public LibraryView Library => View.FindControl<LibraryView>("LibraryPanel")!;
        public Fixture(bool initializeOnOpen = false)
        {
            Directory.CreateDirectory(Root); RelicadePaths.OverrideUserDataRoot = Root;
            Store = new FileSettingsStore();
            Store.Current.FirstStartup = false;
            Store.Current.AppsPath = Path.Combine(Root, "Apps");
            // Keep startup on the library instead of the first-run Browse page.
            Store.Current.LocalFirstCatalogMigrationComplete = true;
            Store.Save(Store.Current);
            Client = new HttpClient(Network);
            Manager = new GameManager(Store, Client);
            View = new MainView(new() { SettingsStore = Store, GameManager = Manager,
                InitializeOnOpen = initializeOnOpen, EnableInput = false, EnableMusic = false });
        }
        public async ValueTask DisposeAsync()
        {
            await View.ShutdownAsync(); Manager.Dispose(); Client.Dispose();
            RelicadePaths.OverrideUserDataRoot = _previous;
            Directory.Delete(Root, true);
        }
    }

}
