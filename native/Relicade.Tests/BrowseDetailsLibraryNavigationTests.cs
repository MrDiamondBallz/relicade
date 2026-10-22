using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Models;
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
        internal readonly GameManager Manager;
        internal readonly MainView View;
        internal readonly Window Window;
        internal readonly BrowseDetailsView Details;
        internal Fixture()
        {
            Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
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
        public void Save(AppSettings settings) { }
    }
    sealed class Offline : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("Unexpected network in local details navigation fixture."); }
    }
}
