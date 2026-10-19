using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>
/// Deletes an app whose folder the player located. Relicade didn't make that folder and it can hold the player's other
/// programs, Relicade's own AppImage among them, so it stays: only the single file Relicade last installed there goes to
/// the Trash, and Relicade's own notes about the install are removed.
/// </summary>
public static class LocatedInstallRemoval
{
    static readonly string[] MetadataFileNames =
    [
        "version.txt",
        InstalledBuild.FileNameOnDisk,
        "selected_executable.txt",
        "LastPlayed.txt",
        GameInstallationService.IncompleteInstallFileName,
    ];

    /// <summary>Whether another library app installs into <paramref name="game"/>'s folder too.</summary>
    public static bool FolderHasOtherApps(GameInfo game, IEnumerable<GameInfo>? library, string gamesFolder)
    {
        var folder = Path.GetFullPath(game.GetInstallPath(gamesFolder));
        return library?.Any(other => other != game && other.InstanceKey != game.InstanceKey &&
            !string.IsNullOrWhiteSpace(other.FolderName) &&
            string.Equals(Path.GetFullPath(other.GetInstallPath(gamesFolder)), folder, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <param name="folderHasOtherApps">
    /// Another library app uses the folder too. Its notes and installed file share the folder, so nothing is removed.
    /// </param>
    public static void Remove(string folder, bool folderHasOtherApps, Action<string> moveToTrash)
    {
        using var lease = new PortableInstallationTransaction(folder).AcquireUnmanagedRemovalLease();
        RemoveWhileLeased(folder, folderHasOtherApps, moveToTrash);
    }

    // Assembly-internal composition only. LibraryActions holds all target/default
    // leases through this helper AND subsequent location persistence. Public callers
    // must use Remove so they cannot bypass the boundary or double-acquire it.
    internal static void RemoveWhileLeased(string folder, bool folderHasOtherApps, Action<string> moveToTrash)
    {
        if (folderHasOtherApps || !Directory.Exists(folder))
            return;

        var installed = Path.GetFileName(InstalledBuild.Read(folder)?.FileName ?? string.Empty);
        if (installed.Length > 0 && File.Exists(Path.Combine(folder, installed)))
            moveToTrash(Path.Combine(folder, installed));

        foreach (var name in MetadataFileNames)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
