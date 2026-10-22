using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.ViewModels;

internal enum OwnedGameSetupUiState { Choose, Inspecting, Confirm, Copying, Completed, Error, Closed }
internal sealed class OwnedGameSetupViewModel : ObservableViewModel
{
    readonly LauncherSession session;
    readonly Func<string, CancellationToken, OwnedDataPreview> preview;
    readonly Func<OwnedDataPreview, CancellationToken, OwnedDataImportResult> apply;
    readonly Func<Task> refresh;
    readonly CancellationTokenSource cancellation;
    bool busy, closed;
    internal Func<Task<string?>> PickFile { get; set; } = () => Task.FromResult<string?>(null);
    internal string InstanceKey { get; }
    internal string Requirement { get; }
    internal OwnedDataPreview? Plan { get; private set; }
    internal OwnedGameSetupUiState State { get; private set; } = OwnedGameSetupUiState.Choose;
    internal string Message { get; private set; } = "Choose a game-data file you may lawfully use. No game data is downloaded or uploaded.";
    internal bool IsClosed => closed || session.IsClosed;
    internal bool CanChoose => !IsClosed && !busy && State != OwnedGameSetupUiState.Completed;
    internal bool CanConfirm => !IsClosed && !busy && State == OwnedGameSetupUiState.Confirm && Plan != null;
    internal OwnedGameSetupViewModel(LauncherSession session, string key, OwnedGameSetupSession setup)
        : this(session, key, $"{setup.Label}\n{setup.RequiredBytes:N0} bytes; MD5 {setup.VariantMd5}\nThis identifies a variant, not ownership or legal entitlement.", setup.Preview, setup.Apply, setup.RefreshAsync) { }
    internal OwnedGameSetupViewModel(LauncherSession session, string key, string requirement,
        Func<string, CancellationToken, OwnedDataPreview> preview,
        Func<OwnedDataPreview, CancellationToken, OwnedDataImportResult> apply, Func<Task> refresh)
    {
        this.session = session; InstanceKey = key; Requirement = requirement;
        this.preview = preview; this.apply = apply; this.refresh = refresh;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
    }
    internal Task ChooseAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanChoose) return Task.CompletedTask;
        busy = true; Plan = null; State = OwnedGameSetupUiState.Inspecting; Message = "Select a local file. Its original will not be changed."; Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var source = await PickFile();
                cancellation.Token.ThrowIfCancellationRequested();
                if (source == null) { State = OwnedGameSetupUiState.Choose; Message = "No file selected. Nothing was copied."; return; }
                Message = "Checking the package and selected file locally…"; Changed();
                var plan = await Task.Run(() => preview(source, cancellation.Token));
                await UpdateAsync(() => { Plan = plan; State = OwnedGameSetupUiState.Confirm;
                    Message = "Review the source, destination and storage below. Confirm only if you may lawfully use this file. The original is retained."; });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally { await UpdateAsync(() => busy = false); }
        });
    }
    internal Task ConfirmAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanConfirm) return Task.CompletedTask;
        var plan = Plan!; busy = true; State = OwnedGameSetupUiState.Copying;
        Message = "Copying verified game data locally. Cancel requests a safe stop; a committed copy is retained if completion is interrupted."; Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var result = await Task.Run(() => apply(plan, cancellation.Token));
                await refresh();
                await UpdateAsync(() => { State = result.Completed ? OwnedGameSetupUiState.Completed : OwnedGameSetupUiState.Error;
                    Message = result.Completed ? "Game data verified and retained locally. Close this window and select Launch. This does not certify gameplay or hardware compatibility."
                        : "Completion was not established. Keep the original and any retained copy; review setup before retrying."; });
            }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally { await UpdateAsync(() => busy = false); }
        });
    }
    internal void Close()
    {
        Dispatcher.UIThread.VerifyAccess(); if (closed) return;
        closed = true; State = OwnedGameSetupUiState.Closed; cancellation.Cancel(); Changed();
    }
    internal void DisposeAfterDrain() => cancellation.Dispose();
    Task UpdateAsync(Action action) => Dispatcher.UIThread.InvokeAsync(() => { if (!IsClosed) { action(); Changed(); } }).GetTask();
    void Fail(Exception ex) { Plan = null; State = OwnedGameSetupUiState.Error; Message = ex.Message + " Any committed copy is retained. Review a fresh preview before confirming again; no automatic retry or deletion occurs."; }
    void Changed() => Notify(null);
}
