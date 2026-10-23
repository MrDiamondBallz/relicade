using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.ViewModels;

internal sealed class PortablePreservationViewModel : ObservableViewModel
{
    readonly LauncherSession session;
    readonly CancellationTokenSource participation;
    readonly Func<Func<PortablePreservationAssessment, Task>, CancellationToken, Task> observe;
    bool closed, busy;
    internal string InstanceKey { get; }
    internal PortablePreservationAssessment? Assessment { get; private set; }
    internal string Message { get; private set; } = "Inspection only. Managed uninstall is not available; nothing will be removed.";
    internal bool IsClosed => closed || session.IsClosed;
    internal bool CanRefresh => !IsClosed && !busy;
    internal Task Work { get; private set; } = Task.CompletedTask;
    internal PortablePreservationViewModel(LauncherSession session, GameManager manager, string key)
    {
        this.session = session; InstanceKey = key;
        participation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        try { observe = PortablePreservationService.Capture(manager, key, () => IsClosed); }
        catch { participation.Dispose(); throw; }
    }
    internal PortablePreservationViewModel(LauncherSession session, string key,
        Func<Func<PortablePreservationAssessment, Task>, CancellationToken, Task> observe)
    { this.session = session; InstanceKey = key; this.observe = observe; participation = CancellationTokenSource.CreateLinkedTokenSource(session.Token); }
    internal Task RefreshAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanRefresh) return Work;
        busy = true; Assessment = null; Message = "Reading bounded local evidence. Close cancels participation and drains the read; it does not remove files."; Changed();
        Work = session.RunAsync(async () =>
        {
            try
            {
                await observe(result =>
                {
                    Dispatcher.UIThread.VerifyAccess();
                    if (!IsClosed)
                    {
                        Assessment = result;
                        Message = result.IsPartial ? "PARTIAL observation: unobserved files remain preserved and uncounted. Inspect the remaining files separately." :
                            "Observed local files only. All files remain preserved. These bytes are NOT reclaimable-space promises or deletion authority.";
                        Changed();
                    }
                    return Task.CompletedTask;
                }, participation.Token);
                if (!IsClosed && Assessment == null) { Message = "Entry or generation changed; stale inspection was not published. Close and reopen for the current entry."; Changed(); }
            }
            catch (OperationCanceledException) when (participation.IsCancellationRequested) { }
            catch (Exception error) { if (!IsClosed) { Message = "Inspection unavailable. Preserve all files. " + error.Message; Changed(); } }
            finally { if (!IsClosed) { busy = false; Changed(); } }
        });
        return Work;
    }
    internal void Close() { Dispatcher.UIThread.VerifyAccess(); if (closed) return; closed = true; participation.Cancel(); Changed(); }
    internal void DisposeAfterDrain() { if (!Work.IsCompleted) throw new InvalidOperationException("Drain inspection before disposing participation."); participation.Dispose(); }
    void Changed() { Notify(nameof(Assessment)); Notify(nameof(Message)); Notify(nameof(CanRefresh)); }
}
