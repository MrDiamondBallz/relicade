using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Views;
public sealed class LibraryNavigation : IFeatureNavigationHandler
{
    private readonly LibraryView _view;
    private readonly IFeatureNavigationHost _host;
    private readonly LauncherSession _session;
    private readonly Func<bool> _isActive, _canSelect, _keepChromeFocus;
    private readonly Action<bool> _clearChrome;
    private readonly Func<GameInfo, Task> _confirm;
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;
    private System.Collections.ObjectModel.ObservableCollection<GameInfo> Games => _view.Model.Games;
    private AppSettings _settings => _view.Model.Settings.Current;
    private bool historySelected;
    private bool HasHistoryAction => _view.LibrarySetupHistoryReviewButton.IsEffectivelyVisible && _view.LibrarySetupHistoryReviewButton.IsEffectivelyEnabled;
    internal void SynchronizeHistoryFocus()
    {
        if (_session.IsClosed || !_canSelect() || !HasHistoryAction) return;
        historySelected = true; _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
    }
    private void SelectHistoryAction()
    {
        if (!HasHistoryAction) return;
        _clearChrome(true); _host.ClearFocus(); ClearLibraryCardGamepadFocus();
        historySelected = true; _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
        _view.LibrarySetupHistoryReviewButton.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(_view.LibrarySetupHistoryReviewButton);
        _view.LibrarySetupHistoryReviewButton.BringIntoView();
    }
    private List<Button> EmptyActions => Games.Count == 0 && _view.EmptyLibraryPanel.IsEffectivelyVisible
        ? new[] { _view.EmptyLibraryAddButton, _view.EmptyLibraryBrowseButton }.Where(b => b.IsEffectivelyVisible && b.IsEnabled).ToList()
        : [];

    internal void SynchronizeEmptyActionFocus(Button button)
    {
        if (_session.IsClosed || !_canSelect()) return;
        var index = EmptyActions.IndexOf(button);
        if (index < 0) return;
        _gamepadNavigation.LibrarySelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
    }

    internal void SynchronizeFreedoomActionFocus(Button button)
    {
        if (_session.IsClosed || !_canSelect() || !button.IsEffectivelyVisible || !button.IsEnabled ||
            button.DataContext is not GameInfo game) return;
        var index = Games.IndexOf(game);
        if (index < 0) return;
        _gamepadNavigation.LibrarySelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
    }

    private void SelectEmptyAction(int index, bool bringIntoView = true)
    {
        var buttons = EmptyActions;
        index = _gamepadNavigation.ClampIndex(index, buttons.Count);
        _clearChrome(true);
        _host.ClearFocus();
        ClearLibraryCardGamepadFocus();
        _gamepadNavigation.LibrarySelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
        if (index < 0) return;
        buttons[index].Classes.Set("gamepad-focused", true);
        if (!bringIntoView) { _host.FocusCard(true); return; }
        GamepadControlActivation.ApplyGamepadHighlightFocus(buttons[index]);
        buttons[index].BringIntoView();
    }

    public LibraryNavigation(LibraryView view, IFeatureNavigationHost host, LauncherSession session, Func<bool> isActive, Func<bool> canSelect, Func<bool> keepChromeFocus, Action<bool> clearChrome, Func<GameInfo, Task> confirm)
    {
        _view = view;
        _host = host;
        _session = session;
        _isActive = isActive;
        _canSelect = canSelect;
        _keepChromeFocus = keepChromeFocus;
        _clearChrome = clearChrome;
        _confirm = confirm;
    }

