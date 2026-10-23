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
    sealed record MetadataLoadContext(string DataRoot, string AppsRoot, string SettingsAppsRoot, string CatalogPath);
    MetadataLoadContext? metadataLoadContext;
    MetadataLoadContext? ObserveMetadataLoadContext()
    {
        // Pure path/context observation only: never load settings or touch the filesystem.
        // A malformed context disables this export, not the underlying legacy loader.
        try
        {
            static string Normalize(string value) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            var data = Normalize(RelicadePaths.UserDataRoot);
            var apps = Normalize(_appsFolder);
            var configured = _settingsStore.Current.AppsPath;
            var settingsApps = Normalize(string.IsNullOrEmpty(configured) ? RelicadePaths.DefaultAppsDirectory : configured);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(apps, settingsApps, comparison)) return null;
            return new(data, apps, settingsApps, Path.GetFullPath(_catalogService.AppsConfigPath));
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return null; }
    }
    static bool SameMetadataContext(MetadataLoadContext? first, MetadataLoadContext? second)
    {
        if (first == null || second == null) return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(first.DataRoot, second.DataRoot, comparison) && string.Equals(first.AppsRoot, second.AppsRoot, comparison) &&
            string.Equals(first.SettingsAppsRoot, second.SettingsAppsRoot, comparison) && string.Equals(first.CatalogPath, second.CatalogPath, comparison);
    }
    internal long BeginMetadataLoad()
    {
        lock (metadataGate)
        {
            if (metadataLoads == 0) metadataConcurrentLoads = false; else metadataConcurrentLoads = true;
            metadataLoads++; metadataLoadSucceeded = false; metadataLoadContext = ObserveMetadataLoadContext(); return ++metadataGeneration;
        }
    }
    internal void CompleteMetadataLoad(long generation, bool succeeded)
    {
        lock (metadataGate)
        {
            metadataLoads--;
            if (generation == metadataGeneration) metadataLoadSucceeded = succeeded && !metadataConcurrentLoads && SameMetadataContext(metadataLoadContext, ObserveMetadataLoadContext());
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
            var context = metadataLoadContext!;
            var data = context.DataRoot;
            var apps = context.AppsRoot;
            var entries = _catalogApps.Where(g => g.IsInLocalAppsJson).ToArray();
            if (entries.Length != _catalogApps.Count) throw new InvalidOperationException("The loaded saved-entry set is incomplete. Reload the library before exporting.");
            var document = LibraryMetadataDocument.Create(entries.Select(LibraryMetadataDocument.Capture), DateTimeOffset.UtcNow);
            var protectedRoots = new[] { data, apps, Path.GetFullPath(_cacheFolder), Path.GetFullPath(Path.GetDirectoryName(context.CatalogPath)!) }
                .Concat(entries.Select(g => Path.GetFullPath(g.GetInstallPath(apps)))).Distinct(StringComparer.Ordinal).ToArray();
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
            commit(); // Revalidates bounded destination/protected-path attributes, then performs
            // the same-directory no-replace rename. No awaited work under this gate.
        }
    }
    void RequireSettledMetadata()
    {
        if (_disposed || metadataLoads != 0 || !metadataLoadSucceeded || !SameMetadataContext(metadataLoadContext, ObserveMetadataLoadContext()))
            throw new InvalidOperationException("Wait for a successful library load before exporting metadata. A failed or incomplete load is not an empty library.");
        if (_settingsStore is ISettingsRecoveryState { IsReadOnly: true })
            throw new IOException("This library session is in settings recovery. Reopen a successfully loaded library before exporting metadata.");
    }
}
