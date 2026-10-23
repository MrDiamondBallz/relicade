using Avalonia.Threading;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

internal static class PortablePreservationService
{
    // Capture loaded UI identity, not a persisted deletion authorization. No general
    // loader, status check, provider, settings save or repair is used by this service.
    internal static Func<Func<PortablePreservationAssessment, Task>, CancellationToken, Task> Capture(
        GameManager manager, string key, Func<bool> closed)
    {
        Dispatcher.UIThread.VerifyAccess();
        var games = manager.Games; var matches = games.Where(g => g.InstanceKey == key).ToArray();
        if (matches.Length != 1) throw new IOException("Select one exact currently loaded portable entry.");
        var game = matches[0]; var settings = manager.CurrentSettings; var store = SettingsStoreProvider.RollbackCurrent;
        var folder = manager.GamesFolder; var appsPath = settings.AppsPath; var root = Path.GetFullPath(game.GetInstallPath(folder));
        var source = game.RepositorySource; var repository = game.Repository; var version = game.InstalledVersion;
        var status = game.Status; var dataRoot = RelicadePaths.UserDataRoot;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool Current()
        {
            if (closed() || !ReferenceEquals(manager.Games, games) || !games.Any(g => ReferenceEquals(g, game)) ||
                games.Count(g => g.InstanceKey == key) != 1 || game.InstanceKey != key ||
                !ReferenceEquals(settings, manager.CurrentSettings) || !ReferenceEquals(store, SettingsStoreProvider.RollbackCurrent) ||
                settings.AppsPath != appsPath || manager.GamesFolder != folder || RelicadePaths.UserDataRoot != dataRoot ||
                Path.GetFullPath(game.GetInstallPath(folder)) != root || game.RepositorySource != source || game.Repository != repository ||
                game.InstalledVersion != version || game.Status != status || settings.KioskMode ||
                store is ISettingsRecoveryState { IsReadOnly: true } || game.IsLoading || game.IsPortableCommitInProgress ||
                game.IsInstallIndeterminate || game.IsManuallyManaged || game.IsWindowsInstaller || game.IsFlatpak ||
                status is not (GameStatus.Installed or GameStatus.UpdateAvailable)) return false;
            manager.VerifyRollbackSettingsRecovery();
            foreach (var other in games.Where(g => !ReferenceEquals(g, game)))
            {
                var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other.GetInstallPath(folder)));
                if (string.Equals(root, path, comparison) || root.StartsWith(path + Path.DirectorySeparatorChar, comparison) ||
                    path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return false;
            }
            return true;
        }
        if (!Current()) throw new IOException("This entry is busy, changed, shared, locked or not a supported installed portable entry. Nothing was changed.");
        return async (publish, token) =>
        {
            // Every IO/authority operation runs off the dispatcher. Existing authority
            // remains held until the queued callback actually drains, even on close.
            await Task.Run(async () =>
            {
                var tx = new PortableInstallationTransaction(root);
                await tx.ObservePreservationAsync(async result =>
                {
                    // The Func<Task> overload completes only after the callback task drains.
                    await Dispatcher.UIThread.InvokeAsync((Func<Task>)(() =>
                    {
                        if (!token.IsCancellationRequested && Current()) return publish(result);
                        return Task.CompletedTask;
                    }));
                }, token).ConfigureAwait(false);
            }, token);
        };
    }
}