    public bool Navigate(NavigationDirection direction) => !_session.IsClosed && _canSelect() && HandleLibraryGamepadNavigation(direction);
    public bool Confirm()
    {
        if (_session.IsClosed || !_canSelect() || _gamepadNavigation.ActiveZone != GamepadNavigationZone.Library)
            return false;
        if (historySelected)
        {
            if (!HasHistoryAction) { historySelected = false; return false; }
            GamepadControlActivation.ActivateButton(_view.LibrarySetupHistoryReviewButton); return true;
        }
        // A live sort or insertion can move the focused card without another GotFocus event.
        // Resolve that action's identity first; an invalid secondary focus must not launch
        // whatever entry happens to occupy the previously selected index.
        if (TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement() is Button focused &&
            focused.Classes.Contains("freedoom-action"))
        {
            if (!focused.IsEffectivelyVisible || !focused.IsEffectivelyEnabled ||
                !_view.IsVisualAncestorOf(focused) || focused.DataContext is not GameInfo target)
                return false;
            var targetIndex = Games.IndexOf(target);
            if (targetIndex < 0) return false;
            _gamepadNavigation.LibrarySelectedIndex = targetIndex;
            GamepadControlActivation.ActivateButton(focused);
            return true;
        }
        var emptyActions = EmptyActions;
        if (emptyActions.Count > 0)
        {
            GamepadControlActivation.ActivateButton(emptyActions[Math.Max(0, _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, emptyActions.Count))]);
            return true;
        }
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
        if (index < 0)
            return false;
        var game = Games[index];
        _ = _session.RunAsync(() => _confirm(game));
        return true;
    }

    public bool Cancel() => false;
    public bool Options()
    {
        if (historySelected) return false;
        if (_session.IsClosed || !_canSelect() || _gamepadNavigation.ActiveZone != GamepadNavigationZone.Library)
            return false;
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
        if (index < 0)
            return false;
        var button = _view.FindGameOptionsButton(Games[index]);
        if (button == null)
            return false;
        _view.OptionsButton_Click(button, new RoutedEventArgs());
        return true;
    }

    public void RestoreFocus() => RestoreFocus(bringIntoView: true);

    public void RestoreFocus(bool bringIntoView)
    {
        if (!_session.IsClosed)
            SyncGamepadLibrarySelection(bringIntoView);
    }

    private (double X, double Y)? GetControlCenter(Control? control)
    {
        var topLeft = control?.TranslatePoint(new Point(), _view);
        return topLeft.HasValue ? (topLeft.Value.X + control!.Bounds.Width / 2, topLeft.Value.Y + control.Bounds.Height / 2) : null;
    }

    internal bool HandleLibraryGamepadNavigation(NavigationDirection direction)
    {
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.Library && historySelected && HasHistoryAction)
        {
            if (direction == NavigationDirection.Down)
            {
                if (Games.Count > 0) ApplyLibraryGamepadSelection(0);
                else if (EmptyActions.Count > 0) SelectEmptyAction(0);
                return true;
            }
            if (direction == NavigationDirection.Up) return _host.ApplyTransition(new(GamepadNavigationZone.TopBar, null));
            if (direction == NavigationDirection.Left) return _host.ApplyTransition(new(GamepadNavigationZone.Sidebar, null));
            return true;
        }
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.Library && HasHistoryAction &&
            direction == NavigationDirection.Up && _gamepadNavigation.LibrarySelectedIndex <= 0)
        { SelectHistoryAction(); return true; }
        var emptyActions = EmptyActions;
        if (emptyActions.Count > 0 && _gamepadNavigation.ActiveZone == GamepadNavigationZone.Library)
        {
            var index = Math.Max(0, _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, emptyActions.Count));
            if (direction == NavigationDirection.Up)
                return _host.ApplyTransition(new(GamepadNavigationZone.TopBar, null));
            if (direction == NavigationDirection.Left && index == 0)
                return _host.ApplyTransition(new(GamepadNavigationZone.Sidebar, null));
            SelectEmptyAction(direction == NavigationDirection.Right ? Math.Min(index + 1, emptyActions.Count - 1)
                : direction == NavigationDirection.Left ? index - 1 : index);
            return true;
        }
        var games = Games.ToList();
        var isListLayout = IsListGamepadLayout();
        var positions = games.Count > 0 && !isListLayout ? CollectGameCardPositions(games) : null;
        var currentIndex = _gamepadNavigation.LibrarySelectedIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, _gamepadNavigation.ActiveZone, _host.MainContentZone, isListLayout, positions, currentIndex, games.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.Library)
            return false;
        if (games.Count == 0)
            return false;
        var nextIndex = _gamepadNavigation.MoveLibraryIndex(currentIndex, direction, games.Count, isListLayout, positions);
        if (nextIndex == currentIndex && direction is NavigationDirection.Left or NavigationDirection.Up)
        {
            var blockedTransition = _gamepadNavigation.TryGetBlockedMoveZoneTransition(direction, _gamepadNavigation.ActiveZone, _host.MainContentZone, isListLayout, currentIndex, games.Count);
            if (blockedTransition.HasValue)
                return _host.ApplyTransition(blockedTransition.Value);
        }

        ApplyLibraryGamepadSelection(nextIndex);
        return true;
    }

    internal void SelectInitialLibraryGamepadItem()
    {
        if (!_host.IsFocusActive || !_canSelect())
        {
            if (!_host.IsFocusActive)
                _host.ClearFocus();
            return;
        }

        if (Games.Count == 0)
        {
            if (HasHistoryAction) { SelectHistoryAction(); return; }
            if (EmptyActions.Count > 0) { SelectEmptyAction(Math.Max(0, _gamepadNavigation.LibrarySelectedIndex)); return; }
            _host.ClearFocus();
            _gamepadNavigation.LibrarySelectedIndex = -1;
            return;
        }

        ApplyLibraryGamepadSelection(_gamepadNavigation.LibrarySelectedIndex < 0 ? 0 : _gamepadNavigation.LibrarySelectedIndex);
    }

    /// <summary>Highlights an app's card and scrolls to it; false when the current search or filters hide it.</summary>
    internal bool SelectGame(GameInfo game)
    {
        var index = Games.IndexOf(game);
        if (index < 0) return false;
        ApplyLibraryGamepadSelection(index, stealFocus: _host.IsFocusActive);
        return true;
    }

    internal void SyncGamepadLibrarySelection(bool bringIntoView = true)
    {
        if (!_host.IsFocusActive)
        {
            _host.ClearFocus();
            return;
        }

        if (!_isActive() || !_canSelect())
            return;
        // Search/filter rebuilds Games; keep chrome focus on search/sidebar.
        if (_keepChromeFocus())
        {
            ClearLibraryCardGamepadFocus();
            return;
        }

        if (Games.Count == 0)
        {
            if (HasHistoryAction) { SelectHistoryAction(); return; }
            if (EmptyActions.Count > 0) { SelectEmptyAction(Math.Max(0, _gamepadNavigation.LibrarySelectedIndex), bringIntoView); return; }
            _host.ClearFocus();
            _gamepadNavigation.LibrarySelectedIndex = -1;
            return;
        }

        var clamped = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
        ApplyLibraryGamepadSelection(clamped, bringIntoView: bringIntoView);
    }

    internal void ClearLibraryCardGamepadFocus()
    {
        historySelected = false;
        _view.LibrarySetupHistoryReviewButton.Classes.Set("gamepad-focused", false);
        _view.EmptyLibraryAddButton.Classes.Set("gamepad-focused", false);
        _view.EmptyLibraryBrowseButton.Classes.Set("gamepad-focused", false);
        foreach (var game in Games)
            game.IsGamepadFocused = false;
    }

    private long _selectionScrollRequest;

    internal void ApplyLibraryGamepadSelection(int index, bool stealFocus = true, bool bringIntoView = true)
    {
        var request = ++_selectionScrollRequest;
        // Overlays sit above the library; never steal zone/focus while they are open.
        if (_session.IsClosed || !_canSelect())
        {
            return;
        }

        if (EmptyActions.Count > 0) { SelectEmptyAction(Math.Max(0, index), bringIntoView); return; }
        historySelected = false;
        _view.LibrarySetupHistoryReviewButton.Classes.Set("gamepad-focused", false);
        var games = Games.ToList();
        index = _gamepadNavigation.ClampIndex(index, games.Count);
        _gamepadNavigation.LibrarySelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
        // Drop chrome focus entirely so sidebar/top bar don't keep or regain orange rings.
        _clearChrome(stealFocus);
        _gamepadNavigation.SidebarSelectedIndex = -1;
        _gamepadNavigation.TopBarSelectedIndex = -1;
        _host.ClearFocus();
        // Cards are not Focusable. Park on a non-tab-stop sink — Focus(null) lets
        // Avalonia's next arrow Tab to Continue (first tab stop).
        _host.FocusCard(stealFocus);
        if (index < 0 || index >= games.Count)
            return;
        games[index].IsGamepadFocused = true;
        if (!bringIntoView) return;
        var selected = games[index];
        Dispatcher.UIThread.Post(() =>
        {
            bool IsCurrentRequest() => request == _selectionScrollRequest && !_session.IsClosed && _canSelect()
                && selected.IsGamepadFocused && _gamepadNavigation.LibrarySelectedIndex == index
                && index < Games.Count && ReferenceEquals(Games[index], selected);
            if (!IsCurrentRequest()) return;
            var card = _view.FindGameCardRoot(selected);
            if (card == null) return;
            // The first scroll materializes lazy content near the destination. Its measured
            // height can replace a placeholder learned from an earlier library/layout, moving
            // the destination after that scroll. Complete this layout before aligning the
            // actual card; this is one bounded materialization/alignment operation, not a timer
            // or a dispatcher retry. No subscription remains after the request completes.
            card.BringIntoView();
            _view.UpdateLayout();
            if (IsCurrentRequest()) _view.FindGameCardRoot(selected)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Keep the selected library card's focus ring while a context menu is open,
    /// without restoring sidebar/top-bar focus rings.
    /// </summary>
    internal void PreserveLibraryGamepadFocusWhileOpeningMenu()
    {
        if (!_host.IsFocusActive || !_isActive())
        {
            _host.ClearFocus();
            return;
        }

        _clearChrome(true);
        _gamepadNavigation.SidebarSelectedIndex = -1;
        _gamepadNavigation.TopBarSelectedIndex = -1;
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
        if (index < 0)
            return;
        var selected = Games[index];
        _host.ClearFocus();
        foreach (var game in Games)
            game.IsGamepadFocused = ReferenceEquals(game, selected);
    }

    internal void RestoreLibraryGamepadFocusAfterMenu()
    {
        if (!_host.IsFocusActive || !_canSelect() || !_isActive())
        {
            if (!_host.IsFocusActive)
                _host.ClearFocus();
            return;
        }

        _clearChrome(true);
        _gamepadNavigation.SidebarSelectedIndex = -1;
        _gamepadNavigation.TopBarSelectedIndex = -1;
        if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.Library)
            return;
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.LibrarySelectedIndex, Games.Count);
        if (index < 0)
            return;
        var selected = Games[index];
        foreach (var game in Games)
            game.IsGamepadFocused = ReferenceEquals(game, selected);
    }

    internal bool IsListGamepadLayout() => !_settings.UseGridView;
    internal List<(double X, double Y)> CollectGameCardPositions(IReadOnlyList<GameInfo> games)
    {
        var positions = new List<(double X, double Y)>();
        foreach (var game in games)
        {
            var card = _view.FindGameCardRoot(game);
            positions.Add(GetControlCenter(card) ?? (0, positions.Count * 120));
        }

        return positions;
    }

    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Card(_gamepadNavigation, Games.ToList(), GamepadNavigationZone.Library, _gamepadNavigation.LibrarySelectedIndex, index => ApplyLibraryGamepadSelection(index, stealFocus: false), source);
    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.Library:
                ApplyLibraryGamepadSelection(transition.SelectedIndex ?? 0);
                return true;
            default:
                return false;
        }
    }
}
