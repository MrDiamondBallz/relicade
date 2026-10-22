using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Relicade.Core.Models;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.Views;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class FreedoomLibraryActionTests
{
    [AvaloniaTheory]
    [InlineData("ListViewTemplate")]
    [InlineData("GridViewTemplate")]
    [InlineData("CompactGridViewTemplate")]
    public async Task Eligible_installed_engine_has_visible_secondary_action_without_replacing_launch(string template)
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Synthetic UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.Installed };
            var card = ((IDataTemplate)library.Resources[template]!).Build(game)!;
            card.DataContext = game; library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("freedoom-action")));
            Assert.True(action.IsEffectivelyVisible); Assert.True(action.IsEnabled);
            Assert.True(action.Bounds.Height >= 44); Assert.Equal("Launch", game.ButtonText);
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }
    [AvaloniaTheory]
    [InlineData("ListViewTemplate", "mouse")][InlineData("GridViewTemplate", "mouse")][InlineData("CompactGridViewTemplate", "mouse")]
    [InlineData("ListViewTemplate", "keyboard")][InlineData("GridViewTemplate", "keyboard")][InlineData("CompactGridViewTemplate", "keyboard")]
    [InlineData("ListViewTemplate", "controller")][InlineData("GridViewTemplate", "controller")][InlineData("CompactGridViewTemplate", "controller")]
    public async Task Secondary_action_routes_selected_instance_without_primary_launch(string template, string input)
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        await using var session = new LauncherSession();
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Synthetic UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.Installed };
            library.Model.Games.Add(game); view.Shell.Mode = MainViewMode.Library; library.UpdateEmptyState(true);
            var primary = 0; var secondary = 0; GameInfo? selected = null;
            library.Navigation = new LibraryNavigation(library, (IFeatureNavigationHost)view, session, () => true, () => true, () => false, _ => { }, _ => { primary++; return Task.CompletedTask; });
            library.ConfigureFreedoomSetup((item, _) => { secondary++; selected = item; return Task.CompletedTask; });
            var card = ((IDataTemplate)library.Resources[template]!).Build(game)!;
            card.DataContext = game; library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("freedoom-action")));
            Assert.True(action.Focus(), $"Focusable={action.Focusable}; enabled={action.IsEffectivelyEnabled}; visible={action.IsEffectivelyVisible}; bounds={action.Bounds}; status={game.Status}; root={card.Bounds}");
            if (input == "keyboard")
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else if (input == "controller") Assert.True(library.Navigation.Confirm());
            else action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, secondary); Assert.Same(game, selected); Assert.Equal(0, primary);
            // Default card confirmation remains the ordinary primary action.
            library.Navigation.ApplyLibraryGamepadSelection(0); Assert.True(library.Navigation.Confirm());
            Assert.Equal(1, primary); Assert.Equal(1, secondary);
        }
        finally { window.Close(); await view.ShutdownAsync(); GamepadFocusChrome.SetKeyboardNavigationActive(false); }
    }

    [AvaloniaTheory]
    [InlineData("ListViewTemplate")]
    [InlineData("GridViewTemplate")]
    [InlineData("CompactGridViewTemplate")]
    public async Task Eligible_uninstalled_engine_has_visible_setup_action_without_replacing_download(string template)
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Synthetic UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.NotInstalled };
            var card = ((IDataTemplate)library.Resources[template]!).Build(game)!;
            card.DataContext = game; library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("freedoom-action")));
            Assert.True(action.IsEffectivelyVisible); Assert.True(action.IsEnabled);
            Assert.True(action.Bounds.Height >= 44); Assert.True(game.CanDownload); Assert.Equal("Set up Freedoom Phase 2…", game.FreedoomActionLabel);
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }
    [AvaloniaTheory]
    [InlineData("ListViewTemplate", "mouse")][InlineData("GridViewTemplate", "mouse")][InlineData("CompactGridViewTemplate", "mouse")]
    [InlineData("ListViewTemplate", "keyboard")][InlineData("GridViewTemplate", "keyboard")][InlineData("CompactGridViewTemplate", "keyboard")]
    [InlineData("ListViewTemplate", "controller")][InlineData("GridViewTemplate", "controller")][InlineData("CompactGridViewTemplate", "controller")]
    public async Task Uninstalled_setup_action_routes_selected_instance_without_primary_launch(string template, string input)
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        await using var session = new LauncherSession();
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Synthetic UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.NotInstalled };
            library.Model.Games.Add(game); view.Shell.Mode = MainViewMode.Library; library.UpdateEmptyState(true);
            var primary = 0; var secondary = 0; GameInfo? selected = null;
            library.Navigation = new LibraryNavigation(library, (IFeatureNavigationHost)view, session, () => true, () => true, () => false, _ => { }, _ => { primary++; return Task.CompletedTask; });
            library.ConfigureFreedoomSetup((item, _) => { secondary++; selected = item; return Task.CompletedTask; });
            var card = ((IDataTemplate)library.Resources[template]!).Build(game)!;
            card.DataContext = game; library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("freedoom-action")));
            Assert.Equal("Set up Freedoom Phase 2…", game.FreedoomActionLabel);
            Assert.True(action.Focus(), $"Focusable={action.Focusable}; enabled={action.IsEffectivelyEnabled}; visible={action.IsEffectivelyVisible}; bounds={action.Bounds}; status={game.Status}; root={card.Bounds}");
            if (input == "keyboard")
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else if (input == "controller") Assert.True(library.Navigation.Confirm());
            else action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, secondary); Assert.Same(game, selected); Assert.Equal(0, primary);
            // Default card confirmation remains the ordinary primary action.
            library.Navigation.ApplyLibraryGamepadSelection(0); Assert.True(library.Navigation.Confirm());
            Assert.Equal(1, primary); Assert.Equal(1, secondary);
        }
        finally { window.Close(); await view.ShutdownAsync(); GamepadFocusChrome.SetKeyboardNavigationActive(false); }
    }

    [AvaloniaFact]
    public async Task Controller_options_still_reaches_optional_play_without_primary_dispatch()
    {
        var store = new Store(); using var manager = new GameManager(store);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Synthetic UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.Installed };
            manager.SetCatalogAppsAndFilter([game], store.Current); view.Shell.Mode = MainViewMode.Library; library.UpdateEmptyState(true);
            var called = 0; library.ConfigureFreedoomSetup((item, _) => { Assert.Same(game, item); called++; return Task.CompletedTask; });
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            library.Navigation.ApplyLibraryGamepadSelection(0);
            Assert.True(library.Navigation.Options()); Dispatcher.UIThread.RunJobs();
            var menu = library.FindGameOptionsButton(game)!.ContextMenu!; Assert.True(menu.IsOpen);
            var navigation = GamepadContextMenuNavigation.Instance;
            navigation.RegisterContextMenu(menu);
            var items = GamepadContextMenuNavigation.CollectNavigableMenuItems(menu);
            var target = items.FindIndex(item => item.Name == "PlayFreedoomMenu"); Assert.True(target >= 0);
            for (var i = 0; i < target; i++) Assert.True(navigation.TryHandleNavigation(Relicade.Services.NavigationDirection.Down));
            Assert.True(navigation.TryHandleConfirm()); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, called); Assert.False(menu.IsOpen);
        }
        finally { window.Close(); await view.ShutdownAsync(); GamepadFocusChrome.SetKeyboardNavigationActive(false); }
    }

    public static IEnumerable<object[]> HiddenCases() =>
        from template in new[] { "ListViewTemplate", "GridViewTemplate", "CompactGridViewTemplate" }
        from reason in new[] { "other-project", "manual", "recovery", "kiosk", "read-only", "loading" }
        select new object[] { template, reason };

    [AvaloniaTheory][MemberData(nameof(HiddenCases))]
    public async Task Ineligible_or_blocked_cards_do_not_offer_secondary_setup(string template, string reason)
    {
        var store = new Store { ReadOnly = reason == "read-only" }; store.Current.KioskMode = reason == "kiosk";
        var view = new MainView(new() { SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Synthetic card", Repository = reason == "other-project" ? "fixture/other" : "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.Installed };
            if (reason == "manual") game.Repository = "";
            if (reason == "loading") game.Status = GameStatus.Downloading;
            if (reason == "recovery") game.PortableRecoveryError = "Synthetic pending recovery";
            var card = ((IDataTemplate)library.Resources[template]!).Build(game)!;
            card.DataContext = game; library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("freedoom-action")));
            Assert.False(action.IsEffectivelyVisible);
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    sealed class Store : ISettingsStore, ISettingsRecoveryState
    {
        public bool ReadOnly;
        public bool IsReadOnly => ReadOnly;
        public Exception? ReadError => ReadOnly ? new IOException("Synthetic read-only settings") : null;
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "relicade-free-action", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
