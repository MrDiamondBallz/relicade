using System.Security.Cryptography;

namespace Relicade.Core.Services;

internal sealed record PreservationRow(string Area, string Path, string Classification, long? ObservedBytes, string? Sha256, string Note);
internal sealed record PortablePreservationAssessment(string Root, string HistoryRoot, string OperationId, string Version,
    DateTimeOffset ObservedAt, IReadOnlyList<PreservationRow> Rows, bool IsPartial, IReadOnlyList<string> Warnings);
internal sealed record PreservationLimits(int Entries = 4096, long HashBytes = 1024L * 1024 * 1024, int MetadataBytes = 2 * 1024 * 1024);

public sealed partial class PortableInstallationTransaction
{
    // An observation, never a deletion plan. Nothing here creates locks, repairs
    // transactions, grants readiness, or treats Inventory's catch-all as ownership.
    internal async Task ObservePreservationAsync(Func<PortablePreservationAssessment, Task> publish,
        CancellationToken token = default, PreservationLimits? limits = null, Action? beforeFinalObservation = null)
    {
        ArgumentNullException.ThrowIfNull(publish);
        limits ??= new();
        if (limits.Entries is < 1 or > 4096 || limits.HashBytes is < 1 or > 1024L * 1024 * 1024 ||
            limits.MetadataBytes is < 1 or > 2 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(limits));
        token.ThrowIfCancellationRequested();
        using var authority = AcquirePreservationAuthority(token);
        var manifestPath = SafePath(root, ManifestFileName);
        var manifestBytes = ReadPreservationMetadata(manifestPath, limits.MetadataBytes, token);
        var manifest = Parse<Manifest>(manifestBytes);
        if (manifest.SchemaVersion != 1 || manifest.InstallKey != key ||
            !Guid.TryParseExact(manifest.OperationId, "N", out _) || manifest.OperationId != manifest.OperationId.ToLowerInvariant() ||
            string.IsNullOrWhiteSpace(manifest.SelectedVersion) || manifest.Files == null || manifest.Files.Count > 32768)
            throw new IOException("Unknown current ownership identity; preserve all files and inspect installation history.");
        ValidateEntries(manifest.Files); ValidateDataPaths(manifest.DataPaths); ValidateRelative(manifest.SelectedExecutable);
        if (!manifest.Files.Any(e => e.Path == manifest.SelectedExecutable && e.Kind == "file" && e.Ownership == "payload") ||
            manifest.Files.Any(e => Metadata(e.Path) || (e.Ownership == "data") != IsData(e.Path, manifest.DataPaths)))
            throw new IOException("Inconsistent current ownership policy; preserve all files.");
        var journalPath = SafePath(operations, manifest.OperationId + "/journal.json");
        var journalBytes = ReadPreservationMetadata(journalPath, limits.MetadataBytes, token);
        var journal = Parse<Journal>(journalBytes);
        ValidateEntries(journal.CandidateFiles); ValidateEntries(journal.OldFiles);
        if (journal.SchemaVersion != 1 || journal.OperationId != manifest.OperationId || journal.InstallKey != key ||
            journal.InstallName != Path.GetFileName(root) || journal.Phase != "Committed" || journal.Resolution != null ||
            journal.CandidatePath != "candidate" || journal.PreviousPath != "previous" || journal.BackupPath != "backup" ||
            journal.NewVersion != manifest.SelectedVersion || journal.ReleaseIdentity != manifest.ReleaseIdentity ||
            journal.AssetIdentity != manifest.AssetIdentity || journal.CandidateManifestHash != Hash(manifestBytes) ||
            journal.CandidateFiles.SingleOrDefault(e => e.Path == ManifestFileName)?.Sha256 != Hash(manifestBytes))
            throw new IOException("Current manifest is not bound to its exact committed journal; preserve all evidence.");

        var rows = new List<PreservationRow>(); var warnings = new List<string>();
        var filesObserved = new List<(string Absolute, string Identity, int Row)>();
        var directoriesObserved = new List<(string Absolute, string Identity, DateTime Written)>();
        var seen = new HashSet<string>(StringComparer.Ordinal); var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expected = manifest.Files.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var partial = false; var visited = 0; long hashed = 0;
        void Warn(string message) { if (!warnings.Contains(message)) warnings.Add(message); }
        void Walk(string start, string area)
        {
            var pending = new Stack<(string Absolute, string Relative, int Depth)>(); pending.Push((start, "", 0));
            while (pending.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var (absolute, relative, depth) = pending.Pop();
                if (++visited > limits.Entries || depth > 64) { partial = true; Warn("Partial observation: entry/depth safety limit reached; unobserved files remain preserved and uncounted."); return; }
                if (relative.Length != 0) ValidateRelative(relative);
                if (!aliases.Add(area + "/" + relative)) throw new IOException("Case-aliased paths require inspection; nothing was changed.");
                var attributes = File.GetAttributes(absolute); var directory = (attributes & FileAttributes.Directory) != 0;
                RequireCleanupKind(absolute, directory); // refuses links, hardlinks and special files before opening
                if (directory)
                {
                    directoriesObserved.Add((absolute, CleanupIdentity(absolute, true), Directory.GetLastWriteTimeUtc(absolute)));
                    if (area == "history" && depth == 1)
                    {
                        if (!Guid.TryParseExact(relative, "N", out _)) Warn("Unknown history directory is preserved and requires inspection: " + relative);
                        else
                        {
                            var retainedJournal = SafePath(absolute, "journal.json");
                            if (!RemovalEntryExists(retainedJournal)) Warn("Pending or unknown history without a journal is preserved: " + relative);
                            else
                            {
                                var retained = Parse<Journal>(ReadPreservationMetadata(retainedJournal, limits.MetadataBytes, token));
                                if (retained.Phase != "Committed" || retained.Resolution != null || retained.InstallKey != key || retained.OperationId != relative)
                                    Warn("Pending or uncertain history is preserved and needs inspection: " + relative);
                            }
                        }
                    }
                    // Bound enumeration itself; never Order/ToArray an unbounded directory.
                    using var children = Directory.EnumerateFileSystemEntries(absolute).GetEnumerator();
                    while (children.MoveNext())
                    {
                        token.ThrowIfCancellationRequested();
                        if (visited + pending.Count >= limits.Entries) { partial = true; Warn("Partial observation: directory entry safety limit reached; totals cover observed files only."); break; }
                        var child = children.Current; var name = Path.GetFileName(child); ValidateRelative(name);
                        pending.Push((child, relative.Length == 0 ? name : relative + "/" + name, depth + 1));
                    }
                    continue;
                }
                var before = CleanupIdentity(absolute, false); var length = new FileInfo(absolute).Length;
                string classification, note = "Preserved. This observation authorizes no removal."; string? digest = null;
                if (area == "history")
                {
                    var top = relative.Split('/')[0];
                    classification = top == "writer.lock" || Guid.TryParseExact(top, "N", out _) ? "Preserved transaction history" : "Unknown history — preserve / inspect";
                }
                else if (Metadata(relative) || relative == ModLock) classification = "Preserved launcher metadata";
                else if (IsData(relative, manifest.DataPaths)) classification = "Preserved current declared data";
                else if (expected.TryGetValue(relative, out var entry) && entry.Kind == "file" && entry.Ownership == "payload")
                {
                    seen.Add(relative);
                    classification = "Changed listed file — preserve / inspect";
                    if (length == entry.Length && length <= limits.HashBytes - hashed)
                    {
                        digest = HashPreservationFile(absolute, length, token); hashed = checked(hashed + length);
                        if (digest == entry.Sha256) classification = "Unchanged listed payload";
                    }
                    else if (length == entry.Length)
                    { classification = "Unverified listed file — preserve / inspect"; partial = true; note = "Hash safety budget reached; ownership match is unverified. Preserve and inspect."; }
                }
                else classification = "Unlisted file — preserve / inspect";
                if (before != CleanupIdentity(absolute, false))
                { partial = true; classification = "Changing file — preserve / inspect"; digest = null; length = -1; Warn("A file changed during observation; its size/hash is uncertain."); }
                filesObserved.Add((absolute, before, rows.Count));
                rows.Add(new(area, relative, classification, length < 0 ? null : length, digest, note));
            }
        }
        Walk(root, "installation");
        if (!partial)
            foreach (var entry in manifest.Files.Where(e => e.Kind == "file" && e.Ownership == "payload" && !seen.Contains(e.Path)))
                rows.Add(new("installation", entry.Path, "Listed file not observed — preserve / inspect", null, null, "No deletion or absence inference for other files."));
        if (visited < limits.Entries) Walk(operations, "history");
        else { partial = true; Warn("Transaction history is not fully observed; it is preserved and uncounted."); }
        // History is inventoried, not certified reusable or recoverable. Unknown and
        // pending names are deliberately retained, not interpreted as cleanup permission.
        Warn("All transaction history is preserved; this assessment does not validate rollback targets or resolve pending/unknown records.");
        Warn("External saves, originals, mods and canonical owned-data copies outside this installation/history are not inventoried. Their absence is not implied.");
        beforeFinalObservation?.Invoke(); // Internal deterministic observation seam; default does nothing.
        // A cooperative generation lease does not freeze external writers. Recheck
        // every bounded observed identity before publication; additions/removals in a
        // directory make the list partial, never a complete deletion inventory.
        foreach (var file in filesObserved)
        {
            token.ThrowIfCancellationRequested(); bool same;
            try { RequireCleanupKind(file.Absolute, false); same = CleanupIdentity(file.Absolute, false) == file.Identity; }
            catch (IOException) { same = false; }
            catch (UnauthorizedAccessException) { same = false; }
            if (!same)
            {
                partial = true;
                rows[file.Row] = rows[file.Row] with { Classification = "Changing file — preserve / inspect", Sha256 = null, ObservedBytes = null,
                    Note = "Identity changed or became unreadable before publication. Preserve and inspect; previous bytes/hash are not current." };
                Warn("Files changed during the bounded observation; changed identities have unknown sizes/hashes.");
            }
        }
        foreach (var directory in directoriesObserved)
        {
            token.ThrowIfCancellationRequested(); bool same;
            try { RequireCleanupKind(directory.Absolute, true); same = CleanupIdentity(directory.Absolute, true) == directory.Identity && Directory.GetLastWriteTimeUtc(directory.Absolute) == directory.Written; }
            catch (IOException) { same = false; }
            catch (UnauthorizedAccessException) { same = false; }
            if (!same) { partial = true; Warn("Directory contents changed or became unreadable during inspection; added/removed paths are not fully inventoried. All remain preserved."); }
        }
        if (!ReadPreservationMetadata(manifestPath, limits.MetadataBytes, token).AsSpan().SequenceEqual(manifestBytes) ||
            !ReadPreservationMetadata(journalPath, limits.MetadataBytes, token).AsSpan().SequenceEqual(journalBytes))
            throw new IOException("The committed generation changed during inspection. Reopen inspection; no result was published.");
        token.ThrowIfCancellationRequested();
        var result = new PortablePreservationAssessment(root, operations, manifest.OperationId, manifest.SelectedVersion,
            DateTimeOffset.UtcNow, Array.AsReadOnly(rows.OrderBy(r => r.Area, StringComparer.Ordinal).ThenBy(r => r.Path, StringComparer.Ordinal).ToArray()),
            partial, Array.AsReadOnly(warnings.ToArray()));
        // Cancellation never detaches a queued callback from held authority.
        await publish(result).ConfigureAwait(false);
    }

