using System.Text.Json;

namespace Relicade.Core.Services;

// A separate consent action: never masquerades as an original-file import.
internal sealed record RetainedOwnedDataPreview(string PlanSha256, string Destination,
    string OperationId, string Sha256, long Length, bool ReceiptExists);
internal sealed record RetainedOwnedDataResult(bool ReceiptConfirmed, bool ReceiptCreated, bool ReadyPublished);

public sealed partial class PortableInstallationTransaction
{
    internal RetainedOwnedDataPreview PreviewRetainedOwnedData(OwnedDataRequirement requirement,
        string dataRoot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var authority = AcquireRollbackAuthority(null);
        var manifest = InspectCleanup().Current;
        RequireRetainedDataWriter(requirement, dataRoot);
        using var dataAuthority = AcquireOwnedDataAuthority(requirement, dataRoot, requireExistingWriter: true);
        return InspectRetainedOwnedData(requirement, dataRoot, manifest, token);
    }

    // Invoke on a worker. Both existing writer authorities span the awaited ready
    // callback; cancellation cannot release them while queued UI work can publish.
    internal async Task<RetainedOwnedDataResult> AdoptRetainedOwnedDataAsync(OwnedDataRequirement requirement,
        string dataRoot, RetainedOwnedDataPreview expected, Func<Task<bool>> publishReady,
        CancellationToken token = default, Action<string>? boundary = null)
    {
        token.ThrowIfCancellationRequested();
        using var authority = AcquireRollbackAuthority(null);
        var manifest = InspectCleanup().Current;
        RequireRetainedDataWriter(requirement, dataRoot);
        using var dataAuthority = AcquireOwnedDataAuthority(requirement, dataRoot, requireExistingWriter: true);
        var current = InspectRetainedOwnedData(requirement, dataRoot, manifest, token);
        if (current != expected) throw new IOException("Retained-file setup changed. Review a fresh reuse preview before confirming.");
        var folder = Path.GetDirectoryName(current.Destination)!;
        var receipt = SafePath(folder, manifest.OperationId + ".json");
        string? staging = null;
        var created = false;
        var confirmed = false;
        try
        {
            boundary?.Invoke("BeforeReceipt"); token.ThrowIfCancellationRequested();
            if (!current.ReceiptExists)
            {
                var temporary = SafePath(folder, "receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = CreateOwnedDataFile(temporary))
                {
                    staging = temporary;
                    output.Write(RetainedReceiptBytes(requirement, manifest, current.Sha256, current.Length));
                    output.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, receipt, false); staging = null; created = true;
                // Once visible, failure/cancellation cannot truthfully imply that
                // no receipt changed. Readiness remains a separate guarded result.
                confirmed = true; Sync(folder);
            }
            else confirmed = true;
            boundary?.Invoke("ReceiptCommitted");
            token.ThrowIfCancellationRequested();
            ValidateOwnedData(requirement, dataRoot, manifest, token);
            boundary?.Invoke("BeforeReady"); token.ThrowIfCancellationRequested();
            var ready = await publishReady().ConfigureAwait(false);
            return new(true, created, ready);
        }
        catch (Exception) when (confirmed)
        {
            // A committed receipt is retained, even when later verification,
            // cancellation or UI participation refuses readiness publication.
            return new(true, created, false);
        }
        finally
        {
            if (staging != null && RemovalEntryExists(staging)) { File.Delete(staging); Sync(folder); }
        }
    }

    void RequireRetainedDataWriter(OwnedDataRequirement requirement, string dataRoot)
    {
        var folder = Path.GetDirectoryName(OwnedDataDestination(requirement, dataRoot))!;
        RequireCleanupKind(folder, true);
        var target = new PortableInstallationTransaction(folder);
        RequireCleanupKind(target.operations, true);
        var writer = SafePath(target.operations, "writer.lock");
        RequireCleanupKind(writer, false);
        if (new FileInfo(writer).Length != 0) throw new IOException("Retained-data writer identity is uncertain. All files were preserved.");
    }

    RetainedOwnedDataPreview InspectRetainedOwnedData(OwnedDataRequirement requirement, string dataRoot,
        Manifest manifest, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ValidateOwnedBinding(requirement, manifest);
        var destination = OwnedDataDestination(requirement, dataRoot);
        var folder = Path.GetDirectoryName(destination)!;
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            token.ThrowIfCancellationRequested();
            if (++count > 4096) throw new IOException("Retained setup evidence exceeds the inspection limit.");
            var name = Path.GetFileName(entry);
            if ((name.StartsWith("import-", StringComparison.Ordinal) || name.StartsWith("receipt-", StringComparison.Ordinal)) &&
                name.EndsWith(".tmp", StringComparison.Ordinal))
                throw new IOException("Pending retained-data work needs review. No staging file or receipt was changed.");
        }
        var hash = HashOwnedInput(destination, requirement, token, out var length);
        var receipt = SafePath(folder, manifest.OperationId + ".json");
        var exists = RemovalEntryExists(receipt);
        if (exists && !ReadOwnedReceiptBytes(receipt).SequenceEqual(RetainedReceiptBytes(requirement, manifest, hash, length)))
            throw new IOException("Existing setup evidence conflicts. All retained files and receipts were preserved.");
        var plan = Hash(JsonSerializer.SerializeToUtf8Bytes(new { action = "ExplicitlyReuseRetainedOwnedGameData",
            root, key, requirement.Id, manifest.OperationId, manifest.SelectedExecutable, requirement.ReleaseIdentity,
            requirement.AssetIdentity, requirement.ExecutableSha256, destination, hash, length, exists }));
        return new(plan, destination, manifest.OperationId, hash, length, exists);
    }

    byte[] RetainedReceiptBytes(OwnedDataRequirement requirement, Manifest manifest, string hash, long length) =>
        JsonSerializer.SerializeToUtf8Bytes(new OwnedDataReceipt(1, requirement.Id, key, manifest.OperationId,
            requirement.AssetIdentity, requirement.ExecutableSha256, hash, length), Json);
}
