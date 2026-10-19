using Avalonia.Platform.Storage;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.ViewModels;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Relicade.Services;
/// <summary>Library management actions using the existing install and persistence services.</summary>
public sealed class LibraryActions
{
    private readonly GameManager _gameManager;
    private readonly LibraryPersistenceService _libraryPersistence;
    private readonly SettingsViewModel _settingsModel;
    private readonly LauncherSession _session;
    private readonly LibraryViewModel _library;
    private readonly Func<IStorageProvider> _storage;
    private readonly Func<string, string, bool, bool, Task<bool>> _prompt;
    private readonly Action<string> _openUrl;
    private readonly Func<Task> _libraryChanged;
    private readonly Action<string> _moveToTrash;
    private AppSettings _settings => _settingsModel.Current;

    public LibraryActions(GameManager manager, LibraryPersistenceService persistence, SettingsViewModel settings, LauncherSession session, LibraryViewModel library, Func<IStorageProvider> storage, Func<string, string, bool, bool, Task<bool>> prompt, Action<string> openUrl, Func<Task> libraryChanged, Action<string>? moveToTrash = null)
    {
        _gameManager = manager;
        _libraryPersistence = persistence;
        _settingsModel = settings;
        _session = session;
        _library = library;
        _storage = storage;
        _prompt = prompt;
        _openUrl = openUrl;
        _libraryChanged = libraryChanged;
        _moveToTrash = moveToTrash ?? RecycleBinHelper.MoveToRecycleBin;
    }

    private bool RefuseKiosk() => _settingsModel.KioskLocked;
    private Task<bool> ShowMessageBoxAsync(string message, string title, bool isQuestion = false, bool preferCancelDefault = false) => _session.IsClosed ? Task.FromResult(false) : _prompt(message, title, isQuestion, preferCancelDefault);
    private void OpenUrl(string url)
    {
        if (!_session.IsClosed)
            _openUrl(url);
    }

    private void Changed()
    {
        if (!_session.IsClosed)
        {
            _library.ApplySorting();
            _library.RefreshContinue();
        }
    }

    public Task OpenRepositoryAsync(GameInfo? game)
    {
        if (RefuseKiosk()) return Task.CompletedTask;
        return _session.RunAsync(async () =>
    {
        if (string.IsNullOrEmpty(game?.Repository)) { await ShowMessageBoxAsync("Failed to open repository page", "Error"); return; }
        try { OpenUrl(RepositorySourceHelper.GetRepositoryPageUrl(game.RepositorySource, game.Repository)); }
        catch (Exception ex) { await ShowMessageBoxAsync($"Failed to open repository page: {ex.Message}", "Error"); }
    });
    }

