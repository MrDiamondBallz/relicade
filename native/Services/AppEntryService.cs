using Relicade.Models;
using Relicade.Core.Services;
using Relicade.Services.Mods;
using Relicade.ViewModels;

namespace Relicade.Services;

public interface IAppEntryService
{
    Task<bool> SaveAsync(AppEntryDraft draft, Action<EntryNotice> notice, Action<GameInfo> openFolder, CancellationToken token);
}

public sealed class AppEntryService(GameManager manager, SettingsViewModel settingsModel) : IAppEntryService
{
    private GameManager _gameManager => manager;
    private SettingsViewModel _settingsModel => settingsModel;
    private AppSettings _settings => settingsModel.Current;
    public async Task<bool> SaveAsync(AppEntryDraft draft, Action<EntryNotice> notice, Action<GameInfo> openFolder, CancellationToken token)
    {
        void Notify(string message, string title) => notice(new EntryNotice(message, title));
        token.ThrowIfCancellationRequested();
        var name = draft.Name;
        var manuallyManaged = draft.ManuallyManaged;
        var repository = manuallyManaged ? "" : draft.Repository;
        var normalizedSource = RepositorySourceHelper.Normalize(draft.RepositorySource, out var unsupportedSource);
        var repositorySource = manuallyManaged ? null : normalizedSource;
        if (!manuallyManaged && unsupportedSource)
            Notify("Unsupported repository source; defaulting to GitHub.", "Repository Source");
        var folderName = draft.FolderName;
        var iconUrl = draft.IconUrl;
        var project = string.IsNullOrWhiteSpace(draft.Project) ? null : draft.Project;
        var customDisplayName = string.IsNullOrWhiteSpace(draft.CustomDisplayName) ? null : draft.CustomDisplayName;
        var tags = TagHelper.ParseCommaSeparatedTags(draft.Tags);
        var filesToAdd = AppFilesToAddService.ParseCommaSeparated(draft.FilesToAdd);
        var releaseAssetFilter = manuallyManaged ? null : RepositorySourceHelper.NormalizeReleaseAssetFilter(draft.ReleaseAssetFilter);
        var modsPath = GameModsConfig.NormalizePath(draft.ModsPath);
        var modsSources = GameModsFormHelper.ParseSourcesFromEditor(draft.ModsSources);
        var modsLayout = draft.ModsFolderPerMod ? GameModsConfig.LayoutFolderPerMod : null;
        var identityKey = RepositorySourceHelper.GetInstanceKey(repositorySource, repository, folderName);
                var games = await _gameManager.CatalogService.LoadLocalAppsAsync();
                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(draft.EditingIdentityKey) || !string.IsNullOrEmpty(draft.EditingRepository))
                {
                    var appToUpdate = games.FirstOrDefault(g =>
                        (!string.IsNullOrEmpty(draft.EditingIdentityKey) &&
                         string.Equals(g.InstanceKey, draft.EditingIdentityKey, StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrEmpty(draft.EditingIdentityKey) &&
                         g.Repository == draft.EditingRepository));
                    if (appToUpdate == null)
                    {
                        Notify("Could not find the app to update.", "Error");
                        return false;
                    }

                    // Name may be shared across ports (distinguished by project / repository).
                    var oldRepository = appToUpdate.Repository;
                    var oldRepositorySource = appToUpdate.RepositorySource;
                    var oldIdentityKey = appToUpdate.InstanceKey;
                    var oldFolderName = appToUpdate.FolderName;
                    if (WindowsInstallerService.HasReceipt(appToUpdate.GetInstallPath(_settings.AppsPath)) &&
                        (appToUpdate.FolderName != folderName || manuallyManaged))
                    {
                        Notify("Remove this library entry before changing its installation folder or switching to manual management. The Windows-installed app will be preserved.", "Windows Installation");
                        return false;
                    }
                    if (!string.Equals(oldIdentityKey, identityKey, StringComparison.OrdinalIgnoreCase) &&
                        games.Any(g =>
                            !ReferenceEquals(g, appToUpdate) &&
                            string.Equals(g.InstanceKey, identityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "Another app already uses this folder name.",
                            "Duplicate Folder");
                        return false;
                    }

                    if (!string.Equals(appToUpdate.FolderName, folderName, StringComparison.OrdinalIgnoreCase) &&
                        games.Any(g =>
                            !ReferenceEquals(g, appToUpdate) &&
                            string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "Another app already uses this folder name.",
                            "Duplicate Folder");
                        return false;
                    }

                    // Coordinate with installers even before the first manifest exists.
                    // Managed roots are path-keyed: moving their folder is not a migration.
                    IDisposable? originalWriter = null;
                    IDisposable? destinationWriter = null;
                    bool managedEntry;
                    try
                    {
                        var originalPath = appToUpdate.GetInstallPath(_settings.AppsPath);
                        originalWriter = new PortableInstallationTransaction(originalPath).AcquireLease();
                        managedEntry = PortableInstallationTransaction.HasEvidence(originalPath);
                        if (managedEntry &&
                            (oldFolderName != folderName || appToUpdate.IsManuallyManaged != manuallyManaged ||
                             !string.Equals(oldRepository, repository, StringComparison.Ordinal) ||
                             !string.Equals(RepositorySourceHelper.Normalize(oldRepositorySource), RepositorySourceHelper.Normalize(repositorySource), StringComparison.OrdinalIgnoreCase) ||
                             !AppFilesToAddService.Normalize(appToUpdate.FilesToAdd).SequenceEqual(filesToAdd, StringComparer.Ordinal) ||
                             !string.Equals(appToUpdate.ReleaseAssetFilter ?? "", releaseAssetFilter ?? "", StringComparison.Ordinal) ||
                             !string.Equals(GameModsConfig.NormalizePath(appToUpdate.ModsPath), modsPath, StringComparison.Ordinal) ||
                             !string.Equals(GameModsConfig.NormalizeLayout(appToUpdate.ModsLayout), GameModsConfig.NormalizeLayout(modsLayout), StringComparison.Ordinal) ||
                             !System.Text.Json.JsonSerializer.Serialize(GameModsConfig.NormalizeSources(appToUpdate.ModsSources))
                                 .Equals(System.Text.Json.JsonSerializer.Serialize(modsSources), StringComparison.Ordinal)))
                            throw new IOException("Managed installation folder, source and setup changes are not supported by entry editing. Keep the existing installation and recovery history; presentation fields can still be edited.");
                        if (oldFolderName != folderName && !string.Equals(Path.GetFullPath(originalPath),
                                Path.GetFullPath(Path.Combine(_settings.AppsPath, oldFolderName)),
                                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                            throw new IOException("Linked installation folder changes require an explicit migration and are not supported by entry editing.");
                        if (oldFolderName != folderName && !string.Equals(originalPath, Path.Combine(_settings.AppsPath, folderName),
                                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        {
                            var destinationPath = Path.Combine(_settings.AppsPath, folderName);
                            destinationWriter = new PortableInstallationTransaction(destinationPath).AcquireLease();
                            if (PortableInstallationTransaction.HasEvidence(destinationPath))
                                throw new IOException("The destination has managed installation or recovery history and cannot be used for this entry move.");
                        }
                    }
                    catch (Exception error)
                    {
                        destinationWriter?.Dispose();
                        originalWriter?.Dispose();
                        Notify($"The entry was not changed. Its managed installation authority could not be secured: {error.Message}", "Entry Update Unavailable");
                        return false;
                    }
                    using var originalWriterScope = originalWriter;
                    using var destinationWriterScope = destinationWriter;

                    // Work only on staged metadata and a deep settings copy until both stores commit.
                    var originalSettings = _settings;
                    var stagedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                        System.Text.Json.JsonSerializer.Serialize(originalSettings))!;
                    stagedSettings.EnsureInitialized();
                    var index = games.IndexOf(appToUpdate);
                    appToUpdate = _gameManager.CatalogService.ParseAppsFromJson(
                        System.Text.Json.JsonSerializer.Serialize(new { apps = new[] { AppCatalogService.SerializeApp(appToUpdate) } })).Single();
                    games[index] = appToUpdate;
                    var oldPath = Path.Combine(originalSettings.AppsPath, oldFolderName);
                    var newPath = Path.Combine(originalSettings.AppsPath, folderName);
                    var renamed = false;

                    var wasManual = appToUpdate.IsManuallyManaged;
                    var previousFilesToAdd = AppFilesToAddService.Normalize(appToUpdate.FilesToAdd);
                    appToUpdate.Name = name;
                    appToUpdate.Project = project;
                    appToUpdate.CustomDisplayName = customDisplayName;
                    appToUpdate.Repository = repository ?? "";
                    appToUpdate.RepositorySource = manuallyManaged || RepositorySourceHelper.IsGitHub(repositorySource)
                        ? null
                        : repositorySource;
                    appToUpdate.FolderName = folderName;
                    appToUpdate.GameIconUrl = iconUrl;
                    appToUpdate.Tags = tags;
                    appToUpdate.FilesToAdd = filesToAdd;
                    appToUpdate.ReleaseAssetFilter = releaseAssetFilter;
                    appToUpdate.ModsPath = modsPath.Length > 0 ? modsPath : null;
                    appToUpdate.ModsSources = modsSources;
                    appToUpdate.ModsLayout = modsLayout;

                    if (manuallyManaged)
                    {
                        appToUpdate.AutoUpdate = false;
                        appToUpdate.PreferredVersion = null;
                        appToUpdate.SkippedUpdateVersion = null;
                        appToUpdate.DeferUpdateTracking = false;
                        appToUpdate.LatestVersion = null;
                    }
                    else if (wasManual)
                    {
                        appToUpdate.AutoUpdate = false;
                        appToUpdate.PreferredVersion = null;
                        appToUpdate.SkippedUpdateVersion = null;
                        appToUpdate.DeferUpdateTracking = true;
                    }

                    if (!string.IsNullOrWhiteSpace(appToUpdate.Repository))
                        stagedSettings.UserAppDisplayNames.Remove(appToUpdate.Repository);
                    AppIdentityMigration.MigrateIdentity(stagedSettings, oldRepositorySource, oldRepository,
                        appToUpdate.RepositorySource, appToUpdate.Repository, oldFolderName, folderName, clearVersionCache: false);
                    token.ThrowIfCancellationRequested();
                    var settingsCommitted = false;
                    try
                    {
                        if (oldFolderName != folderName && !string.IsNullOrEmpty(oldFolderName))
                        {
                            // Refuse an occupied target even when the source directory is missing.
                            if (Directory.Exists(newPath) || File.Exists(newPath))
                                throw new IOException("The destination folder already exists and has not been changed.");
                            if (Directory.Exists(oldPath))
                            {
                                Directory.Move(oldPath, newPath);
                                renamed = true;
                            }
                        }
                        // Commit/compensation is deliberately noninterruptible; never claim cancellation undid it.
                        _settingsModel.PersistEntrySettings(stagedSettings);
                        settingsCommitted = true;
                        await SaveGamesToJsonAsync(games);
                    }
                    catch (Exception error)
                    {
                        var recoveryErrors = new List<Exception>();
                        if (settingsCommitted)
                        {
                            try { _settingsModel.PersistEntrySettings(originalSettings); }
                            catch (Exception restoreError) { recoveryErrors.Add(restoreError); }
                        }
                        if (renamed)
                        {
                            try { Directory.Move(newPath, oldPath); }
                            catch (Exception restoreError) { recoveryErrors.Add(restoreError); }
                        }
                        _settingsModel.ReplaceCurrent(originalSettings);
                        if (recoveryErrors.Count > 0)
                        {
                            // Preserve affected identity maps locally, never credentials or the full settings document.
                            var evidence = Path.Combine(Path.GetDirectoryName(_gameManager.CatalogService.AppsConfigPath)!,
                                "entry-recovery-" + Guid.NewGuid().ToString("N") + ".json");
                            var recoveryRecord = $"Recovery record: {evidence}.";
                            try
                            {
                                await File.WriteAllTextAsync(evidence, System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    oldFolderName, folderName, oldIdentityKey, identityKey,
                                    originalSettings.UserAppTags, originalSettings.UserAppDisplayNames,
                                    operationError = error.GetType().Name,
                                    recoveryErrors = recoveryErrors.Select(e => e.GetType().Name).ToArray()
                                }));
                            }
                            catch (Exception)
                            {
                                recoveryRecord = "A recovery record could not be written. Keep both folders and repair the library and settings before retrying.";
                            }
                            Notify($"The entry update failed and recovery is incomplete. Do not move or delete either folder. {recoveryRecord} {error.Message}", "Entry Recovery Required");
                        }
                        else Notify($"The entry update failed. The original entry and folder were preserved. {error.Message}", "Entry Update Failed");
                        return false;
                    }
                    _settingsModel.PublishEntrySettings();
                    if (AppIdentityMigration.HasIdentityChanged(oldRepositorySource, oldRepository,
                            appToUpdate.RepositorySource, appToUpdate.Repository, oldFolderName, folderName) &&
                        !string.IsNullOrWhiteSpace(oldRepository))
                        GitHubApiCache.RemoveCache(oldRepositorySource, oldRepository);
                    try
                    {
                        if (managedEntry)
                        {
                            // Presentation-only edits must not mutate the committed payload.
                        }
                        else if (appToUpdate.IsManuallyManaged)
                            ManualAppFolderService.EnsurePrepared(appToUpdate, _gameManager.GamesFolder, throwOnFailure: true, previousFilesToAdd: previousFilesToAdd);
                        else
                            AppFilesToAddService.SyncForGame(appToUpdate, _gameManager.GamesFolder, previousFilesToAdd, throwOnFailure: true);
                    }
                    catch (Exception error)
                    {
                        Notify($"The entry was saved, but follow-up file preparation failed. Retry preparation; the saved entry uses the new folder. {error.Message}", "Entry Saved · Preparation Failed");
                        return false;
                    }
                    Notify("App entry updated successfully", "App Updated");
                }
                else
                {
                    // Name may be shared across ports; uniqueness is install folder
                    // (hosted apps may share a repository when folders differ).
                    if (games.Any(g =>
                            string.Equals(g.InstanceKey, identityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "A library entry already uses this folder name, even if its app has not been installed yet.",
                            "Duplicate Folder");
                        return false;
                    }

                    if (games.Any(g =>
                            string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "A library entry already uses this folder name, even if its app has not been installed yet.",
                            "Duplicate Folder");
                        return false;
                    }

                    var newApp = new GameInfo
                    {
                        Name = name,
                        Project = project,
                        CustomDisplayName = customDisplayName,
                        Repository = repository ?? "",
                        RepositorySource = manuallyManaged || RepositorySourceHelper.IsGitHub(repositorySource)
                            ? null
                            : repositorySource,
                        FolderName = folderName,
                        GameIconUrl = iconUrl,
                        Tags = tags,
                        FilesToAdd = filesToAdd,
                        ReleaseAssetFilter = releaseAssetFilter,
                        ModsPath = modsPath.Length > 0 ? modsPath : null,
                        ModsSources = modsSources,
                        ModsLayout = modsLayout,
                        AutoUpdate = !manuallyManaged && _settings.AutoUpdateNewlyAddedApps,
                        IsCustom = true,
                        IsExperimental = false
                    };
                    games.Add(newApp);

                    await SaveGamesToJsonAsync(games);
                    if (newApp.IsManuallyManaged)
                    {
                        ManualAppFolderService.EnsurePrepared(newApp, _gameManager.GamesFolder);
                        openFolder(newApp);
                        Notify(
                            "Manually managed app added. Place the app files in the opened folder.",
                            "App Added");
                    }
                    else
                    {
                        AppFilesToAddService.SyncForGame(newApp, _gameManager.GamesFolder);
                    }
                }


        return true;
    }
    private async Task SaveGamesToJsonAsync(List<GameInfo> games)
    {
        foreach(var game in games) game.GameManager ??= _gameManager;
        await _gameManager.CatalogService.SaveLocalAppsAsync(games);
    }
}
