using System.Text.Json;

namespace Relicade.Core.Services;

internal sealed class PortableRollbackPreview
{
    internal PortableRollbackPreview(string hash, PortableInstallationTransaction.Manifest current, PortableInstallationTransaction.Manifest target)
    {
        PlanSha256 = hash; CurrentVersion = current.SelectedVersion; TargetVersion = target.SelectedVersion;
        CurrentOperationId = current.OperationId; RetainedOperationId = target.OperationId;
        TargetExecutable = target.SelectedExecutable;
        PreservedDataPaths = Array.AsReadOnly(current.DataPaths.ToArray());
    }
    public string PlanSha256 { get; }
    public string CurrentVersion { get; }
    public string TargetVersion { get; }
    public string CurrentOperationId { get; }
    public string RetainedOperationId { get; }
    public string TargetExecutable { get; }
    public IReadOnlyList<string> PreservedDataPaths { get; }
}

internal sealed class PortableRollbackResult
{
    internal PortableRollbackResult(string hash, string operation, string version, bool completed)
    { PlanSha256 = hash; ActualOperationId = operation; ActualVersion = version; Completed = completed; }
    public string PlanSha256 { get; }
    public string ActualOperationId { get; }
    public string ActualVersion { get; }
    public bool Completed { get; }
}

public sealed partial class PortableInstallationTransaction
{
    sealed record RollbackState(Manifest Current, Manifest Target, string Previous, List<Entry> Before, CleanupSnapshot[] Snapshot, string Hash);

    internal PortableRollbackPreview PreviewRollback(string contextSha256, Action validateContext, string? alternativeRoot = null)
    {
        ArgumentNullException.ThrowIfNull(validateContext);
        if (!ValidHash(contextSha256)) throw new ArgumentException("Invalid rollback context SHA256.");
        using var authority = AcquireRollbackAuthority(alternativeRoot);
        validateContext();
        var state = InspectRollback(contextSha256);
        validateContext();
        if (InspectRollback(contextSha256).Hash != state.Hash) throw new IOException("Rollback evidence changed during preview. Preview again.");
        return new(state.Hash, state.Current, state.Target);
    }

    internal PortableRollbackResult RollbackConfirmed(string expectedPlanSha256, string contextSha256, Action validateContext,
        CancellationToken cancellationToken = default, string? alternativeRoot = null)
    {
        ArgumentNullException.ThrowIfNull(validateContext);
        if (!ValidHash(expectedPlanSha256) || !ValidHash(contextSha256)) throw new ArgumentException("Expected an exact lower-case SHA256 rollback plan.");
        cancellationToken.ThrowIfCancellationRequested();
        using var authority = AcquireRollbackAuthority(alternativeRoot);
        validateContext();
        var state = InspectRollback(contextSha256);
        if (state.Hash != expectedPlanSha256) throw new IOException("The rollback preview is stale. Preview again and explicitly confirm the new displayed hash.");
        validateContext(); cancellationToken.ThrowIfCancellationRequested();
        // No Recover: interrupted history must be separately inspected/repaired.
        var id = Guid.NewGuid().ToString("N"); var dir = SafePath(operations, id);
        var candidate = SafePath(dir, "candidate");
        Directory.CreateDirectory(candidate); Sync(operations);
        var prior = state.Target;
        try
        {
            foreach (var e in prior.Files.Where(e => e.Ownership == "payload"))
            { cancellationToken.ThrowIfCancellationRequested(); CopyEntry(state.Previous, candidate, e); }
            foreach (var e in state.Before.Where(e => IsData(e.Path, state.Current.DataPaths)))
            { cancellationToken.ThrowIfCancellationRequested(); CopyEntry(root, candidate, e); }
            prior.OperationId = id; prior.Files = Inventory(candidate, state.Current.DataPaths);
            WriteCandidateMetadata(candidate, prior, Optional(SafePath(state.Previous, "installed-build.txt")));
            FlushTree(candidate); options.TransactionBoundary?.Invoke("Staged");
            cancellationToken.ThrowIfCancellationRequested(); validateContext();
            AssertInventory(root, state.Before);
            // Original held evidence must remain exact during the slow copy. Only
            // this invocation's newly staged operation is excluded from this check.
            var observed = CaptureRollbackSnapshot(id);
            if (!observed.SequenceEqual(state.Snapshot)) throw new IOException("Rollback evidence changed during staging; no activation is authorized.");
            validateContext();
            Commit(dir, prior, state.Current, state.Before, cancellationToken);
        }
        catch (Exception error)
        {
            // Existing Commit may throw AFTER durable Committed. Observe exactly
            // this invocation, under the same authority; never recover or retry.
            try { return ObserveRollbackResult(expectedPlanSha256, id, prior.SelectedVersion); }
            catch { throw new IOException("Rollback did not return a confirmed committed result. Outcome may be incomplete or unknown; inspect retained transaction evidence before any new attempt. No automatic recovery or retry was performed.", error); }
        }
        return ObserveRollbackResult(expectedPlanSha256, id, prior.SelectedVersion);
    }

