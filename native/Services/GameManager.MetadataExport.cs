using Avalonia.Threading;
using Relicade.Models;

namespace Relicade.Services;

internal sealed record LoadedMetadataSnapshot(long Generation, string DataRoot, string AppsRoot,
    LibraryMetadataDocument Document, string[] ProtectedRoots);

public partial class GameManager
{
    readonly object metadataGate = new();
    long metadataGeneration;
    int metadataLoads;
    bool metadataLoadSucceeded, metadataConcurrentLoads;
    internal long BeginMetadataLoad()
    {
        lock (metadataGate)
        {
            if (metadataLoads == 0) metadataConcurrentLoads = false; else metadataConcurrentLoads = true;
            metadataLoads++; metadataLoadSucceeded = false; return ++metadataGeneration;
        }
    }
    internal void CompleteMetadataLoad(long generation, bool succeeded)
    {
        lock (metadataGate)
        {
            metadataLoads--;
            if (generation == metadataGeneration) metadataLoadSucceeded = succeeded && !metadataConcurrentLoads;
            // Concurrent loads are ambiguous even if the newer one completed first.
            if (metadataLoads == 0 && generation != metadataGeneration) metadataLoadSucceeded = false;
        }
    }
    internal LoadedMetadataSnapshot CaptureLibraryMetadata()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (metadataGate)
        {
            RequireSettledMetadata();
            var data = Path.GetFullPath(RelicadePaths.UserDataRoot);
            var apps = Path.GetFullPath(_appsFolder);
            var entries = _catalogApps.Where(g => g.IsInLocalAppsJson).ToArray();
            if (entries.Length != _catalogApps.Count) throw new InvalidOperationException("The loaded saved-entry set is incomplete. Reload the library before exporting.");
            var document = LibraryMetadataDocument.Create(entries.Select(LibraryMetadataDocument.Capture), DateTimeOffset.UtcNow);
            var protectedRoots = new[] { data, apps, Path.GetFullPath(_cacheFolder), Path.GetFullPath(Path.GetDirectoryName(_catalogService.AppsConfigPath)!) }
                .Concat(entries.Select(g => Path.GetFullPath(g.GetInstallPath(_appsFolder)))).Distinct(StringComparer.Ordinal).ToArray();
            return new(metadataGeneration, data, apps, document, protectedRoots);
        }
    }
    internal bool IsCurrentMetadata(LoadedMetadataSnapshot snapshot)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (metadataGate)
        {
            try
            {
                var current = CaptureLibraryMetadata();
                return current.Generation == snapshot.Generation && current.DataRoot == snapshot.DataRoot && current.AppsRoot == snapshot.AppsRoot &&
                    current.Document.Entries.SequenceEqual(snapshot.Document.Entries) && current.ProtectedRoots.SequenceEqual(snapshot.ProtectedRoots);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or ArgumentException) { return false; }
        }
    }
    internal void CommitMetadataExport(LoadedMetadataSnapshot snapshot, Action commit)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (metadataGate)
        {
            if (!IsCurrentMetadata(snapshot)) throw new InvalidOperationException("The loaded library changed. Close and preview a fresh export.");
            commit(); // Only a bounded same-directory no-replace rename; no awaited work under this gate.
        }
    }
    void RequireSettledMetadata()
    {
        if (_disposed || metadataLoads != 0 || !metadataLoadSucceeded)
            throw new InvalidOperationException("Wait for a successful library load before exporting metadata. A failed or incomplete load is not an empty library.");
        if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true })
            throw new IOException("This library session is in settings recovery. Reopen a successfully loaded library before exporting metadata.");
    }
}
