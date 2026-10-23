using Relicade.Core.Services;

namespace Relicade.Services;

internal sealed class MetadataExportCommitException(Exception inner) : IOException("The no-replace commit outcome is uncertain. Inspect the chosen destination before trying again; no rollback or replacement was attempted.", inner);

internal static class LibraryMetadataExportWriter
{
    internal static void ValidateDestination(string destination, IReadOnlyList<string> protectedRoots)
    {
        // Refuse network/device spellings before any filesystem observation.
        if (destination.StartsWith("//", StringComparison.Ordinal) || destination.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Choose a local destination, not a network or device path.");
        if (!Path.IsPathFullyQualified(destination)) throw new IOException("Choose a fully qualified local destination.");
        var full = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(full) ?? throw new IOException("Choose a local file, not a root.");
        PortableInstallationTransaction.CheckLinks(full);
        if (!Directory.Exists(parent) || File.Exists(full) || Directory.Exists(full))
            throw new IOException("Choose a new file in an existing local folder. Existing files are never replaced.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var observedAncestors = new HashSet<string>(comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in protectedRoots)
        {
            if (root.StartsWith("//", StringComparison.Ordinal) || root.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(root))
                throw new InvalidDataException("A protected library path cannot be safely inspected. Reopen a local library before exporting.");
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            CheckProtectedAncestors(normalized, observedAncestors);
            if (IsWithinProtectedRoot(full, normalized, Path.DirectorySeparatorChar, comparison))
                throw new IOException("Export outside Relicade state, recovery and current game folders.");
        }
    }
    static void CheckProtectedAncestors(string path, HashSet<string> observed)
    {
        // Inspect from the filesystem root down: never resolve a linked protected
        // alias or inspect its descendants before refusing it. No directory crawl.
        var ancestors = new Stack<string>();
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            if (ancestors.Count == 256) throw new InvalidDataException("A protected library path exceeds the inspection depth limit.");
            ancestors.Push(current);
        }
        foreach (var ancestor in ancestors)
        {
            if (!observed.Add(ancestor)) continue;
            try
            {
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked library or game folders require inspection before metadata export.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        // This observation is repeated at commit, not a lock against external edits.
    }
    // Inputs are canonical paths from ValidateDestination; this is containment,
    // not a second path parser. Separator is explicit for cross-platform lexical tests.
    internal static bool IsWithinProtectedRoot(string full, string normalizedRoot, char separator, StringComparison comparison)
    {
        var prefix = normalizedRoot.EndsWith(separator) ? normalizedRoot : normalizedRoot + separator;
        return string.Equals(full, normalizedRoot, comparison) || full.StartsWith(prefix, comparison);
    }
    internal static async Task WriteNewAsync(byte[] bytes, string destination, IReadOnlyList<string> protectedRoots,
        CancellationToken token, Action<Action> commit, Func<Task>? beforeCommit = null)
    {
        if (bytes.Length is 0 or > LibraryMetadataDocument.MaxBytes) throw new InvalidDataException("Invalid metadata export size.");
        ValidateDestination(destination, protectedRoots); token.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, ".relicade-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                created = true;
                await stream.WriteAsync(bytes, token); await stream.FlushAsync(token); stream.Flush(true);
            }
            if (beforeCommit != null) await beforeCommit();
            token.ThrowIfCancellationRequested();
            commit(() =>
            {
                token.ThrowIfCancellationRequested(); ValidateDestination(full, protectedRoots);
                PortableInstallationTransaction.CheckLinks(temporary);
                try { File.Move(temporary, full, overwrite: false); created = false; }
                catch (IOException error) { created = false; throw new MetadataExportCommitException(error); }
            });
        }
        finally
        {
            if (created)
            {
                // Never follow a replaced ancestor to clean an uncertain path.
                PortableInstallationTransaction.CheckLinks(temporary);
                File.Delete(temporary);
            }
        }
    }
}
