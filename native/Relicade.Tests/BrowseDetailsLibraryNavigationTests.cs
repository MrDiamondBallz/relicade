using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Models;
using Relicade.Core.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class BrowseDetailsLibraryNavigationTests
{
    [AvaloniaTheory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)][InlineData(true, true)]
    public async Task Existing_or_newly_added_details_open_the_actual_library_entry(bool existing, bool keyboard)
    {
        await using var fixture = new Fixture();
        var app = new GameInfo { Name = "Synthetic local game", FolderName = "Fixture", IsCustom = true };
        if (existing) await fixture.Add(app);
        fixture.Open(app);
        var open = fixture.Details.FindControl<Button>("BrowseDetailsOpenLibraryButton");
        Assert.NotNull(open);
        if (!existing)
        {
            Assert.False(open.IsVisible); Assert.True(fixture.Details.BrowseDetailsAddButton.IsVisible);
            var shown = Visibility(open, true);
            fixture.Details.BrowseDetailsAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await shown.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(open.IsVisible); Assert.True(fixture.Details.BrowseDetailsRemoveButton.IsVisible);
        Assert.False(fixture.Details.BrowseDetailsAddButton.IsVisible);
        var entry = Assert.Single(fixture.Manager.LibraryApps);
        var requests = 0; GameInfo? requested = null;
        fixture.Details.OpenInLibraryRequested += item => { requests++; requested = item.ListApp; };
        fixture.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        if (keyboard)
        {
            Assert.True(open.Focus());
            fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            fixture.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            // The existing details keyboard mode first highlights its primary action.
            Assert.Equal(0, requests); Assert.Contains("gamepad-focused", open.Classes);
            fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            fixture.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        }
        else
        {
            fixture.Details.Confirm(); // The first controller confirm focuses the primary action.
            if (fixture.View.Shell.BrowseDetailsOpen) fixture.Details.Confirm();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, requests); Assert.Same(entry, requested);
        Assert.False(fixture.View.Shell.BrowseDetailsOpen); Assert.Equal(MainViewMode.Library, fixture.View.Shell.Mode);
        Assert.Equal(0, ((IFeatureNavigationHost)fixture.View).Navigation.LibrarySelectedIndex);
        Assert.Single(fixture.Manager.LibraryApps); Assert.Equal(0, fixture.Network.Calls);
        Assert.False(fixture.Manager.HasLibraryReveal);
    }

    [AvaloniaFact]
    public async Task Missing_entry_hides_open_and_stale_click_does_not_navigate()
    {
        await using var fixture = new Fixture();
        fixture.Open(new GameInfo { Name = "Not in library", FolderName = "Absent", IsCustom = true });
        var open = fixture.Details.FindControl<Button>("BrowseDetailsOpenLibraryButton");
        Assert.NotNull(open); Assert.False(open.IsVisible);
        var requests = 0; fixture.Details.OpenInLibraryRequested += _ => requests++;
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, requests); Assert.True(fixture.View.Shell.BrowseDetailsOpen);
        Assert.Empty(fixture.Manager.LibraryApps); Assert.Equal(0, fixture.Network.Calls);
    }

    [AvaloniaFact]
    public async Task Separate_remove_action_preserves_files_and_hides_the_navigation_action()
    {
        await using var fixture = new Fixture();
        var app = new GameInfo { Name = "Synthetic local game", FolderName = "Fixture", IsCustom = true };
        await fixture.Add(app); fixture.Open(app);
        var entry = Assert.Single(fixture.Manager.LibraryApps);
        var retained = Path.Combine(entry.GetInstallPath(fixture.Manager.GamesFolder), "save.fixture");
        File.WriteAllText(retained, "retain these owned synthetic bytes");
        var open = fixture.Details.FindControl<Button>("BrowseDetailsOpenLibraryButton")!;
        Assert.True(open.IsVisible); Assert.True(fixture.Details.BrowseDetailsRemoveButton.IsVisible);
        var hidden = Visibility(open, false);
        fixture.Details.BrowseDetailsRemoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await hidden.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(fixture.Manager.LibraryApps); Assert.True(fixture.Details.BrowseDetailsAddButton.IsVisible);
        Assert.False(fixture.Details.BrowseDetailsRemoveButton.IsVisible);
        Assert.Equal("retain these owned synthetic bytes", File.ReadAllText(retained));
        var requests = 0; fixture.Details.OpenInLibraryRequested += _ => requests++;
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, requests); Assert.True(fixture.View.Shell.BrowseDetailsOpen);
    }

    [AvaloniaTheory]
    [InlineData("leave")][InlineData("search")][InlineData("scope")][InlineData("tag")]
    public async Task Filtered_target_is_temporarily_revealed_without_replacing_filters(string restore)
    {
        await using var fixture = new Fixture();
        var app = new GameInfo { Name = "Synthetic selected game", FolderName = "Fixture", IsCustom = true };
        await fixture.Add(app);
        var entry = Assert.Single(fixture.Manager.LibraryApps); entry.Status = GameStatus.NotInstalled;
        var settings = fixture.Settings.Current;
        var filter = new TagDisplayFilter { Name = "Different tag", Tags = ["other"] };
        settings.TagDisplayFilters.Add(filter); settings.ActiveTagDisplayFilterId = filter.Id;
        settings.ListScope = AppListScope.InstalledOnly;
        fixture.Manager.SetCatalogAppsAndFilter([entry], settings);
        await fixture.View.Library.SearchAsync("nonmatching query", CancellationToken.None, debounce: false);
        Assert.Empty(fixture.Manager.Games);
        fixture.Open(app);
        var saves = fixture.Settings.Saves;
        fixture.Details.FindControl<Button>("BrowseDetailsOpenLibraryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs(); fixture.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Same(entry, Assert.Single(fixture.Manager.Games));
        Assert.Equal(0, ((IFeatureNavigationHost)fixture.View).Navigation.LibrarySelectedIndex);
        Assert.Equal(AppListScope.InstalledOnly, settings.ListScope);
        Assert.Equal(filter.Id, settings.ActiveTagDisplayFilterId);
        Assert.Equal("nonmatching query", fixture.Manager.LibrarySearchText);
        Assert.Equal(saves, fixture.Settings.Saves);
        var notice = fixture.View.FindControl<LibraryView>("LibraryPanel")!.FindControl<TextBlock>("LibraryRevealNotice");
        Assert.NotNull(notice); Assert.True(notice.IsVisible);
        switch (restore)
        {
            case "leave": fixture.View.Shell.Mode = MainViewMode.Browse; break;
            case "search": await fixture.View.Library.SearchAsync("another query", CancellationToken.None, debounce: false); break;
            case "scope": new LibraryFiltersViewModel(fixture.Manager, fixture.View.Library).SetScope(AppListScope.InstalledOnly); break;
            case "tag": new LibraryFiltersViewModel(fixture.Manager, fixture.View.Library).Toggle(filter.Id); break;
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.Manager.Games); Assert.False(notice.IsVisible);
        Assert.Same(entry, Assert.Single(fixture.Manager.LibraryApps)); Assert.Equal(0, fixture.Network.Calls);
        if (restore == "leave")
        {
            fixture.View.Shell.Mode = MainViewMode.Library; Dispatcher.UIThread.RunJobs();
            Assert.Empty(fixture.Manager.Games);
            Assert.Equal(AppListScope.InstalledOnly, settings.ListScope);
            Assert.Equal(filter.Id, settings.ActiveTagDisplayFilterId);
            Assert.Equal("nonmatching query", fixture.Manager.LibrarySearchText);
            Assert.Equal(saves, fixture.Settings.Saves);
        }
    }

    [AvaloniaFact]
    public async Task Pending_search_cannot_hide_the_target_after_navigation()
    {
        await using var fixture = new Fixture();
        var app = new GameInfo { Name = "Synthetic pending search target", FolderName = "Fixture", IsCustom = true };
        await fixture.Add(app); fixture.Open(app);
        var entry = Assert.Single(fixture.Manager.LibraryApps);
        var pending = fixture.View.Library.SearchAsync("pending unmatched query", CancellationToken.None);
        fixture.Details.FindControl<Button>("BrowseDetailsOpenLibraryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await pending);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("pending unmatched query", fixture.Manager.LibrarySearchText);
        Assert.Same(entry, Assert.Single(fixture.Manager.Games));
        Assert.Equal(0, ((IFeatureNavigationHost)fixture.View).Navigation.LibrarySelectedIndex);
        fixture.View.Shell.Mode = MainViewMode.Browse;
        Assert.Empty(fixture.Manager.Games); Assert.Equal(0, fixture.Network.Calls);
    }

    [AvaloniaFact]
    public async Task Temporary_reveal_keeps_matching_entries_and_drops_removed_targets()
    {
        await using var fixture = new Fixture();
        var app = new GameInfo { Name = "Synthetic selected game", FolderName = "Fixture", IsCustom = true };
        await fixture.Add(app);
        var target = Assert.Single(fixture.Manager.LibraryApps); target.Status = GameStatus.NotInstalled;
        var other = new GameInfo { Name = "Matching installed fixture", FolderName = "Other", IsCustom = true, Status = GameStatus.Installed };
        fixture.Settings.Current.ListScope = AppListScope.InstalledOnly;
        fixture.Manager.SetCatalogAppsAndFilter([target, other], fixture.Settings.Current);
        fixture.Open(app);
        fixture.Details.FindControl<Button>("BrowseDetailsOpenLibraryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, fixture.Manager.Games.Count); Assert.Contains(other, fixture.Manager.Games); Assert.Contains(target, fixture.Manager.Games);
        fixture.Manager.ApplyTagDisplayFilter();
        Assert.Equal(2, fixture.Manager.Games.Count); // Background refilter retains the selected target.
        fixture.Manager.SetCatalogAppsAndFilter([other], fixture.Settings.Current);
        Assert.Same(other, Assert.Single(fixture.Manager.Games)); Assert.False(fixture.Manager.HasLibraryReveal);
        fixture.Manager.RevealLibraryGame(target); // A stale target cannot re-enter the library.
        Assert.Same(other, Assert.Single(fixture.Manager.Games)); Assert.Equal(0, fixture.Network.Calls);
        fixture.Manager.SetCatalogAppsAndFilter([other, target], fixture.Settings.Current);
        Assert.Same(other, Assert.Single(fixture.Manager.Games)); Assert.False(fixture.Manager.HasLibraryReveal);
    }

    static Task Visibility(Control control, bool value)
    {
        if (control.IsVisible == value) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Control.IsVisibleProperty && control.IsVisible == value)
            { control.PropertyChanged -= Changed; completion.TrySetResult(); }
        }
        control.PropertyChanged += Changed; return completion.Task;
    }

    sealed class Fixture : IAsyncDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "details-library-" + Guid.NewGuid().ToString("N"));
        readonly string? previous = RelicadePaths.OverrideUserDataRoot;
        readonly HttpClient http;
        internal readonly Offline Network = new();
        internal readonly Store Settings = new();
        internal readonly GameManager Manager;
        internal readonly MainView View;
        internal readonly Window Window;
        internal readonly BrowseDetailsView Details;
        internal Fixture()
        {
            Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
            var store = Settings; store.Current.AppsPath = Path.Combine(root, "Apps");
            http = new HttpClient(Network);
            Manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root)) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
            View = new MainView(new() { SettingsStore = store, GameManager = Manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
            Window = new Window { Content = View, Width = 1280, Height = 860 };
            Details = View.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
            Window.Show(); Dispatcher.UIThread.RunJobs();
        }
        internal Task Add(GameInfo app) => (Task)typeof(MainView).GetMethod("AddFromBrowseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(View, [app, "fixture", null])!;
        internal void Open(GameInfo app)
        {
            View.Shell.Mode = MainViewMode.Browse;
            typeof(MainView).GetMethod("OpenBrowseDetails", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(BrowseItem), typeof(bool)])!.Invoke(View, [BrowseItem.FromList(app), false]);
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        }
        public async ValueTask DisposeAsync()
        {
            Window.Close(); await View.ShutdownAsync(); Manager.Dispose(); http.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false); RelicadePaths.OverrideUserDataRoot = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        internal int Saves;
        public void Save(AppSettings settings) { Saves++; }
    }
    sealed class Offline : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("Unexpected network in local details navigation fixture."); }
    }
}
