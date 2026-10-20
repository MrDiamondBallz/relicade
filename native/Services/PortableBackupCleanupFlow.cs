using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Services;

/// <summary>One active cleanup dialog/workflow per launcher session, including its drain.</summary>
internal sealed class PortableBackupCleanupFlow(GameManager manager, SettingsViewModel settings, LauncherSession session, LauncherDialogLifetime dialogs)
{
    bool active;
    internal bool IsActive => active;
    internal async Task OpenAsync(GameInfo? game, Control? origin)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (session.IsClosed || active || settings.KioskLocked || settings.IsReadOnly) return;
        var key = game?.InstanceKey; // Capture before any await; never consult later selection.
        if (string.IsNullOrWhiteSpace(key)) return;
        active = true;
        await session.RunAsync(async () =>
        {
            var model = new PortableBackupCleanupViewModel(session, manager, key);
            var dialog = new PortableBackupCleanupDialog(model);
            try
            {
                dialogs.Track(dialog);
                dialog.BeginPreview();
                await GameDialogService.ShowWindowAsync(dialog, session.Token);
            }
            catch (OperationCanceledException) when (session.IsClosed) { }
            finally
            {
                model.Close();
                dialog.Close();
                // Closing revokes participation immediately, but no manager/session
                // dependency can be released until the noncancellable preview drains.
                await dialog.Work;
                model.DisposeAfterDrain();
                active = false;
                if (!session.IsClosed && origin != null && TopLevel.GetTopLevel(origin) != null && origin.IsEffectivelyVisible)
                    origin.Focus();
            }
        });
    }
}
