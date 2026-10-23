using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

// This is a record of an explicit guided first-install request, not a queue,
// transaction journal, resumable payload, or authority to install on restart.
internal sealed class GuidedInstallIntent : IDisposable
{
    internal sealed record Identity(string EntryKey, string ContextSha256, string Repository,
        string ReleaseTag, string AssetName, string AssetSha256, string ExecutableSha256);
    internal sealed record Record(int SchemaVersion, string AttemptId, string? PreviousAttemptId,
        Identity Intent, string Stage, int Sequence);
    static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    static readonly string[] Stages = ["Resolving", "Downloading", "Verifying", "Staging", "Committing", "Completed", "Cancelled", "Failed"];
    readonly string directory, path;
    readonly FileStream lease;
    Record current;
    bool disposed;
    internal bool Committed { get; private set; }
    internal bool StatusUncertain { get; private set; }
    internal string Stage => current.Stage;

    GuidedInstallIntent(string directory, string path, FileStream lease, Record current)
    { this.directory = directory; this.path = path; this.lease = lease; this.current = current; }

    internal static Identity For(GameInfo game, GameManager manager, GuidedEngineInstallService.PreparedInstall prepared) => new(
        Hash(game.InstanceKey), Hash(Path.GetFullPath(RelicadePaths.UserDataRoot) + "\n" + Path.GetFullPath(game.GetInstallPath(manager.GamesFolder))),
        prepared.Project.Repository, prepared.Requirement.ReleaseIdentity, prepared.AssetName,
        prepared.Constraint.Sha256, prepared.Requirement.ExecutableSha256);
    internal static Identity ForCaptured(string instanceKey, string dataRoot, string installRoot,
        GuidedEngineInstallService.Project project)
    {
        var split = project.Requirement.AssetIdentity.Split("|sha256:", StringSplitOptions.None);
        if (split.Length != 2 || split[1].Length != 64 || !split[1].All(Uri.IsHexDigit))
            throw new IOException("The compiled setup identity is invalid.");
        return new(Hash(instanceKey), Hash(Path.GetFullPath(dataRoot) + "\n" + Path.GetFullPath(installRoot)),
            project.Repository, project.Requirement.ReleaseIdentity, split[0], split[1], project.Requirement.ExecutableSha256);
    }
    static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static string DirectoryPath => Path.Combine(RelicadePaths.UserDataRoot, "DownloadIntents");
    static void CheckDirectory(string directory)
    {
        PortableInstallationTransaction.CheckLinks(directory);
        if (File.Exists(directory)) throw new IOException("The saved setup intent location is not a directory. It was preserved.");
    }

    // No directory creation, lock acquisition, transaction recovery, or remote request.
    internal static Record? Inspect(Identity identity)
    {
        var directory = DirectoryPath; CheckDirectory(directory);
        if (!Directory.Exists(directory)) return null;
        var paths = Directory.EnumerateFileSystemEntries(directory, identity.EntryKey + "-*").Take(257).ToArray();
        return InspectCaptured(identity, directory, paths, CancellationToken.None);
    }

