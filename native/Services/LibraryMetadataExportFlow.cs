using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Services;

internal sealed class LibraryMetadataExportFlow(GameManager manager, SettingsViewModel settings, LauncherSession session,
    LauncherDialogLifetime dialogs, Func<Task<string?>>? chooseDestination = null)
{
    bool active;
    internal LibraryMetadataExportDialog? Dialog { get; private set; }
    internal async Task OpenAsync(Control? origin)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (active || session.IsClosed || settings.KioskLocked || settings.IsReadOnly) return;
        active = true;
        await session.RunAsync(async () =>
        {
            LibraryMetadataExportViewModel? model = null; LibraryMetadataExportDialog? dialog = null;
            try
            {
                async Task<string?> Choose()
                {
                    if (chooseDestination != null) return await chooseDestination();
                    if (dialog == null || !dialog.StorageProvider.CanSave) throw new IOException("A local save picker is unavailable on this platform.");
                    // Await actual picker completion; close/cancellation never leaves an
                    // unobserved later write. Only a returned local path is usable.
                    using var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Save export-only library metadata to a new file",
                        SuggestedFileName = "relicade-library-metadata.json", DefaultExtension = "json",
                        FileTypeChoices = [new FilePickerFileType("JSON metadata") { Patterns = ["*.json"] }],
                        ShowOverwritePrompt = true,
                    });
                    return file == null ? null : file.TryGetLocalPath() ?? throw new IOException("Choose a local filesystem destination, not remote storage.");
                }
                model = new(manager, settings, session, Choose); dialog = new(model); Dialog = dialog;
                if (origin != null)
                {
                    foreach (var key in new[] { "ThemeBase", "ThemeLighter", "ThemeDarker", "ThemeBorder", "ThemeText", "ThemeTextSecondary", "ThemeFocusRing" })
                        if (origin.TryFindResource(key, origin.ActualThemeVariant, out var value)) dialog.Resources[key] = value;
                    if (origin.TryFindResource("ThemeBase", origin.ActualThemeVariant, out var background) && background is IBrush brush) dialog.Background = brush;
                    if (origin.TryFindResource("ThemeText", origin.ActualThemeVariant, out var foreground) && foreground is IBrush ink) dialog.Foreground = ink;
                }
                dialogs.Track(dialog); await GameDialogService.ShowWindowAsync(dialog, session.Token);
            }
            catch (OperationCanceledException) when (session.IsClosed) { }
            finally
            {
                try { model?.Close(); dialog?.Close(); if (model != null) await model.Work; }
                finally
                {
                    try { model?.Dispose(); }
                    finally { Dialog = null; active = false; if (!session.IsClosed && origin is { IsEffectivelyVisible: true } && TopLevel.GetTopLevel(origin) != null) origin.Focus(); }
                }
            }
        });
    }
}