    public void OpenGameFolder(GameInfo game)
    {
        if (RefuseKiosk() || _session.IsClosed) return;
        if (string.IsNullOrEmpty(game.FolderName) && string.IsNullOrWhiteSpace(game.InstallPath))
        {
            _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the game folder.", "Action Error"));
            return;
        }

        try
        {
            if (game.IsManuallyManaged)
                ManualAppFolderService.EnsurePrepared(game, _gameManager.GamesFolder);
            var folderPath = game.GetInstallPath(_gameManager.GamesFolder);
            if (FlatpakService.HasReceipt(folderPath))
            {
                var receipt = FlatpakService.ReadReceipt(folderPath) ?? FlatpakService.ReadReceipt(folderPath, pending: true);
                if (receipt != null && Directory.Exists(FlatpakService.DataDirectory(receipt)))
                    folderPath = FlatpakService.DataDirectory(receipt);
            }
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the game folder.", "Action Error"));
                return;
            }

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
            OpenUrl(folderPath);
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open folder: {ex.Message}", "Action Error"));
        }
    }

    public async Task ConfigureRunnerAsync(GameInfo? game)
    {
        if (RefuseKiosk()) return;
        await _session.RunAsync(async () =>
        {
            if (game == null || string.IsNullOrWhiteSpace(game.FolderName))
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await ShowMessageBoxAsync("Windows runner settings are only used on Linux.", "Windows Runner");
                return;
            }

            try
            {
                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                var config = await GameDialogService.ShowLinuxWindowsRunnerDialogAsync(gamePath, LinuxWindowsRunnerConfig.FromGame(game), isInstall: false, cancellationToken: _session.Token);
                if (config == null || _session.IsClosed)
                    return;
                config.ApplyTo(game);
                var allGames = await LoadGamesFromJsonAsync();
                var matchingGame = FindMatchingSavedApp(allGames, game);
                if (matchingGame != null)
                {
                    matchingGame.LinuxRunner = game.LinuxRunner;
                    matchingGame.LinuxPrefixPath = game.LinuxPrefixPath;
                    matchingGame.LinuxProtonPath = game.LinuxProtonPath;
                    matchingGame.LinuxCustomLaunchCommand = game.LinuxCustomLaunchCommand;
                    await SaveGamesToJsonAsync(allGames);
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to save Windows runner settings: {ex.Message}", "Windows Runner");
            }
        });
    }

    public async Task LocateInstallAsync(GameInfo? game)
    {
        if (RefuseKiosk()) return;
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var folders = await _storage().OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Select existing install folder for {game.Name}", AllowMultiple = false }).WaitAsync(_session.Token);
            _session.Token.ThrowIfCancellationRequested();
            if (folders == null || folders.Count == 0)
                return;
            var selectedPath = folders[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
            {
                await ShowMessageBoxAsync("The selected install folder could not be found.", "Install Folder Not Found");
                return;
            }

            GameInstallLocationService.ApplyLocatedPath(game, selectedPath);
            await _libraryPersistence.SaveInstallLocationAsync(game);
            await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder, forceUpdateCheck: true);
            Changed();
        });
    }

    // Inspect both the located path and default slot: stale flags/InstallPath must
    // not hide owned history behind an OS receipt or another root.
    private string[] RemovalPaths(GameInfo game)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(game.FolderName))
        {
            PortableInstallationTransaction.ValidateRelative(game.FolderName);
            paths.Add(Path.GetFullPath(Path.Combine(_gameManager.GamesFolder, game.FolderName)));
        }
        if (!string.IsNullOrWhiteSpace(game.InstallPath)) paths.Add(Path.GetFullPath(game.InstallPath));
        if (paths.Count == 0) throw new IOException("The game folder is not configured.");
        return paths.Select(Path.TrimEndingDirectorySeparator).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
    }
    // Consent is scoped to the previewed directory, not a later replacement at
    // that path. This conservative timestamp check complements disk authority;
    // cooperative locks are not immunity from hostile external timestamp/TOCTOU races.
    private static string RemovalPreview(string[] paths) => string.Join("|", paths.Select(path =>
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return $"{attributes}:{Directory.GetCreationTimeUtc(path).Ticks}:{Directory.GetLastWriteTimeUtc(path).Ticks}";
        }
        catch (FileNotFoundException) { return "missing"; }
        catch (DirectoryNotFoundException) { return "missing"; }
    }));
    private IDisposable AcquireRemovalLeases(GameInfo game, string[] expected)
    {
        if (!RemovalPaths(game).SequenceEqual(expected, StringComparer.Ordinal))
            throw new IOException("The installation location changed while confirming removal. Refresh and try again.");
        return PortableInstallationTransaction.AcquireUnmanagedRemovalLeases(expected);
    }

    public async Task UninstallAsync(GameInfo? game)
    {
        if (RefuseKiosk()) return;
        await _session.RunAsync(async () =>
        {
            if (game == null) return;
            bool? previousLoading = null;
            try
            {
                var paths = RemovalPaths(game);
                // Never retain exclusion while awaiting consent.
                string preview;
                using (AcquireRemovalLeases(game, paths)) { preview = RemovalPreview(paths); }
                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                if (WindowsInstallerService.HasReceipt(gamePath))
                {
                    using var lease = AcquireRemovalLeases(game, paths);
                    if (!OperatingSystem.IsWindows())
                        await ShowMessageBoxAsync("Uninstall this application using Installed apps on Windows.", "Windows Installation");
                    else OpenUrl(WindowsInstallerService.InstalledAppsUri);
                    return;
                }
                var flatpak = FlatpakService.HasReceipt(gamePath);
                if (game.Status == GameStatus.NotInstalled && !flatpak)
                {
                    await ShowMessageBoxAsync($"{game.Name} is not installed.", "Nothing to Delete");
                    return;
                }
                if (PlatformCapabilities.IsMobile)
                {
                    using var lease = AcquireRemovalLeases(game, paths);
                    await AndroidLibraryUninstall.RunAsync(game, gamePath, AppInstallLaunch.Current);
                    TrackUninstalled(game); Changed(); return;
                }
                var located = !string.IsNullOrWhiteSpace(game.InstallPath);
                var message = flatpak ? $"Uninstall {game.Name}?\n\nYour saves and application data will be preserved." :
                    located ? $"Delete {game.Name}?\n\nYou chose its folder, {gamePath}, so Relicade leaves the folder " +
                    "and your other files in it alone. Only the file Relicade downloaded there, if any, moves to the Recycle Bin / Trash, " +
                    "and Relicade stops using the folder." :
                    $"Are you sure you want to delete {game.Name}?\n\n" +
                    "Relicade will attempt to move game files to your system's Recycle Bin / Trash so you can restore them if needed. " +
                    "Portable installs may include save data in the same folder.";
                if (!await ShowMessageBoxAsync(message, flatpak ? "Uninstall Flatpak" : "Confirm Deletion", true, true) ||
                    _session.IsClosed || RefuseKiosk()) return;
                using (AcquireRemovalLeases(game, paths))
                {
                    _session.Token.ThrowIfCancellationRequested();
                    if (RemovalPreview(paths) != preview || game.GetInstallPath(_gameManager.GamesFolder) != gamePath ||
                        FlatpakService.HasReceipt(gamePath) != flatpak || WindowsInstallerService.HasReceipt(gamePath))
                        throw new IOException("The installation changed while confirming removal. Refresh and try again.");
                    previousLoading = game.IsLoading;
                    game.IsLoading = true;
                    if (flatpak) await FlatpakService.Current.UninstallAsync(gamePath);
                    else if (located)
                    {
                        var shared = LocatedInstallRemoval.FolderHasOtherApps(game, _gameManager.Games, _gameManager.GamesFolder);
                        // This scope already owns the leases: no nested acquisition.
                        await Task.Run(() => LocatedInstallRemoval.RemoveWhileLeased(gamePath, shared, _moveToTrash), _session.Token);
                        game.InstallPath = null;
                        await _libraryPersistence.SaveInstallLocationAsync(game);
                    }
                    else
                    {
                        if (!Directory.Exists(gamePath)) throw new DirectoryNotFoundException("The previewed game folder is missing; no files were removed.");
                        await Task.Run(() => _moveToTrash(gamePath), _session.Token);
                    }
                    TrackUninstalled(game);
                }
                if (!_session.IsClosed)
                {
                    await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder, checkRemoteVersion: false);
                    Changed();
                }
            }
            catch (OperationCanceledException) when (_session.IsClosed) { }
            catch (Exception ex)
            {
                // No status/recovery/network refresh on refusal or callback failure:
                // that can itself resolve retained journals or fabricate success.
                await ShowMessageBoxAsync($"Failed to delete {game.Name}: {ex.Message}", "Deletion Failed");
            }
            finally { if (previousLoading.HasValue) game.IsLoading = previousLoading.Value; }
        });
    }

    private static void TrackUninstalled(GameInfo game) => Telemetry.Current.Track("app_uninstalled", Telemetry.AppRef(game));

    private Task<List<GameInfo>> LoadGamesFromJsonAsync() => _gameManager.CatalogService.LoadLocalAppsAsync();
    private async Task SaveGamesToJsonAsync(List<GameInfo> appsToSave)
    {
        foreach (var app in appsToSave)
            app.GameManager ??= _gameManager;
        await _gameManager.CatalogService.SaveLocalAppsAsync(appsToSave);
    }

    private async Task SaveGamesToJsonAsync(Dictionary<string, JsonElement> gamesData)
    {
        var imported = _gameManager.CatalogService.ParseAppsFromDictionary(gamesData);
        foreach (var app in imported)
            app.GameManager = _gameManager;
        await _gameManager.CatalogService.SaveLocalAppsAsync(imported);
    }

    private static GameInfo? FindMatchingSavedApp(IEnumerable<GameInfo> apps, GameInfo game) => apps.FirstOrDefault(g => string.Equals(g.InstanceKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase));
    /// <summary>
    /// Removes the app from the library; its files stay. From the Library it asks first, so a card isn't removed by
    /// accident, but only for an installed app: one that isn't installed has nothing to keep and is one click to add back.
    /// From the App Catalog it doesn't ask (<paramref name="confirm"/> false): the page shows the change.
    /// </summary>
    public async Task RemoveEntryAsync(GameInfo? game, bool confirm = true)
    {
        if (RefuseKiosk()) return;
        await _session.RunAsync(async () =>
        {
            if (game == null || string.IsNullOrEmpty(game.Name))
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the selected app.", "Error"));
                return;
            }

            if (!game.IsInLocalAppsJson)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("This app is not in your local list.", "Error"));
                return;
            }

            try
            {
                var installed = game.IsInstalled || Directory.Exists(game.GetInstallPath(_gameManager.GamesFolder));
                if (confirm && installed && !await ShowMessageBoxAsync($"Remove '{game.Name}' from your Library?\n\nYour files will not be deleted.", "Remove from Library", true))
                    return;
                if (_session.IsClosed)
                    return;
                var games = await LoadGamesFromJsonAsync();
                var gameToRemove = FindMatchingSavedApp(games, game);
                if (gameToRemove == null)
                {
                    await ShowMessageBoxAsync($"Could not find '{game.Name}' in the saved apps list.", "Error");
                    return;
                }

                games.Remove(gameToRemove);
                await SaveGamesToJsonAsync(games);
                Telemetry.Current.Track("app_removed", Telemetry.AppRef(game));
                // Drop only Relicade's link; Windows continues to own the installed application.
                var metadataPath = game.GetInstallPath(_gameManager.GamesFolder);
                if (WindowsInstallerService.HasReceipt(metadataPath))
                {
                    try
                    {
                        using var lease = new PortableInstallationTransaction(metadataPath).AcquireUnmanagedRemovalLease();
                        File.Delete(Path.Combine(metadataPath, WindowsInstallerService.ReceiptFileName));
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        // Removing a library link keeps all managed/uncertain bytes,
                        // even a conflicting OS receipt. This is not an uninstall.
                    }
                }
                await _gameManager.LoadGamesAsync();
                if (_session.IsClosed)
                    return;
                Changed();
                _settingsModel.SaveCurrent();
                // No "removed" message: the card going away says it.
                await _libraryChanged();
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Error removing app: {ex.Message}", "Error"));
            }
        });
    }

    public async Task CreateShortcutAsync(GameInfo? game)
    {
        if (RefuseKiosk()) return;
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                var target = await PrepareShortcutAsync(game);
                if (target == null || _session.IsClosed) return;
                await ShortcutHelper.CreateGameShortcutAsync(game, target, _gameManager.CacheFolder);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to create shortcut: {ex.Message}", "Error");
            }
        });
    }

    public async Task AddToSteamAsync(GameInfo? game)
    {
        if (RefuseKiosk()) return;
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                var target = await PrepareShortcutAsync(game);
                if (target == null || _session.IsClosed) return;
                string resultMessage = ShortcutHelper.IsSteamRunning()
                    ? ShortcutHelper.QueueGameAddToSteam(game, ShortcutHelper.ResolveLauncherPath() ?? "")
                    : await Task.Run(() => ShortcutHelper.AddGameToSteam(game, target, _gameManager.CacheFolder), _session.Token);
                await ShowMessageBoxAsync(resultMessage, "Steam Shortcut");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to add the game to Steam: {ex.Message}", "Error");
            }
        });
    }

    private Task<GameShortcutTarget?> PrepareShortcutAsync(GameInfo game) =>
        GameShortcutLaunch.PrepareAsync(game, _gameManager.GamesFolder, _settings, async candidates =>
        {
            var dialog = new Relicade.Views.ShortcutExecutableDialog(game.Name ?? "this app", candidates);
            await GameDialogService.ShowWindowAsync(dialog, _session.Token);
            return dialog.SelectedExecutable;
        }, _session.Token);
}
