using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Relicade.Core.Services;

// Definitions are compiled, reviewed adapters, never instructions from catalog metadata.
internal enum OwnedDataPlatform { LinuxX64, WindowsX64 }
internal sealed record OwnedDataRequirement(string Id, string Label, string ReleaseIdentity,
    string AssetIdentity, string ExecutableSha256, long Length, string Md5,
    string FileName = "game-data.z64", string? ExpectedSha256 = null, OwnedNesBodyRule? NesBody = null,
    OwnedDataPlatform Platform = OwnedDataPlatform.LinuxX64)
{
    internal long? ExecutableLength { get; init; }
    internal bool AcceptsLength(long length) => length == Length || NesBody != null && length == checked(Length + 16);
    internal long MaximumLength => NesBody == null ? Length : checked(Length + 16);
}
internal sealed record OwnedNesBodyRule(string Sha1, uint Crc32);
internal sealed record OwnedDataPreview(string PlanSha256, string Source, string Destination,
    string OperationId, string SourceSha256, long BytesToCopy, bool ReuseExisting, OwnedDataRepair? Repair = null, long SourceLength = 0);
internal sealed record OwnedDataRepair(string RetainedDestination, long PreviousLength, string PreviousSha256, bool ReuseRetained);
internal sealed class OwnedDataNeedsRepairException(string message) : IOException(message);
internal sealed record OwnedDataImportResult(string Destination, string Sha256, bool Completed);

public sealed partial class PortableInstallationTransaction
{
    sealed record OwnedDataReceipt(int SchemaVersion, string RequirementId, string InstallKey,
        string OperationId, string AssetIdentity, string ExecutableSha256, string Sha256, long Length);

    // Reuse the existing nonrecovering, regular-file/history inspection. No cleanup
    // plan is applied and no game payload, receipt or retained generation is changed.
    internal OwnedDataPreview PreviewOwnedData(OwnedDataRequirement requirement, string dataRoot, string source,
        CancellationToken token = default)
    {
        using var authority = AcquireRollbackAuthority(null);
        var manifest = InspectCleanup().Current;
        using var dataAuthority = AcquireOwnedDataAuthority(requirement, dataRoot);
        return InspectOwnedData(requirement, dataRoot, source, manifest, token);
    }

