using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class LibraryMetadataNavigationTests
{
    [AvaloniaFact]
    public async Task Stacked_library_actions_follow_vertical_order_without_opening_or_saving()
    {
        await using var f = await LibraryMetadataExportTests.ExportScope.Create();
        using var model = new LibraryViewModel(f.Manager, f.Settings);
        var view = new LibraryView(); view.Configure(model, f.Session, null!, (_, _) => { });
        await model.SetupHistory.Work;
        // Controlled presentation only: actual stacked controls, no fabricated saved intent.
        view.LibrarySetupHistoryReviewButton.GetVisualAncestors().OfType<Border>().First().IsVisible = true;
        var host = new Host();
        var navigation = new LibraryNavigation(view, host, f.Session, () => true, () => true, () => false,
            _ => { }, _ => throw new InvalidOperationException("No game action permitted."));
        view.Navigation = navigation;
        var window = new Window { Content = view, Width = 900, Height = 650 };
        var before = Snapshot(f.Root);
        var priorChrome = GamepadFocusChrome.IsActive;
        try
        {
            window.Show(); GamepadFocusChrome.SetActive(true, window); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(view.LibrarySetupHistoryReviewButton.IsEffectivelyVisible);
            Assert.True(view.ExportLibraryMetadataButton.IsEffectivelyVisible);
            navigation.SynchronizeHistoryFocus();
            Assert.True(navigation.Navigate(NavigationDirection.Up));
            Assert.Equal(GamepadNavigationZone.Library, host.Navigation.ActiveZone);
            Assert.Same(view.ExportLibraryMetadataButton, window.FocusManager!.GetFocusedElement());
            Assert.True(navigation.Navigate(NavigationDirection.Down));
            Assert.Same(view.LibrarySetupHistoryReviewButton, window.FocusManager.GetFocusedElement());
            Assert.True(navigation.Navigate(NavigationDirection.Up));
            Assert.True(navigation.Navigate(NavigationDirection.Up));
            Assert.Equal(GamepadNavigationZone.TopBar, host.Navigation.ActiveZone);
            Assert.Equal(before, Snapshot(f.Root));
            Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally
        {
            try { window.Close(); }
            finally { try { await f.Session.DisposeAsync(); } finally { GamepadFocusChrome.SetActive(priorChrome); } }
        }
    }
    static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => Path.GetRelativePath(root, p) + "|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.Library;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) { Navigation.ActiveZone = transition.Zone; return true; }
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }
}
