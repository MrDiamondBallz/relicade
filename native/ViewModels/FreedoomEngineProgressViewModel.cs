using System.ComponentModel;
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
        game.PropertyChanged += GameChanged;
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
        Message = IsBusy ? "Canceling setup. Waiting for the active transfer and installation work to finish safely…"
            : "Setup canceled. Nothing is played automatically.";
        cancellation.Cancel(); Changed();
    }

    void GameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameInfo.DownloadProgress)) return;
        var current = Math.Clamp(game.DownloadProgress, 0, 100);
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || !IsBusy || CancellationRequested) return;
            progress = current;
            Message = $"Engine download: {Progress:0}%. Verification and installation must still finish.";
            Changed();
        });
    }
    void Changed() { Notify(nameof(Message)); Notify(nameof(Progress)); Notify(nameof(IsBusy)); Notify(nameof(CanStart)); Notify(nameof(CanCancel)); Notify(nameof(Succeeded)); }
    internal void DisposeAfterDrain()
    {
        if (!Work.IsCompleted) throw new InvalidOperationException("Engine setup must drain before disposal.");
        if (disposed) return;
        disposed = true; game.PropertyChanged -= GameChanged; cancellation.Dispose();
    }
}