    PortableRollbackResult ObserveRollbackResult(string hash, string id, string version)
    {
        var actual = ReadManifest(root, true) ?? throw new IOException("No complete live manifest observed.");
        ValidateCommittedIdentity(actual);
        if (actual.OperationId != id || actual.SelectedVersion != version) throw new IOException("The requested rollback is not confirmed committed.");
        return new(hash, actual.OperationId, actual.SelectedVersion, true);
    }

    IDisposable AcquireRollbackAuthority(string? alternativeRoot)
    {
        if (options.IsSharedFolder) throw new IOException("Shared installations cannot authorize portable rollback.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var held = new Dictionary<string, IDisposable>(comparer);
        try
        {
            ValidateCleanupOutsideAuthority(); // Regular-file/case/link authority primitive; no cleanup plan/API.
            if (alternativeRoot != null)
            {
                var other = new PortableInstallationTransaction(alternativeRoot);
                if (!comparer.Equals(root, other.root))
                { other.ValidateCleanupOutsideAuthority(); other.AcquireRemovalAuthority(held); }
            }
            AcquireRemovalAncestorLeases(held);
            RequireCleanupKind(root, true); RequireCleanupKind(operations, true);
            var writer = SafePath(operations, "writer.lock"); RequireCleanupKind(writer, false);
            HoldRemovalLease(held, writer, () => new FileStream(writer, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            void Mod(string path)
            {
                var file = SafePath(path, ModLock);
                if (!RemovalEntryExists(file)) return;
                RequireCleanupKind(file, false);
                HoldRemovalLease(held, file, () => new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            }
            Mod(root);
            foreach (var op in Directory.EnumerateDirectories(operations))
                foreach (var name in new[] { "previous", "backup" })
                    if (RemovalEntryExists(SafePath(op, name))) Mod(SafePath(op, name));
            return new RemovalAuthorityLeases(held.Values.ToList());
        }
        catch { foreach (var lease in held.Values.Reverse()) lease.Dispose(); throw; }
    }

    CleanupSnapshot[] CaptureRollbackSnapshot(string? ownStaging = null)
    {
        var rows = new List<CleanupSnapshot>();
        CaptureCleanupTree(root, "live", rows);
        // Include approval receipts, journal bytes and retained data, not just live
        // payload. Completed cleanup absences are validated separately below.
        foreach (var item in Directory.EnumerateFileSystemEntries(operations).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(item); ValidateRelative(name);
            if (name == ownStaging) continue;
            CaptureCleanupTree(item, "operations/" + name, rows);
        }
        return rows.OrderBy(r => r.Path, StringComparer.Ordinal).ToArray();
    }

    RollbackState InspectRollback(string context)
    {
        var snapshot = CaptureRollbackSnapshot(); // Bounds, regular files, hardlinks, case aliases before reads.
        var receipts = new List<CleanupApproval>(); var dirs = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Directory.EnumerateFileSystemEntries(operations))
        {
            var name = Path.GetFileName(item);
            if (!names.Add(name)) throw new IOException("Case-aliased rollback history.");
            if (Directory.Exists(item))
            {
                if (!Guid.TryParseExact(name, "N", out _) || name != name.ToLowerInvariant()) throw new IOException("Unknown rollback history requires explicit repair.");
                dirs.Add(item);
            }
            else if (name == "writer.lock") { }
            else if (name.StartsWith(CleanupPrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)) receipts.Add(ReadCleanupApproval(item));
            else throw new IOException("Launch, pending or unknown history blocks rollback; no evidence was repaired.");
        }
        RejectModEvidence(root);
        var current = ReadManifest(root, true) ?? throw new IOException("No managed current generation. Reopen/repair the installation.");
        ValidateCommittedIdentity(current);
        foreach (var dir in dirs)
        {
            var journal = ReadJournal(dir);
            if (journal.Phase != "Committed" || journal.Resolution != null) throw new IOException("Interrupted rollback/install evidence needs explicit recovery/repair, not a new rollback.");
            var allowed = new HashSet<string>(["journal.json", "previous", "backup"], StringComparer.Ordinal);
            foreach (var child in Directory.EnumerateFileSystemEntries(dir))
                if (!allowed.Remove(Path.GetFileName(child))) throw new IOException("Unknown or staged rollback history needs explicit repair.");
            if (allowed.Count != 0) throw new IOException("Incomplete retained rollback history.");
            var previous = SafePath(dir, "previous"); var backup = SafePath(dir, "backup");
            RequireCleanupKind(previous, true); RequireCleanupKind(backup, true);
            RejectModEvidence(previous); RejectModEvidence(backup);
            AssertInventory(previous, journal.OldFiles);
            var old = ReadManifest(previous, true);
            if (FileHash(SafePath(previous, ManifestFileName)) != journal.OldManifestHash || old?.SelectedVersion != journal.OldVersion)
                throw new IOException("Retained manifest does not match its journal.");
            if (old != null)
            {
                var oldCommit = ReadJournal(SafePath(operations, old.OperationId));
                if (oldCommit.Phase != "Committed" || oldCommit.Resolution != null || oldCommit.CandidateManifestHash != journal.OldManifestHash)
                    throw new IOException("Retained generation has no complete committed identity.");
                if (!current.DataPaths.SequenceEqual(old.DataPaths, StringComparer.Ordinal)) throw new IOException("Data policy changed; explicit repair is required before rollback.");
            }
            var actual = Inventory(backup);
            var missing = journal.OldFiles.Where(e => !actual.Any(a => a.Path == e.Path)).ToArray();
            foreach (var entry in missing)
            {
                var witness = receipts.SelectMany(r => r.Files).SingleOrDefault(f => f.OperationId == journal.OperationId && f.BackupRelativePath == "backup/" + entry.Path);
                if (old == null || witness == null || journal.OperationId == current.OperationId || IsData(entry.Path, old.DataPaths) || CleanupProtected(entry.Path) ||
                    !old.Files.Any(e => e.Path == entry.Path && e.Kind == "file" && e.Ownership == "payload" && e.Length == witness.Length && e.Sha256 == witness.Sha256))
                    throw new IOException("Unapproved missing backup evidence blocks rollback.");
                VerifyCleanupFile(SafePath(dir, witness.RetainedRelativePath), witness);
            }
            EqualEntries(actual, journal.OldFiles.Where(e => !missing.Contains(e)).ToList());
        }
        foreach (var receipt in receipts)
            foreach (var file in receipt.Files)
            {
                var dir = SafePath(operations, file.OperationId); var journal = ReadJournal(dir);
                var old = ReadManifest(SafePath(dir, "previous"), true); var path = file.BackupRelativePath[7..];
                if (old == null || file.OperationId == current.OperationId || IsData(path, old.DataPaths) || CleanupProtected(path) ||
                    !journal.OldFiles.Any(e => e.Path == path && e.Kind == "file" && e.Length == file.Length && e.Sha256 == file.Sha256) ||
                    !old.Files.Any(e => e.Path == path && e.Kind == "file" && e.Ownership == "payload" && e.Length == file.Length && e.Sha256 == file.Sha256))
                    throw new IOException("Invalid cleanup receipt witness blocks rollback.");
                VerifyCleanupFile(SafePath(dir, file.RetainedRelativePath), file);
            }
        var previousDir = SafePath(operations, current.OperationId + "/previous");
        var target = ReadManifest(previousDir, true) ?? throw new IOException("No complete previous portable generation is retained. Install a supported version before attempting rollback.");
        var before = Inventory(root);
        // Schema1 is fixed-order UTF8 JSON, ordinal path/inventory ordering and
        // lowercase digests. Snapshot includes current declared data and metadata.
        var hash = Hash(JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, action = "RestorePreviousPortablePayload", installKey = key, root,
            currentManifestSha256 = FileHash(SafePath(root, ManifestFileName)),
            retainedManifestSha256 = FileHash(SafePath(previousDir, ManifestFileName)),
            contextSha256 = context,
            snapshot = snapshot.Select(r => new { path = r.Path, kind = r.Kind, length = r.Length, sha256 = r.Sha256, identity = r.Identity }).ToArray()
        }));
        return new(current, target, previousDir, before, snapshot, hash);
    }
}
