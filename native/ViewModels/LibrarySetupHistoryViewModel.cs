using System.Collections.ObjectModel;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.ViewModels;

public sealed class LibrarySetupHistoryRow
{
    internal LibrarySetupHistoryInspection.Entry Captured { get; }
    public string Name => Captured.Name;
    public string Message { get; }
    internal LibrarySetupHistoryRow(LibrarySetupHistoryInspection.Finding finding)
    { Captured = finding.Entry; Message = finding.Message; }
}

public sealed class LibrarySetupHistoryViewModel : ObservableViewModel, IDisposable
{
    readonly GameManager manager;
    readonly Func<LibrarySetupHistoryInspection, CancellationToken, Task<LibrarySetupHistoryInspection.Step>> read;
    LauncherSession? session;
    CancellationTokenSource? request;
    LibrarySetupHistoryInspection? inspection;
    string? capturedRoot;
    LibrarySetupHistoryInspection.Entry[] captured = [];
    long generation;
    bool closed, busy, visible, more;
    string message = "";
    public ObservableCollection<LibrarySetupHistoryRow> Rows { get; } = [];
    public bool IsBusy => busy;
    public bool IsVisible => visible;
    public bool HasMore => more;
    public string Message => message;
    internal Task Work { get; private set; } = Task.CompletedTask;

    internal LibrarySetupHistoryViewModel(GameManager manager,
        Func<LibrarySetupHistoryInspection, CancellationToken, Task<LibrarySetupHistoryInspection.Step>>? read = null)
    {
        this.manager = manager;
        this.read = read ?? ((reader, token) => Task.Run(() => reader.Next(token), token));
    }
    internal void Attach(LauncherSession owner)
    {
        if (session != null) return;
        session = owner; owner.OnShutdown(Dispose);
        if (manager.HasLoadedLibrary || manager.LibraryApps.Count > 0) Refresh();
    }
    internal void Refresh()
    {
        if (closed || session == null || session.IsClosed) return;
        request?.Cancel();
        var prior = Work; var priorInspection = inspection; var priorRequest = request;
        inspection = null;
        var current = ++generation;
        var source = request = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        capturedRoot = Path.GetFullPath(RelicadePaths.UserDataRoot);
        var root = capturedRoot;
        captured = Capture(root);
        var entries = captured;
        Rows.Clear(); more = false; busy = true; visible = entries.Length > 0;
        message = "Inspecting saved setup history locally. Nothing resumes automatically."; Notify(null);
        Work = session.RunAsync(async () =>
        {
            try
            {
                try { await prior; } catch (OperationCanceledException) { }
                priorInspection?.Dispose(); priorRequest?.Dispose();
                source.Token.ThrowIfCancellationRequested();
                var reader = new LibrarySetupHistoryInspection(root, entries);
                if (current != generation || closed) { reader.Dispose(); return; }
                inspection = reader;
                await ReadStep(reader, current, source.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (source.IsCancellationRequested || closed || current != generation)
                { if (current == generation) { inspection?.Dispose(); inspection = null; if (closed) source.Dispose(); } }
            }
        });
    }
    internal void InvalidateIfChanged()
    {
        if (session != null && !closed && capturedRoot != null && !StillCurrent()) Refresh();
    }
    internal void Continue()
    {
        if (closed || session == null || session.IsClosed || busy || !more || inspection == null || request == null) return;
        if (!StillCurrent()) { Refresh(); return; }
        var reader = inspection; var current = generation; var token = request.Token;
        busy = true; Notify(null);
        Work = session.RunAsync(async () =>
        {
            try { await ReadStep(reader, current, token); }
            catch (OperationCanceledException) { reader.Dispose(); }
            finally { if (closed) request?.Dispose(); }
        });
    }
    async Task ReadStep(LibrarySetupHistoryInspection reader, long current, CancellationToken token)
    {
        var result = await read(reader, token);
        if (token.IsCancellationRequested || closed || session!.IsClosed || current != generation) return;
        if (!StillCurrent())
        {
            reader.Dispose(); inspection = null; Rows.Clear(); busy = false; more = false; visible = true;
            message = "The library or setup context changed. Refresh to inspect current entries."; Notify(null); return;
        }
        Rows.Clear(); foreach (var finding in result.Findings) Rows.Add(new(finding));
        busy = false; more = result.More; message = result.Message;
        visible = more || Rows.Count > 0 || message.Contains("could not", StringComparison.Ordinal);
        Notify(null);
    }
    LibrarySetupHistoryInspection.Entry[] Capture(string root)
    {
        var entries = new List<LibrarySetupHistoryInspection.Entry>();
        foreach (var game in manager.LibraryApps)
        {
            if (!game.HasOwnedDataAdapter && !game.OffersFreedoomSetup) continue;
            try
            {
                var project = GuidedEngineInstallService.ProjectFor(game);
                var install = Path.GetFullPath(game.GetInstallPath(manager.GamesFolder));
                entries.Add(new(game, game.Name, game.InstanceKey, install,
                    GuidedInstallIntent.ForCaptured(game.InstanceKey, root, install, project), game.IsInstalled, game.Status));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { }
        }
        return entries.ToArray();
    }
    bool StillCurrent()
    {
        if (closed || capturedRoot != Path.GetFullPath(RelicadePaths.UserDataRoot)) return false;
        var now = Capture(capturedRoot);
        return now.Length == captured.Length && now.Zip(captured).All(pair =>
            ReferenceEquals(pair.First.Game, pair.Second.Game) && pair.First == pair.Second);
    }
    internal bool TryGetCurrent(LibrarySetupHistoryRow row, out GameInfo? game)
    {
        game = null;
        if (!Rows.Contains(row) || !StillCurrent()) return false;
        game = row.Captured.Game; return true;
    }
    internal async Task CancelAndDrain()
    {
        request?.Cancel();
        try { await Work; }
        finally
        {
            inspection?.Dispose(); inspection = null;
            if (busy && !closed)
            {
                more = false;
                message = "Inspection stopped before completion. Review or refresh to inspect again; nothing resumed.";
            }
            busy = false;
            if (!closed) Notify(null);
        }
    }
    public void Dispose()
    {
        if (closed) return; closed = true; request?.Cancel();
        if (Work.IsCompleted) { inspection?.Dispose(); request?.Dispose(); }
        // Active work is owned/drained by LauncherSession and closes its reader on cancellation.
    }
}
