using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>
/// Creates and inspects install folders for apps that are not downloaded from GitHub/GitLab.
/// </summary>
public static class ManualAppFolderService
{
    public const string InstructionFileName = "Place app files here.txt";

    public static readonly string InstructionContents =
        "Most apps in Relicade come from GitHub or GitLab Releases, so Relicade can download them and keep them up to date. This app is not on GitHub or GitLab Releases, so Relicade cannot download or update it." +
        Environment.NewLine +
        Environment.NewLine +
        "Download the app yourself and put its files in this folder. Relicade will detect an executable and let you launch it. You will need to keep it up to date yourself." +
        Environment.NewLine +
        Environment.NewLine +
        "If a GitHub or GitLab release becomes available and this app has a repository set, this entry will gain version checks and updates.";

    public static string? GetInstallPath(GameInfo game, string appsFolder)
    {
        var path = game.GetInstallPath(appsFolder);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static bool EnsurePrepared(GameInfo game, string appsFolder, bool throwOnFailure = false, IEnumerable<string>? previousFilesToAdd = null)
    {
        var installPath = GetInstallPath(game, appsFolder);
        if (installPath == null)
            return false;

        // Validate before creating a missing directory beneath an existing parent.
        PortableInstallationTransaction.CheckLinks(installPath);
        Directory.CreateDirectory(installPath);
        if (throwOnFailure) _ = Directory.EnumerateFileSystemEntries(installPath).ToArray();
        // Keep the existing committed-install validation ahead of all preparation writes.
        var hasLaunchableFiles = HasLaunchableFiles(game, appsFolder);
        AppFilesToAddService.SyncForGame(game, appsFolder, previousFilesToAdd, throwOnFailure);

        if (!hasLaunchableFiles)
        {
            try
            {
                var instructionPath = PortableInstallationTransaction.SafePath(installPath, InstructionFileName);
                FileStream created;
                try { created = new FileStream(instructionPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) when (File.Exists(instructionPath))
                {
                    PortableInstallationTransaction.CheckLinks(instructionPath);
                    return true; // Existing instructions or user content are never truncated.
                }
                using (created)
                {
                    created.Write(System.Text.Encoding.UTF8.GetBytes(InstructionContents));
                    created.Flush(true);
                }
            }
            catch when (!throwOnFailure)
            {
                // Best-effort instructions; a failed write may leave a partial new file.
            }
        }

        return true;
    }

    public static bool HasLaunchableFiles(GameInfo game, string appsFolder)
    {
        var installPath = GetInstallPath(game, appsFolder);
        if (installPath == null || !Directory.Exists(installPath))
            return false;

        var executables = GameInstallationService.FindExecutableCandidates(
            installPath,
            SearchOption.AllDirectories,
            game.GetInstallationOptions(),
            out _);
        return executables.Count > 0;
    }
}