    IDisposable AcquirePreservationAuthority(CancellationToken token)
    {
        if (options.IsSharedFolder) throw new IOException("Shared installation ownership requires separate inspection.");
        var held = new Dictionary<string, IDisposable>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            // Bound sibling alias inspection before using existing ancestor authority.
            for (string? current = root; current != null; current = Path.GetDirectoryName(current))
            {
                token.ThrowIfCancellationRequested(); CheckLinks(current);
                var outside = Path.GetDirectoryName(current);
                if (outside != null)
                {
                    var historyParent = SafePath(outside, DirectoryName);
                    if (RemovalEntryExists(historyParent))
                    {
                        RequireCleanupKind(historyParent, true); var count = 0;
                        var identity = Hash(System.Text.Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? current.ToUpperInvariant() : current));
                        foreach (var item in Directory.EnumerateFileSystemEntries(historyParent))
                        {
                            token.ThrowIfCancellationRequested();
                            if (++count > 4096) throw new IOException("Authority directory exceeds inspection safety limit; no files changed.");
                            if (string.Equals(Path.GetFileName(item), identity, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(item) != identity)
                                throw new IOException("Case-aliased installation authority requires inspection.");
                        }
                        var history = SafePath(historyParent, identity);
                        if (RemovalEntryExists(history))
                        { RequireCleanupKind(history, true); var writer = SafePath(history, "writer.lock"); RequireCleanupKind(writer, false); if (new FileInfo(writer).Length != 0) throw new IOException("Unknown writer evidence."); }
                    }
                }
                var mod = SafePath(current, ModLock);
                if (RemovalEntryExists(mod)) { RequireCleanupKind(mod, false); if (new FileInfo(mod).Length != 0) throw new IOException("Unknown mod writer evidence."); }
            }
            AcquireRemovalAncestorLeases(held);
            RequireCleanupKind(root, true); RequireCleanupKind(operations, true);
            var writerPath = SafePath(operations, "writer.lock"); RequireCleanupKind(writerPath, false);
            HoldRemovalLease(held, writerPath, () => new FileStream(writerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            var modPath = SafePath(root, ModLock);
            if (RemovalEntryExists(modPath)) HoldRemovalLease(held, modPath, () => new FileStream(modPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            RejectModEvidence(root);
            return new RemovalAuthorityLeases(held.Values.ToList());
        }
        catch { foreach (var lease in held.Values.Reverse()) lease.Dispose(); throw; }
    }

    static byte[] ReadPreservationMetadata(string path, int maximum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireCleanupKind(path, false);
        var before = CleanupIdentity(path, false);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw new IOException("Ownership metadata exceeds the inspection safety limit; preserve and inspect it separately.");
        using var output = new MemoryStream(); var buffer = new byte[16384];
        int count; while ((count = input.Read(buffer)) != 0)
        { token.ThrowIfCancellationRequested(); if (output.Length + count > maximum) throw new IOException("Growing ownership metadata is uncertain."); output.Write(buffer, 0, count); }
        if (before != CleanupIdentity(path, false)) throw new IOException("Ownership metadata changed while reading.");
        return output.ToArray();
    }

    static string HashPreservationFile(string path, long length, CancellationToken token)
    {
        RequireCleanupKind(path, false);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; long read = 0;
        int count; while ((count = input.Read(buffer)) != 0)
        { token.ThrowIfCancellationRequested(); read = checked(read + count); if (read > length) throw new IOException("File grew during observation; preserve and inspect."); hash.AppendData(buffer, 0, count); }
        if (read != length) throw new IOException("File length changed during observation; preserve and inspect.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
