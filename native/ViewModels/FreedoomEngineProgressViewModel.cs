using Avalonia.Threading;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.ViewModels;

internal sealed class FreedoomEngineProgressViewModel : ObservableViewModel
{
    readonly LauncherSession session;
    readonly GameInfo game;
    readonly Func<CancellationToken, Task<bool>> install;
    readonly CancellationTokenSource cancellation;
    bool started, cancelled, disposed;
    double progress;
    long reportedSequence, appliedSequence;
    GuidedEngineProgress observation = new(GuidedEnginePhase.Resolving);
    internal bool IsProgressIndeterminate { get; private set; } = true;
    internal GuidedEnginePhase Phase => observation.Phase;
    internal string Title { get; }
    internal string Confirmation { get; }
    internal string InstallLabel { get; }
    internal bool HasPreviousAttempt { get; }
    internal Task Work { get; private set; } = Task.CompletedTask;
    internal bool IsBusy { get; private set; }
    internal bool Succeeded { get; private set; }
    internal bool CanStart => !started && !cancelled && !session.IsClosed;
    internal bool CanCancel => !cancelled && !Succeeded;
    internal bool CancellationRequested => cancelled || cancellation.IsCancellationRequested;
    internal bool SessionClosed => session.IsClosed;
    internal double Progress => progress;
    internal string Message { get; private set; } = "Confirm the pinned engine installation. Game-data setup is a separate step; nothing is played automatically.";

    internal FreedoomEngineProgressViewModel(LauncherSession session, GameInfo game,
        Func<CancellationToken, Task<bool>> install, string title = "Set up Freedoom Phase 2", string? confirmation = null, string? previousAttempt = null)
    {
        Title = title; Confirmation = confirmation ?? GuidedEngineInstallService.ConfirmationMessage();
        HasPreviousAttempt = previousAttempt != null;
        InstallLabel = HasPreviousAttempt ? "Retry engine install" : "Install engine";
        if (previousAttempt != null) Message = previousAttempt;
        this.session = session; this.game = game; this.install = install;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
    }

    internal Task StartAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanStart) return Work;
        started = true; IsBusy = true;
        Message = "Checking the pinned engine and preparing its download…";
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work = completion.Task; // Publish ownership before invoking any synchronous callbacks.
        Changed();
        _ = RunAsync(completion);
        return Work;
    }

    async Task RunAsync(TaskCompletionSource completion)
    {
        try
        {
            var installed = false; string? failure = null;
            try
            {
                await session.RunAsync(async () =>
                {
                    installed = await LauncherSession.WithOperationCancellationAsync(cancellation.Token,
                        () => install(cancellation.Token));
                    cancellation.Token.ThrowIfCancellationRequested();
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { failure = ex.Message; }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false; Succeeded = installed && !CancellationRequested && failure == null;
                if (CancellationRequested)
                    Message = game.IsInstalled
                        ? "Setup canceled. The engine remains installed; no game-data handoff or play was started. Close this window to inspect the library entry."
                        : "Setup canceled. Retained files or recovery work may remain. No game-data handoff or play was started; inspect the entry before retrying.";
                else if (failure != null) Message = "Engine setup stopped: " + failure;
                else Message = installed ? "Engine installed. Opening the separate game-data setup…"
                    : "Engine setup did not complete. Inspect this entry before trying again.";
                Changed();
            });
            completion.TrySetResult();
        }
        catch (Exception ex) { completion.TrySetException(ex); }
    }

    internal void Cancel()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (cancelled || disposed) return;
        cancelled = true; Succeeded = false;
        Message = IsBusy ? CancellationMessage()
            : "Setup canceled. Nothing is played automatically.";
        IsProgressIndeterminate = true; cancellation.Cancel(); Changed();
    }

    string CancellationMessage() => observation.Phase >= GuidedEnginePhase.Committing
        ? "Cancellation requested. Finishing safely; an activated engine is retained. No game-data handoff or play will start."
        : "Canceling setup. Waiting for the active transfer and installation work to finish safely…";

    internal void ReportProgress(GuidedEngineProgress current)
    {
        var sequence = Interlocked.Increment(ref reportedSequence);
        // Capture immutable values, not the shared library progress field. A model
        // cannot restart; every retry has its own callback and sequence lifetime.
        void Apply()
        {
            if (disposed || !IsBusy || sequence <= appliedSequence || current.Phase < observation.Phase) return;
            if (current.ReceivedBytes < 0 || !Enum.IsDefined(current.Phase)) return;
            if (current.Phase == GuidedEnginePhase.Downloading && observation.Phase == current.Phase &&
                current.ReceivedBytes < observation.ReceivedBytes) return;
            appliedSequence = sequence; observation = current;
            var total = current.TotalBytes;
            var measured = current.Phase == GuidedEnginePhase.Downloading && total is > 0 && current.ReceivedBytes <= total;
            IsProgressIndeterminate = !measured || CancellationRequested;
            progress = measured ? Math.Clamp(100d * current.ReceivedBytes / total!.Value, 0, 100) : 0;
            Message = CancellationRequested ? CancellationMessage() : current.Phase switch
            {
                GuidedEnginePhase.Resolving => "Checking the pinned engine and preparing its download…",
                GuidedEnginePhase.Downloading when measured => $"Downloading engine: {current.ReceivedBytes:N0} of {total:N0} bytes ({progress:0.#}%). Verification is next.",
                GuidedEnginePhase.Downloading => $"Downloading engine: {current.ReceivedBytes:N0} bytes received. Total size is unknown.",
                GuidedEnginePhase.Verifying => "Verifying downloaded engine. No installation has been activated yet.",
                GuidedEnginePhase.Staging => "Preparing engine files. No installation has been activated yet.",
                GuidedEnginePhase.Committing => "Finishing engine installation safely. Cancellation cannot interrupt activation; game-data setup is separate.",
                GuidedEnginePhase.Committed => "Engine committed. Finishing setup checks; game-data setup is a separate step.",
                _ => "Checking engine setup…"
            };
            Changed();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }
    void Changed() { Notify(nameof(Message)); Notify(nameof(Progress)); Notify(nameof(IsProgressIndeterminate)); Notify(nameof(IsBusy)); Notify(nameof(CanStart)); Notify(nameof(CanCancel)); Notify(nameof(Succeeded)); }
    internal void DisposeAfterDrain()
    {
        if (!Work.IsCompleted) throw new InvalidOperationException("Engine setup must drain before disposal.");
        if (disposed) return;
        disposed = true; cancellation.Dispose();
    }
}
