using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
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
                if (freedoom && !game!.IsInstalled) throw new IOException("Install the supported UZDoom 5.0.3 Linux x64 AppImage before setting up Freedoom.");
                var setup = new OwnedGameSetupSession(manager, key, freedoom ? OwnedGameSetupService.FreedoomPhase2 : null);
                model = new OwnedGameSetupViewModel(session, key, setup);
                if (freedoom)
                {
                    var ready = false;
                    try { await Task.Run(() => setup.ValidateReady(session.Token)); ready = true; }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                    ConfigureFreedoomPlay(model, ready,
                        token => Task.Run(() => setup.ValidateReady(token)),
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
    internal static void ConfigureFreedoomPlay(OwnedGameSetupViewModel model, bool ready,
        Func<CancellationToken, Task> validate, Func<CancellationToken, Task<bool>> launch, Action<bool>? afterLaunch)
    {
        model.ConfigureFreedoom(async () =>
        {
            var token = model.OperationCancellation;
            token.ThrowIfCancellationRequested();
            await validate(token);
            token.ThrowIfCancellationRequested();
            return await launch(token);
        }, ready, afterLaunch);
    }

}
