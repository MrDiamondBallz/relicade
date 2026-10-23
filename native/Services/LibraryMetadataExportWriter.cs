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
        foreach (var root in protectedRoots)
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (string.Equals(full, normalized, comparison) || full.StartsWith(normalized + Path.DirectorySeparatorChar, comparison))
                throw new IOException("Export outside Relicade state, recovery and current game folders.");
        }
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
