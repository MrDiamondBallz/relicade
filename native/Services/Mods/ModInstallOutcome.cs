namespace Relicade.Services.Mods;

/// <summary>An authoritative selected-file transaction that committed during this request.</summary>
public sealed record ModCommittedIdentity(string Provider, string SourceKey, string PackageId, string? FileId)
{
    internal static ModCommittedIdentity From(InstalledModRecord record) =>
        new(record.Provider, record.SourceKey, record.Id, record.DownloadFileId);

    public override string ToString() => $"{Provider}/{SourceKey}/{PackageId} (file: {FileId ?? "package"})";
}

/// <summary>Partial work is not a graph-wide rollback. Cancellation retains its original exception.</summary>
public static class ModInstallOutcome
{
    const string IdentityKey = "CommittedModIdentities";

    public static IReadOnlyList<ModCommittedIdentity> GetCommitted(Exception error) =>
        error.Data[IdentityKey] is ModCommittedIdentity[] identities ? Array.AsReadOnly(identities) : [];

    internal static void Attach(Exception error, IEnumerable<ModCommittedIdentity> committed)
    {
        var identities = committed.Concat(GetCommitted(error)).Distinct().ToArray();
        error.Data[IdentityKey] = identities;
        // Retain the existing diagnostic entry, now with complete provider/source/package/file identities.
        error.Data["CommittedMods"] = identities.Select(i => i.ToString()).ToArray();
    }

    internal static Exception FollowupFailure(Exception original, InstalledModRecord? record, string message)
    {
        // The payload/manifest generation was validated as committed, even though cleanup/reporting failed.
        var error = original is OperationCanceledException ? original : new IOException(message, original);
        if (record != null) Attach(error, [ModCommittedIdentity.From(record)]);
        return error;
    }

    internal static async Task RunAsync(Func<List<ModCommittedIdentity>, Task> action)
    {
        var committed = new List<ModCommittedIdentity>();
        try { await action(committed).ConfigureAwait(false); }
        catch (Exception original)
        {
            committed.AddRange(GetCommitted(original));
            if (original is OperationCanceledException || committed.Count == 0)
            {
                Attach(original, committed);
                throw;
            }
            var error = new IOException($"Mod installation failed; already committed dependencies/files remain installed: {string.Join(", ", committed.Distinct())}. The failed replacement retained its previous generation or recovery evidence. Cause: {original.Message}", original);
            Attach(error, committed);
            throw error;
        }
    }
}
