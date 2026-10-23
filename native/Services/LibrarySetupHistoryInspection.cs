using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

// Informational local inspection only. This state never authorizes a retry or launch.
internal sealed class LibrarySetupHistoryInspection : IDisposable
{
    internal sealed record Entry(GameInfo Game, string Name, string InstanceKey, string InstallRoot,
        GuidedInstallIntent.Identity Identity, bool Installed, GameStatus Status);
    internal sealed record Finding(Entry Entry, string Message);
    internal sealed record Step(IReadOnlyList<Finding> Findings, bool More, string Message);
    const int EntryBatch = 256, DirectoryBatch = 1024, ReadBudget = 8 * 1024 * 1024;
    readonly string directory;
    readonly Entry[] entries;
    readonly List<Finding> findings = [];
    readonly Dictionary<string, List<string>> paths = new(StringComparer.Ordinal);
    readonly HashSet<string> anomalous = new(StringComparer.Ordinal);
    IEnumerator<string>? cursor;
    int start, end, validate;
    bool initialized, directoryDone, done, disposed;

    internal bool IsDisposed => disposed;
    internal LibrarySetupHistoryInspection(string dataRoot, Entry[] entries)
    { directory = Path.Combine(dataRoot, "DownloadIntents"); this.entries = entries; }

    internal Step Next(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        if (done || entries.Length == 0) { done = true; return Result(false); }
        try
        {
            PortableInstallationTransaction.CheckLinks(directory);
            if (File.Exists(directory)) throw new IOException("Invalid history directory.");
            if (!Directory.Exists(directory)) { done = true; return Result(false); }
            if (!initialized)
            {
                end = Math.Min(start + EntryBatch, entries.Length); validate = start;
                paths.Clear(); anomalous.Clear();
                for (var i = start; i < end; i++) paths.TryAdd(entries[i].Identity.EntryKey, []);
                cursor = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                initialized = true; directoryDone = false;
            }
            if (!directoryDone)
            {
                for (var count = 0; count < DirectoryBatch; count++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!cursor!.MoveNext()) { cursor.Dispose(); cursor = null; directoryDone = true; break; }
                    var path = cursor.Current; var name = Path.GetFileName(path);
                    if (name.Length < 65 || name[64] != '-' || !paths.TryGetValue(name[..64], out var matching)) continue;
                    if (matching.Count < 256) matching.Add(path); else anomalous.Add(name[..64]);
                }
                if (!directoryDone) return Result(true);
            }
            var remaining = ReadBudget;
            while (validate < end)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[validate]; var key = entry.Identity.EntryKey;
                var matching = paths[key];
                // Charge the maximum permitted bytes before reading, not a mutable stat length.
                var charge = matching.Count * 16384;
                if (charge > remaining) return Result(true);
                remaining -= charge; validate++;
                if (anomalous.Contains(key))
                { findings.Add(new(entry, "Saved setup history exceeds the per-entry safety limit. Inspection is needed; files were preserved.")); continue; }
                try
                {
                    var saved = GuidedInstallIntent.InspectCaptured(entry.Identity, directory, matching, token);
                    if (saved == null || saved.Stage == "Completed" && entry.Installed) continue;
                    var message = saved.Stage switch
                    {
                        "Committing" or "Completed" => "A saved setup may have committed an engine. Inspect this entry and its recovery controls; no retry was started.",
                        "Cancelled" => "Previous setup was cancelled. Nothing resumes automatically; review this entry before choosing setup again.",
                        "Failed" => "Previous setup failed. Nothing resumes automatically; review this entry before choosing setup again.",
                        _ => $"Previous setup: {saved.Stage}. It may have stopped or still be active. Nothing resumes automatically."
                    };
                    findings.Add(new(entry, message));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { findings.Add(new(entry, "Saved setup history is unknown, changed or unreadable. Inspection is needed; files were preserved.")); }
            }
            start = end; initialized = false;
            done = start == entries.Length;
            return Result(!done);
        }
        catch (OperationCanceledException) { Dispose(); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cursor?.Dispose(); cursor = null; done = true;
            return new(findings.ToArray(), false, "Saved setup history could not be fully inspected. Files were preserved. Refresh to inspect again.");
        }
    }
    Step Result(bool more) => new(findings.ToArray(), more, more
        ? "Inspection is incomplete. Continue inspection to check the remaining saved history. Nothing resumes automatically."
        : findings.Count == 0 ? "No saved setup requiring attention was found for the inspected current entries."
        : "Saved setup history needs attention. Showing a game in your library does not start setup or a download.");
    public void Dispose() { if (disposed) return; disposed = true; cursor?.Dispose(); cursor = null; }
}
