using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Relicade.Models;
using Relicade.Core.Services;

namespace Relicade.Services;

public static class AppFilesToAddService
{
    public static List<string> Normalize(IEnumerable<string>? fileNames)
    {
        if (fileNames == null)
            return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in fileNames)
        {
            if (!TryNormalizeFileName(raw, out var fileName))
                continue;

            if (seen.Add(fileName))
                result.Add(fileName);
        }

        return result;
    }

    public static List<string> ParseCommaSeparated(string? input) =>
        Normalize(input?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string FormatForDisplay(IEnumerable<string>? fileNames)
    {
        var normalized = Normalize(fileNames);
        return normalized.Count == 0 ? string.Empty : string.Join(", ", normalized);
    }

    public static bool AreEquivalent(IEnumerable<string>? a, IEnumerable<string>? b) =>
        string.Equals(FormatForDisplay(a), FormatForDisplay(b), StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeFileName(string? raw, out string fileName)
    {
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed is "." or ".." ||
            trimmed.Contains('/') ||
            trimmed.Contains('\\') ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        try { PortableInstallationTransaction.ValidateRelative(trimmed); }
        catch (InvalidDataException) { return false; }
        if (trimmed.StartsWith(".relicade-", StringComparison.OrdinalIgnoreCase) ||
            GameInstallationService.IsLauncherMetadataFile(trimmed) ||
            trimmed.Equals(WindowsInstallerService.ReceiptFileName, StringComparison.OrdinalIgnoreCase))
            return false;

        fileName = trimmed;
        return true;
    }

    /// <summary>
    /// Creates missing empty markers without overwriting existing paths. Removed
    /// metadata is not ownership evidence: existing files are retained and returned.
    /// Strict callers receive an explicit preservation error before any creation.
    /// </summary>
    public static IReadOnlyList<string> Sync(string? installPath, IEnumerable<string>? previous, IEnumerable<string>? next, bool throwOnFailure = false)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return [];
        try { PortableInstallationTransaction.CheckLinks(installPath); }
        catch when (!throwOnFailure) { return []; }
        if (WindowsInstallerService.HasReceipt(installPath)) return [];

        var previousNames = Normalize(previous);
        var nextNames = Normalize(next);
        var nextSet = new HashSet<string>(nextNames, StringComparer.OrdinalIgnoreCase);
        var preserved = new List<string>();
        foreach (var removed in previousNames.Where(name => !nextSet.Contains(name)))
        {
            var path = Path.Combine(installPath, removed);
            // Do not follow, truncate or delete removed entries, even empty ones.
            // Legacy lists and files from packages cannot establish our ownership.
            if (Path.Exists(path)) preserved.Add(removed);
        }
        if (throwOnFailure && preserved.Count > 0)
            throw new IOException("Existing marker files were preserved because their ownership is unknown: " +
                string.Join(", ", preserved) + ". No marker files were created or removed. Review these files before removing them yourself.");

        foreach (var fileName in nextNames)
        {
            try
            {
                var path = PortableInstallationTransaction.SafePath(installPath, fileName);
                FileStream created;
                try
                {
                    // CreateNew is atomic with respect to another creator. Never use
                    // an existence check followed by a truncating write here.
                    created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // An existing or concurrently created file belongs to its owner.
                    // Recheck links so a newly introduced link is not treated as success.
                    PortableInstallationTransaction.CheckLinks(path);
                    continue;
                }
                using (created) created.Flush(true);
            }
            catch when (!throwOnFailure)
            {
                // Best-effort creation does not establish a complete setup result.
            }
        }
        return preserved.AsReadOnly();
    }

    public static void SyncForGame(GameInfo game, string appsFolder, IEnumerable<string>? previous = null, bool throwOnFailure = false)
    {
        if (string.IsNullOrWhiteSpace(appsFolder) && string.IsNullOrWhiteSpace(game.InstallPath))
            return;

        var installPath = game.GetInstallPath(appsFolder);
        if (WindowsInstallerService.HasReceipt(installPath)) return;
        Sync(installPath, previous, game.FilesToAdd, throwOnFailure);
    }
}
