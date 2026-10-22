using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services.Mods;
using Relicade.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;

namespace Relicade.Services
{
    public class GameManager : INotifyPropertyChanged, IDisposable
    {
        private static readonly RelicadeProfile Profile = RelicadeProfile.Instance;
        private readonly ISettingsStore _settingsStore;
        internal AppSettings CurrentSettings => _settingsStore.Current;

        // Cleanup-only: inspect THIS session's injected store without loading,
        // saving, exposing it or replacing the live library collection.
        internal void VerifyRollbackSettingsRecovery()
        {
            if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true })
                throw new IOException("This library session's settings are in recovery; repair and reopen before portable rollback.");
        }

        internal void VerifyBackupCleanupSettingsRecovery()
        {
            if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true })
                throw new IOException("This library session's settings are in recovery; repair and reopen before backup cleanup.");
        }
        public AppSettings _settings = new();
        private readonly HttpClient _httpClient;
        private readonly AppCatalogService _catalogService;
        private readonly ModProviderRegistry _modProviderRegistry;
        private bool _disposed;
        private string _appsFolder;
        private readonly string _cacheFolder;
        private List<GameInfo> _catalogApps = [];
        private List<GameInfo> _allGames = [];
        private readonly object _catalogInitializationLock = new();
        private Task? _catalogInitialization;

        public Func<Action, Task>? UiThreadInvoker { get; set; }
        public bool HasLoadedLibrary { get; private set; }

        private async Task RunOnUiThreadAsync(Action action)
        {
            var invoker = UiThreadInvoker;
            if (invoker == null)
            {
                action();
                return;
            }

            await invoker(action).ConfigureAwait(false);
        }

        public ObservableCollection<GameInfo> Games { get; set; } = [];
        public IReadOnlyList<GameInfo> LibraryApps => _catalogApps.Count > 0 ? _catalogApps : Games.ToArray();
        /// <summary>Session-only library search. Applied after Show scope and tag display filters.</summary>
        public string LibrarySearchText { get; set; } = "";
        private GameInfo? _revealedLibraryGame;
        public bool HasLibraryReveal => _revealedLibraryGame != null && _catalogApps.Contains(_revealedLibraryGame);
        public bool HasLibrarySearch => !string.IsNullOrWhiteSpace(LibrarySearchText);
        public bool HasNoLibrarySearchMatches =>
            !IsLibraryEmpty && HasLibrarySearch && Games.Count == 0;
        public HttpClient HttpClient => _httpClient;
        public AppCatalogService CatalogService => _catalogService;
        /// <summary>Which library apps are in the quiverlauncher.com catalog, and which of their releases the source catalog verified.</summary>
        public CatalogReleases CatalogReleases => _catalogReleases ??= new CatalogReleases(new QuiverCatalogClient(_httpClient), _cacheFolder);
        private CatalogReleases? _catalogReleases;
        public ModProviderRegistry ModProviderRegistry => _modProviderRegistry;
        public string AppsFolder => _appsFolder;
        public string GamesFolder => _appsFolder;
        public string CacheFolder => _cacheFolder;

        private string _currentVersionString = string.Empty;
        public string CurrentVersionString
        {
            get => _currentVersionString;
            set
            {
                if (_currentVersionString != value)
                {
                    _currentVersionString = value;
                    OnPropertyChanged(nameof(CurrentVersionString));
                }
            }
        }

        public GameManager(
            ISettingsStore? settingsStore = null,
            HttpClient? httpClient = null,
            AppCatalogService? catalogService = null)
        {
            _settingsStore = settingsStore ?? SettingsStoreProvider.Default;
            _httpClient = httpClient ?? CreateDefaultHttpClient();
            _catalogService = catalogService ?? new AppCatalogService(this);

            try
            {
                _settings = _settingsStore.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings in GameManager: {ex.Message}");
                _settings = new AppSettings();
            }

            RelicadePaths.EnsureUserDataRootExists();

            _appsFolder = !string.IsNullOrEmpty(_settings?.AppsPath)
                ? _settings.AppsPath
                : RelicadePaths.DefaultAppsDirectory;

            _cacheFolder = RelicadePaths.CacheDirectory;

            try
            {
                Directory.CreateDirectory(_appsFolder);
                Directory.CreateDirectory(_cacheFolder);
                GitHubApiCache.Initialize(_cacheFolder);
                ReleaseRequestCoordinator.Configure(_httpClient, _cacheFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create directories: {ex.Message}");
            }

            _modProviderRegistry = new ModProviderRegistry(_httpClient, _cacheFolder);

            LoadVersionString();
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient(new ReleaseApiTransport());
            client.DefaultRequestHeaders.Add("User-Agent", Profile.UserAgent);
            client.Timeout = TimeSpan.FromMinutes(30);
            return client;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                _httpClient.Dispose();

            _disposed = true;
        }

        public async Task CheckAllUpdatesAsync()
        {
            await CheckInstalledUpdatesAsync(true, null, CancellationToken.None);
        }

        public async Task<LibraryCheckResult> CheckInstalledUpdatesAsync(bool manual,
            IProgress<AppCheckProgress>? progress, CancellationToken token, IReadOnlySet<string>? appKeys = null)
        {
            var apps = LibraryApps.Where(app => appKeys == null || appKeys.Contains(app.InstanceKey)).ToArray();
            var expected = apps.Where(a => a.IsInstalled && !a.IsManuallyManaged && !string.IsNullOrWhiteSpace(a.Repository))
                .DistinctBy(a => a.InstanceKey, StringComparer.OrdinalIgnoreCase).Select(a =>
                    new AppCheckResult(a.InstanceKey, AppCheckOutcome.Cancelled, a.DisplayName, "The update check did not finish for this app.")).ToArray();
            progress?.Report(new(0, expected.Length, Targets: expected));
            await CatalogReleases.RefreshAsync(apps, token);
            await SyncCatalogDetailsAsync(apps, token);
            foreach (var app in apps)
            {
                token.ThrowIfCancellationRequested();
                if (app.Status is GameStatus.Downloading or GameStatus.Installing or GameStatus.Updating) continue;
                await GameStatusService.CheckStatusAsync(app, _httpClient, _appsFolder,
                    checkRemoteVersion: false, applyCachedRelease: false);
            }
            return await new LibraryUpdateChecker(_httpClient, _settingsStore.Current).CheckAsync(
                apps.Where(a => a.IsInstalled), manual, TimeSpan.FromHours(6), progress, token);
        }

        public async Task<LibraryCheckResult> RefreshUninstalledUpdatesAsync(CancellationToken token)
        {
            var apps = LibraryApps.Where(a => a.Status == GameStatus.NotInstalled).ToArray();
            await CatalogReleases.RefreshAsync(apps, token);
            return await new LibraryUpdateChecker(_httpClient, _settingsStore.Current).CheckAsync(apps, false, TimeSpan.FromHours(24), null, token);
        }

        private void LoadVersionString()
        {
            CurrentVersionString = LauncherVersionService.ReadInstalledVersion();
        }

        public GameInfo? GetLatestPlayedInstalledGame()
        {
            if (_catalogApps.Count == 0 || string.IsNullOrEmpty(_appsFolder))
                return null;

            var settings = _settingsStore.Current;
            settings.EnsureInitialized();

            DateTime latestTime = DateTime.MinValue;
            GameInfo? latestGame = null;
            foreach (var game in _catalogApps)
            {
                if (game == null || string.IsNullOrEmpty(game.FolderName))
                    continue;

                if (IsGameManuallyHidden(settings, game))
                    continue;

                var gamePath = game.GetInstallPath(_appsFolder);
                var lastPlayedPath = Path.Combine(gamePath, "LastPlayed.txt");
                if (File.Exists(lastPlayedPath))
                {
                    var timeString = File.ReadAllText(lastPlayedPath).Trim();
                    if (DateTime.TryParseExact(timeString, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out DateTime lastPlayed) && lastPlayed > latestTime)
                    {
                        latestTime = lastPlayed;
                        latestGame = game;
                    }
                }
            }
            return latestGame;
        }

        public async Task LoadCustomAndCachedIconsAsync(bool allowDownload = true, CancellationToken cancellationToken = default)
        {
            if (Games == null || string.IsNullOrEmpty(_cacheFolder))
                return;

            foreach (var game in Games)
            {
                game?.LoadCustomIcon(_cacheFolder);
            }

            var tasks = Games
                .Where(g => g != null)
                .Select(g => g.LoadAndCacheDefaultIconAsync(_cacheFolder, _settingsStore.Current.GitHubApiToken, allowDownload, cancellationToken));

            await Task.WhenAll(tasks);
        }

        public async Task ClearIconCacheAsync()
        {
            try
            {
                var iconsDir = Path.Combine(_cacheFolder, "Icons");
                if (Directory.Exists(iconsDir))
                {
                    Directory.Delete(iconsDir, true);
                    await LoadCustomAndCachedIconsAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear icon cache: {ex.Message}");
            }
        }

        public GameInfo? FindGameByName(string name)
        {
            return Games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public GameInfo? FindGameByFolderName(string folderName)
        {
            return Games.FirstOrDefault(g => string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
        }

        // Removal-only loading intentionally bypasses LibraryFileStore.ReadAsync:
        // even its validated read creates/prunes backups and can migrate games.json.
        // Reuse only the existing pure strict parser; never run status or recovery.
        private byte[]? _removalLibrary;
        internal static string ValidateRemovalAppsPath(AppSettings settings)
        {
            var path = string.IsNullOrEmpty(settings.AppsPath) ? RelicadePaths.DefaultAppsDirectory : settings.AppsPath;
            ValidateRemovalAbsolutePath(path);
            // An absent library base is not permission to recreate a lost generation
            // via the unchanged GameManager constructor. Removal needs an existing base.
            if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                throw new IOException("The configured apps directory is unavailable; no files were removed.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private static void ValidateRemovalAbsolutePath(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) ||
                path.Replace('\\', '/').Split('/').Any(p => p is "." or ".." ||
                    p.Equals(PortableInstallationTransaction.DirectoryName, StringComparison.OrdinalIgnoreCase)) ||
                Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) == null)
                throw new IOException("Unsafe or ambiguous local removal path. Repair the library first.");
            for (var item = new DirectoryInfo(path); item != null; item = item.Parent)
            {
                try
                {
                    if ((File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Linked removal paths require explicit repair; no files were removed.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        internal async Task LoadGamesForRemovalAsync()
        {
            var settings = _settingsStore.Load();
            if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true } recovery)
                throw new SettingsRecoveryRequiredException(recovery.ReadError ?? new IOException("Settings recovery is required before removal."));
            if (!string.Equals(ValidateRemovalAppsPath(settings), Path.TrimEndingDirectorySeparator(Path.GetFullPath(_appsFolder)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("The configured apps directory changed. Reopen before removal.");
            ValidateRemovalAbsolutePath(_catalogService.AppsConfigPath);
            var bytes = await File.ReadAllBytesAsync(_catalogService.AppsConfigPath);
            ManagedShortcutLaunch.ValidateLocalIdentities(bytes);
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            var sections = new Dictionary<string, System.Text.Json.JsonElement>();
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                sections.Add("apps", doc.RootElement);
            else
                foreach (var field in doc.RootElement.EnumerateObject())
                {
                    if (field.Name is not ("apps" or "standard" or "experimental" or "custom"))
                        throw new IOException("Unknown local library format; repair before removal.");
                    sections.Add(field.Name, field.Value);
                }
            var apps = _catalogService.ParseAppsFromDictionary(sections);
            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.FolderName))
                    throw new IOException("Incomplete local removal identity; repair the library first.");
                PortableInstallationTransaction.ValidateRelative(app.FolderName);
                ValidateRemovalAbsolutePath(Path.Combine(_appsFolder, app.FolderName));
                if (app.InstallPath != null) ValidateRemovalAbsolutePath(app.InstallPath);
                app.GameManager = this;
                app.IsInLocalAppsJson = true;
            }
            _removalLibrary = bytes;
            VerifyRemovalLibrary();
            // In-memory lookup data only; do not project installed/recovery status,
            // read artwork, create snapshots, normalize settings or save anything.
            _catalogApps = apps;
            Games = new ObservableCollection<GameInfo>(apps);
        }

        internal void VerifyRemovalLibrary()
        {
            // Direct internal method tests predate the public command loader. Public
            // removal always sets this snapshot and revalidates it under the leases.
            if (_removalLibrary == null) return;
            if (!File.ReadAllBytes(_catalogService.AppsConfigPath).AsSpan().SequenceEqual(_removalLibrary))
                throw new IOException("The local library changed while preparing removal. Retry; no files were removed.");
            var settings = _settingsStore.Load();
            if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true } ||
                !string.Equals(ValidateRemovalAppsPath(settings), Path.TrimEndingDirectorySeparator(Path.GetFullPath(_appsFolder)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Settings changed or need recovery; no files were removed.");
        }

        internal Task LoadGamesForCliAsync() => LoadGamesCoreAsync(false, listScopeOverride: AppListScope.AllApps);

        private byte[]? _managedShortcutLibrary;
        internal async Task LoadGamesForManagedShortcutAsync()
        {
            _settingsStore.Load();
            if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true } recovery)
                throw new InvalidOperationException(recovery.ReadError?.ToString() ?? "Settings recovery is required before shortcut launch.");
            // Do not migrate or manufacture a library for this exact-identity command.
            var bytes = await File.ReadAllBytesAsync(_catalogService.AppsConfigPath);
            await _catalogService.LoadLocalAppsAsync(); // existing complete strict validation
            ManagedShortcutLaunch.ValidateLocalIdentities(bytes);
            _managedShortcutLibrary = bytes;
            await LoadGamesCoreAsync(false, allowNetwork: false, listScopeOverride: AppListScope.AllApps, preserveSettings: true);
            VerifyManagedShortcutLibrary();
        }

        internal void VerifyManagedShortcutLibrary()
        {
            if (_managedShortcutLibrary == null || !File.ReadAllBytes(_catalogService.AppsConfigPath).AsSpan().SequenceEqual(_managedShortcutLibrary))
                throw new IOException("The local library changed while preparing the shortcut. Retry from the current library tile.");
        }

        public Task LoadGamesAsync(bool forceUpdateCheck = false) =>
            LoadGamesCoreAsync(forceUpdateCheck);

        internal async Task RefreshLoadedLibraryMetadataAsync(CancellationToken cancellationToken)
        {
            // Keep the same app instances and collection while online results arrive.
            var apps = _catalogApps.ToArray();
            await CatalogReleases.RefreshAsync(apps, cancellationToken);
            await SyncCatalogDetailsAsync(apps, cancellationToken);
            await new LibraryUpdateChecker(_httpClient, _settingsStore.Current).CheckStartupAsync(apps, cancellationToken);
            await Task.WhenAll(apps.Select(async app =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await app.LoadAndCacheDefaultIconAsync(_cacheFolder, app.GetReleaseApiToken(_settingsStore.Current), cancellationToken: cancellationToken);
            }));
        }

        /// <summary>
        /// Reloads the library from local apps.json, checking every app's status on disk. Set
        /// <paramref name="allowNetwork"/> to false (startup) to use cached releases and icons
        /// without waiting for remote metadata.
        /// </summary>
        public Task ReloadLibraryFromDiskAsync(bool allowNetwork = true) =>
            LoadGamesCoreAsync(forceUpdateCheck: false, allowNetwork);

        private async Task LoadGamesCoreAsync(
            bool forceUpdateCheck,
            bool allowNetwork = true,
            AppListScope? listScopeOverride = null,
            bool preserveSettings = false)
        {
            // Validate and back up the existing library without rewriting it. Keep
            // initialization within the caller's awaited load/session lifetime.
            Task initialization;
            lock (_catalogInitializationLock)
            {
                if (_catalogInitialization is { IsFaulted: true } or { IsCanceled: true }) _catalogInitialization = null;
                initialization = _catalogInitialization ??= _catalogService.ValidateAndFixLocalAppsJsonAsync();
            }
            await initialization.ConfigureAwait(false);

            _settings = _settingsStore.Load();
            if (preserveSettings)
                _settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_settings))!;
            _settings.EnsureInitialized();

            Games ??= [];
            var previousByKey = _catalogApps.Where(a => !string.IsNullOrWhiteSpace(a.InstanceKey)).GroupBy(a => a.InstanceKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var allApps = await _catalogService.LoadLocalCatalogAsync(_settings).ConfigureAwait(false);
            _catalogApps = allApps.Where(app => app != null).Cast<GameInfo>().ToList();

            foreach (var app in _catalogApps)
            {
                if (app == null)
                    continue;

                app.IsInLocalAppsJson = true;
                CatalogReleases.ApplyKnown(app);
                if (previousByKey.TryGetValue(app.InstanceKey, out var previousCheck) &&
                    string.Equals(app.Repository, previousCheck.Repository, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(app.EffectiveRepositorySource, previousCheck.EffectiveRepositorySource, StringComparison.OrdinalIgnoreCase))
                    app.RepositoryCheckError = previousCheck.RepositoryCheckError;
            }

            if (!string.IsNullOrEmpty(_appsFolder))
            {
                await Task.WhenAll(_catalogApps.Where(app => app != null).Select(async app =>
                {
                    try
                    {
                        await GameStatusService.CheckStatusAsync(app, _httpClient, _appsFolder, forceUpdateCheck,
                            checkRemoteVersion: allowNetwork, applyCachedRelease: allowNetwork);
                        if (!allowNetwork) StartupVersionResolver.Apply(app);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking status for {app.Name}: {ex.Message}");
                    }
                }));
            }

            var displaySettings = _settings;
            if (listScopeOverride is { } scope)
            {
                displaySettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                    System.Text.Json.JsonSerializer.Serialize(_settings))!;
                displaySettings.ListScope = scope;
            }
            await RebuildVisibleGamesAsync(displaySettings);

            // The local collection is ready; artwork and online metadata must not extend startup loading UI.
            await RunOnUiThreadAsync(() =>
            {
                HasLoadedLibrary = true;
                OnPropertyChanged(nameof(HasLoadedLibrary));
            });

            await LoadCustomAndCachedIconsAsync(allowDownload: allowNetwork);
        }

        public async Task InsertCatalogAppAsync(GameInfo app, AppSettings settings)
        {
            app.GameManager = this;
            app.IsInLocalAppsJson = true;
            AppCatalogService.ApplyUserAppTags(app, settings);
            AppCatalogService.ApplyUserAppDisplayNames(app, settings);
            app.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
            app.TruncateLibraryCardTitles = settings.TruncateLibraryCardTitles;
            AppCatalogService.RefreshLibraryCardTags([app], settings);
            await Task.Run(() => GameGridViewModel.GetLastPlayedTime(app, _appsFolder));
            await RunOnUiThreadAsync(() =>
            {
                if (_catalogApps.Any(g => g.InstanceKey.Equals(app.InstanceKey, StringComparison.OrdinalIgnoreCase))) return;
                _settings = settings;
                _catalogApps.Add(app);
                app.IsManuallyHidden = IsGameManuallyHidden(settings, app);
                if (FilterCatalogByListScope([app], settings).Count > 0)
                {
                    _allGames.Add(app);
                    if (GetVisibleGames(settings).Contains(app))
                    {
                        var low = 0;
                        var high = Games.Count;
                        while (low < high)
                        {
                            var middle = (low + high) / 2;
                            if (GameGridViewModel.CompareForInsertion(app, Games[middle], settings.SortBy ?? "Name", settings.IgnoreArticlesWhenSorting) >= 0)
                                low = middle + 1;
                            else high = middle;
                        }
                        Games.Insert(low, app);
                    }
                }
                OnPropertyChanged(nameof(IsLibraryEmpty));
                OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
            });
        }

        /// <summary>
        /// Gives library apps from the catalog its current name, project, icon and tags, keeping what the player changed
        /// (see <see cref="CatalogLibrarySync"/>), and saves them to apps.json.
        /// </summary>
        internal async Task SyncCatalogDetailsAsync(IReadOnlyList<GameInfo> apps, CancellationToken token)
        {
            var changed = new List<GameInfo>();
            void Apply()
            {
                foreach (var app in apps)
                {
                    if (CatalogReleases.ListedApp(app) is not { } entry || !CatalogLibrarySync.Apply(app, entry)) continue;
                    changed.Add(app);
                    app.RefreshLibraryCardTags();
                }
            }
            if (UiThreadInvoker is { } invoker) await invoker(Apply).ConfigureAwait(false);
            else Apply();
            if (changed.Count == 0) return;
            token.ThrowIfCancellationRequested();
            try
            {
                // Saved onto what is on disk now, so changes made meanwhile elsewhere aren't lost.
                var saved = await _catalogService.LoadLocalAppsAsync().ConfigureAwait(false);
                var byKey = changed.ToDictionary(a => a.InstanceKey, StringComparer.OrdinalIgnoreCase);
                foreach (var app in saved)
                    if (byKey.TryGetValue(app.InstanceKey, out var live)) CatalogLibrarySync.CopyTo(live, app);
                await _catalogService.SaveLocalAppsAsync(saved).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                System.Diagnostics.Debug.WriteLine($"Catalog details not saved: {ex.Message}");
            }
        }

        public async Task ExportGamesAsync()
        {
            try
            {
                var apps = await _catalogService.LoadLocalAppsAsync().ConfigureAwait(false);
                await _catalogService.SaveLocalAppsAsync(apps).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Apps exported successfully to {_catalogService.AppsConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting apps: {ex.Message}");
            }
        }

        public async Task UpdateGamesFolderAsync(string newPath)
        {
            try
            {
                string targetPath;

                if (!string.IsNullOrWhiteSpace(newPath))
                {
                    if (!Directory.Exists(newPath))
                        Directory.CreateDirectory(newPath);

                    targetPath = newPath;
                }
                else
                {
                    targetPath = RelicadePaths.DefaultAppsDirectory;
                    Directory.CreateDirectory(targetPath);
                }

                _appsFolder = targetPath;
                Games.Clear();

                await LoadGamesAsync();

                OnPropertyChanged(nameof(Games));
                OnPropertyChanged(nameof(AppsFolder));
                OnPropertyChanged(nameof(GamesFolder));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating apps folder: {ex.Message}");
                _appsFolder = RelicadePaths.DefaultAppsDirectory;
                Directory.CreateDirectory(_appsFolder);
                throw;
            }
        }

        private static string GetHiddenGameKey(GameInfo game)
        {
            if (!string.IsNullOrWhiteSpace(game.FolderName))
                return $"folder:{game.FolderName}";

            if (!string.IsNullOrWhiteSpace(game.Repository))
                return $"repo:{game.Repository}";

            return $"name:{game.Name ?? string.Empty}";
        }

        public void ToggleUserHide(GameInfo game)
        {
            if (game == null)
                return;

            var settings = _settingsStore.Current;
            if (IsGameManuallyHidden(settings, game))
            {
                RemoveManuallyHiddenGame(settings, game);
            }
            else
            {
                AddManuallyHiddenGame(settings, game);
            }
            _settingsStore.Save(settings);
            _settings = settings;
            FilterGames(settings);
        }

        public bool IsManuallyHidden(GameInfo game)
        {
            return IsGameManuallyHidden(_settingsStore.Current, game);
        }

        public void HideGame(GameInfo game)
        {
            if (game == null)
                return;

            var settings = _settingsStore.Current;
            settings.EnsureInitialized();
            if (!IsGameManuallyHidden(settings, game))
            {
                AddManuallyHiddenGame(settings, game);
                _settingsStore.Save(settings);
                _settings = settings;
                FilterGames(settings);
            }
        }

        public void UnhideAllGames()
        {
            SetListScope(AppListScope.AllApps);
        }

        public void HideAllNonInstalledGames()
        {
            SetListScope(AppListScope.InstalledOnly);
        }

        public void SetListScope(AppListScope scope, AppSettings? settings = null)
        {
            settings ??= _settingsStore.Load();
            settings.EnsureInitialized();
            _revealedLibraryGame = null;
            settings.ListScope = scope;
            settings.HiddenApps.Clear();
            _settingsStore.Save(settings);
            _settings = settings;
            FilterGames(settings);
        }

        private void FilterGames(AppSettings settings)
        {
            RebuildVisibleGames(settings);
        }

        public void RevealLibraryGame(GameInfo game)
        {
            if (!_catalogApps.Contains(game)) return;
            _revealedLibraryGame = null;
            var visible = GetVisibleGames(_settings);
            if (!visible.Contains(game)) { _revealedLibraryGame = game; visible.Add(game); }
            ApplyGamesList(visible);
        }

        public void ClearLibraryReveal()
        {
            if (_revealedLibraryGame == null) return;
            _revealedLibraryGame = null;
            ApplyGamesList(GetVisibleGames(_settings));
        }

        public void ApplyTagDisplayFilter(AppSettings? settings = null)
        {
            settings ??= _settingsStore.Load();
            settings.EnsureInitialized();

            ApplyGamesList(GetVisibleGames(settings));
        }

        private async Task ApplyTagDisplayFilterAsync(AppSettings settings)
        {
            var gamesToShow = GetVisibleGames(settings);
            await RunOnUiThreadAsync(() => ApplyGamesList(gamesToShow));
        }

        private List<GameInfo> GetVisibleGames(AppSettings settings)
        {
            settings.EnsureInitialized();

            IEnumerable<GameInfo> visibleGames = _allGames;
            if (!string.IsNullOrWhiteSpace(settings.ActiveTagDisplayFilterId))
            {
                var filter = settings.TagDisplayFilters.FirstOrDefault(f =>
                    string.Equals(f.Id, settings.ActiveTagDisplayFilterId, StringComparison.OrdinalIgnoreCase));

                if (filter != null)
                {
                    visibleGames = _allGames.Where(game =>
                        TagHelper.MatchesDisplayFilter(
                            game.Tags,
                            filter.Tags,
                            filter.MatchMode,
                            filter.ExcludeTags,
                            filter.ExcludeMatchMode));
                }
            }

            if (!string.IsNullOrWhiteSpace(LibrarySearchText))
                visibleGames = visibleGames.Where(game => AppSearch.Matches(game, LibrarySearchText));

            var result = visibleGames.ToList();
            if (_revealedLibraryGame != null && !_catalogApps.Contains(_revealedLibraryGame)) _revealedLibraryGame = null;
            if (HasLibraryReveal && !result.Contains(_revealedLibraryGame!)) result.Add(_revealedLibraryGame!);
            return result;
        }

        private void ApplyGamesList(List<GameInfo> gamesToShow)
        {
            var sorted = new GameGridViewModel().SortGames(
                gamesToShow,
                _settings.SortBy ?? "Name",
                _appsFolder ?? string.Empty,
                _settings.IgnoreArticlesWhenSorting);

            Games.Clear();
            foreach (var app in sorted)
                Games.Add(app);

            OnPropertyChanged(nameof(Games));
            OnPropertyChanged(nameof(HasLibraryReveal));
            OnPropertyChanged(nameof(HasLibrarySearch));
            OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
        }

        private async Task RebuildVisibleGamesAsync(AppSettings settings)
        {
            settings.EnsureInitialized();
            SyncManuallyHiddenFlags(settings);

            _allGames = FilterCatalogByListScope(_catalogApps, settings);

            await ApplyTagDisplayFilterAsync(settings);

            await RunOnUiThreadAsync(() =>
            {
                OnPropertyChanged(nameof(IsLibraryEmpty));
                OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
            });
        }

        private void RebuildVisibleGames(AppSettings settings)
        {
            settings.EnsureInitialized();
            SyncManuallyHiddenFlags(settings);

            _allGames = FilterCatalogByListScope(_catalogApps, settings);

            ApplyTagDisplayFilter(settings);
            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
        }

        private static List<GameInfo> FilterCatalogByListScope(IEnumerable<GameInfo> catalogApps, AppSettings settings)
        {
            var showHiddenOnly = settings.ListScope == AppListScope.HiddenOnly;

            return catalogApps
                .Where(app => app != null)
                .Where(app =>
                {
                    var isHidden = IsGameManuallyHidden(settings, app);
                    return showHiddenOnly ? isHidden : !isHidden;
                })
                .Where(app => settings.ListScope != AppListScope.InstalledOnly
                    || app.Status != GameStatus.NotInstalled)
                .ToList();
        }

        private void SyncManuallyHiddenFlags(AppSettings settings)
        {
            foreach (var app in _catalogApps)
            {
                if (app != null)
                    app.IsManuallyHidden = IsGameManuallyHidden(settings, app);
            }
        }

        public bool IsLibraryEmpty => _catalogApps.Count == 0;

        internal void SetCatalogAppsAndFilter(List<GameInfo> catalogApps, AppSettings settings)
        {
            _catalogApps = catalogApps;
            _settings = settings;
            RebuildVisibleGames(settings);
        }

        private static bool IsGameManuallyHidden(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return false;

            var key = GetHiddenGameKey(game);
            return settings.ManuallyHiddenApps.Contains(key) ||
                   (!string.IsNullOrWhiteSpace(game.Name) && settings.ManuallyHiddenApps.Contains(game.Name));
        }

        private static void AddManuallyHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return;

            var key = GetHiddenGameKey(game);
            if (!settings.ManuallyHiddenApps.Contains(key))
                settings.ManuallyHiddenApps.Add(key);
        }

        private static void RemoveManuallyHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return;

            var key = GetHiddenGameKey(game);
            settings.ManuallyHiddenApps.Remove(key);
            if (!string.IsNullOrWhiteSpace(game.Name))
                settings.ManuallyHiddenApps.Remove(game.Name);
        }

        public void RefreshGamesWithFilter(AppSettings settings)
        {
            _ = LoadGamesAsync();
        }

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
