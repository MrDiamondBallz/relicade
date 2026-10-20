using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.ViewModels;

internal enum PortableRollbackUiState { Previewing, Ready, Applying, Completed, Error, Closed }

internal sealed class PortableRollbackViewModel : ObservableViewModel
{
    readonly LauncherSession session;
    readonly Func<PortableRollbackPreview> preview;
    readonly Func<string, CancellationToken, PortableRollbackResult> apply;
    readonly CancellationTokenSource participation;
    CancellationTokenSource? applying;
    bool busy, closed;
    internal string InstanceKey { get; }
    internal PortableRollbackPreview? Plan { get; private set; }
    internal PortableRollbackResult? Result { get; private set; }
    internal PortableRollbackUiState State { get; private set; } = PortableRollbackUiState.Previewing;
    internal string Message { get; private set; } = "Reading and hashing the immediate retained portable generation…";
    internal bool IsClosed => closed || session.IsClosed;
    internal bool IsBusy => busy;
    internal bool CanApply => !IsClosed && !busy && State == PortableRollbackUiState.Ready && Plan != null;
    internal bool CanPreview => !IsClosed && !busy && State == PortableRollbackUiState.Error;

    internal PortableRollbackViewModel(LauncherSession session, GameManager manager, string instanceKey)
        : this(session, instanceKey, () => PortableRollbackService.Preview(manager, instanceKey), (hash, token) => PortableRollbackService.Apply(manager, instanceKey, hash, token)) { }
    internal PortableRollbackViewModel(LauncherSession session, string instanceKey, Func<PortableRollbackPreview> preview, Func<string, CancellationToken, PortableRollbackResult> apply)
    {
        this.session = session; InstanceKey = instanceKey; this.preview = preview; this.apply = apply;
        participation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
    }
    internal Task PreviewAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsClosed || busy) return Task.CompletedTask;
        busy = true; Plan = null; Result = null; State = PortableRollbackUiState.Previewing;
        Message = "Reading and hashing retained payload and current declared data… Cancel closes participation; hashing drains safely.";
        Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var plan = await Task.Run(() => { participation.Token.ThrowIfCancellationRequested(); var p = preview(); participation.Token.ThrowIfCancellationRequested(); return p; });
                await UpdateAsync(() => { Plan = plan; State = PortableRollbackUiState.Ready; Message = "Review the exact previous payload and all limits. Restore authorizes ONLY the displayed immutable hash."; });
            }
            catch (OperationCanceledException) when (participation.IsCancellationRequested) { }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally { await UpdateAsync(() => busy = false); }
        });
    }
    internal Task ConfirmAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanApply) return Task.CompletedTask;
        var hash = Plan!.PlanSha256;
        busy = true; State = PortableRollbackUiState.Applying;
        Message = "Restoring retained payload while keeping CURRENT declared data… Cancellation is honored at safe boundaries; activation may finish safely after cancellation.";
        applying = CancellationTokenSource.CreateLinkedTokenSource(participation.Token);
        var cancellation = applying; Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var result = await Task.Run(() => apply(hash, cancellation.Token));
                await UpdateAsync(() =>
                {
                    Result = result; State = result.Completed ? PortableRollbackUiState.Completed : PortableRollbackUiState.Error;
                    Message = result.Completed ? $"Confirmed committed: {result.ActualVersion} · operation {result.ActualOperationId}. Current declared data retained; save compatibility is not guaranteed." :
                        "Incomplete or unknown outcome. Transaction-internal recovery may already have run; inspect transaction evidence. No new repair, resume or retry is initiated by this dialog.";
                });
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
        if (State == PortableRollbackUiState.Applying)
        {
            applying?.Cancel(); Message = "Cancellation requested; waiting for an observed committed result or an incomplete/unknown outcome. No unchanged-state claim or immediate cancellation guarantee."; Changed();
        }
        else Close();
    }
    internal void Close()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (closed) return;
        closed = true; State = PortableRollbackUiState.Closed; participation.Cancel(); Changed();
    }
    internal void DisposeAfterDrain() => participation.Dispose();
    Task UpdateAsync(Action action) => Dispatcher.UIThread.InvokeAsync(() => { if (!IsClosed) { action(); Changed(); } }).GetTask();
    void Fail(Exception ex) { State = PortableRollbackUiState.Error; Message = ex.Message + " No completion is claimed. Inspect any interrupted evidence; a new valid preview AND fresh explicit confirmation are required. No new repair, resume or retry is initiated by this dialog."; }
    void Changed() => Notify(null);
}
