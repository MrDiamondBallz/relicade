using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relicade.Core.Services;

/// <summary>
/// Same-volume, journaled generation replacement. The two directory renames are NOT
/// an atomic pair. Files and Linux directory entries are flushed in order; other
/// platforms provide process-interruption recovery, not certified power-loss safety.
/// Unknown ownership, links and concurrent external changes require repair, never deletion.
/// </summary>
public sealed partial class PortableInstallationTransaction
{
    public const string ManifestFileName = ".relicade-portable.json";
    public const string DirectoryName = ".relicade-game-transactions";
    const string ModLock = ".relicade-mod-writer.lock";
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public sealed class Entry
    {
        [JsonRequired] public string Path { get; set; } = "";
        [JsonRequired] public string Kind { get; set; } = "file";
        [JsonRequired] public string Ownership { get; set; } = "payload";
        [JsonRequired] public string Sha256 { get; set; } = "";
        [JsonRequired] public long Length { get; set; }
    }
    public sealed class Manifest
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public string InstallKey { get; set; } = "";
        [JsonRequired] public string OperationId { get; set; } = "";
        [JsonRequired] public string SelectedVersion { get; set; } = "";
        [JsonRequired] public string SelectedExecutable { get; set; } = "";
        [JsonRequired] public string? ReleaseIdentity { get; set; }
        [JsonRequired] public string? AssetIdentity { get; set; }
        [JsonRequired] public string[] DataPaths { get; set; } = [];
        [JsonRequired] public List<Entry> Files { get; set; } = [];
    }
    public sealed class Journal
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public string OperationId { get; set; } = "";
        [JsonRequired] public string InstallKey { get; set; } = "";
        [JsonRequired] public string InstallName { get; set; } = "";
        [JsonRequired] public string Phase { get; set; } = "Prepared";
        [JsonRequired] public string? Resolution { get; set; }
        [JsonRequired] public string CandidatePath { get; set; } = "candidate";
        [JsonRequired] public string PreviousPath { get; set; } = "previous";
        [JsonRequired] public string BackupPath { get; set; } = "backup";
        [JsonRequired] public string? OldManifestHash { get; set; }
        [JsonRequired] public string CandidateManifestHash { get; set; } = "";
        [JsonRequired] public string? OldVersion { get; set; }
        [JsonRequired] public string NewVersion { get; set; } = "";
        [JsonRequired] public string? ReleaseIdentity { get; set; }
        [JsonRequired] public string? AssetIdentity { get; set; }
        [JsonRequired] public List<Entry> OldFiles { get; set; } = [];
        [JsonRequired] public List<Entry> CandidateFiles { get; set; } = [];
    }
    readonly string root, parent, key, operations;
    readonly GameInstallationOptions options;
    public PortableInstallationTransaction(string installRoot, GameInstallationOptions? options = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        parent = Path.GetDirectoryName(root) ?? throw new IOException("A volume root cannot be a portable installation.");
        CheckLinks(root); ValidateRelative(Path.GetFileName(root));
        key = Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root));
        this.options = options ?? GameInstallationOptions.Default;
        operations = SafePath(parent, DirectoryName + "/" + key);
    }
    public string OperationsPath => operations;
    public string InstallKey => key;
    public static bool HasEvidence(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var tx = new PortableInstallationTransaction(path);
        // Access denial is evidence uncertainty, not proof of an unmanaged root.
        return Optional(SafePath(tx.root, ManifestFileName)) != null || Directory.Exists(tx.operations) &&
            Directory.GetDirectories(tx.operations).Any(d => Optional(SafePath(d, "journal.json")) != null || Directory.Exists(SafePath(d, "previous")));
    }
    // The lock is outside the swappable generation and is never removed. FileShare.None
    // is an OS cross-process lock; no delete-and-recreate lock identity.
    public IDisposable AcquireLease()
    {
        CheckLinks(root); CheckLinks(operations);
        Directory.CreateDirectory(operations);
        var stream = new FileStream(SafePath(operations, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { Sync(operations); Sync(Path.GetDirectoryName(operations)!); Sync(parent); return stream; }
        catch { stream.Dispose(); throw; }
    }
    /// <summary>
    /// G05 must take the same stable, outside-generation lease BEFORE creating its
    /// root or taking its legacy in-generation lock. Refuse unresolved recovery;
    /// only the game recovery path may decide which complete generation is live.
    /// The returned lease spans the entire mod read/write/commit operation.
    /// </summary>
    public IDisposable AcquireModWriterLease()
    {
        var lease = AcquireLease();
        try
        {
            PortableInstallationLifetime.EnsureNotRunning(operations);
            foreach (var dir in Directory.GetDirectories(operations))
            {
                CheckLinks(dir);
                if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out _))
                    throw new InvalidDataException("Unknown portable recovery operation directory.");
                var journal = SafePath(dir, "journal.json");
                // Use actual reads, not File.Exists: access denial is not absence.
                if (Optional(journal) == null)
                {
                    if (Directory.Exists(SafePath(dir, "previous")))
                        throw new IOException("Portable recovery journal is missing; recover/repair before managing mods.");
                    continue; // pre-Prepared staging has never mutated live
                }
                var j = ReadJournal(dir);
                if (j.Phase != "Committed" && j.Resolution != "Old")
                    throw new IOException("Portable installation recovery is needed before managing mods. Evidence retained; reopen/repair the game installation first.");
            }
            var manifest = ReadManifest(root, allowDataChanges: true);
            ValidateCommittedIdentity(manifest);

            // G05 mutates payload/sidecars in-place. Until an explicit compatible
            // game/mod data policy exists, even a healthy managed generation must
            // refuse BEFORE G05 creates locks, staging, downloads or metadata in live.
            if (manifest != null)
                throw new IOException("Mods are not supported for managed portable installations yet. Use a separate unmanaged installation with an explicit mod/data policy.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    /// <summary>
    /// Loss-prevention boundary, NOT managed uninstall. Take the stable writer lock
    /// before inspecting live/history, without Recover or stale-receipt cleanup.
    /// Only writer.lock may exist in history; even pre-Prepared/resolved evidence
    /// needs explicit repair/retention policy before any removal can be safe.
    /// Caller must hold this lease through its complete unmanaged mutation.
    /// </summary>
    public IDisposable AcquireUnmanagedRemovalLease() => AcquireUnmanagedRemovalLeases([root]);
    /// <summary>
    /// Compose all effective roots for ONE removal. Canonical lock identities are
    /// held once within this explicitly owned scope, never borrowed from another
    /// operation or made ambient/reentrant. Every target is independently checked.
    /// </summary>
    public static IDisposable AcquireUnmanagedRemovalLeases(IEnumerable<string> installRoots)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var held = new Dictionary<string, IDisposable>(comparer);
        try
        {
            var roots = installRoots.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
                .Distinct(comparer).Order(StringComparer.Ordinal).ToArray();
            if (roots.Length == 0) throw new IOException("The removal folder is not configured.");
            // Deterministic target order, then outside-in ancestor discovery per
            // target. Locks fail immediately, never wait on another removal.
            foreach (var path in roots) new PortableInstallationTransaction(path).AcquireRemovalAuthority(held);
            return new RemovalAuthorityLeases(held.Values.ToList());
        }
        catch
        {
            foreach (var lease in held.Values.Reverse()) lease.Dispose();
            throw;
        }
    }
    /// <summary>
    /// Coordinate presentation-only library edits without recovering or changing
    /// installation data. Reject enclosing managed roots before creating child
    /// history. The root's own history is retained, including launch receipts.
    /// This lease does not authorize payload, setup, path or identity changes.
    /// </summary>
    public IDisposable AcquireMetadataEditLease()
    {
        var held = new Dictionary<string, IDisposable>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            AcquireRemovalAncestorLeases(held);
            HoldRemovalLease(held, SafePath(operations, "writer.lock"), AcquireLease);
            return new RemovalAuthorityLeases(held.Values.ToList());
        }
        catch
        {
            foreach (var lease in held.Values.Reverse()) lease.Dispose();
            throw;
        }
    }

    void AcquireRemovalAuthority(Dictionary<string, IDisposable> held)
    {
        // A selected/located directory can be current data inside another managed
        // root. Check ancestors BEFORE creating this target's child lock/history.
        AcquireRemovalAncestorLeases(held);
        HoldRemovalLease(held, SafePath(operations, "writer.lock"), AcquireLease);
        foreach (var entry in Directory.EnumerateFileSystemEntries(operations))
        {
            CheckLinks(entry);
            if (Path.GetFileName(entry) != "writer.lock" ||
                (File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                throw new IOException("Managed/recovery removal is not supported safely yet. Use Remove from Library to keep files, or explicitly repair the retained installation evidence.");
        }
        // In particular launch.json (live, uncertain OR stale) is refused above.
        // EnsureNotRunning would delete a stale receipt, which removal must not do.
        // File.GetAttributes/Enumerate propagate access denial, unlike Exists.
        void Inspect(string dir)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(dir); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
            CheckLinks(dir);
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException("The installation path is not a readable directory; repair before removal.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                CheckLinks(entry);
                var name = Path.GetFileName(entry);
                if (name.StartsWith(".relicade-", StringComparison.OrdinalIgnoreCase) &&
                    !(dir == root && name == ModLock))
                    throw new IOException("Managed/recovery removal is not supported safely yet. Use Remove from Library to keep files, or explicitly repair the retained installation evidence.");
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) Inspect(entry);
            }
        }
        Inspect(root);
        // Older mod writers may hold the in-generation lock alone. Do not create
        // it (or the root); retain an existing lock until the mutation finishes.
        var mod = SafePath(root, ModLock);
        try { HoldRemovalLease(held, mod, () => new FileStream(mod, FileMode.Open, FileAccess.ReadWrite, FileShare.None)); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    static void HoldRemovalLease(Dictionary<string, IDisposable> held, string identity, Func<IDisposable> acquire)
    {
        // Full paths plus platform case rules identify the SAME stable file; link
        // aliases are still rejected even when this operation already holds it.
        identity = Path.GetFullPath(identity);
        CheckLinks(identity);
        if (!held.ContainsKey(identity)) held.Add(identity, acquire());
    }
    const string RemovalRefusal = "Managed/recovery removal is not supported safely yet. Use Remove from Library to keep files, or explicitly repair the retained installation evidence.";
    // Read-only discovery: never create ancestor/child history to establish that
    // a selected folder lies inside a managed generation. A marker is authority
    // even when corrupt; missing markers cannot revoke retained history. Existing
    // ancestor writer identities are opened, not recreated, and held until the
    // entire allowed callback/metadata mutation completes. No Recover is used.
    void AcquireRemovalAncestorLeases(Dictionary<string, IDisposable> held, bool includeRoot = false)
    {
        var chain = new List<string>();
        for (string? path = root; path != null; path = Path.GetDirectoryName(path))
        {
            CheckLinks(path);
            if (Path.GetFileName(path).StartsWith(".relicade-", StringComparison.OrdinalIgnoreCase))
                throw new IOException(RemovalRefusal); // transaction/staging tree, not an install
            if (includeRoot || path != root) chain.Add(path);
        }
        foreach (var ancestor in chain.AsEnumerable().Reverse())
        {
            if (RemovalEntryExists(Path.Combine(ancestor, ManifestFileName)))
                throw new IOException(RemovalRefusal);
            var outside = Path.GetDirectoryName(ancestor);
            if (outside == null) continue; // a volume root cannot own a portable install
            var ancestorKey = Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? ancestor.ToUpperInvariant() : ancestor));
            var history = SafePath(outside, DirectoryName + "/" + ancestorKey);
            if (RemovalEntryExists(history))
            {
                // Missing/unknown/unreadable writer identity is uncertainty, not
                // authorization to manufacture a replacement inside a generation.
                var writer = SafePath(history, "writer.lock");
                HoldRemovalLease(held, writer, () => new FileStream(writer, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                foreach (var entry in Directory.EnumerateFileSystemEntries(history))
                {
                    CheckLinks(entry);
                    if (Path.GetFileName(entry) != "writer.lock" ||
                        (File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                        throw new IOException(RemovalRefusal);
                }
            }
            var mod = SafePath(ancestor, ModLock);
            if (RemovalEntryExists(mod))
                HoldRemovalLease(held, mod, () => new FileStream(mod, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        }
    }
    static bool RemovalEntryExists(string path)
    {
        CheckLinks(path);
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    sealed class RemovalAuthorityLeases(List<IDisposable> held) : IDisposable
    {
        public void Dispose() { foreach (var lease in held.AsEnumerable().Reverse()) lease.Dispose(); }
    }

    static FileStream AcquireModLease(string path)
    {
        Directory.CreateDirectory(path);
        return new FileStream(SafePath(path, ModLock), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Recover()
    {
        using var lease = AcquireLease();
        RecoverLocked();
        ValidateCommittedIdentity(ReadManifest(root, allowDataChanges: true));
    }
    public Manifest? ReadCommitted()
    {
        using var lease = AcquireLease(); RecoverLocked();
        var m = ReadManifest(root, allowDataChanges: true);
        ValidateCommittedIdentity(m); return m;
    }
    internal void ValidateCommittedIdentity(Manifest? m)
    {
        if (m == null)
        {
            // All callers hold the stable outside-generation lease. A missing live
            // manifest must not revoke durable ownership and turn an owned game into
            // an unmanaged launch/write path. Pre-Prepared staging and a resolved
            // failed FIRST install have never committed ownership and remain distinct.
            foreach (var operation in Directory.GetDirectories(operations))
            {
                CheckLinks(operation);
                if (!Guid.TryParseExact(Path.GetFileName(operation), "N", out _))
                    throw new InvalidDataException("Unknown portable recovery operation directory.");
                if (Optional(SafePath(operation, "journal.json")) == null)
                {
                    if (Directory.Exists(SafePath(operation, "previous")))
                        throw new IOException("Portable recovery journal is missing; generations retained for explicit repair.");
                    continue;
                }
                var journal = ReadJournal(operation);
                if (journal.Phase == "Committed" || journal.OldManifestHash != null)
                    throw new IOException("The owned portable manifest is missing; recovery/repair is required. Retained generations and journals must not be removed.");
            }
            return;
        }
        var dir = SafePath(operations, m.OperationId);
        if (!File.Exists(SafePath(dir, "journal.json"))) throw new IOException("The portable ownership commit is missing; explicit adoption/repair is required.");
        var j = ReadJournal(dir);
        if (j.Phase != "Committed" || j.Resolution != null || j.NewVersion != m.SelectedVersion ||
            j.ReleaseIdentity != m.ReleaseIdentity || j.AssetIdentity != m.AssetIdentity ||
            FileHash(SafePath(root, ManifestFileName)) != j.CandidateManifestHash)
            throw new IOException("Portable manifest does not match its committed journal; recovery/repair is required.");
        EqualEntries(Inventory(root).Where(e => Metadata(e.Path)).ToList(), j.CandidateFiles.Where(e => Metadata(e.Path)).ToList());
    }
    public async Task InstallAsync(Func<string, Task> stage, string version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Any(char.IsControl)) throw new InvalidDataException("A portable generation needs a nonempty selected version without control characters.");
        using var lease = AcquireLease(); PortableInstallationLifetime.EnsureNotRunning(operations); RecoverLocked();
        if (options.IsSharedFolder) throw new IOException("Shared or linked installations cannot be swapped. Choose a new empty managed folder; explicit adoption/repair is required.");
        cancellationToken.ThrowIfCancellationRequested();
        var existed = Directory.Exists(root);
        if (!existed) Directory.CreateDirectory(root);
        using var modLease = AcquireModLease(root);
        // G05 has independent selected-file ownership. No silent full-game/mod-profile
        // compatibility assumption: existing mod evidence excludes a game swap.
        RejectModEvidence(root);
        var old = ReadManifest(root, allowDataChanges: true);
        ValidateCommittedIdentity(old);
        if (old == null && Directory.EnumerateFileSystemEntries(root).Any(p => Path.GetFileName(p) != ModLock))
            throw new IOException("This existing folder has no trusted portable manifest. Explicit adoption/repair and a game-data policy are required; install to a new empty folder instead.");
        var oldInventory = Inventory(root, old?.DataPaths);
        if (old == null && oldInventory.Count != 0)
            throw new IOException("This existing folder has no trusted portable manifest. Explicit adoption/repair and a game-data policy are required; install to a new empty folder instead.");
        var dataPaths = old?.DataPaths ?? ValidateDataPaths(options.PreservedDataPaths);
        var id = Guid.NewGuid().ToString("N"); var dir = SafePath(operations, id);
        Directory.CreateDirectory(dir); Sync(operations);
        var candidate = SafePath(dir, "candidate"); Directory.CreateDirectory(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        await stage(candidate).ConfigureAwait(false);
        options.TransactionBoundary?.Invoke("Extracted");
        cancellationToken.ThrowIfCancellationRequested();
        options.PrepareCandidate?.Invoke(candidate);
        var payload = Inventory(candidate);
        foreach (var file in payload)
        {
            if (IsData(file.Path, dataPaths) || Reserved(file.Path))
                throw new InvalidDataException("Release payload collides with reserved metadata or declared user data.");
        }
        if (payload.Count == 0) throw new InvalidDataException("The staged payload is empty.");
        var executables = GameInstallationService.FindExecutableCandidates(candidate, SearchOption.AllDirectories, null, out _);
        var selected = options.SelectedExecutableRelativePath;
        if (selected == null && old != null && executables.Contains(SafePath(candidate, old.SelectedExecutable), StringComparer.Ordinal)) selected = old.SelectedExecutable;
        if (selected == null && old == null) selected = options.InitialExecutableRelativePath;
        if (selected != null)
        {
            ValidateRelative(selected);
            if (!executables.Contains(SafePath(candidate, selected), StringComparer.Ordinal)) throw new InvalidDataException("The selected executable is absent from the staged release.");
        }
        else if (executables.Count == 1) selected = Path.GetRelativePath(candidate, executables[0]).Replace('\\', '/');
        else if (executables.Count > 1 && options.ChooseExecutableAsync is { } choose)
        {
            var choices = Array.AsReadOnly(executables.Select(p => Path.GetRelativePath(candidate, p).Replace('\\', '/'))
                .Order(StringComparer.Ordinal).ToArray());
            try { selected = await choose(choices, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            { throw new PortableExecutableSelectionCancelledException(cancellationToken, ex); }
            if (cancellationToken.IsCancellationRequested || selected == null)
                throw new PortableExecutableSelectionCancelledException(cancellationToken);
            ValidateRelative(selected);
            if (!choices.Contains(selected, StringComparer.Ordinal))
                throw new InvalidDataException("The selected launch file was not one of the staged choices.");
            // A dialog may remain open while other software changes files. Never
            // silently accept a different staged payload after the displayed choice.
            AssertInventory(candidate, payload);
        }
        else if (executables.Count(e => Path.GetDirectoryName(e) == candidate) == 1)
            selected = Path.GetFileName(executables.Single(e => Path.GetDirectoryName(e) == candidate));
        else throw new InvalidDataException("The installation did not leave a launchable app with one selected target. Select one unambiguous executable before installing this release.");
        foreach (var file in oldInventory.Where(e => IsData(e.Path, dataPaths))) CopyEntry(root, candidate, file);
        var manifest = new Manifest { InstallKey = key, OperationId = id, SelectedVersion = version,
            SelectedExecutable = selected, DataPaths = dataPaths, ReleaseIdentity = options.ReleaseIdentity,
            AssetIdentity = options.AssetIdentity, Files = Inventory(candidate, dataPaths) };
        WriteCandidateMetadata(candidate, manifest, options.InstalledBuildText == null ? null : Encoding.UTF8.GetBytes(options.InstalledBuildText));
        FlushTree(candidate);
        options.TransactionBoundary?.Invoke("Staged");
        cancellationToken.ThrowIfCancellationRequested();
        // Revalidate after slow extraction/copy and immediately before Prepared. Unknown
        // files and changes to declared data are not silently lost to a stale snapshot.
        AssertInventory(root, oldInventory);
        Commit(dir, manifest, old, oldInventory, cancellationToken);
    }
    public void Rollback(CancellationToken cancellationToken = default)
    {
        using var lease = AcquireLease(); PortableInstallationLifetime.EnsureNotRunning(operations); RecoverLocked();
        using var modLease = AcquireModLease(root); RejectModEvidence(root);
        var current = ReadManifest(root, true) ?? throw new IOException("No managed generation to roll back.");
        ValidateCommittedIdentity(current);
        var previousDir = SafePath(operations, current.OperationId + "/previous");
        var prior = ReadManifest(previousDir, true) ?? throw new IOException("No complete previous generation is retained.");
        if (!current.DataPaths.SequenceEqual(prior.DataPaths, StringComparer.Ordinal)) throw new IOException("Data policy changed; explicit repair is required before rollback.");
        var before = Inventory(root);
        var id = Guid.NewGuid().ToString("N"); var dir = SafePath(operations, id); Directory.CreateDirectory(dir);
        var candidate = SafePath(dir, "candidate"); Directory.CreateDirectory(candidate);
        // Copy only validated immutable payload. Never restore the previous save snapshot.
        foreach (var e in prior.Files.Where(e => e.Ownership == "payload")) CopyEntry(previousDir, candidate, e);
        foreach (var e in before.Where(e => IsData(e.Path, current.DataPaths))) CopyEntry(root, candidate, e);
        prior.OperationId = id; prior.Files = Inventory(candidate, current.DataPaths);
        var build = Optional(SafePath(previousDir, "installed-build.txt"));
        WriteCandidateMetadata(candidate, prior, build);
        FlushTree(candidate); options.TransactionBoundary?.Invoke("Staged"); cancellationToken.ThrowIfCancellationRequested();
        AssertInventory(root, before);
        Commit(dir, prior, current, before, cancellationToken);
    }
    public void SelectExecutable(string relative, CancellationToken cancellationToken = default)
    {
        ValidateRelative(relative);
        using var lease = AcquireLease(); PortableInstallationLifetime.EnsureNotRunning(operations); RecoverLocked();
        using var modLease = AcquireModLease(root); RejectModEvidence(root);
        var current = ReadManifest(root, true) ?? throw new IOException("No managed generation to select an executable from.");
        ValidateCommittedIdentity(current);
        if (!current.Files.Any(e => e.Path == relative && e.Kind == "file" && e.Ownership == "payload"))
            throw new IOException("The selected target must belong to the current validated payload.");
        var platform = OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        if (!GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories, null, out _, platform).Contains(SafePath(root, relative), StringComparer.Ordinal))
            throw new IOException("The selected payload file is not a supported executable target.");
        if (current.SelectedExecutable == relative) return;
        var before = Inventory(root, current.DataPaths);
        var next = Parse<Manifest>(JsonSerializer.SerializeToUtf8Bytes(current, Json));
        var id = Guid.NewGuid().ToString("N"); var dir = SafePath(operations, id); Directory.CreateDirectory(dir);
        var candidate = SafePath(dir, "candidate"); Directory.CreateDirectory(candidate);
        foreach (var e in before.Where(e => !Metadata(e.Path))) CopyEntry(root, candidate, e);
        next.OperationId = id; next.SelectedExecutable = relative; next.Files = Inventory(candidate, current.DataPaths);
        WriteCandidateMetadata(candidate, next, Optional(SafePath(root, "installed-build.txt")));
        FlushTree(candidate); cancellationToken.ThrowIfCancellationRequested(); AssertInventory(root, before);
        Commit(dir, next, current, before, cancellationToken);
    }
    void WriteCandidateMetadata(string candidate, Manifest manifest, byte[]? build)
    {
        options.TransactionBoundary?.Invoke("BeforeVersionMetadata");
        WriteNew(SafePath(candidate, "version.txt"), Encoding.UTF8.GetBytes(manifest.SelectedVersion));
        options.TransactionBoundary?.Invoke("VersionMetadata");
        if (build != null)
        {
            options.TransactionBoundary?.Invoke("BeforeBuildMetadata");
            WriteNew(SafePath(candidate, "installed-build.txt"), build);
            options.TransactionBoundary?.Invoke("BuildMetadata");
        }
        options.TransactionBoundary?.Invoke("BeforeCandidateManifest");
        WriteNew(SafePath(candidate, ManifestFileName), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
        options.TransactionBoundary?.Invoke("CandidateManifest");
    }
    void Commit(string dir, Manifest candidateManifest, Manifest? old, List<Entry> before, CancellationToken token)
    {
        var candidate = SafePath(dir, "candidate");
        using var candidateModLease = AcquireModLease(candidate);
        var j = new Journal { OperationId = Path.GetFileName(dir), InstallKey = key, InstallName = Path.GetFileName(root),
            OldManifestHash = FileHash(SafePath(root, ManifestFileName)), CandidateManifestHash = FileHash(SafePath(candidate, ManifestFileName))!,
            OldVersion = old?.SelectedVersion, NewVersion = candidateManifest.SelectedVersion,
            ReleaseIdentity = candidateManifest.ReleaseIdentity, AssetIdentity = candidateManifest.AssetIdentity,
            OldFiles = before, CandidateFiles = Inventory(candidate, candidateManifest.DataPaths) };
        // The old complete generation is already durable and stays in place until the
        // Prepared+Applying records are flushed; its rename is the retained backup.
        var backup = SafePath(dir, "backup"); Directory.CreateDirectory(backup);
        if (new DriveInfo(parent).AvailableFreeSpace < before.Where(e => e.Kind == "file").Sum(e => e.Length))
            throw new IOException("Not enough space for a durable portable backup; the previous generation is unchanged.");
        foreach (var e in before) CopyEntry(root, backup, e);
        FlushTree(backup); AssertInventory(backup, before);
        options.TransactionBoundary?.Invoke("BackedUp");
        AssertInventory(root, before); FlushTree(root);
        options.TransactionBoundary?.Invoke("BeforePrepared"); WriteJournal(dir, j);
        try
        {
            options.TransactionBoundary?.Invoke("Prepared"); token.ThrowIfCancellationRequested();
            AssertInventory(root, before); AssertInventory(candidate, j.CandidateFiles);
            options.TransactionBoundary?.Invoke("BeforeApplying");
            j.Phase = "Applying"; WriteJournal(dir, j);
            // Commit is deliberately noninterruptible after this durable boundary.
            options.TransactionBoundary?.Invoke("Applying");
            options.TransactionBoundary?.Invoke("BeforePreviousRename");
            Directory.Move(root, SafePath(dir, "previous")); Sync(parent); Sync(dir);
            options.TransactionBoundary?.Invoke("PreviousRenamed");
            AssertInventory(SafePath(dir, "previous"), j.OldFiles);
            options.TransactionBoundary?.Invoke("BeforeCandidateRename");
            Directory.Move(candidate, root); Sync(dir); Sync(parent);
            options.TransactionBoundary?.Invoke("CandidateRenamed");
            AssertInventory(root, j.CandidateFiles);
            AssertInventory(SafePath(dir, "previous"), j.OldFiles);
            options.TransactionBoundary?.Invoke("BeforeCommitted");
            j.Phase = "Committed"; WriteJournal(dir, j);
            options.TransactionBoundary?.Invoke("Committed");
        }
        catch (Exception original)
        {
            // If commit actually reached disk, do not call it a cancelled/rolled-back install.
            if (ReadJournal(dir).Phase == "Committed")
                throw new IOException("The portable generation committed successfully, but completion reporting failed. Refresh local status before retrying.", original);
            try { options.TransactionBoundary?.Invoke("Recovering"); RecoverOne(dir, ReadJournal(dir), locksAlreadyHeld: true); }
            catch (Exception recovery) { throw new IOException("Portable installation recovery is needed. Both generations and the journal are retained; repair permissions or external changes and retry recovery.", new AggregateException(original, recovery)); }
            throw;
        }
    }
    internal void RecoverLocked()
    {
        if (!Directory.Exists(operations)) return;
        foreach (var dir in Directory.GetDirectories(operations).Order(StringComparer.Ordinal))
        {
            CheckLinks(dir);
            if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out _)) throw new InvalidDataException("Unknown portable recovery operation directory.");
            var path = SafePath(dir, "journal.json");
            if (Optional(path) == null)
            {
                if (Directory.Exists(SafePath(dir, "previous"))) throw new IOException("Portable recovery journal is missing; generations retained for explicit repair.");
                continue; // pre-Prepared staging: no live mutation, retained for explicit cleanup
            }
            var j = ReadJournal(dir);
            if (j.Phase == "Committed" || j.Resolution == "Old") continue;
            RecoverOne(dir, j);
        }
    }
    Journal ReadJournal(string dir)
    {
        var j = Parse<Journal>(Optional(SafePath(dir, "journal.json")) ?? throw new IOException("Portable recovery journal disappeared; evidence retained."));
        if (j.SchemaVersion != 1 || j.OperationId != Path.GetFileName(dir) || j.InstallKey != key || j.InstallName != Path.GetFileName(root) ||
            j.Phase is not ("Prepared" or "Applying" or "Committed") || j.Resolution is not (null or "Old") ||
            j.CandidatePath != "candidate" || j.PreviousPath != "previous" || j.BackupPath != "backup" || !ValidHash(j.CandidateManifestHash) ||
            j.OldManifestHash != null && !ValidHash(j.OldManifestHash) || string.IsNullOrWhiteSpace(j.NewVersion) ||
            (j.OldManifestHash == null) != (j.OldVersion == null) || j.Phase == "Committed" && j.Resolution != null)
            throw new InvalidDataException("Malformed, unsupported or cross-install portable recovery journal.");
        ValidateEntries(j.OldFiles); ValidateEntries(j.CandidateFiles);
        if (j.CandidateFiles.Count == 0 || j.CandidateFiles.SingleOrDefault(e => e.Path == ManifestFileName)?.Sha256 != j.CandidateManifestHash ||
            j.OldFiles.SingleOrDefault(e => e.Path == ManifestFileName)?.Sha256 != j.OldManifestHash)
            throw new InvalidDataException("Incomplete portable journal inventory.");
        return j;
    }
    void RecoverOne(string dir, Journal j, bool locksAlreadyHeld = false)
    {
        PortableInstallationLifetime.EnsureNotRunning(operations);
        var candidate = SafePath(dir, j.CandidatePath); var previous = SafePath(dir, j.PreviousPath);
        // A killed game writer released its in-generation G05 locks. Reopened recovery
        // must exclude mod writers on every occupied generation before validation/renames.
        FileStream? ExistingLease(string path) => locksAlreadyHeld || !Directory.Exists(path) ? null :
            new FileStream(SafePath(path, ModLock), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var liveModLease = ExistingLease(root);
        using var candidateModLease = ExistingLease(candidate);
        using var previousModLease = ExistingLease(previous);
        var backup = SafePath(dir, j.BackupPath);
        if (!Directory.Exists(backup)) throw new IOException("Portable recovery backup is missing; evidence retained.");
        AssertInventory(backup, j.OldFiles);
        if (ReadManifest(backup, true)?.SelectedVersion != j.OldVersion) throw new InvalidDataException("Portable journal old version is inconsistent.");
        // Determine the actual rename state using exact complete inventories, never names
        // alone. Every occupied location must be known BEFORE any recovery mutation.
        var liveExists = Directory.Exists(root); var candidateExists = Directory.Exists(candidate); var previousExists = Directory.Exists(previous);
        if (previousExists) AssertInventory(previous, j.OldFiles);
        if (candidateExists) AssertInventory(candidate, j.CandidateFiles);
        var stagedManifest = ReadManifest(candidateExists ? candidate : root, true);
        if (stagedManifest?.OperationId != j.OperationId || stagedManifest.SelectedVersion != j.NewVersion ||
            stagedManifest.ReleaseIdentity != j.ReleaseIdentity || stagedManifest.AssetIdentity != j.AssetIdentity)
            throw new InvalidDataException("Portable journal candidate identity is inconsistent.");
        if (liveExists) AssertInventory(root, previousExists ? j.CandidateFiles : j.OldFiles);
        if (!previousExists)
        {
            if (!liveExists || !candidateExists) throw new IOException("Portable recovery has missing or ambiguous generations.");
        }
        else
        {
            if (liveExists && candidateExists || !liveExists && !candidateExists) throw new IOException("Portable recovery has an ambiguous generation layout.");
            if (liveExists)
            {
                options.TransactionBoundary?.Invoke("BeforeRecoveryCandidateRename");
                Directory.Move(root, candidate); Sync(parent); Sync(dir);
                options.TransactionBoundary?.Invoke("RecoveryCandidateRenamed");
            }
            options.TransactionBoundary?.Invoke("BeforeRecoveryPreviousRename");
            Directory.Move(previous, root); Sync(dir); Sync(parent);
            options.TransactionBoundary?.Invoke("RecoveryPreviousRenamed");
        }
        AssertInventory(root, j.OldFiles);
        options.TransactionBoundary?.Invoke("BeforeRecoveryResolved");
        j.Resolution = "Old"; WriteJournal(dir, j);
        options.TransactionBoundary?.Invoke("RecoveryResolved");
    }
    internal Manifest? ReadManifest(string path, bool allowDataChanges)
    {
        var bytes = Optional(SafePath(path, ManifestFileName)); if (bytes == null) return null;
        var m = Parse<Manifest>(bytes);
        if (m.SchemaVersion != 1 || m.InstallKey != key || !Guid.TryParseExact(m.OperationId, "N", out _) || string.IsNullOrWhiteSpace(m.SelectedVersion))
            throw new InvalidDataException("Invalid portable ownership manifest; explicit repair is required.");
        ValidateRelative(m.SelectedExecutable); ValidateEntries(m.Files); ValidateDataPaths(m.DataPaths);
        if (m.Files.Count == 0 || !m.Files.Any(e => e.Path == m.SelectedExecutable && e.Ownership == "payload")) throw new InvalidDataException("Portable manifest has no selected payload target.");
        var actual = Inventory(path, m.DataPaths).Where(e => !Metadata(e.Path)).ToList();
        var payload = m.Files.Where(e => e.Ownership == "payload").ToList();
        foreach (var e in m.Files)
            if ((e.Ownership == "data") != IsData(e.Path, m.DataPaths) || Metadata(e.Path)) throw new InvalidDataException("Invalid manifest ownership policy.");
        EqualEntries(actual.Where(e => e.Ownership == "payload").ToList(), payload);
        if (!allowDataChanges) EqualEntries(actual, m.Files);
        if (Encoding.UTF8.GetString(Optional(SafePath(path, "version.txt")) ?? []) != m.SelectedVersion) throw new IOException("Portable version metadata is inconsistent; recovery/repair is required.");
        return m;
    }
    static bool Metadata(string p) => p is ManifestFileName or "version.txt" or "installed-build.txt";
    static bool Reserved(string p) => Metadata(p) || p == ModLock || p.StartsWith(".relicade-", StringComparison.OrdinalIgnoreCase) || p is "selected_executable.txt" or "install-incomplete.txt";
    static void RejectModEvidence(string path)
    {
        if (Directory.Exists(SafePath(path, ".relicade-mod-transactions")) || Directory.Exists(SafePath(path, ".relicade-mod-staging")) || File.Exists(SafePath(path, ".relicade-mods.json")))
            throw new IOException("This installation has mod transaction/profile state. Full-game replacement requires an explicit compatible mod/data policy; repair or use a separate managed installation.");
    }
    static string[] ValidateDataPaths(IEnumerable<string>? paths)
    {
        if (paths == null) throw new InvalidDataException("Missing portable data policy.");
        var list = paths.ToArray();
        foreach (var p in list) { ValidateRelative(p); if (Reserved(p)) throw new InvalidDataException("A reserved path cannot be user data."); }
        if (list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length || list.Any(p => list.Any(q => p != q && p.StartsWith(q + "/", StringComparison.OrdinalIgnoreCase)))) throw new InvalidDataException("Overlapping data policy paths.");
        return list;
    }
    static bool IsData(string p, string[] data) => data.Any(d => p == d || p.StartsWith(d + "/", StringComparison.Ordinal));
    // Reuse the existing regular-file/link-count guard before evidence readers
    // open a path. In particular, a FIFO open cannot be cancelled by a token.
    // This is not a sandbox against hostile concurrent path replacement.
    internal static void RequireRegularEvidenceFile(string path) => RequireCleanupKind(path, false);

    public static void CheckLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
        {
            try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked portable payload/recovery paths require explicit repair."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
    }
    public static void ValidateRelative(string p)
    {
        if (string.IsNullOrWhiteSpace(p) || p.Contains('\\') || p.Contains(':') || p.StartsWith('/') || p.Split('/').Any(c =>
            c.Length == 0 || c is "." or ".." || c.EndsWith('.') || c.EndsWith(' ') || c.Any(ch => ch < 32 || "<>\"|?*".Contains(ch)) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(c.Split('.')[0], StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid relative portable payload/recovery path.");
    }
    public static string SafePath(string basePath, string relative)
    {
        ValidateRelative(relative); var result = Path.GetFullPath(Path.Combine(basePath, relative)); CheckLinks(result); return result;
    }
    static List<Entry> Inventory(string path, string[]? data = null)
    {
        var result = new List<Entry>(); if (!Directory.Exists(path)) return result;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string dir)
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
            {
                CheckLinks(p); var relative = Path.GetRelativePath(path, p).Replace('\\', '/'); ValidateRelative(relative);
                if (!names.Add(relative)) throw new InvalidDataException("Case-colliding portable paths.");
                if (Directory.Exists(p))
                {
                    result.Add(new Entry { Path = relative, Kind = "directory", Sha256 = Hash([]), Length = 0, Ownership = data != null && IsData(relative, data) ? "data" : "payload" });
                    Walk(p); continue;
                }
                if (relative == ModLock) continue;
                using var f = File.OpenRead(p);
                result.Add(new Entry { Path = relative, Sha256 = Convert.ToHexStringLower(SHA256.HashData(f)), Length = f.Length, Ownership = data != null && IsData(relative, data) ? "data" : "payload" });
            }
        }
        Walk(path); return result.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
    }
    static void ValidateEntries(List<Entry> entries)
    {
        if (entries == null || entries.Any(e => e == null) || entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count) throw new InvalidDataException("Duplicate or missing portable inventory paths.");
        foreach (var e in entries) { ValidateRelative(e.Path); if (!ValidHash(e.Sha256) || e.Length < 0 || e.Kind is not ("file" or "directory") || e.Kind == "directory" && (e.Length != 0 || e.Sha256 != Hash([])) || e.Ownership is not ("payload" or "data")) throw new InvalidDataException("Invalid portable inventory."); }
    }
    static void AssertInventory(string path, List<Entry> expected) => EqualEntries(Inventory(path), expected);
    static void EqualEntries(List<Entry> actual, List<Entry> expected)
    {
        var lookup = expected.ToDictionary(e => e.Path, StringComparer.Ordinal);
        if (actual.Count != expected.Count || actual.Any(a => !lookup.TryGetValue(a.Path, out var e) || e.Kind != a.Kind || e.Sha256 != a.Sha256 || e.Length != a.Length))
            throw new IOException("Portable generation contains unknown, changed or missing bytes. Recovery evidence retained; explicit repair is required.");
    }
    static void CopyEntry(string source, string destination, Entry e)
    {
        var from = SafePath(source, e.Path); var to = SafePath(destination, e.Path); Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (e.Kind == "directory") { if (!Directory.Exists(from)) throw new IOException("Portable directory disappeared."); Directory.CreateDirectory(to); return; }
        if (FileHash(from) != e.Sha256) throw new IOException("Portable source changed while preparing a generation.");
        File.Copy(from, to, overwrite: false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(to, File.GetUnixFileMode(from));
        if (FileHash(to) != e.Sha256) throw new IOException("Portable copy verification failed.");
    }
    static T Parse<T>(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            void Unique(JsonElement e) { if (e.ValueKind == JsonValueKind.Object) { var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw new JsonException("Duplicate field"); Unique(p.Value); } } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Unique(item); }
            Unique(doc.RootElement);
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new JsonException("Null document");
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed portable recovery metadata; evidence retained.", ex); }
    }
    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static bool ValidHash(string s) => s != null && s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    static byte[]? Optional(string p)
    {
        CheckLinks(p);
        try
        {
            using var f = File.OpenRead(p);
            if (f.Length > 64L * 1024 * 1024) throw new InvalidDataException("Portable metadata exceeds its size budget; evidence retained.");
            using var memory = new MemoryStream(); f.CopyTo(memory); return memory.ToArray();
        }
        catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
    }
    static string? FileHash(string p)
    {
        CheckLinks(p);
        try { using var f = File.OpenRead(p); return Convert.ToHexStringLower(SHA256.HashData(f)); }
        catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
    }
    static void WriteNew(string path, byte[] bytes)
    {
        using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); f.Write(bytes); f.Flush(true);
    }
    static void WriteJournal(string dir, Journal j)
    {
        var temp = SafePath(dir, "journal-" + Guid.NewGuid().ToString("N") + ".tmp");
        WriteNew(temp, JsonSerializer.SerializeToUtf8Bytes(j, Json));
        File.Move(temp, SafePath(dir, "journal.json"), true); Sync(dir);
    }
    static void FlushTree(string path)
    {
        foreach (var e in Inventory(path).Where(e => e.Kind == "file")) { using var f = new FileStream(SafePath(path, e.Path), FileMode.Open, FileAccess.Read, FileShare.Read); f.Flush(true); }
        foreach (var d in Directory.GetDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length)) Sync(d);
        Sync(path);
    }
    [DllImport("libc", SetLastError = true)] static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] static extern int close(int fd);
    internal static void Sync(string dir)
    {
        if (!OperatingSystem.IsLinux()) return;
        var fd = open(dir, 0x10000); if (fd < 0) throw new IOException("Cannot open portable directory for durable synchronization.");
        try { if (fsync(fd) != 0) throw new IOException("Cannot synchronize portable directory."); } finally { close(fd); }
    }
}
