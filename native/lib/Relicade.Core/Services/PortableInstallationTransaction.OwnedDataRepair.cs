using System.Security.Cryptography;
using System.Text.Json;

namespace Relicade.Core.Services;

public sealed partial class PortableInstallationTransaction
{
    static byte[] ReadOwnedReceiptBytes(string path)
    {
        RequireCleanupKind(path, false);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 4096) throw new IOException("Unexpected setup evidence size.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new IOException("Setup evidence changed during inspection.");
        return bytes;
    }

    void RequireRepairReceipt(OwnedDataRequirement requirement, Manifest manifest, string path, string? replacementSha256)
    {
        var bytes = ReadOwnedReceiptBytes(path);
        var record = JsonSerializer.Deserialize<OwnedDataReceipt>(bytes, Json)
            ?? throw new IOException("Missing setup evidence. Existing data was preserved.");
        if (record.Sha256 is not { Length: 64 } || record.Sha256.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
            !requirement.AcceptsLength(record.Length) ||
            record != new OwnedDataReceipt(1, requirement.Id, key, manifest.OperationId, requirement.AssetIdentity,
                requirement.ExecutableSha256, record.Sha256, record.Length) ||
            requirement.ExpectedSha256 is { } pinned && record.Sha256 != pinned ||
            replacementSha256 != null && record.Sha256 != replacementSha256 ||
            !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(record, Json)))
            throw new IOException("Existing setup evidence does not authorize this replacement. Data and evidence were preserved.");
    }

    // A changed file is retained, not accepted as playable data. Bound inspection
    // even when its current length no longer matches the adapter's expected input.
    static (long Length, string Sha256) ObserveChangedOwnedData(string path, OwnedDataRequirement requirement, CancellationToken token)
    {
        RequireCleanupKind(path, false);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = file.Length;
        var maximum = checked(requirement.MaximumLength * 2);
        if (length > maximum) throw new IOException("Changed setup data exceeds the bounded preservation limit. Inspect it separately; it was not modified.");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024]; long read = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = file.Read(buffer); if (count == 0) break;
            read = checked(read + count);
            if (read > length) throw new IOException("Existing setup data changed during inspection.");
            sha.AppendData(buffer, 0, count);
        }
        if (read != length) throw new IOException("Existing setup data changed during inspection.");
        return (length, Convert.ToHexStringLower(sha.GetHashAndReset()));
    }

    void PreserveChangedOwnedData(OwnedDataPreview plan, OwnedDataRequirement requirement, CancellationToken token, Action<string>? boundary)
    {
        var repair = plan.Repair ?? throw new IOException("Replacement requires a preservation preview.");
        if (repair.ReuseRetained)
        {
            if (ObserveChangedOwnedData(repair.RetainedDestination, requirement, token) != (repair.PreviousLength, repair.PreviousSha256))
                throw new IOException("The previously retained copy changed. No replacement was made.");
            return;
        }
        var folder = Path.GetDirectoryName(plan.Destination)!;
        string? staging = null;
        try
        {
            boundary?.Invoke("BeforeRepairBackup"); token.ThrowIfCancellationRequested();
            RequireCleanupKind(plan.Destination, false);
            var temporary = SafePath(folder, "preserve-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var input = new FileStream(plan.Destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = CreateOwnedDataFile(temporary))
            {
                staging = temporary;
                if (input.Length != repair.PreviousLength) throw new IOException("The confirmed data changed before preservation.");
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024]; long copied = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested(); var count = input.Read(buffer); if (count == 0) break;
                    copied = checked(copied + count);
                    if (copied > repair.PreviousLength) throw new IOException("The confirmed data grew during preservation.");
                    sha.AppendData(buffer, 0, count); output.Write(buffer, 0, count);
                    boundary?.Invoke("Copying");
                }
                if (copied != repair.PreviousLength || Convert.ToHexStringLower(sha.GetHashAndReset()) != repair.PreviousSha256)
                    throw new IOException("The confirmed data changed during preservation.");
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); CheckLinks(repair.RetainedDestination);
            File.Move(staging, repair.RetainedDestination, false); staging = null; Sync(folder);
            boundary?.Invoke("BackupCommitted");
        }
        finally
        {
            // Never delete a retained backup, even after cancellation or a failed
            // later commit. Only this invocation's incomplete temporary is owned.
            if (staging != null && RemovalEntryExists(staging)) { File.Delete(staging); Sync(folder); }
        }
    }
}
