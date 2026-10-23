using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Services;

internal sealed class PortablePreservationFlow(GameManager manager, SettingsViewModel settings, LauncherSession session, LauncherDialogLifetime dialogs)
{
    bool active;
    internal PortablePreservationDialog? Dialog { get; private set; }
    internal async Task OpenAsync(GameInfo? game, Control? origin)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (session.IsClosed || active || settings.KioskLocked || settings.IsReadOnly || game == null) return;
        var key = game.InstanceKey; active = true;
        await session.RunAsync(async () =>
        {
            PortablePreservationViewModel? model = null; PortablePreservationDialog? dialog = null;
            try
            {
                model = new(session, manager, key); dialog = new(model); Dialog = dialog;
                if (origin != null)
                {
                    foreach (var resource in new[] { "ThemeBase", "ThemeLighter", "ThemeDarker", "ThemeBorder", "ThemeText", "ThemeTextSecondary", "ThemeFocusRing" })
                        if (origin.TryFindResource(resource, origin.ActualThemeVariant, out var value)) dialog.Resources[resource] = value;
                    if (origin.TryFindResource("ThemeBase", origin.ActualThemeVariant, out var background) && background is IBrush brush) dialog.Background = brush;
                    if (origin.TryFindResource("ThemeText", origin.ActualThemeVariant, out var foreground) && foreground is IBrush ink) dialog.Foreground = ink;
                }
                dialogs.Track(dialog); _ = dialog.BeginInspection();
                await GameDialogService.ShowWindowAsync(dialog, session.Token);
            }
            catch (OperationCanceledException) when (session.IsClosed) { }
            catch (Exception error) { if (!session.IsClosed) await GameDialogService.ShowMessageBoxAsync("Inspection unavailable; all files preserved. " + error.Message, "Preservation inspection"); }
            finally
            {
                try { model?.Close(); dialog?.Close(); if (model != null) await model.Work; }
                finally
                {
                    try { model?.DisposeAfterDrain(); }
                    finally { Dialog = null; active = false; if (!session.IsClosed && origin != null && TopLevel.GetTopLevel(origin) != null && origin.IsEffectivelyVisible) origin.Focus(); }
                }
            }
        });
    }
}
