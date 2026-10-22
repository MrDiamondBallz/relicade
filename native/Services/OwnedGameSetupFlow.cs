using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Services;

internal sealed class OwnedGameSetupFlow(GameManager manager, SettingsViewModel settings, LauncherSession session, LauncherDialogLifetime dialogs)
{
    bool active;
    internal async Task OpenAsync(GameInfo? game, Control? origin)
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
                model = new OwnedGameSetupViewModel(session, key, new OwnedGameSetupSession(manager, key));
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
}
