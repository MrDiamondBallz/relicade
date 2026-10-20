using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relicade.Core.Services;

public sealed record BackupCleanupFile(
    [property: JsonRequired] string OperationId,
    [property: JsonRequired] string BackupRelativePath,
    [property: JsonRequired] string RetainedRelativePath,
    [property: JsonRequired] long Length,
    [property: JsonRequired] string Sha256);

public sealed class BackupCleanupPreview
{
    public string PlanSha256 { get; }
    public IReadOnlyList<BackupCleanupFile> Files { get; }
    public long ReclaimableBytes { get; }
    internal BackupCleanupPreview(string hash, IEnumerable<BackupCleanupFile> files)
    {
        PlanSha256 = hash; Files = Array.AsReadOnly(files.ToArray());
        ReclaimableBytes = Files.Aggregate(0L, (sum, f) => checked(sum + f.Length));
    }
}

public sealed record BackupCleanupResult(string PlanSha256, int DeletedFiles, long ReclaimedBytes, bool Completed);

/// <summary>Truthful invocation-local progress; deleted redundant copies are NOT rolled back.</summary>
public sealed class BackupCleanupInterruptedException : IOException
{
    public BackupCleanupResult Result { get; }
    internal BackupCleanupInterruptedException(BackupCleanupResult result, Exception cause)
        : base($"Backup cleanup interrupted: {result.DeletedFiles} files / {result.ReclaimedBytes} bytes removed this invocation. Retained generations were not deleted. Explicitly resume the same approved plan after resolving the error.", cause) => Result = result;
}

public sealed partial class PortableInstallationTransaction
{
    const string CleanupAction = "RedundantPortableBackupPayloadCleanup";
    const string CleanupPrefix = "backup-cleanup-";
    static readonly JsonSerializerOptions CleanupJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    // Only digests and relative paths are persisted, never CLI settings/credentials.
    sealed record CleanupSnapshot(
        [property: JsonRequired] string Path,
        [property: JsonRequired] string Kind,
        [property: JsonRequired] long Length,
        [property: JsonRequired] string Sha256,
        [property: JsonRequired] string Identity);
    sealed class CleanupApproval
    {
        [JsonRequired] public int SchemaVersion { get; init; } = 1;
        [JsonRequired] public string Action { get; init; } = CleanupAction;
        [JsonRequired] public string InstallKey { get; init; } = "";
        [JsonRequired] public string CurrentManifestSha256 { get; init; } = "";
        [JsonRequired] public string ContextSha256 { get; init; } = "";
        [JsonRequired] public string PlanSha256 { get; set; } = "";
        [JsonRequired] public CleanupSnapshot[] Snapshot { get; init; } = [];
        [JsonRequired] public BackupCleanupFile[] Files { get; init; } = [];
    }
    sealed record CleanupState(Manifest Current, CleanupSnapshot[] Snapshot, BackupCleanupFile[] Files, List<CleanupApproval> Approvals);

    public BackupCleanupPreview PreviewRedundantBackups() => PreviewRedundantBackups(Hash([]), () => { });
    public BackupCleanupResult CleanupRedundantBackups(string expectedPlanSha256, CancellationToken cancellationToken = default) =>
        CleanupRedundantBackups(expectedPlanSha256, Hash([]), () => { }, cancellationToken);

    // Cleanup-only composition: bind read-only library/settings evidence into the
    // SAME canonical plan, and revalidate it while Core holds all authority.
    internal BackupCleanupPreview PreviewRedundantBackups(string contextSha256, Action validateContext)
    {
        if (!ValidHash(contextSha256)) throw new ArgumentException("Invalid cleanup context digest.");
        using var authority = AcquireCleanupAuthority();
        validateContext();
        var state = InspectCleanup();
        // An approval interrupted before/among deletes must resume its original
        // exact snapshot, never approve a smaller replacement plan implicitly.
        foreach (var approval in state.Approvals.Where(a => a.Files.Any(f => RemovalEntryExists(CleanupSource(f)))))
        {
            ValidateCleanupSnapshot(state, approval, contextSha256);
            return new(approval.PlanSha256, approval.Files);
        }
        var plan = MakeCleanupApproval(state, contextSha256);
        validateContext();
        return new(plan.PlanSha256, plan.Files);
    }

