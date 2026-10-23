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
    Func<Task<bool>>? play;
    Action<bool>? afterLaunch;
    internal CancellationToken OperationCancellation => cancellation.Token;
    internal bool IsFreedoom => play != null;
    bool isNes;
    internal string FileTypeLabel => isNes ? "NES game data" : IsFreedoom ? "Freedoom Phase 2 data" : "Nintendo 64 big-endian data";
    internal string FilePattern => isNes ? "*.nes" : IsFreedoom ? "*.wad" : "*.z64";
    internal string Title => IsFreedoom ? "Play Freedoom Phase 2" : "Finish game setup";
    internal bool CanPlay => IsFreedoom && !IsClosed && !busy && State == OwnedGameSetupUiState.Completed;
    internal void ConfigureFreedoom(Func<Task<bool>> launch, bool ready, Action<bool>? completed = null)
    {
        play = launch; afterLaunch = completed;
        if (ready) State = OwnedGameSetupUiState.Completed;
        Message = ready ? "The retained Freedoom copy is verified for this package. Play rechecks it before starting."
            : "Download Freedoom v0.13.0 from its official release, extract the ZIP and choose freedoom2.wad. This dialog does not download files.";
        Changed();
    }
    Func<IProgress<double>?, CancellationToken, Task<FreeDataDownloadResult>>? downloadData;
    internal bool HasFreeDataDownload => downloadData != null;
    internal string DataPolicy => HasFreeDataDownload
        ? "Only an explicit free-data download is offered. The verified ZIP and WAD are retained locally, including original notices in the ZIP. No commercial ROM or BIOS is downloaded."
        : "Local only. Originals and prior copies are preserved; no ROM or BIOS download is provided.";
    internal void ConfigureFreeDataDownload(Func<IProgress<double>?, CancellationToken, Task<FreeDataDownloadResult>> download)
    {
        downloadData = download;
        if (State != OwnedGameSetupUiState.Completed)
            Message = "Get Freedoom v0.13.0 from its official GitHub release, or choose your existing freedoom2.wad. The download retains a 24 MB ZIP and 29 MB WAD; confirming the later copy needs another 29 MB. Downloading does not install or start the engine.";
        Changed();
    }
    internal void ShowPreservedDataDiagnostic(string message)
    {
        Message = message; Changed();
    }
    internal Task DownloadAndPreviewAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanChoose || downloadData == null) return Task.CompletedTask;
        busy = true; Plan = null; State = OwnedGameSetupUiState.Inspecting;
        Message = "Downloading and checking the pinned free-data archive…"; Changed();
        return session.RunAsync(async () =>
        {
            var acceptingProgress = true;
            try
            {
                var progress = new Progress<double>(value =>
                {
                    if (acceptingProgress && !IsClosed)
                    { Message = $"Downloading free game data: {Math.Clamp(value, 0, 1):P0}. Hash and archive checks must still finish."; Changed(); }
                });
                var downloaded = await downloadData(progress, cancellation.Token);
                acceptingProgress = false; cancellation.Token.ThrowIfCancellationRequested();
                var plan = await Task.Run(() => preview(downloaded.Source, cancellation.Token));
                await UpdateAsync(() =>
                {
                    Plan = plan; State = OwnedGameSetupUiState.Confirm;
                    Message = (plan.Repair == null ? "Free data verified. Review and confirm the separate local copy. "
                        : "Replacement preview: preserve the changed copy in a separate backup before replacing it with verified free data. Review both destinations before confirming. ")
                        + "The original ZIP, with COPYING and credits, is retained at: " + downloaded.Archive;
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally { acceptingProgress = false; await UpdateAsync(() => busy = false); }
        });
    }
    internal Task PlayAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanPlay) return Task.CompletedTask;
        busy = true; Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var started = await play!();
                if (started && !IsClosed) afterLaunch?.Invoke(true);
                await UpdateAsync(() => Message = started ? "Launch started. Gameplay compatibility is not certified." : "Launch did not complete. Review the reported error before retrying.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { await UpdateAsync(() => Fail(ex)); }
            finally { await UpdateAsync(() => busy = false); }
        });
    }
    internal const string OfficialFreedoomRelease = "https://github.com/freedoom/freedoom/releases/tag/v0.13.0";
    internal Func<Task<bool>> OpenDownload { get; set; } = () => Task.FromResult(false);
    internal Task OpenDownloadAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsFreedoom || !CanChoose) return Task.CompletedTask;
        busy = true; Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                if (!await OpenDownload()) await UpdateAsync(() => Message = "Could not open the browser. Open this official release yourself: " + OfficialFreedoomRelease);
            }
            catch (Exception) { await UpdateAsync(() => Message = "Could not open the browser. Open this official release yourself: " + OfficialFreedoomRelease); }
            finally { await UpdateAsync(() => busy = false); }
        });
    }
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
        : this(session, key, setup.Description, setup.Preview, setup.Apply, setup.RefreshAsync) { isNes = setup.IsNes; }
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
                    Message = plan.Repair == null
                        ? "Review the source, destination and storage below. Confirm only if you may lawfully use this file. The original is retained."
                        : "Replacement preview: the changed copy will be preserved in a separate backup before the active copy is replaced. Review both destinations and the extra storage below. Nothing changes until you confirm."; });
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
        Message = plan.Repair == null
            ? "Copying verified game data locally. Cancel requests a safe stop; a committed copy is retained if completion is interrupted."
            : "Preserving the changed copy before replacing it with verified data. Cancel requests a safe stop; any retained backup or committed replacement remains available.";
        Changed();
        return session.RunAsync(async () =>
        {
            try
            {
                var result = await Task.Run(() => apply(plan, cancellation.Token));
                await refresh();
                await UpdateAsync(() => { State = result.Completed ? OwnedGameSetupUiState.Completed : OwnedGameSetupUiState.Error;
                    Message = result.Completed ? (IsFreedoom ? "Freedoom verified and retained locally. Select Play Freedoom Phase 2. Ordinary engine Launch keeps your existing WAD configuration." : "Game data verified and retained locally. Close this window and select Launch. This does not certify gameplay or hardware compatibility.")
                        : "Completion was not established. Keep the original and any retained copy; review setup before retrying.";
                    if (result.Completed && plan.Repair != null) Message += " Previous data retained at: " + plan.Repair.RetainedDestination;
                });
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
