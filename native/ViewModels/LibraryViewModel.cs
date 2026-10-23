using System.Collections.ObjectModel;
using System.ComponentModel;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.ViewModels;

public sealed class LibraryViewModel : ObservableViewModel, IDisposable
{
    private readonly GameManager _manager;
    private readonly GameGridViewModel _sorting = new();
    private CancellationTokenSource? _search;
    private string? _pendingSearchText;
    private GameInfo? _continue;
    private string _sortBy = "Name";
    private bool _closed;
    private bool _initialLoading;
    private bool _initialLoadFailed;
    public SettingsViewModel Settings { get; }
    public LibrarySetupHistoryViewModel SetupHistory { get; }
    public ObservableCollection<GameInfo> Games => _manager.Games;
    public ObservableCollection<TagDisplayFilterListItem> TagDisplayFilters { get; } = new();
    public string SortBy { get => _sortBy; set => Set(ref _sortBy, value); }
    public GameInfo? ContinueGameInfo { get => _continue; private set => Set(ref _continue, value); }
    public bool IsContinueVisible => ContinueGameInfo != null;
    public bool IsInitialLoading => _initialLoading;
    public bool IsLibraryEmpty => !_initialLoading && !_initialLoadFailed && _manager.IsLibraryEmpty;
    public bool HasNoSearchMatches => !_initialLoading && !_initialLoadFailed && _manager.HasNoLibrarySearchMatches;
    public bool HasNavigationReveal => _manager.HasLibraryReveal;
    public string SearchText => _manager.LibrarySearchText;
    public double CardPixelSize => PlatformCapabilities.IsMobile ? double.NaN : Settings.SlotSize;
    public bool ShowLibraryManagement => !Settings.KioskLocked;

    public LibraryViewModel(GameManager manager, SettingsViewModel settings) : this(manager, settings, null) { }
    internal LibraryViewModel(GameManager manager, SettingsViewModel settings,
        Func<LibrarySetupHistoryInspection, CancellationToken, Task<LibrarySetupHistoryInspection.Step>>? read)
    {
        _manager = manager; Settings = settings; SetupHistory = new(manager, read);
        manager.PropertyChanged += ManagerChanged;
        settings.PropertyChanged += SettingsChanged;
    }
    private void ManagerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameManager.HasLoadedLibrary) && _manager.HasLoadedLibrary)
        {
            _initialLoading = false;
            _initialLoadFailed = false;
            SetupHistory.Refresh();
            Notify(null);
        }
        if (e.PropertyName == nameof(GameManager.IsLibraryEmpty)) SetupHistory.InvalidateIfChanged();
        if (e.PropertyName is nameof(GameManager.IsLibraryEmpty) or nameof(GameManager.HasNoLibrarySearchMatches) or nameof(GameManager.LibrarySearchText) or nameof(GameManager.HasLibraryReveal)) Notify(null);
    }
    internal void BeginInitialLoad()
    {
        if (_closed || _manager.HasLoadedLibrary || Games.Count > 0) return;
        _initialLoading = true;
        _initialLoadFailed = false;
        Notify(null);
    }
    internal void FailInitialLoad()
    {
        if (_closed) return;
        _initialLoading = false;
        _initialLoadFailed = !_manager.HasLoadedLibrary;
        Notify(null);
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SettingsViewModel.KioskLocked))
            Notify(nameof(ShowLibraryManagement));
        if (e.PropertyName == nameof(SettingsViewModel.KioskLocked))
            ApplyDisplaySettings();
        Notify(nameof(CardPixelSize));
    }
    public void RefreshContinue()
    {
        if (_closed) return;
        ContinueGameInfo = _manager.GetLatestPlayedInstalledGame();
        Notify(nameof(IsContinueVisible));
    }
    public void ApplySorting() => _sorting.ApplySort(Games, SortBy, _manager.GamesFolder, Settings.Current.IgnoreArticlesWhenSorting);
    public async Task RefreshManualStatusesAsync(CancellationToken token)
    {
        foreach (var game in Games.Where(g => g.IsManuallyManaged).ToList())
        {
            if (_closed || token.IsCancellationRequested) return;
            try { await game.CheckStatusAsync(_manager.HttpClient, _manager.GamesFolder); }
            catch { /* Returning from the file explorer is a best-effort refresh. */ }
        }
        if (_closed || token.IsCancellationRequested) return;
        ApplySorting();
        RefreshContinue();
    }
    public void RefreshFilters()
    {
        var settings = Settings.Current;
        settings.EnsureInitialized();
        if (TagDisplayFilterListItem.TryUpdateSelection(TagDisplayFilters, settings.TagDisplayFilters, settings.ActiveTagDisplayFilterId)) return;
        TagDisplayFilters.Clear();
        foreach (var filter in settings.TagDisplayFilters)
            TagDisplayFilters.Add(TagDisplayFilterListItem.FromFilter(filter, string.Equals(filter.Id, settings.ActiveTagDisplayFilterId, StringComparison.OrdinalIgnoreCase)));
    }
    public void ApplyDisplaySettings()
    {
        var settings = Settings.Current;
        foreach (var game in Games)
        {
            game.LibraryNameStyle = settings.LibraryNameStyle;
            game.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
            game.TruncateLibraryCardTitles = settings.TruncateLibraryCardTitles;
        }
        AppCatalogService.RefreshLibraryCardTags(Games, settings);
    }
    public async Task<bool> SearchAsync(string query, CancellationToken lifetime, bool debounce = true)
    {
        if (_closed) return false;
        CancelSearch();
        _manager.ClearLibraryReveal();
        _pendingSearchText = query;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _search = request;
        try
        {
            if (debounce && !string.IsNullOrWhiteSpace(query)) await Task.Delay(250, request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_search, request)) return false;
            _manager.LibrarySearchText = query;
            _manager.ApplyTagDisplayFilter(Settings.Current);
            ApplySorting();
            Notify(null);
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        finally
        {
            if (ReferenceEquals(_search, request)) { _search = null; _pendingSearchText = null; }
            request.Dispose();
        }
    }
    public void RevealForNavigation(GameInfo game)
    {
        // Finish the latest typed query without waiting for its debounce, so a
        // pending search cannot hide the selected target just after navigation.
        var pending = _pendingSearchText;
        CancelSearch();
        if (pending != null) _manager.LibrarySearchText = pending;
        _manager.RevealLibraryGame(game);
    }
    public void CancelSearch() { _search?.Cancel(); _search = null; _pendingSearchText = null; }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        SetupHistory.Dispose();
        _initialLoading = false;
        _initialLoadFailed |= !_manager.HasLoadedLibrary;
        Notify(nameof(IsInitialLoading));
        CancelSearch();
        _manager.PropertyChanged -= ManagerChanged;
        Settings.PropertyChanged -= SettingsChanged;
    }
}