    // Caller supplies only a completed bounded directory pass, never a partial chain.
    internal static Record? InspectCaptured(Identity identity, string directory, IReadOnlyList<string> paths,
        CancellationToken token)
    {
        CheckDirectory(directory);
        var records = new Dictionary<string, Record>(StringComparer.Ordinal);
        if (paths.Count > 256) throw new IOException("Too many saved setup attempts; inspect the retained history before retrying.");
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetDirectoryName(path) != directory || !Path.GetFileName(path).StartsWith(identity.EntryKey + "-", StringComparison.Ordinal))
                throw new IOException("Saved setup evidence belongs to another entry. It was preserved.");
            if (!path.EndsWith(".json", StringComparison.Ordinal)) throw new IOException("Unfinished or unknown setup evidence was preserved. Inspect it before retrying.");
            PortableInstallationTransaction.RequireRegularEvidenceFile(path);
            if (new FileInfo(path).Length is <= 0 or > 16384) throw new IOException("Saved setup intent has an invalid size. It was preserved.");
            Record record;
            try { record = JsonSerializer.Deserialize<Record>(ReadBoundedRecord(path), Json) ?? throw new JsonException(); }
            catch (JsonException ex) { throw new IOException("Saved setup intent is unreadable. It was preserved; no retry started.", ex); }
            if (record.SchemaVersion != 1 || record.Intent != identity || !Guid.TryParseExact(record.AttemptId, "N", out _) ||
                Path.GetFileName(path) != identity.EntryKey + "-" + record.AttemptId + ".json" ||
                !Stages.Contains(record.Stage) || record.Sequence < 0 || record.Sequence > 7 ||
                (record.PreviousAttemptId != null && !Guid.TryParseExact(record.PreviousAttemptId, "N", out _)) ||
                !records.TryAdd(record.AttemptId, record))
                throw new IOException("Saved setup intent does not match this reviewed request. It was preserved.");
        }
        if (records.Count == 0) return null;
        var referenced = records.Values.Where(r => r.PreviousAttemptId != null).Select(r => r.PreviousAttemptId!).ToHashSet(StringComparer.Ordinal);
        var tips = records.Values.Where(r => !referenced.Contains(r.AttemptId)).ToArray();
        if (tips.Length != 1 || referenced.Any(id => !records.ContainsKey(id))) throw new IOException("Saved setup history is incomplete or conflicting. It was preserved.");
        var seen = new HashSet<string>(); Record? cursor = tips[0];
        while (cursor != null && seen.Add(cursor.AttemptId)) cursor = cursor.PreviousAttemptId == null ? null : records[cursor.PreviousAttemptId];
        if (cursor != null || seen.Count != records.Count) throw new IOException("Saved setup history is conflicting. It was preserved.");
        return tips[0];
    }

    static byte[] ReadBoundedRecord(string path)
    {
        // A concurrent writer cannot turn the preceding size check into an unbounded read.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[16385]; var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count is 0 or > 16384) throw new IOException("Saved setup intent has an invalid size. It was preserved.");
        return bytes.AsSpan(0, count).ToArray();
    }

    internal static GuidedInstallIntent Begin(Identity identity)
    {
        var directory = DirectoryPath; CheckDirectory(directory); Directory.CreateDirectory(directory); CheckDirectory(directory);
        PortableInstallationTransaction.Sync(Path.GetDirectoryName(directory)!);
        var lockPath = Path.Combine(directory, identity.EntryKey + ".lock");
        PortableInstallationTransaction.CheckLinks(lockPath);
        if (File.Exists(lockPath)) PortableInstallationTransaction.RequireRegularEvidenceFile(lockPath);
        var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var previous = Inspect(identity);
            if (previous?.Stage is "Committing" or "Completed")
                throw new IOException("A previous attempt may have committed an engine. Inspect the installed entry and recovery controls; no new transfer was started.");
            var record = new Record(1, Guid.NewGuid().ToString("N"), previous?.AttemptId, identity, "Resolving", 0);
            var path = Path.Combine(directory, identity.EntryKey + "-" + record.AttemptId + ".json");
            var result = new GuidedInstallIntent(directory, path, lease, record);
            result.Write(record, replace: false);
            return result;
        }
        catch { lease.Dispose(); throw; }
    }

    internal void Advance(string stage)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Committed || current.Stage is "Failed" or "Cancelled" || !Stages.Contains(stage))
            throw new IOException("The setup attempt is no longer writable.");
        if (stage is not ("Failed" or "Cancelled") && Array.IndexOf(Stages, stage) != Array.IndexOf(Stages, current.Stage) + 1)
            throw new IOException("The setup stage is out of order.");
        if (stage == "Completed") Committed = true; // Disk outcome cannot be reversed by a status-write failure.
        var next = current with { Stage = stage, Sequence = checked(current.Sequence + 1) };
        try { Write(next, replace: true); current = next; }
        catch when (Committed) { StatusUncertain = true; }
    }
    internal void Finish(bool cancelled)
    {
        if (Committed || current.Stage is "Failed" or "Cancelled") return;
        // A durable applying boundary is deliberately not rewritten as a clean failure.
        if (current.Stage == "Committing") { StatusUncertain = true; return; }
        Advance(cancelled ? "Cancelled" : "Failed");
    }
    void Write(Record record, bool replace)
    {
        CheckDirectory(directory);
        if (replace)
        {
            PortableInstallationTransaction.RequireRegularEvidenceFile(path);
            if (new FileInfo(path).Length > 16384 || !File.ReadAllBytes(path).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(current, Json)))
                throw new IOException("The saved setup attempt changed. It was preserved; no stale status was written.");
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, record, Json); stream.Flush(true); }
        // Do not delete abandoned evidence on failure, and never overwrite another attempt.
        File.Move(temporary, path, overwrite: replace);
        PortableInstallationTransaction.Sync(directory);
    }
    public void Dispose() { if (disposed) return; disposed = true; lease.Dispose(); }
}
