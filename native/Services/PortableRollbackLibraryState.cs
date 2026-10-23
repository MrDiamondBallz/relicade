using Avalonia.Threading;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>Publish observed rollback metadata to the same unchanged live entry only.</summary>
internal static class PortableRollbackLibraryState
{
    internal static Func<PortableRollbackResult, Task<bool>> Capture(GameManager manager, string instanceKey, Func<bool> isClosed, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        var collection = manager.Games;
        var matches = collection.Where(g => g.InstanceKey == instanceKey).ToArray();
        if (matches.Length != 1) return _ => Task.FromResult(false);
        var game = matches[0];
        var folder = manager.GamesFolder;
        var root = game.GetInstallPath(folder);
        var settings = manager.CurrentSettings;
        var appsPath = settings.AppsPath;
        var version = game.InstalledVersion;
        var build = game.InstalledBuild;
        var executable = game.SelectedExecutable;
        var status = game.Status;
        var ready = game.OwnedDataReady;
        var recovery = game.PortableRecoveryError;
        var source = game.RepositorySource;
        bool Publish(PortableRollbackResult result)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (isClosed() || !result.Completed || result.ActualExecutable == null ||
                !ReferenceEquals(collection, manager.Games) ||
                manager.Games.Count(g => g.InstanceKey == instanceKey) != 1 ||
                !manager.Games.Any(g => ReferenceEquals(g, game) && g.InstanceKey == instanceKey) ||
                manager.GamesFolder != folder || game.GetInstallPath(folder) != root || game.RepositorySource != source ||
                !ReferenceEquals(settings, manager.CurrentSettings) || settings.AppsPath != appsPath || settings.KioskMode ||
                game.InstalledVersion != version || game.InstalledBuild != build || game.SelectedExecutable != executable ||
                game.Status != status || game.OwnedDataReady != ready || game.PortableRecoveryError != recovery ||
                game.IsLoading || game.IsPortableCommitInProgress || game.IsInstallIndeterminate ||
                status is not (GameStatus.Installed or GameStatus.UpdateAvailable)) return false;
            manager.VerifyRollbackSettingsRecovery();
            // The guarded callback performs no IO, status load, recovery, save or provider call.
            // These fields came from the validated manifest under rollback authority.
            var selected = Path.Combine(root, result.ActualExecutable.Replace('/', Path.DirectorySeparatorChar));
            InstalledBuild? observedBuild = null;
            var asset = result.ActualAssetIdentity;
            if (asset != null && asset.LastIndexOf("|sha256:", StringComparison.Ordinal) is var delimiter && delimiter > 0 &&
                PublishedAssetDigest.Normalize(asset[(delimiter + 8)..]) is { } digest)
                observedBuild = new InstalledBuild(asset[..delimiter], digest);
            if (game.HasOwnedDataAdapter)
            {
                game.OwnedDataReady = false;
                game.OwnedDataSetupMessage = "The portable version changed. Finish game setup to revalidate your retained game files before Play.";
            }
            game.InstalledBuild = observedBuild;
            game.SelectedExecutable = selected;
            game.InstalledVersion = result.ActualVersion;
            game.IsManagedPortable = true;
            game.Status = GameStatus.Installed;
            game.RefreshInstalledStatus();
            return true;
        }
        return result => Task.Run(() => new PortableInstallationTransaction(root).PublishObservedRollbackAsync(result,
            () => Dispatcher.UIThread.InvokeAsync(() => Publish(result)).GetTask(), cancellationToken));
    }
}
