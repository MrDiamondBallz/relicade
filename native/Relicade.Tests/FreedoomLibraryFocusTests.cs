using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class FreedoomLibraryFocusTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Focused_secondary_keeps_its_identity_after_live_collection_changes(bool grid, bool compact, bool insert)
    {
        var store = new Store();
        store.Current.UseGridView = grid;
        store.Current.GridCompactCards = compact;
        using var http = new HttpClient(new RejectNetwork());
        using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: store.Current.AppsPath));
        var view = new MainView(new()
        {
            SettingsStore = store, GameManager = manager, EnableInput = false,
            EnableMusic = false, InitializeOnOpen = false
        });
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        await using var session = new LauncherSession();
        try
        {
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var target = new GameInfo { Name = "A UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", Status = GameStatus.Installed };
            var other = new GameInfo { Name = "B other game", Repository = "fixture/other", RepositorySource = "github", Status = GameStatus.Installed };
            manager.SetCatalogAppsAndFilter(insert ? [target] : [target, other], store.Current);
            view.Shell.Mode = MainViewMode.Library;
            library.UpdateEmptyState(true);
            GameInfo? primary = null;
            GameInfo? secondary = null;
            library.Navigation = new LibraryNavigation(library, (IFeatureNavigationHost)view, session,
                () => true, () => true, () => false, _ => { }, game => { primary = game; return Task.CompletedTask; });
            library.ConfigureFreedoomSetup((game, _) => { secondary = game; return Task.CompletedTask; });
            window.Show();
            Layout();
            var action = Assert.Single(library.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Classes.Contains("freedoom-action") && button.IsEffectivelyVisible && ReferenceEquals(button.DataContext, target)));
            Assert.True(action.Focus());
            Assert.Same(action, window.FocusManager!.GetFocusedElement());

            // Exercise the actual ItemsControl, including the normal incremental Add route.
            // Neither mutation gives the already-focused button another GotFocus event.
            if (insert) await manager.InsertCatalogAppAsync(other, store.Current);
            else library.Model.Games.Move(0, 1);
            Layout();
            Assert.Same(action, window.FocusManager.GetFocusedElement());
            Assert.True(library.Navigation.Confirm());
            Dispatcher.UIThread.RunJobs();
            Assert.Null(primary);
            Assert.Same(target, secondary);
            Assert.Equal(library.Model.Games.IndexOf(target), ((IFeatureNavigationHost)view).Navigation.LibrarySelectedIndex);

            // Deliberately leaving the secondary button still restores normal card confirmation.
            library.Navigation.ApplyLibraryGamepadSelection(library.Model.Games.IndexOf(other));
            Assert.True(library.Navigation.Confirm());
            Assert.Same(other, primary);
            Assert.Same(target, secondary);
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
        }

        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new()
        {
            FirstStartup = false, LocalFirstCatalogMigrationComplete = true,
            AppsPath = Path.Combine(Path.GetTempPath(), "Relicade.FocusTests", Guid.NewGuid().ToString("N"))
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected network request in a synthetic library navigation test.");
    }
}