    internal BackupCleanupResult CleanupRedundantBackups(string expectedPlanSha256, string contextSha256,
        Action validateContext, CancellationToken cancellationToken = default)
    {
        if (!ValidHash(expectedPlanSha256) || !ValidHash(contextSha256)) throw new ArgumentException("Expected an exact lower-case SHA256 cleanup plan.");
        cancellationToken.ThrowIfCancellationRequested();
        using var authority = AcquireCleanupAuthority();
        validateContext();
        var state = InspectCleanup();
        var approval = state.Approvals.SingleOrDefault(a => a.PlanSha256 == expectedPlanSha256);
        if (approval == null)
        {
            if (state.Approvals.Any(a => a.Files.Any(f => RemovalEntryExists(CleanupSource(f)))))
                throw new IOException("An existing approval requires explicit resume; no new cleanup plan was applied.");
            approval = MakeCleanupApproval(state, contextSha256);
            if (approval.PlanSha256 != expectedPlanSha256) throw new IOException("The cleanup preview is stale. Preview again; no files were deleted.");
        }
        ValidateCleanupSnapshot(state, approval, contextSha256);
        validateContext(); cancellationToken.ThrowIfCancellationRequested();
        if (approval.Files.Length == 0) return new(expectedPlanSha256, 0, 0, true);
        if (!state.Approvals.Any(a => a.PlanSha256 == expectedPlanSha256))
        {
            var path = SafePath(operations, CleanupPrefix + expectedPlanSha256 + ".json");
            var pending = path + ".pending";
            // A known but incomplete pending receipt is repair evidence, not an
            // invitation to overwrite/discard approval. InspectCleanup refuses it.
            WriteNew(pending, JsonSerializer.SerializeToUtf8Bytes(approval, CleanupJson));
            Sync(operations);
            File.Move(pending, path, false); Sync(operations);
        }
        var deleted = 0; long reclaimed = 0;
        try
        {
            options.TransactionBoundary?.Invoke("BackupCleanupApproved");
            foreach (var file in approval.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                validateContext();
                // Reinspect the ENTIRE preserved state, not only this candidate.
                // Missing files normalize ONLY from this exact durable approval.
                ValidateCleanupSnapshot(InspectCleanup(), approval, contextSha256);
                var source = CleanupSource(file);
                if (!RemovalEntryExists(source)) continue;
                options.TransactionBoundary?.Invoke("BeforeBackupCleanupDelete");
                cancellationToken.ThrowIfCancellationRequested(); validateContext();
                ValidateCleanupSnapshot(InspectCleanup(), approval, contextSha256);
                VerifyCleanupFile(source, file);
                VerifyCleanupFile(CleanupRetained(file), file);
                DeleteCleanupFile(source);
                deleted++; reclaimed = checked(reclaimed + file.Length);
                Sync(Path.GetDirectoryName(source)!);
                options.TransactionBoundary?.Invoke("BackupCleanupDeleted");
            }
            cancellationToken.ThrowIfCancellationRequested(); validateContext();
            ValidateCleanupSnapshot(InspectCleanup(), approval, contextSha256);
            if (approval.Files.Any(f => RemovalEntryExists(CleanupSource(f)))) throw new IOException("Approved duplicate files remain.");
            options.TransactionBoundary?.Invoke("BackupCleanupCompleted");
            return new(expectedPlanSha256, deleted, reclaimed, true);
        }
        catch (Exception ex) { throw new BackupCleanupInterruptedException(new(expectedPlanSha256, deleted, reclaimed, false), ex); }
    }

