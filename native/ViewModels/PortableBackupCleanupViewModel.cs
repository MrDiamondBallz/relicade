using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.ViewModels;

internal enum PortableBackupCleanupUiState { Previewing, Ready, Empty, Applying, Interrupted, Completed, Error, Closed }

/// <summary>One dialog's immutable identity/consent and drainable background work. UI-thread owned.</summary>
internal sealed class PortableBackupCleanupViewModel : ObservableViewModel
{
    readonly LauncherSession session;
    readonly Func<BackupCleanupPreview> preview;
    readonly Func<string, CancellationToken, BackupCleanupResult> apply;
    readonly CancellationTokenSource participation;
    CancellationTokenSource? applying;
    bool busy;
    bool closed;
    internal string InstanceKey { get; }
    internal BackupCleanupPreview? Plan { get; private set; }
    internal BackupCleanupResult? Result { get; private set; }
    internal PortableBackupCleanupUiState State { get; private set; } = PortableBackupCleanupUiState.Previewing;
    internal string Message { get; private set; } = "Reading and hashing retained history… Cancel closes the preview; hashing drains safely in the background.";
    internal bool IsClosed => closed || session.IsClosed;
    internal bool IsBusy => busy;
    internal bool CanApply => !IsClosed && !busy && State == PortableBackupCleanupUiState.Ready && Plan?.Files.Count > 0;
    internal bool CanResume => !IsClosed && !busy && State == PortableBackupCleanupUiState.Interrupted && Plan != null;
    internal bool CanPreview => !IsClosed && !busy && State is PortableBackupCleanupUiState.Error or PortableBackupCleanupUiState.Empty or PortableBackupCleanupUiState.Interrupted;

    internal PortableBackupCleanupViewModel(LauncherSession session, GameManager manager, string instanceKey)
        : this(session, instanceKey, () => PortableBackupCleanupService.Preview(manager, instanceKey), (hash, token) => PortableBackupCleanupService.Apply(manager, instanceKey, hash, token)) { }

    // Instance-local seams for deterministic lifecycle/boundary tests, never a global manager or Core bypass in production.
    internal PortableBackupCleanupViewModel(LauncherSession session, string instanceKey, Func<BackupCleanupPreview> preview, Func<string, CancellationToken, BackupCleanupResult> apply)
    {
        this.session = session; InstanceKey = instanceKey; this.preview = preview; this.apply = apply;
        participation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
    }

    internal Task PreviewAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsClosed || busy) return Task.CompletedTask;
        busy = true; Plan = null; Result = null;
        State = PortableBackupCleanupUiState.Previewing;
        Message = "Reading and hashing retained history… Cancel closes the preview; hashing drains safely in the background.";
        Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var plan = await Task.Run(() =>
                {
                    participation.Token.ThrowIfCancellationRequested();
                    var value = preview(); // Existing Core preview is deliberately noncancellable.
                    participation.Token.ThrowIfCancellationRequested();
                    return value;
                });
                await UpdateAsync(() =>
                {
                    Plan = plan; State = plan.Files.Count == 0 ? PortableBackupCleanupUiState.Empty : PortableBackupCleanupUiState.Ready;
                    Message = plan.Files.Count == 0 ? "Nothing to clean. No redundant payload files are eligible." :
                        "Review every file below. Clean authorizes only this displayed plan SHA256, not a replacement plan.";
                });
            }
            catch (OperationCanceledException) when (participation.IsCancellationRequested) { }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally { await UpdateAsync(() => busy = false); }
        });
    }

    internal Task ConfirmAsync() => CanApply ? ApplyAsync() : Task.CompletedTask;
    internal Task ResumeAsync() => CanResume ? ApplyAsync() : Task.CompletedTask;

    Task ApplyAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var hash = Plan!.PlanSha256; // Consent is immutable even if selection changes elsewhere.
        busy = true; State = PortableBackupCleanupUiState.Applying;
        Message = "Cleaning only approved redundant copies… Cancel requests a stop at the next safe boundary. Removed copies are not rolled back.";
        applying = CancellationTokenSource.CreateLinkedTokenSource(participation.Token);
        var cancellation = applying;
        Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var result = await Task.Run(() => apply(hash, cancellation.Token));
                await UpdateAsync(() =>
                {
                    Result = result;
                    State = result.Completed ? PortableBackupCleanupUiState.Completed : PortableBackupCleanupUiState.Interrupted;
                    Message = Describe(result);
                });
            }
            catch (BackupCleanupInterruptedException ex)
            {
                await UpdateAsync(() => { Result = ex.Result; State = PortableBackupCleanupUiState.Interrupted; Message = Describe(ex.Result) + " " + ex.Message; });
            }
            catch (OperationCanceledException)
            {
                await UpdateAsync(() => { State = PortableBackupCleanupUiState.Error; Message = "Cancelled before cleanup progress was reported. No completion is claimed. Preview again and explicitly confirm before another attempt."; });
            }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(applying, cancellation)) applying = null;
                    cancellation.Dispose();
                    if (!IsClosed) { busy = false; Changed(); }
                });
            }
        });
    }

    internal void Cancel()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsClosed) return;
        if (State == PortableBackupCleanupUiState.Applying)
        {
            applying?.Cancel();
            Message = "Cancellation requested. Waiting for truthful invocation-local results; this is not rollback or an immediate cancellation guarantee.";
            Changed();
        }
        else Close();
    }

    internal void Close()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (closed) return;
        closed = true; State = PortableBackupCleanupUiState.Closed;
        participation.Cancel(); // Work is still registered in LauncherSession until it drains.
        Changed();
    }

    internal void DisposeAfterDrain() { participation.Dispose(); }
    Task UpdateAsync(Action update) => Dispatcher.UIThread.InvokeAsync(() => { if (!IsClosed) { update(); Changed(); } }).GetTask();
    void Fail(Exception ex) { State = PortableBackupCleanupUiState.Error; Message = ex.Message + " Preview again and explicitly confirm the new plan. No completion or unreported deletion count is claimed."; }
    static string Describe(BackupCleanupResult result) => $"{(result.Completed ? "Completed" : "Incomplete")}. Removed THIS invocation: {result.DeletedFiles} files / {result.ReclaimedBytes} bytes. " +
        (result.Completed ? "All approved targets verified absent; retained generations remain." : "Removed redundant copies are not rolled back. Resolve the cause, then explicitly Resume this same approved hash; no automatic retry.");
    void Changed() => Notify(null);
}
