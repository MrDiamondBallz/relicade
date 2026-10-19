using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relicade.Services.Mods;

/// <summary>
/// One selected-file payload and sidecar transaction. All readers and writers take the
/// same cross-process lock. A retained journal is recovery evidence, never disposable cache.
/// </summary>
internal static class ModInstallTransaction
{
    internal const string DirectoryName = ".relicade-mod-transactions";
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    internal sealed class Journal
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public string OperationId { get; set; } = "";
        [JsonRequired] public string Phase { get; set; } = "Prepared";
        [JsonRequired] public string Provider { get; set; } = "";
        [JsonRequired] public string SourceKey { get; set; } = "";
        [JsonRequired] public string PackageId { get; set; } = "";
        [JsonRequired] public string? FileId { get; set; }
        [JsonRequired] public string ModsPath { get; set; } = "";
        [JsonRequired] public string? OldManifestHash { get; set; }
        [JsonRequired] public string NewManifestHash { get; set; } = "";
        [JsonRequired] public int[] OldRecordIndexes { get; set; } = [];
        [JsonRequired] public int NewRecordIndex { get; set; } = -1;
        [JsonRequired] public List<Payload> Payloads { get; set; } = [];
    }
    internal sealed class Payload
    {
        [JsonRequired] public string Path { get; set; } = "";
        [JsonRequired] public string? OldSha256 { get; set; }
        [JsonRequired] public string? NewSha256 { get; set; }
        [JsonRequired] public string BackupPath { get; set; } = "";
        [JsonRequired] public string StagedPath { get; set; } = "";
    }
    sealed class Lease(SemaphoreSlim gate, FileStream stream) : IDisposable
    {
        public void Dispose() { stream.Dispose(); gate.Release(); }
    }
    internal static IDisposable Acquire(string root)
    {
        root = Path.GetFullPath(root);
        CheckLinks(root);
        Directory.CreateDirectory(root);
        var gate = Gates.GetOrAdd(root, _ => new(1));
        gate.Wait();
        try
        {
            var path = SafePath(root, ".relicade-mod-writer.lock");
            // FileShare.None uses an OS lock, including independent launcher processes.
            return new Lease(gate, new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch { gate.Release(); throw; }
    }

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.StartsWith('/') || relative.Split('/').Any(InvalidComponent))
            throw new InvalidDataException("Invalid relative mod payload or recovery path.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Mod path escapes its root.");
        CheckLinks(full);
        return full;
    }
    static bool InvalidComponent(string p)
    {
        if (p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.Any(c => c < 32 || "<>\"|?*".Contains(c))) return true;
        var stem = p.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
    internal static void CheckLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
        {
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked mod payload or recovery paths are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    internal static byte[]? ReadOptional(string path)
    {
        CheckLinks(path);
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    internal static string? Hash(byte[]? bytes) => bytes == null ? null : Convert.ToHexString(SHA256.HashData(bytes));
    static string? FileHash(string path) => Hash(ReadOptional(path));

    internal static void AtomicWrite(string path, byte[] bytes)
    {
        CheckLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { f.Write(bytes); f.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: true);
            SyncDirectory(Path.GetDirectoryName(path)!);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    // fsync directory entries on Linux. Other hosts retain process-interruption recovery,
    // not a claim of portable power-loss atomicity or storage hardware guarantees.
    [DllImport("libc", SetLastError = true)] static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] static extern int close(int fd);
    static void SyncDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        int fd = open(path, 0x10000);
        if (fd < 0) throw new IOException("Cannot open mod transaction directory for synchronization.");
        try { if (fsync(fd) != 0) throw new IOException("Cannot synchronize mod transaction directory."); }
        finally { close(fd); }
    }
    static void WriteJournal(string dir, Journal j) => AtomicWrite(SafePath(dir, "journal.json"), JsonSerializer.SerializeToUtf8Bytes(j, Json));
    internal static string CreateOperation(string root)
    {
        // Staging is not an active journal until the complete Prepared record is published.
        // A crash during download/extraction therefore cannot block reads of the old generation.
        var parent = SafePath(root, ".relicade-mod-staging"); Directory.CreateDirectory(parent);
        var dir = SafePath(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        SyncDirectory(parent);
        SyncDirectory(root);
        return dir;
    }

    internal static void EnsureSameVolume(string staging, string destination)
    {
        CheckLinks(staging); CheckLinks(destination);
        Directory.CreateDirectory(destination);
        var name = ".relicade-volume-check-" + Guid.NewGuid().ToString("N");
        var source = SafePath(staging, name); var target = SafePath(destination, name);
        Directory.CreateDirectory(source);
        try
        {
            // Unlike file moves (which may copy across devices), a directory rename
            // must remain on one volume. This empty owned probe touches no old payload.
            Directory.Move(source, target);
            Directory.Move(target, source);
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source);
            if (Directory.Exists(target)) Directory.Delete(target);
        }
    }

    internal static void Commit(string root, string modsRelative, string dir, InstalledModsDocument document,
        IReadOnlyList<InstalledModRecord> replaced, InstalledModRecord? record, Action<string>? boundary = null)
    {
        var mods = SafePath(root, modsRelative);
        var oldBytes = ReadOptional(SafePath(root, InstalledModsStore.SidecarFileName));
        if (oldBytes != null) InstalledModsStore.Parse(oldBytes);
        var oldFiles = replaced.SelectMany(r => r.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newFiles = record?.Files.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var siblings = document.Mods.Except(replaced).SelectMany(r => r.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Reject shared ownership even for removals: never delete a sibling's payload.
        if (oldFiles.Concat(newFiles).Any(siblings.Contains)) throw new IOException("Mod destination is owned by another installed file.");
        foreach (var relative in oldFiles.Concat(newFiles)) SafePath(mods, relative);
        foreach (var relative in newFiles)
        {
            var target = SafePath(mods, relative);
            if (!oldFiles.Contains(relative) && (File.Exists(target) || Directory.Exists(target)))
                throw new IOException("Mod destination has unowned existing content.");
        }
        EnsureSameVolume(dir, mods);
        foreach (var parent in oldFiles.Concat(newFiles).Select(f => Path.GetDirectoryName(SafePath(mods, f))!).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!string.Equals(parent, mods, StringComparison.OrdinalIgnoreCase)) EnsureSameVolume(dir, parent);
        var oldIndexes = replaced.Select(r => document.Mods.IndexOf(r)).ToArray();
        document.Mods.RemoveAll(replaced.Contains);
        if (record != null) document.Mods.Add(record);
        document.Generation = Path.GetFileName(dir);
        var candidate = InstalledModsStore.Serialize(document);
        var journal = new Journal
        {
            OperationId = Path.GetFileName(dir), Provider = record?.Provider ?? replaced[0].Provider,
            SourceKey = record?.SourceKey ?? replaced[0].SourceKey, PackageId = record?.Id ?? replaced[0].Id,
            FileId = record?.DownloadFileId ?? replaced.FirstOrDefault()?.DownloadFileId, ModsPath = modsRelative,
            OldRecordIndexes = oldIndexes, NewRecordIndex = record == null ? -1 : document.Mods.Count - 1,
            OldManifestHash = Hash(oldBytes), NewManifestHash = Hash(candidate)!
        };
        if (oldBytes != null) AtomicWrite(SafePath(dir, "old-manifest.json"), oldBytes);
        AtomicWrite(SafePath(dir, "new-manifest.json"), candidate);
        foreach (var relative in oldFiles.Concat(newFiles).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var live = SafePath(mods, relative);
            var old = ReadOptional(live);
            if (oldFiles.Contains(relative) && old == null) throw new IOException("Installed mod payload is missing; repair is required before replacement.");
            var index = journal.Payloads.Count;
            var backup = $"backup/{index}";
            var staged = "staged/" + relative;
            if (old != null) AtomicWrite(SafePath(dir, backup), old);
            var next = newFiles.Contains(relative) ? ReadOptional(SafePath(dir, staged)) : null;
            if (newFiles.Contains(relative) && next == null) throw new IOException("Staged mod payload is missing.");
            if (next != null) AtomicWrite(SafePath(dir, staged), next); // flush extracted bytes before Prepared
            journal.Payloads.Add(new() { Path = modsRelative + "/" + relative, OldSha256 = Hash(old), NewSha256 = Hash(next), BackupPath = backup, StagedPath = staged });
        }
        SyncTree(dir);
        WriteJournal(dir, journal);
        var transactions = SafePath(root, DirectoryName);
        Directory.CreateDirectory(transactions);
        var published = SafePath(transactions, journal.OperationId);
        Directory.Move(dir, published);
        SyncDirectory(transactions);
        SyncDirectory(root);
        SyncDirectory(Path.GetDirectoryName(dir)!);
        dir = published;
        try
        {
            boundary?.Invoke("Prepared");
            journal.Phase = "Applying"; WriteJournal(dir, journal); boundary?.Invoke("Applying");
            foreach (var entry in journal.Payloads)
            {
                if (FileHash(SafePath(root, entry.Path)) != entry.OldSha256)
                    throw new IOException("Mod payload changed during activation; recovery is required.");
                SetPayload(root, dir, entry, useNew: true, boundary);
                boundary?.Invoke("Payload:" + entry.Path);
            }
            boundary?.Invoke("BeforeManifest");
            // No intervening external generation may be replaced silently.
            if (FileHash(SafePath(root, InstalledModsStore.SidecarFileName)) != journal.OldManifestHash)
                throw new IOException("Mod sidecar changed during activation; recovery is required.");
            AtomicWrite(SafePath(root, InstalledModsStore.SidecarFileName), candidate);
            boundary?.Invoke("Manifest");
            journal.Phase = "Committed"; WriteJournal(dir, journal); boundary?.Invoke("Committed");
            VerifyGeneration(root, journal, useNew: true);
        }
        catch (Exception original)
        {
            bool committed = false;
            try
            {
                committed = FileHash(SafePath(root, InstalledModsStore.SidecarFileName)) == journal.NewManifestHash
                    && journal.Payloads.All(e => FileHash(SafePath(root, e.Path)) == e.NewSha256);
                boundary?.Invoke("Recovering"); RecoverOne(root, dir);
            }
            catch (Exception recovery)
            {
                var error = new IOException("Mod operation failed and recovery is incomplete; retained transaction evidence requires repair.", new AggregateException(original, recovery));
                if (committed && record != null) ModInstallOutcome.Attach(error, [ModCommittedIdentity.From(record)]);
                if (original is OperationCanceledException)
                {
                    original.Data["RecoveryError"] = error;
                    ModInstallOutcome.Attach(original, ModInstallOutcome.GetCommitted(error));
                    throw original;
                }
                throw error;
            }
            if (committed) throw ModInstallOutcome.FollowupFailure(original, record,
                "Mod replacement committed successfully, but a follow-up operation failed; the new generation remains installed.");
            throw;
        }
        try { Cleanup(dir, boundary); }
        catch (Exception ex) { throw ModInstallOutcome.FollowupFailure(ex, record, "Mod replacement committed, but a follow-up transaction cleanup failed; reload to finalize cleanup."); }
    }

    static void SetPayload(string root, string dir, Payload entry, bool useNew, Action<string>? boundary = null)
    {
        var live = SafePath(root, entry.Path);
        var expected = useNew ? entry.NewSha256 : entry.OldSha256;
        var source = SafePath(dir, useNew ? entry.StagedPath : entry.BackupPath);
        if (expected == null)
        {
            if (Directory.Exists(live)) throw new IOException("A directory obstructs mod recovery.");
            File.Delete(live);
            if (Directory.Exists(Path.GetDirectoryName(live))) SyncDirectory(Path.GetDirectoryName(live)!);
        }
        else
        {
            var bytes = ReadOptional(source);
            if (Hash(bytes) != expected) throw new InvalidDataException("Mod transaction payload evidence is missing or damaged.");
            // Keep temporary payload bytes out of the live mods tree, including on kill.
            // Retained staging/backup evidence is not consumed by the atomic activation rename.
            var activation = SafePath(dir, "activation/" + Guid.NewGuid().ToString("N"));
            AtomicWrite(activation, bytes!);
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            boundary?.Invoke("BeforePayloadActivate:" + entry.Path);
            CheckLinks(live);
            File.Move(activation, live, overwrite: true);
            SyncDirectory(Path.GetDirectoryName(live)!);
            SyncDirectory(Path.GetDirectoryName(activation)!);
        }
    }
    static void VerifyGeneration(string root, Journal j, bool useNew)
    {
        if (FileHash(SafePath(root, InstalledModsStore.SidecarFileName)) != (useNew ? j.NewManifestHash : j.OldManifestHash) ||
            j.Payloads.Any(e => FileHash(SafePath(root, e.Path)) != (useNew ? e.NewSha256 : e.OldSha256)))
            throw new IOException("Mod payload and manifest generation are not coherent.");
    }
    internal static void Recover(string root)
    {
        var parent = SafePath(root, DirectoryName);
        if (!Directory.Exists(parent)) return;
        var entries = Directory.GetFileSystemEntries(parent);
        if (entries.Length > 1) throw new InvalidDataException("Multiple active mod journals require explicit recovery review; evidence retained.");
        foreach (var entry in entries)
        {
            CheckLinks(entry);
            if (!Directory.Exists(entry)) throw new InvalidDataException("Unexpected mod recovery evidence.");
            RecoverOne(root, entry);
        }
    }
    internal static void RecoverOne(string root, string dir)
    {
        Journal j;
        try
        {
            j = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(SafePath(dir, "journal.json")), Json)
                ?? throw new JsonException("Missing transaction document.");
            if (j.SchemaVersion != 1 || j.OperationId != Path.GetFileName(dir) || !Guid.TryParseExact(j.OperationId, "N", out _) ||
                j.Phase is not ("Prepared" or "Applying" or "Committed") || j.Payloads == null ||
                string.IsNullOrWhiteSpace(j.Provider) || j.SourceKey == null || string.IsNullOrWhiteSpace(j.PackageId) || !ValidHash(j.NewManifestHash) ||
                (j.OldManifestHash != null && !ValidHash(j.OldManifestHash))) throw new JsonException("Invalid mod journal.");
            SafePath(root, j.ModsPath);
            if (j.ModsPath.Split('/')[0].StartsWith(".relicade-", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Journal payload overlaps launcher state.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in j.Payloads)
            {
                if (e == null || e.Path == null || !seen.Add(e.Path) || !e.Path.StartsWith(j.ModsPath + "/", StringComparison.Ordinal) ||
                    (e.OldSha256 != null && !ValidHash(e.OldSha256)) || (e.NewSha256 != null && !ValidHash(e.NewSha256)))
                    throw new JsonException("Invalid journal payload.");
                SafePath(root, e.Path); SafePath(dir, e.BackupPath); SafePath(dir, e.StagedPath);
                if (!e.BackupPath.StartsWith("backup/", StringComparison.Ordinal) || !e.StagedPath.StartsWith("staged/", StringComparison.Ordinal))
                    throw new JsonException("Invalid journal evidence path.");
                if (e.OldSha256 != null && FileHash(SafePath(dir, e.BackupPath)) != e.OldSha256) throw new JsonException("Invalid backup hash.");
                if (e.NewSha256 != null && FileHash(SafePath(dir, e.StagedPath)) != e.NewSha256) throw new JsonException("Invalid staged hash.");
            }
            var old = ReadOptional(SafePath(dir, "old-manifest.json"));
            var candidate = ReadOptional(SafePath(dir, "new-manifest.json"));
            if (Hash(old) != j.OldManifestHash || Hash(candidate) != j.NewManifestHash) throw new JsonException("Invalid retained manifest hash.");
            var oldDocument = old == null ? new InstalledModsDocument() : InstalledModsStore.Parse(old);
            var candidateDocument = InstalledModsStore.Parse(candidate!);
            if (candidateDocument.Generation != j.OperationId || j.OldManifestHash == j.NewManifestHash)
                throw new JsonException("Recovery generation identity is invalid.");
            ValidateManifestTransition(j, oldDocument, candidateDocument);
        }
        catch (Exception ex) when (ex is JsonException or FileNotFoundException or InvalidDataException)
        { throw new InvalidDataException("Mod recovery evidence is invalid; original transaction retained.", ex); }
        var manifest = FileHash(SafePath(root, InstalledModsStore.SidecarFileName));
        // Validate the complete preimage before any recovery writes. Never overwrite unknown external edits.
        if (manifest != j.OldManifestHash && manifest != j.NewManifestHash) throw new IOException("Unknown mod manifest generation; recovery stopped.");
        foreach (var e in j.Payloads)
        {
            var live = FileHash(SafePath(root, e.Path));
            if (live != e.OldSha256 && live != e.NewSha256) throw new IOException("Unknown mod payload modification; recovery stopped.");
        }
        bool complete = manifest == j.NewManifestHash && j.Payloads.All(e => FileHash(SafePath(root, e.Path)) == e.NewSha256);
        if (!complete)
        {
            foreach (var e in j.Payloads)
                if (FileHash(SafePath(root, e.Path)) != e.OldSha256)
                {
                    EnsureSameVolume(dir, Path.GetDirectoryName(SafePath(root, e.Path))!);
                    SetPayload(root, dir, e, useNew: false);
                }
            var sidecar = SafePath(root, InstalledModsStore.SidecarFileName);
            if (manifest != j.OldManifestHash)
            {
                if (j.OldManifestHash == null) { File.Delete(sidecar); SyncDirectory(root); }
                else AtomicWrite(sidecar, File.ReadAllBytes(SafePath(dir, "old-manifest.json")));
            }
        }
        VerifyGeneration(root, j, useNew: complete);
        Cleanup(dir);
    }
    static bool ValidHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    static void ValidateManifestTransition(Journal j, InstalledModsDocument old, InstalledModsDocument candidate)
    {
        if (j.OldRecordIndexes == null || j.OldRecordIndexes.Distinct().Count() != j.OldRecordIndexes.Length ||
            j.OldRecordIndexes.Any(i => i < 0 || i >= old.Mods.Count) || j.NewRecordIndex < -1 || j.NewRecordIndex >= candidate.Mods.Count)
            throw new JsonException("Invalid recovery record selection.");
        var previous = j.OldRecordIndexes.Select(i => old.Mods[i]).ToList();
        var next = j.NewRecordIndex == -1 ? null : candidate.Mods[j.NewRecordIndex];
        var identity = next ?? previous.FirstOrDefault() ?? throw new JsonException("Empty recovery selection.");
        if (identity.Provider != j.Provider || identity.SourceKey != j.SourceKey || identity.Id != j.PackageId || identity.DownloadFileId != j.FileId)
            throw new JsonException("Recovery package/file identity mismatch.");
        var expected = old.Mods.Where((r, i) => !j.OldRecordIndexes.Contains(i)).ToList();
        if (next != null) expected.Add(next);
        if (!InstalledModsStore.Serialize(new() { Mods = expected }).AsSpan().SequenceEqual(InstalledModsStore.Serialize(new() { Mods = candidate.Mods })))
            throw new JsonException("Recovery would alter unselected mod records.");
        var oldFiles = previous.SelectMany(r => r.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newFiles = next?.Files.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var paths = oldFiles.Concat(newFiles).Distinct(StringComparer.OrdinalIgnoreCase).Select(f => j.ModsPath + "/" + f).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!paths.SetEquals(j.Payloads.Select(p => p.Path))) throw new JsonException("Recovery payload list does not match the selected manifests.");
        foreach (var p in j.Payloads)
        {
            var relative = p.Path[(j.ModsPath.Length + 1)..];
            if (oldFiles.Contains(relative) != (p.OldSha256 != null) || newFiles.Contains(relative) != (p.NewSha256 != null))
                throw new JsonException("Recovery payload ownership/hash mismatch.");
        }
    }
    internal static void Cleanup(string dir, Action<string>? boundary = null)
    {
        // Evidence is removed only after successful validation (or before Prepared/live writes).
        ValidateTree(dir);
        if (Path.GetFileName(Path.GetDirectoryName(dir)) == DirectoryName)
        {
            // Retire atomically before deleting backups. A kill during recursive cleanup must
            // not leave a half-deleted active journal blocking an already coherent generation.
            var root = Path.GetDirectoryName(Path.GetDirectoryName(dir))!;
            var retired = SafePath(root, ".relicade-mod-cleanup"); Directory.CreateDirectory(retired);
            var target = SafePath(retired, Path.GetFileName(dir));
            Directory.Move(dir, target);
            SyncDirectory(Path.GetDirectoryName(dir)!); SyncDirectory(retired); SyncDirectory(root);
            dir = target;
            boundary?.Invoke("Retired");
        }
        Directory.Delete(dir, recursive: true);
        SyncDirectory(Path.GetDirectoryName(dir)!);
    }
    static void ValidateTree(string dir)
    {
        CheckLinks(dir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            CheckLinks(entry);
            if (Directory.Exists(entry)) ValidateTree(entry);
        }
    }
    static void SyncTree(string dir)
    {
        CheckLinks(dir);
        foreach (var child in Directory.EnumerateDirectories(dir)) SyncTree(child);
        SyncDirectory(dir);
    }
}
