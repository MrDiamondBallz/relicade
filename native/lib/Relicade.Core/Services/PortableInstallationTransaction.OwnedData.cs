using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Relicade.Core.Services;

// Definitions are compiled, reviewed adapters, never instructions from catalog metadata.
internal sealed record OwnedDataRequirement(string Id, string Label, string ReleaseIdentity,
    string AssetIdentity, string ExecutableSha256, long Length, string Md5,
    string FileName = "game-data.z64", string? ExpectedSha256 = null);
internal sealed record OwnedDataPreview(string PlanSha256, string Source, string Destination,
    string OperationId, string SourceSha256, long BytesToCopy, bool ReuseExisting);
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
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RequireCleanupKind(folder, true);
        var receipt = SafePath(folder, manifest.OperationId + ".json");
        var record = new OwnedDataReceipt(1, requirement.Id, key, manifest.OperationId,
            requirement.AssetIdentity, requirement.ExecutableSha256, current.SourceSha256, requirement.Length);
        var recordBytes = JsonSerializer.SerializeToUtf8Bytes(record, Json);
        // An existing receipt must agree byte-for-byte. No silent overwrite or
        // deletion, even if a previous import was interrupted after copying data.
        if (RemovalEntryExists(receipt))
        {
            RequireCleanupKind(receipt, false);
            if (new FileInfo(receipt).Length > 4096) throw new IOException("Unexpected setup evidence size.");
            if (!File.ReadAllBytes(receipt).SequenceEqual(recordBytes))
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
                        if (copied > requirement.Length) throw new IOException("The selected file changed during import.");
                        digest.AppendData(buffer, 0, count); output.Write(buffer, 0, count);
                        boundary?.Invoke("Copying");
                    }
                    if (copied != requirement.Length || Convert.ToHexStringLower(digest.GetHashAndReset()) != current.SourceSha256)
                        throw new IOException("The selected file changed during import. Its original was not modified.");
                    output.Flush(true);
                }
                token.ThrowIfCancellationRequested(); boundary?.Invoke("BeforeCommit");
                // The install writer lease is still held. Check the exact generation
                // again without recovery before making the data copy visible.
                var actual = ReadManifest(root, true) ?? throw new IOException("Installation disappeared during setup.");
                ValidateCommittedIdentity(actual);
                if (actual.OperationId != manifest.OperationId) throw new IOException("Installation changed during setup.");
                CheckLinks(current.Destination);
                token.ThrowIfCancellationRequested();
                File.Move(staging, current.Destination, false); staging = null; Sync(folder);
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
        var hash = HashOwnedInput(source, requirement, token);
        var reuse = RemovalEntryExists(destination);
        if (reuse && HashOwnedInput(destination, requirement, token) != hash)
            throw new IOException("An existing setup copy differs. It was preserved; automatic replacement is not supported.");
        if (!reuse)
        {
            var existing = new DirectoryInfo(Path.GetDirectoryName(destination)!);
            while (!existing.Exists) existing = existing.Parent ?? throw new IOException("No storage location is available for setup.");
            if (new DriveInfo(existing.FullName).AvailableFreeSpace < checked(requirement.Length + 4096))
                throw new IOException("Not enough available space for a preserved local copy and setup receipt.");
        }
        var plan = Hash(JsonSerializer.SerializeToUtf8Bytes(new { action = "CopyOwnedGameData", requirement.Id,
            root, key, manifest.OperationId, requirement.AssetIdentity, requirement.ExecutableSha256, source, destination, hash, reuse }));
        return new(plan, source, destination, manifest.OperationId, hash, reuse ? 0 : requirement.Length, reuse);
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
        RequireCleanupKind(receipt, false);
        if (new FileInfo(receipt).Length > 4096) throw new IOException("Unexpected setup evidence size.");
        var record = JsonSerializer.Deserialize<OwnedDataReceipt>(File.ReadAllBytes(receipt), Json)
            ?? throw new IOException("Missing setup evidence.");
        var hash = HashOwnedInput(destination, requirement, token);
        if (record != new OwnedDataReceipt(1, requirement.Id, key, manifest.OperationId, requirement.AssetIdentity,
                requirement.ExecutableSha256, hash, requirement.Length))
            throw new IOException("Setup evidence does not match this installation. Review game setup again.");
        return destination;
    }

    IDisposable AcquireOwnedDataAuthority(OwnedDataRequirement requirement, string dataRoot, bool createWriter = false, Action? inspectBeforeWriter = null)
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
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IOException("This setup adapter is verified only for the specified Linux x64 package.");
        if (manifest.InstallKey != key || manifest.ReleaseIdentity != requirement.ReleaseIdentity || manifest.AssetIdentity != requirement.AssetIdentity ||
            !manifest.Files.Any(f => f.Path == manifest.SelectedExecutable && f.Kind == "file" && f.Ownership == "payload" && f.Sha256 == requirement.ExecutableSha256))
            throw new IOException("Requirements are not confirmed for this package or selected executable.");
        RequireCleanupKind(SafePath(root, manifest.SelectedExecutable), false);
        if (FileHash(SafePath(root, manifest.SelectedExecutable)) != requirement.ExecutableSha256)
            throw new IOException("The selected executable changed. Repair the installation before setup or launch.");
    }

    static FileStream CreateOwnedDataFile(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
    });

    static string HashOwnedInput(string path, OwnedDataRequirement requirement, CancellationToken token)
    {
        RequireCleanupKind(path, false);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != requirement.Length) throw new IOException($"Expected {requirement.Label}: {requirement.Length:N0} bytes. The selected file was not changed.");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[64 * 1024]; long read = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = file.Read(buffer); if (count == 0) break;
            read = checked(read + count); if (read > requirement.Length) throw new IOException("Input changed while validating.");
            sha.AppendData(buffer, 0, count); md5.AppendData(buffer, 0, count);
        }
        if (read != requirement.Length || Convert.ToHexStringLower(md5.GetHashAndReset()) != requirement.Md5)
            throw new IOException($"This is not the supported {requirement.Label}. A hash identifies a variant, not ownership or legal entitlement. The original was preserved.");
        var digest = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (requirement.ExpectedSha256 is { } expected && digest != expected)
            throw new IOException("The selected data does not match this adapter's pinned SHA-256. The original was preserved.");
        return digest;
    }
}
