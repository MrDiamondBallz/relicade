using Avalonia.Threading;
using Relicade.Services;

namespace Relicade.ViewModels;

internal sealed class LibraryMetadataExportViewModel : ObservableViewModel, IDisposable
{
    readonly GameManager manager;
    readonly LauncherSession session;
    readonly SettingsViewModel settings;
    readonly Func<Task<string?>> choose;
    readonly CancellationTokenSource cancellation;
    readonly LoadedMetadataSnapshot? snapshot;
    readonly byte[]? bytes;
    bool busy, closed, saved;
    string message;
    internal Task Work { get; private set; } = Task.CompletedTask;
    internal bool IsClosed => closed;
    internal bool Saved => saved;
    public bool CanSave => !closed && !busy && !saved && bytes != null;
    public string Message { get => message; private set { message = value; Notify(); } }
    internal IReadOnlyList<string> Pages { get; }
    internal LibraryMetadataExportViewModel(GameManager manager, SettingsViewModel settings, LauncherSession session, Func<Task<string?>> choose)
    {
        this.manager = manager; this.settings = settings; this.session = session; this.choose = choose;
        message = "Preview the exact metadata before choosing a new local file. Nothing has been saved.";
        var pages = new List<string> { LibraryMetadataDocument.Disclosure };
        try
        {
            if (settings.KioskLocked || settings.IsReadOnly) throw new InvalidOperationException("Metadata export is unavailable in this session.");
            snapshot = manager.CaptureLibraryMetadata(); bytes = snapshot.Document.Encode();
            pages.Add($"Loaded snapshot: {snapshot.Document.ObservedAt:O}\nEntries: {snapshot.Document.Entries.Length}\nJSON bytes: {bytes.Length}\nFormat: {LibraryMetadataDocument.FormatName}, version 1. Export only; no restore capability.\nAll names and logical keys below are part of this file. Review them for personal information.");
            // Exact JSON, including null/excluded field distinctions, is the preview.
            pages.AddRange(System.Text.Encoding.UTF8.GetString(bytes).Chunk(900).Select(chunk => new string(chunk)));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        { message = "Export unavailable. " + error.Message; pages.Add(message); }
        Pages = pages.ToArray();
        // Allocate linked lifetime only after construction/validation has safely completed.
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
    }
    internal Task SaveAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanSave) return Work;
        busy = true; Notify(nameof(CanSave));
        Work = SaveCoreAsync(); return Work;
    }
    async Task SaveCoreAsync()
    {
        try
        {
            RequireCurrent(); var destination = await choose();
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination == null) { if (!closed) Message = "File selection cancelled. Nothing was saved."; return; }
            RequireCurrent();
            await LibraryMetadataExportWriter.WriteNewAsync(bytes!, destination, snapshot!.ProtectedRoots, cancellation.Token,
                commit => { RequireCurrent(); manager.CommitMetadataExport(snapshot, commit); });
            // Do not let cancellation or closed presentation reinterpret a real commit.
            saved = true;
            if (!closed && !session.IsClosed) Message = "Metadata file saved. This is an export-only list, not a restorable backup.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { if (!closed) Message = "Export cancelled before commit. No destination was created."; }
        catch (MetadataExportCommitException error) { if (!closed && !session.IsClosed) Message = error.Message; }
        catch (Exception error) { if (!closed && !session.IsClosed) Message = "Export not saved. " + error.Message; }
        finally { busy = false; if (!closed) Notify(nameof(CanSave)); }
    }
    void RequireCurrent()
    {
        if (closed || session.IsClosed || settings.KioskLocked || settings.IsReadOnly || snapshot == null || !manager.IsCurrentMetadata(snapshot))
            throw new InvalidOperationException("The library or session changed. Close and preview a fresh export.");
    }
    internal void Close() { if (closed) return; closed = true; cancellation.Cancel(); }
    public void Dispose() { if (!Work.IsCompleted) throw new InvalidOperationException("Drain metadata export before disposing it."); cancellation.Dispose(); }
}