    internal OwnedDataImportResult ImportOwnedData(OwnedDataRequirement requirement, string dataRoot,
        OwnedDataPreview expected, CancellationToken token = default, Action<string>? boundary = null)
    {
        token.ThrowIfCancellationRequested();
        using var authority = AcquireRollbackAuthority(null);
        var manifest = InspectCleanup().Current;
        OwnedDataPreview current = expected;
        using var dataAuthority = AcquireOwnedDataAuthority(requirement, dataRoot, createWriter: true, inspectBeforeWriter: () =>
        {
            // Reject changed input/approval before creating even writer history.
            // Existing destination/ancestor authority remains held during inspection.
            current = InspectOwnedData(requirement, dataRoot, expected.Source, manifest, token);
            if (current != expected) throw new IOException("Game setup changed. Review a fresh import preview before confirming.");
        });
        var folder = Path.GetDirectoryName(current.Destination)!;
        CheckLinks(folder);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(folder);
        else Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RequireCleanupKind(folder, true);
        var receipt = SafePath(folder, manifest.OperationId + ".json");
        var record = new OwnedDataReceipt(1, requirement.Id, key, manifest.OperationId,
            requirement.AssetIdentity, requirement.ExecutableSha256, current.SourceSha256, current.SourceLength);
        var recordBytes = JsonSerializer.SerializeToUtf8Bytes(record, Json);
        // An existing receipt must agree byte-for-byte. No silent overwrite or
        // deletion, even if a previous import was interrupted after copying data.
        if (RemovalEntryExists(receipt))
        {
            if (!ReadOwnedReceiptBytes(receipt).SequenceEqual(recordBytes))
                throw new IOException("Existing setup evidence conflicts. Keep both files and resolve the conflict before importing.");
        }
        string? staging = null;
        try
        {
            if (!current.ReuseExisting)
            {
                var temporaryCopy = SafePath(folder, "import-" + Guid.NewGuid().ToString("N") + ".tmp");
                RequireCleanupKind(current.Source, false);
                using (var input = new FileStream(current.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = CreateOwnedDataFile(temporaryCopy))
                {
                    staging = temporaryCopy; // Ownership begins only after create-new succeeds.
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[64 * 1024]; long copied = 0;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        var count = input.Read(buffer); if (count == 0) break;
                        copied = checked(copied + count);
                        if (copied > current.SourceLength) throw new IOException("The selected file changed during import.");
                        digest.AppendData(buffer, 0, count); output.Write(buffer, 0, count);
                        boundary?.Invoke("Copying");
                    }
                    if (copied != current.SourceLength || Convert.ToHexStringLower(digest.GetHashAndReset()) != current.SourceSha256)
                        throw new IOException("The selected file changed during import. Its original was not modified.");
                    output.Flush(true);
                }
                token.ThrowIfCancellationRequested(); boundary?.Invoke("BeforeCommit");
                // The install writer lease is still held. Check the exact generation
                // again without recovery before making the data copy visible.
                var actual = ReadManifest(root, true) ?? throw new IOException("Installation disappeared during setup.");
                ValidateCommittedIdentity(actual);
                if (actual.OperationId != manifest.OperationId) throw new IOException("Installation changed during setup.");
                if (current.Repair is { } repair)
                {
                    PreserveChangedOwnedData(current, requirement, token, boundary);
                    boundary?.Invoke("BeforeRepairCommit");
                    // Recheck the exact confirmed bytes and canonical receipt after
                    // preservation. The backup is durable before the atomic replace.
                    RequireRepairReceipt(requirement, manifest, receipt, current.SourceSha256);
                    if (ObserveChangedOwnedData(current.Destination, requirement, token) != (repair.PreviousLength, repair.PreviousSha256) ||
                        ObserveChangedOwnedData(repair.RetainedDestination, requirement, token) != (repair.PreviousLength, repair.PreviousSha256))
                        throw new IOException("The confirmed data or retained copy changed. Review a fresh replacement preview.");
                }
                CheckLinks(current.Destination);
                token.ThrowIfCancellationRequested();
                File.Move(staging, current.Destination, current.Repair != null); staging = null; Sync(folder);
                boundary?.Invoke("DataCommitted");
            }
            // Once data exists, cancellation leaves it retained, never falsely
            // reports that nothing changed, and requires a fresh preview to adopt.
            boundary?.Invoke("BeforeReceipt");
            token.ThrowIfCancellationRequested();
            if (!RemovalEntryExists(receipt))
            {
                var temporaryReceipt = SafePath(folder, "receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = CreateOwnedDataFile(temporaryReceipt))
                { staging = temporaryReceipt; output.Write(recordBytes); output.Flush(true); }
                File.Move(temporaryReceipt, receipt, false); staging = null; Sync(folder);
            }
            ValidateOwnedData(requirement, dataRoot, manifest, token);
            return new(current.Destination, current.SourceSha256, true);
        }
        catch (Exception ex) when (current.Repair != null && ex is not OperationCanceledException)
        {
            throw new IOException("Replacement did not finish normally. The selected original and any retained copy were preserved. " +
                "A replacement may already be committed; reopen setup before retrying. Retained-copy destination: " + current.Repair.RetainedDestination, ex);
        }
        finally
        {
            // Only this operation's uncommitted staging file is disposable. The
            // source, committed copy and all previous setup evidence are retained.
            if (staging != null && RemovalEntryExists(staging)) { File.Delete(staging); Sync(folder); }
        }
    }

    OwnedDataPreview InspectOwnedData(OwnedDataRequirement requirement, string dataRoot, string source,
        Manifest manifest, CancellationToken token)
    {
        ValidateOwnedBinding(requirement, manifest);
        source = Path.GetFullPath(source);
        var destination = OwnedDataDestination(requirement, dataRoot);
        if (string.Equals(source, destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Choose the original file, not Relicade's retained setup copy.");
        var hash = HashOwnedInput(source, requirement, token, out var sourceLength);
        var reuse = RemovalEntryExists(destination);
        OwnedDataRepair? repair = null;
        if (reuse)
        {
            string existingHash;
            try { existingHash = HashOwnedInput(destination, requirement, token, out _); }
            catch (IOException)
            {
                // Changed data is not ownership evidence. A canonical receipt for
                // this exact generation must also match the verified replacement.
                var receipt = SafePath(Path.GetDirectoryName(destination)!, manifest.OperationId + ".json");
                RequireRepairReceipt(requirement, manifest, receipt, hash);
                var old = ObserveChangedOwnedData(destination, requirement, token);
                var retained = SafePath(Path.GetDirectoryName(destination)!, "retained-" + old.Sha256 + "-" + requirement.FileName);
                var reuseRetained = RemovalEntryExists(retained);
                if (reuseRetained && ObserveChangedOwnedData(retained, requirement, token) != old)
                    throw new IOException("An existing retained-copy destination conflicts. Every file was preserved.");
                repair = new(retained, old.Length, old.Sha256, reuseRetained);
                existingHash = "";
            }
            if (repair == null && existingHash != hash)
                throw new IOException("An existing valid setup copy differs. It was preserved; replacement is not authorized.");
        }
        var bytes = repair == null ? (reuse ? 0 : sourceLength)
            : checked(sourceLength + (repair.ReuseRetained ? 0 : repair.PreviousLength));
        if (bytes > 0)
        {
            var existing = new DirectoryInfo(Path.GetDirectoryName(destination)!);
            while (!existing.Exists) existing = existing.Parent ?? throw new IOException("No storage location is available for setup.");
            if (new DriveInfo(existing.FullName).AvailableFreeSpace < checked(bytes + 4096))
                throw new IOException("Not enough available space for the verified copy, any retained old data and setup receipt.");
        }
        var plan = Hash(JsonSerializer.SerializeToUtf8Bytes(new { action = repair == null ? "CopyOwnedGameData" : "PreserveAndReplaceOwnedGameData", requirement.Id,
            root, key, manifest.OperationId, requirement.AssetIdentity, requirement.ExecutableSha256, source, destination, hash, sourceLength, reuse, repair }));
        return new(plan, source, destination, manifest.OperationId, hash, bytes, reuse && repair == null, repair, sourceLength);
    }

    internal string ReadOwnedData(OwnedDataRequirement requirement, string dataRoot, CancellationToken token = default)
    {
        using var authority = AcquireRollbackAuthority(null);
        var manifest = InspectCleanup().Current;
        using var dataAuthority = AcquireOwnedDataAuthority(requirement, dataRoot);
        return ValidateOwnedData(requirement, dataRoot, manifest, token);
    }

    internal string ValidateOwnedData(OwnedDataRequirement requirement, string dataRoot, Manifest manifest,
        CancellationToken token = default)
    {
        ValidateOwnedBinding(requirement, manifest);
        var destination = OwnedDataDestination(requirement, dataRoot);
        var receipt = SafePath(Path.GetDirectoryName(destination)!, manifest.OperationId + ".json");
        var record = JsonSerializer.Deserialize<OwnedDataReceipt>(ReadOwnedReceiptBytes(receipt), Json)
            ?? throw new IOException("Missing setup evidence.");
        string hash; long sourceLength;
        try { hash = HashOwnedInput(destination, requirement, token, out sourceLength); }
        catch (IOException)
        {
            RequireRepairReceipt(requirement, manifest, receipt, requirement.ExpectedSha256);
            var observed = ObserveChangedOwnedData(destination, requirement, token);
            if (observed.Sha256 != record.Sha256)
                throw new OwnedDataNeedsRepairException("Recorded setup data no longer verifies. It is retained. Choose a verified original to preview preservation and replacement; nothing changes until you confirm.");
            throw;
        }
        if (record != new OwnedDataReceipt(1, requirement.Id, key, manifest.OperationId, requirement.AssetIdentity,
                requirement.ExecutableSha256, hash, sourceLength))
            throw new IOException("Setup evidence does not match this installation. Review game setup again.");
        return destination;
    }

    IDisposable AcquireOwnedDataAuthority(OwnedDataRequirement requirement, string dataRoot, bool createWriter = false, Action? inspectBeforeWriter = null, bool requireExistingWriter = false)
    {
        var folder = Path.GetDirectoryName(OwnedDataDestination(requirement, dataRoot))!;
        var held = new Dictionary<string, IDisposable>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            // Inspect the exact destination as well as its ancestors before any
            // creation. An unrelated managed root or initial writer is authority,
            // even when no payload manifest has been committed yet.
            var target = new PortableInstallationTransaction(folder);
            target.AcquireRemovalAncestorLeases(held, includeRoot: true);
            if (requireExistingWriter)
            {
                var writer = SafePath(target.operations, "writer.lock");
                RequireCleanupKind(writer, false);
                if (new FileInfo(writer).Length != 0) throw new IOException("Retained-data writer identity is uncertain.");
                HoldRemovalLease(held, writer, () => new FileStream(writer, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            }
            // Preview/readiness never create history. Confirmed import additionally
            // holds the destination's stable writer lease through copy and receipt.
            // The shared lease map avoids opening an already-held writer twice.
            if (createWriter) { inspectBeforeWriter?.Invoke(); target.AcquireRemovalAuthority(held); }
            return new RemovalAuthorityLeases(held.Values.ToList());
        }
        catch { foreach (var lease in held.Values.Reverse()) lease.Dispose(); throw; }
    }

    string OwnedDataDestination(OwnedDataRequirement requirement, string dataRoot)
    {
        ValidateRelative(requirement.Id);
        if (requirement.Id.Contains('/')) throw new IOException("Invalid setup adapter identity.");
        ValidateRelative(requirement.FileName);
        if (requirement.FileName.Contains('/')) throw new IOException("Invalid setup data filename.");
        dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        CheckLinks(dataRoot);
        var destination = SafePath(dataRoot, "owned-game-data/" + key + "/" + requirement.Id + "/" + requirement.FileName);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (destination.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
            root.StartsWith(Path.Combine(dataRoot, "owned-game-data") + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Owned game data must be separate from game installations.");
        return destination;
    }

    void ValidateOwnedBinding(OwnedDataRequirement requirement, Manifest manifest)
    {
        var platformMatches = requirement.Platform switch
        {
            OwnedDataPlatform.LinuxX64 => OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid(),
            OwnedDataPlatform.WindowsX64 => OperatingSystem.IsWindows(),
            _ => false
        };
        if (!platformMatches || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IOException("This setup adapter requires its exact compiled package and matching x64 host.");
        if (manifest.InstallKey != key || manifest.ReleaseIdentity != requirement.ReleaseIdentity || manifest.AssetIdentity != requirement.AssetIdentity ||
            !manifest.Files.Any(f => f.Path == manifest.SelectedExecutable && f.Kind == "file" && f.Ownership == "payload" && f.Sha256 == requirement.ExecutableSha256))
            throw new IOException("Requirements are not confirmed for this package or selected executable.");
        RequireCleanupKind(SafePath(root, manifest.SelectedExecutable), false);
        if (FileHash(SafePath(root, manifest.SelectedExecutable)) != requirement.ExecutableSha256)
            throw new IOException("The selected executable changed. Repair the installation before setup or launch.");
    }

    static FileStream CreateOwnedDataFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new(path, options);
    }

    static string HashOwnedInput(string path, OwnedDataRequirement requirement, CancellationToken token, out long length)
    {
        RequireCleanupKind(path, false);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        length = file.Length;
        if (!requirement.AcceptsLength(length)) throw new IOException($"Expected {requirement.Label}. The selected file has an unsupported length and was not changed.");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var variant = IncrementalHash.CreateHash(requirement.NesBody == null ? HashAlgorithmName.MD5 : HashAlgorithmName.SHA1);
        uint crc = uint.MaxValue;
        var skip = requirement.NesBody != null && length == requirement.Length + 16 ? 16 : 0;
        if (requirement.NesBody != null)
        {
            Span<byte> magic = stackalloc byte[4]; file.ReadExactly(magic); file.Position = 0;
            // Match the upstream magic-based header decision, not an arbitrary
            // sixteen-byte skip. A header-shaped body alone is not a bare ROM.
            if (magic.SequenceEqual("NES\u001a"u8) != (skip == 16))
                throw new IOException("Expected a headerless body or a complete 16-byte iNES header beginning NES followed by 0x1a. The original was preserved.");
        }
        var buffer = new byte[64 * 1024]; long read = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = file.Read(buffer); if (count == 0) break;
            var offset = (int)Math.Clamp(skip - read, 0, count);
            read = checked(read + count); if (read > length) throw new IOException("Input changed while validating.");
            sha.AppendData(buffer, 0, count); variant.AppendData(buffer, offset, count - offset);
            if (requirement.NesBody != null)
                foreach (var value in buffer.AsSpan(offset, count - offset))
                {
                    crc ^= value;
                    for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
                }
        }
        var expectedVariant = requirement.NesBody?.Sha1 ?? requirement.Md5;
        if (read != length || Convert.ToHexStringLower(variant.GetHashAndReset()) != expectedVariant ||
            requirement.NesBody is { } body && (crc ^ uint.MaxValue) != body.Crc32)
            throw new IOException($"This is not the supported {requirement.Label}. A hash identifies a variant, not ownership or legal entitlement. The original was preserved.");
        var digest = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (requirement.ExpectedSha256 is { } expected && digest != expected)
            throw new IOException("The selected data does not match this adapter's pinned SHA-256. The original was preserved.");
        return digest;
    }
}
