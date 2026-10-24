using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Services;

internal sealed class OwnedGameSetupFlow(GameManager manager, SettingsViewModel settings, LauncherSession session, LauncherDialogLifetime dialogs, Action<bool>? afterLaunch = null)
{
    bool active;
    internal Task OpenAsync(GameInfo? game, Control? origin) => OpenCoreAsync(game, origin, false);
    internal Task OpenFreedoomAsync(GameInfo? game, Control? origin) => OpenCoreAsync(game, origin, true);
    async Task OpenCoreAsync(GameInfo? game, Control? origin, bool freedoom)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (session.IsClosed || active || settings.KioskLocked || settings.IsReadOnly) return;
        var key = game?.InstanceKey; if (string.IsNullOrWhiteSpace(key)) return;
        active = true;
        await session.RunAsync(async () =>
        {
            OwnedGameSetupViewModel? model = null; OwnedGameSetupDialog? dialog = null;
            try
            {
                if (freedoom && (game == null || !OwnedGameSetupService.OffersFreedoom(game))) throw new IOException("Freedoom setup is unavailable for this project.");
                if (!game!.IsInstalled && (freedoom || OwnedGameSetupService.Requirement(game) == OwnedGameSetupService.Z2rs ||
                    GuidedEngineInstallService.OffersRequiredFirstInstall(game)) &&
                    !await InstallEngineAsync(game)) return;
                session.Token.ThrowIfCancellationRequested();
                var setup = new OwnedGameSetupSession(manager, key, freedoom ? OwnedGameSetupService.CurrentFreedoomPhase2 : null);
                model = new OwnedGameSetupViewModel(session, key, setup);
                if (freedoom)
                {
                    await InitializeFreedoomAsync(model, setup, session.Token,
                        token => GameLaunchService.LaunchFreedoomAsync(game!, manager.GamesFolder, token), afterLaunch);
                }
                dialog = new OwnedGameSetupDialog(model); dialogs.Track(dialog);
                await GameDialogService.ShowWindowAsync(dialog, session.Token);
            }
            catch (OperationCanceledException) when (session.IsClosed) { }
            catch (Exception ex) { if (!session.IsClosed) await GameDialogService.ShowMessageBoxAsync(ex.Message, "Game setup unavailable"); }
            finally
            {
                model?.Close(); dialog?.Close();
                if (dialog != null) await dialog.Work;
                model?.DisposeAfterDrain(); active = false;
                if (!session.IsClosed && origin != null && TopLevel.GetTopLevel(origin) != null && origin.IsEffectivelyVisible) origin.Focus();
            }
        });
    }
    async Task<bool> InstallEngineAsync(GameInfo game)
    {
        var progress = CreateEngineProgress(game, manager, settings.Current, session);
        var window = new FreedoomEngineProgressDialog(progress); dialogs.Track(window);
        try
        {
            await GameDialogService.ShowWindowAsync(window, session.Token);
            await progress.Work;
            return progress.Succeeded && !progress.CancellationRequested && !session.IsClosed;
        }
        finally
        {
            if (!progress.Succeeded) progress.Cancel();
            await progress.Work;
            window.Close(); progress.DisposeAfterDrain();
        }
    }
    internal static FreedoomEngineProgressViewModel CreateEngineProgress(GameInfo game, GameManager manager,
        AppSettings settings, LauncherSession session, IGameDownloadDialogs? engineDialogs = null, OwnedDataRequirement? definition = null)
    {
        // Preserve the existing pre-prompt refusal and original library binding.
        // The installer also repeats its current-target checks when Install is chosen.
        var prepared = GuidedEngineInstallService.PrepareFirstInstall(game, manager, session.Token, definition);
        var previous = GuidedInstallIntent.Inspect(GuidedInstallIntent.For(game, manager, prepared));
        if (previous?.Stage is "Committing" or "Completed")
            throw new IOException("Previous engine setup reached its commit boundary. Inspect the installed entry or existing recovery controls before retrying. No transfer or recovery was started.");
        FreedoomEngineProgressViewModel? progress = null;
        progress = new(session, game, token =>
        {
            prepared.Context.ValidateDownloadRequest(token);
            return GuidedEngineInstallService.InstallAsync(game, manager, settings, token,
                (_, _) => Task.FromResult(true), engineDialogs, definition, progress!.ReportProgress); // Only the explicit Install action invokes this.
        }, prepared.Project.Title, prepared.Project.Confirmation, previous == null ? null
            : $"Previous attempt: {previous.Stage}. It may have stopped or still be active; nothing resumes automatically. Retry starts a new transfer only after fresh source and entry checks. Prior attempts and personal files are retained.");
        return progress;
    }
    internal static async Task InitializeFreedoomAsync(OwnedGameSetupViewModel model, OwnedGameSetupSession setup,
        CancellationToken token, Func<CancellationToken, Task<bool>> launch, Action<bool>? afterLaunch)
    {
        var ready = false; string? repairMessage = null;
        try { await Task.Run(() => setup.ValidateReady(token)); ready = true; }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Relicade.Core.Services.OwnedDataNeedsRepairException ex) { repairMessage = ex.Message; }
        ConfigureFreedoomPlay(model, ready,
            operation => Task.Run(() => setup.ValidateReady(operation)), launch, afterLaunch, GameLaunchService.FreedoomPlayUnavailableReason);
        ConfigureFreedoomDownload(model, setup, FreedoomDataDownload.DownloadAsync);
        if (repairMessage != null) model.ShowPreservedDataDiagnostic(repairMessage);
    }
    internal static void ConfigureFreedoomDownload(OwnedGameSetupViewModel model, OwnedGameSetupSession setup,
        Func<IProgress<double>?, CancellationToken, Task<FreeDataDownloadResult>> download)
    {
        model.ConfigureFreeDataDownload(async (progress, token) =>
        {
            await Task.Run(() => setup.ValidateDownloadRequest(token));
            token.ThrowIfCancellationRequested();
            return await download(progress, token);
        });
    }
    internal static void ConfigureFreedoomPlay(OwnedGameSetupViewModel model, bool ready,
        Func<CancellationToken, Task> validate, Func<CancellationToken, Task<bool>> launch, Action<bool>? afterLaunch,
        string? unavailableReason = null)
    {
        model.ConfigureFreedoom(async () =>
        {
            var token = model.OperationCancellation;
            token.ThrowIfCancellationRequested();
            await validate(token);
            token.ThrowIfCancellationRequested();
            return await launch(token);
        }, ready, afterLaunch, unavailableReason);
    }

}