    IDisposable AcquireCleanupAuthority()
    {
        if (options.IsSharedFolder) throw new IOException("Shared installations cannot authorize portable backup cleanup.");
        var held = new Dictionary<string, IDisposable>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            ValidateCleanupOutsideAuthority();
            AcquireRemovalAncestorLeases(held);
            // A cleanup cannot manufacture a lost installation, history or mod lock.
            RequireCleanupKind(root, true); RequireCleanupKind(operations, true);
            var writer = SafePath(operations, "writer.lock"); RequireCleanupKind(writer, false);
            HoldRemovalLease(held, writer, () => new FileStream(writer, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            void ExistingMod(string dir)
            {
                var mod = SafePath(dir, ModLock);
                if (!RemovalEntryExists(mod)) return;
                RequireCleanupKind(mod, false);
                HoldRemovalLease(held, mod, () => new FileStream(mod, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            }
            ExistingMod(root);
            foreach (var entry in Directory.EnumerateFileSystemEntries(operations))
            {
                RequireCleanupKind(entry, (File.GetAttributes(entry) & FileAttributes.Directory) != 0);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) == 0) continue;
                if (!Guid.TryParseExact(Path.GetFileName(entry), "N", out _)) throw new IOException("Unknown operation directory; explicit repair required.");
                foreach (var name in new[] { "previous", "backup" })
                    if (RemovalEntryExists(SafePath(entry, name))) ExistingMod(SafePath(entry, name));
            }
            return new RemovalAuthorityLeases(held.Values.ToList());
        }
        catch { foreach (var lease in held.Values.Reverse()) lease.Dispose(); throw; }
    }

    internal IDisposable AcquireCleanupUnmanagedAlternativeAuthority()
    {
        // The CLI's absent default slot is part of this cleanup's authority too.
        // Do not let the unchanged general removal helper accept special/aliased
        // lock evidence there merely because the selected located root is healthy.
        ValidateCleanupOutsideAuthority();
        return AcquireUnmanagedRemovalLease();
    }

    void ValidateCleanupOutsideAuthority()
    {
        // The reused removal exclusion intentionally has a different remit. Its
        // lock opens must not let cleanup accept a FIFO/device or a case-aliased
        // OUTSIDE authority that the selected generation snapshot cannot see.
        for (string? path = root; path != null; path = Path.GetDirectoryName(path))
        {
            CheckLinks(path);
            var outside = Path.GetDirectoryName(path);
            if (outside != null)
            {
                var historyParent = SafePath(outside, DirectoryName);
                var identity = Hash(System.Text.Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path));
                if (RemovalEntryExists(historyParent))
                {
                    RequireCleanupKind(historyParent, true);
                    foreach (var entry in Directory.EnumerateFileSystemEntries(historyParent))
                        if (string.Equals(Path.GetFileName(entry), identity, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(entry) != identity)
                            throw new IOException("Case-aliased portable authority requires repair before cleanup.");
                    var history = SafePath(historyParent, identity);
                    if (RemovalEntryExists(history))
                    {
                        RequireCleanupKind(history, true);
                        var writer = SafePath(history, "writer.lock"); RequireCleanupKind(writer, false);
                        if (new FileInfo(writer).Length != 0) throw new IOException("Unknown outside writer lock contents.");
                    }
                }
            }
            var mod = SafePath(path, ModLock);
            if (RemovalEntryExists(mod))
            {
                RequireCleanupKind(mod, false);
                if (new FileInfo(mod).Length != 0) throw new IOException("Unknown ancestor mod lock contents.");
            }
        }
    }

    CleanupState InspectCleanup()
    {
        RequireCleanupKind(root, true); RequireCleanupKind(operations, true);
        var approvals = new List<CleanupApproval>(); var dirs = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Directory.EnumerateFileSystemEntries(operations).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(item);
            if (!names.Add(name)) throw new IOException("Case-aliased portable history.");
            var directory = (File.GetAttributes(item) & FileAttributes.Directory) != 0;
            RequireCleanupKind(item, directory);
            if (directory)
            {
                if (!Guid.TryParseExact(name, "N", out _) || name != name.ToLowerInvariant()) throw new IOException("Unknown portable operation.");
                dirs.Add(item);
            }
            else if (name == "writer.lock") { }
            else if (name.StartsWith(CleanupPrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
                approvals.Add(ReadCleanupApproval(item));
            else throw new IOException("Launch, pending or unknown portable evidence blocks cleanup. Explicit repair/resume is required; nothing was recovered automatically.");
        }
        RejectModEvidence(root);
        // Check special files recursively BEFORE existing Inventory/Optional APIs
        // could block reading a FIFO/device. Bound scans and all metadata reads.
        var snapshot = new List<CleanupSnapshot>();
        CaptureCleanupTree(root, "live", snapshot);
        CaptureCleanupTree(operations, "operations", snapshot, skipApprovals: true);
        var current = ReadManifest(root, true) ?? throw new IOException("The live managed manifest is missing; repair is required.");
        ValidateCommittedIdentity(current);
        var eligible = new List<BackupCleanupFile>();
        var knownMissing = approvals.SelectMany(a => a.Files).GroupBy(CleanupFileKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var dir in dirs)
        {
            var j = ReadJournal(dir);
            if (j.Phase != "Committed" || j.Resolution != null) throw new IOException("Unresolved portable operations require recovery/repair, not cleanup.");
            var allowed = new HashSet<string>(["journal.json", "backup", "previous"], StringComparer.Ordinal);
            foreach (var child in Directory.EnumerateFileSystemEntries(dir))
                if (!allowed.Remove(Path.GetFileName(child))) throw new IOException("Unknown or staged evidence in committed operation.");
            if (allowed.Count != 0) throw new IOException("A committed operation has missing evidence.");
            var previous = SafePath(dir, "previous"); var backup = SafePath(dir, "backup");
            RequireCleanupKind(previous, true); RequireCleanupKind(backup, true);
            AssertInventory(previous, j.OldFiles);
            var prior = ReadManifest(previous, true);
            if (FileHash(SafePath(previous, ManifestFileName)) != j.OldManifestHash || prior?.SelectedVersion != j.OldVersion)
                throw new IOException("Previous manifest differs from the committed journal.");
            if (prior != null)
            {
                if (!prior.DataPaths.SequenceEqual(current.DataPaths, StringComparer.Ordinal)) throw new IOException("Changed data policy requires repair.");
                var oldCommit = ReadJournal(SafePath(operations, prior.OperationId));
                if (oldCommit.Phase != "Committed" || oldCommit.Resolution != null || oldCommit.CandidateManifestHash != j.OldManifestHash)
                    throw new IOException("Previous generation has no exact committed identity.");
            }
            var possible = new List<BackupCleanupFile>();
            if (j.OperationId != current.OperationId && prior != null)
                foreach (var e in j.OldFiles.Where(e => e.Kind == "file" && e.Ownership == "payload"))
                {
                    // Rollback records Inventory(root) WITHOUT data ownership. The
                    // validated PREVIOUS manifest policy is authoritative, never
                    // the journal's payload flag alone (including nested data).
                    if (IsData(e.Path, prior.DataPaths) || CleanupProtected(e.Path) ||
                        !prior.Files.Any(p => p.Path == e.Path && p.Kind == "file" && p.Ownership == "payload" && p.Length == e.Length && p.Sha256 == e.Sha256)) continue;
                    possible.Add(new(j.OperationId, "backup/" + e.Path, "previous/" + e.Path, e.Length, e.Sha256));
                }
            var actual = Inventory(backup);
            var missing = j.OldFiles.Where(e => !actual.Any(a => a.Path == e.Path)).ToArray();
            foreach (var e in missing)
            {
                var f = possible.SingleOrDefault(p => p.BackupRelativePath == "backup/" + e.Path);
                if (f == null || !knownMissing.TryGetValue(CleanupFileKey(f), out var approved) || f != approved)
                    throw new IOException("An unapproved backup file is missing; repair required.");
                VerifyCleanupFile(CleanupRetained(f), f);
            }
            EqualEntries(actual, j.OldFiles.Where(e => !missing.Contains(e)).ToList());
            // Metadata/data/directories must still match exactly; missing files
            // above are accepted ONLY when eligible + approved + retained intact.
            foreach (var f in possible)
            {
                VerifyCleanupFile(CleanupRetained(f), f);
                if (RemovalEntryExists(CleanupSource(f))) { VerifyCleanupFile(CleanupSource(f), f); eligible.Add(f); }
            }
        }
        // Every receipt must still refer to real independently validated evidence,
        // not arbitrary paths, guessed filenames or a count declaring completion.
        foreach (var approval in approvals)
            foreach (var file in approval.Files)
            {
                var dir = SafePath(operations, file.OperationId); var j = ReadJournal(dir);
                var prior = ReadManifest(SafePath(dir, "previous"), true);
                var rel = file.BackupRelativePath[7..];
                if (prior == null || file.OperationId == current.OperationId || IsData(rel, prior.DataPaths) || CleanupProtected(rel) ||
                    !j.OldFiles.Any(e => e.Path == rel && e.Kind == "file" && e.Length == file.Length && e.Sha256 == file.Sha256) ||
                    !prior.Files.Any(e => e.Path == rel && e.Kind == "file" && e.Ownership == "payload" && e.Length == file.Length && e.Sha256 == file.Sha256))
                    throw new IOException("Cleanup approval no longer has valid retained payload evidence.");
                VerifyCleanupFile(CleanupRetained(file), file);
            }
        return new(current, snapshot.OrderBy(s => s.Path, StringComparer.Ordinal).ToArray(),
            eligible.OrderBy(CleanupFileKey, StringComparer.Ordinal).ToArray(), approvals);
    }

    static bool CleanupProtected(string path) => path.Split('/').Any(p => Reserved(p) ||
        p.Equals("LastPlayed.txt", StringComparison.OrdinalIgnoreCase) || p.Equals("saves", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("config", StringComparison.OrdinalIgnoreCase) || p.Equals("owneddata", StringComparison.OrdinalIgnoreCase));
    static string CleanupFileKey(BackupCleanupFile f) => f.OperationId + "/" + f.BackupRelativePath;
    string CleanupSource(BackupCleanupFile f) => SafePath(operations, CleanupFileKey(f));
    string CleanupRetained(BackupCleanupFile f) => SafePath(operations, f.OperationId + "/" + f.RetainedRelativePath);
    [DllImport("libc", SetLastError = true, EntryPoint = "unlink")]
    static extern int CleanupUnlink([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeleteFileW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CleanupDeleteWindows(string path);
    static void DeleteCleanupFile(string path)
    {
        // Unlike File.Delete's absent-file no-op, these calls return whether THIS
        // invocation actually unlinked a file; an absent raced target is not counted.
        var removed = OperatingSystem.IsWindows() ? CleanupDeleteWindows(path) : CleanupUnlink(path) == 0;
        if (!removed) throw new IOException("The approved duplicate could not be removed; no deletion counted.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }
    static void VerifyCleanupFile(string path, BackupCleanupFile f)
    {
        RequireCleanupKind(path, false);
        if (new FileInfo(path).Length != f.Length || FileHash(path) != f.Sha256) throw new IOException("Cleanup source or retained duplicate changed.");
    }
    CleanupApproval MakeCleanupApproval(CleanupState state, string context)
    {
        var a = new CleanupApproval { InstallKey = key, CurrentManifestSha256 = FileHash(SafePath(root, ManifestFileName))!,
            ContextSha256 = context, Snapshot = state.Snapshot, Files = state.Files };
        a.PlanSha256 = CleanupPlanHash(a);
        // A newly offered plan must satisfy the SAME closed schema and budgets
        // as a durable receipt, before preview or any pending/publication write.
        // Empty plans are read-only noops and are never persisted as approvals.
        return ParseCleanupApproval(JsonSerializer.SerializeToUtf8Bytes(a, CleanupJson),
            CleanupPrefix + a.PlanSha256 + ".json", allowEmpty: true);
    }
    static string CleanupPlanHash(CleanupApproval a) => Hash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        a.SchemaVersion, a.Action, a.InstallKey, a.CurrentManifestSha256, a.ContextSha256, a.Snapshot, a.Files
    }, CleanupJson));
    void ValidateCleanupSnapshot(CleanupState state, CleanupApproval a, string context)
    {
        if (a.ContextSha256 != context || a.CurrentManifestSha256 != FileHash(SafePath(root, ManifestFileName)))
            throw new IOException("The current manifest/library/settings differs from the approved preview.");
        var rows = state.Snapshot.ToDictionary(s => s.Path, StringComparer.Ordinal);
        foreach (var file in a.Files)
        {
            var path = "operations/" + CleanupFileKey(file);
            var original = a.Snapshot.SingleOrDefault(s => s.Path == path);
            if (original == null || original.Kind != "file" || original.Sha256 != file.Sha256 || original.Length != file.Length)
                throw new IOException("Approval has no exact source snapshot.");
            if (!rows.ContainsKey(path)) rows.Add(path, original);
        }
        if (!rows.Values.OrderBy(s => s.Path, StringComparer.Ordinal).SequenceEqual(a.Snapshot))
            throw new IOException("The validated cleanup snapshot changed; no new deletion is authorized.");
        // The recomputed eligible set may only shrink by approved absences.
        var present = a.Files.Where(f => RemovalEntryExists(CleanupSource(f))).ToArray();
        if (!present.SequenceEqual(state.Files)) throw new IOException("Cleanup candidates changed; preview cannot silently expand.");
    }
    CleanupApproval ReadCleanupApproval(string path) =>
        ParseCleanupApproval(Optional(path) ?? throw new IOException("Cleanup approval disappeared."), path);

    CleanupApproval ParseCleanupApproval(byte[] bytes, string path, bool allowEmpty = false)
    {
        // Match Optional's unchanged metadata-read ceiling for generated bytes as
        // well: no immutable receipt may be published that cannot be read back.
        if (bytes.LongLength > 64L * 1024 * 1024) throw new InvalidDataException("Cleanup approval exceeds its metadata size budget.");
        var a = Parse<CleanupApproval>(bytes);
        if (a.SchemaVersion != 1 || a.Action != CleanupAction || a.InstallKey != key || !ValidHash(a.PlanSha256) ||
            !ValidHash(a.CurrentManifestSha256) || !ValidHash(a.ContextSha256) || a.Files == null || a.Snapshot == null ||
            (!allowEmpty && a.Files.Length == 0) || a.Files.Length > options.MaximumFileCount || a.Snapshot.Length > options.MaximumFileCount * 8L ||
            Path.GetFileName(path) != CleanupPrefix + a.PlanSha256 + ".json") throw new InvalidDataException("Malformed cleanup approval.");
        long total = 0; string? previous = null;
        foreach (var file in a.Files)
        {
            if (file == null || !Guid.TryParseExact(file.OperationId, "N", out _) || file.OperationId != file.OperationId.ToLowerInvariant() ||
                !file.BackupRelativePath.StartsWith("backup/", StringComparison.Ordinal) ||
                file.RetainedRelativePath != "previous/" + file.BackupRelativePath[7..] || file.Length < 0 || !ValidHash(file.Sha256)) throw new InvalidDataException("Invalid approval file row.");
            ValidateRelative(file.BackupRelativePath); ValidateRelative(file.RetainedRelativePath);
            var next = CleanupFileKey(file);
            if (previous != null && StringComparer.Ordinal.Compare(previous, next) >= 0) throw new InvalidDataException("Unsorted/duplicate approval rows.");
            previous = next; total = checked(total + file.Length);
        }
        if (total > options.MaximumExpandedBytes) throw new InvalidDataException("Cleanup approval exceeds byte budget.");
        previous = null;
        foreach (var s in a.Snapshot)
        {
            if (s == null || s.Kind is not ("file" or "directory" or "lock") || s.Length < 0 || !ValidHash(s.Sha256) ||
                string.IsNullOrEmpty(s.Identity) || s.Identity.Length > 512 || s.Identity.Any(char.IsControl) ||
                !(s.Path == "live" || s.Path.StartsWith("live/", StringComparison.Ordinal) || s.Path == "operations" || s.Path.StartsWith("operations/", StringComparison.Ordinal)))
                throw new InvalidDataException("Invalid cleanup snapshot row.");
            ValidateRelative(s.Path);
            if (previous != null && StringComparer.Ordinal.Compare(previous, s.Path) >= 0) throw new InvalidDataException("Unsorted/duplicate snapshot.");
            previous = s.Path;
        }
        if (CleanupPlanHash(a) != a.PlanSha256 || !JsonSerializer.SerializeToUtf8Bytes(a, CleanupJson).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Cleanup approval is not exact canonical schema1 evidence.");
        foreach (var f in a.Files)
        {
            var source = a.Snapshot.SingleOrDefault(s => s.Path == "operations/" + CleanupFileKey(f));
            var retained = a.Snapshot.SingleOrDefault(s => s.Path == "operations/" + f.OperationId + "/" + f.RetainedRelativePath);
            if (source == null || retained == null || source.Kind != "file" || retained.Kind != "file" || source.Length != f.Length || retained.Length != f.Length ||
                source.Sha256 != f.Sha256 || retained.Sha256 != f.Sha256) throw new InvalidDataException("Approval lacks exact source and retained snapshot rows.");
        }
        return a;
    }

    void CaptureCleanupTree(string directory, string anchor, List<CleanupSnapshot> rows, bool skipApprovals = false)
    {
        long bytes = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string path, string relative)
        {
            var attrs = File.GetAttributes(path); var isDir = (attrs & FileAttributes.Directory) != 0;
            RequireCleanupKind(path, isDir);
            if (!names.Add(relative)) throw new InvalidDataException("Case-aliased cleanup paths.");
            var isLock = Path.GetFileName(path) == ModLock || relative == "operations/writer.lock";
            var length = isDir ? 0 : new FileInfo(path).Length;
            if (isLock && length != 0) throw new IOException("Unknown lock contents block cleanup.");
            bytes = checked(bytes + length);
            if (rows.Count >= options.MaximumFileCount * 8L || bytes > options.MaximumExpandedBytes * 8L) throw new IOException("Cleanup snapshot exceeds its inventory budget.");
            var identity = CleanupIdentity(path, isDir);
            rows.Add(new(relative, isDir ? "directory" : isLock ? "lock" : "file", length, isDir || isLock ? Hash([]) : FileHash(path)!, identity));
            if (identity != CleanupIdentity(path, isDir)) throw new IOException("A cleanup file changed during hashing.");
            if (!isDir) return;
            foreach (var child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(child); ValidateRelative(name);
                if (skipApprovals && path == directory && name.StartsWith(CleanupPrefix, StringComparison.Ordinal)) continue;
                Walk(child, relative + "/" + name);
            }
        }
        Walk(directory, anchor);
    }

    // statx type/identity checks happen before .NET opens a file, preventing FIFO
    // hangs and refusing hardlinks/special files. Other platforms remain qualified
    // by their native runtime tests; these checks are not a hostile-writer sandbox.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    struct CleanupStat
    {
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanos;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [DllImport("libc", SetLastError = true, EntryPoint = "statx")]
    static extern int CleanupStatx(int fd, string path, int flags, uint mask, out CleanupStat value);
    static CleanupStat CleanupStatOf(string path)
    {
        if (CleanupStatx(-100, path, 0x100, 0x7ff, out var value) != 0) throw new IOException("Cannot inspect regular cleanup file identity.");
        return value;
    }
    static void RequireCleanupKind(string path, bool directory)
    {
        CheckLinks(path);
        var attrs = File.GetAttributes(path);
        if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 || ((attrs & FileAttributes.Directory) != 0) != directory)
            throw new IOException("Cleanup requires unlinked regular files/directories.");
        if (OperatingSystem.IsLinux())
        {
            var st = CleanupStatOf(path);
            if ((st.Mode & 0xf000) != (directory ? 0x4000 : 0x8000) || !directory && st.Links != 1)
                throw new IOException("Special or hardlinked cleanup evidence requires repair.");
        }
    }
    static string CleanupIdentity(string path, bool directory)
    {
        if (OperatingSystem.IsLinux())
        {
            var st = CleanupStatOf(path);
            return $"{st.DeviceMajor}:{st.DeviceMinor}:{st.Inode}:{st.Mode}:{(directory ? "directory" : $"{st.Links}:{st.ChangeSeconds}:{st.ChangeNanos}")}";
        }
        var f = new FileInfo(path);
        return $"{(int)f.Attributes}:{f.CreationTimeUtc.Ticks}:{(directory ? 0 : f.LastWriteTimeUtc.Ticks)}";
    }
}
