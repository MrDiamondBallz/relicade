using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class PortableExecutableChoiceDialogTests
{
    [AvaloniaTheory]
    [InlineData(720, 540, false)][InlineData(480, 420, false)]
    [InlineData(720, 540, true)][InlineData(480, 420, true)]
    public void Long_valid_relative_paths_keep_confirmation_visible_and_operable(int width, int height, bool keyboard)
    {
        var path = string.Join('/', Enumerable.Range(0, 36).Select(i => $"segment-{i:00}-" + new string('x', 32))) + "/game.exe";
        Relicade.Core.Services.PortableInstallationTransaction.ValidateRelative(path);
        var dialog = new PortableExecutableChoiceDialog("Synthetic long-path package", [path, "tools/editor.exe"]) { Width = width, Height = height };
        try
        {
            dialog.Show(); dialog.ChoicesList.SelectedIndex = 0; dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var button in new[] { dialog.ConfirmButton, dialog.CancelButton })
            {
                var point = button.TranslatePoint(default, dialog); Assert.NotNull(point);
                Assert.InRange(point.Value.Y, 0, dialog.ClientSize.Height - button.Bounds.Height);
            }
            if (keyboard)
            {
                Assert.True(dialog.ConfirmButton.Focus());
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                // Enter closes this window on key-down; its disposed headless host cannot receive key-up.
                if (dialog.IsVisible) dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else
            {
                var nav = GamepadModalDialogNavigation.Instance;
                Assert.True(nav.FocusDialogControl(dialog.ConfirmButton)); Assert.True(nav.TryHandleConfirm());
            }
            Assert.Equal(path, dialog.SelectedRelativePath); Assert.False(dialog.IsVisible);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaFact]
    public void Choosing_a_row_is_not_installation_and_close_rejects_late_confirmation()
    {
        var dialog = new PortableExecutableChoiceDialog("Synthetic package", ["bin/game.exe", "tools/editor.exe"]);
        try
        {
            Assert.False(dialog.ConfirmButton.IsEnabled); Assert.False(dialog.ConfirmButton.IsDefault); Assert.True(dialog.CancelButton.IsDefault);
            dialog.Show(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            dialog.ChoicesList.SelectedIndex = 1;
            Assert.True(dialog.ConfirmButton.IsEnabled); Assert.Null(dialog.SelectedRelativePath);
            dialog.Close(); dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(dialog.SelectedRelativePath); Assert.False(dialog.IsVisible);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public void Controller_can_choose_or_cancel_without_executing_anything(bool cancel)
    {
        var dialog = new PortableExecutableChoiceDialog("Synthetic package", ["bin/game.exe", "tools/editor.exe"]);
        var nav = GamepadModalDialogNavigation.Instance;
        try
        {
            GamepadFocusChrome.SetActive(true, dialog); dialog.Show(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            nav.RefreshDialogButtons(); Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.ChoicesList.Focusable);
            Assert.Contains(dialog.ChoicesList, GamepadModalDialogNavigation.CollectDialogFocusableControls(dialog));
            Assert.True(nav.TryHandleNavigation(Relicade.Services.NavigationDirection.Down));
            if (cancel) { Assert.True(nav.TryHandleCancel()); Assert.Null(dialog.SelectedRelativePath); }
            else { Assert.True(nav.TryHandleConfirm()); Assert.Equal("tools/editor.exe", dialog.SelectedRelativePath); }
            Assert.False(dialog.IsVisible);
        }
        finally { GamepadFocusChrome.SetActive(false); dialog.Close(); }
    }

    [AvaloniaFact]
    public void Moving_to_install_button_keeps_the_visible_file_choice()
    {
        var dialog = new PortableExecutableChoiceDialog("Synthetic package", ["bin/game.exe", "tools/editor.exe"]);
        var nav = GamepadModalDialogNavigation.Instance;
        try
        {
            GamepadFocusChrome.SetActive(true, dialog); dialog.Show(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            dialog.ChoicesList.SelectedIndex = 1;
            Assert.True(nav.FocusDialogControl(dialog.ConfirmButton));
            Assert.True(dialog.ConfirmButton.IsEnabled); Assert.True(nav.TryHandleConfirm());
            Assert.Equal("tools/editor.exe", dialog.SelectedRelativePath);
        }
        finally { GamepadFocusChrome.SetActive(false); dialog.Close(); }
    }

    [AvaloniaFact]
    public async Task Host_cancellation_closes_and_drains_the_actual_dialog_without_a_selection()
    {
        using var cancel = new CancellationTokenSource();
        var dialog = new PortableExecutableChoiceDialog("Synthetic package", ["bin/game.exe", "tools/editor.exe"]);
        var showing = GameDialogService.ShowWindowAsync(dialog, cancel.Token);
        try
        {
            Assert.True(dialog.IsVisible); cancel.Cancel(); Dispatcher.UIThread.RunJobs();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(dialog.IsVisible); Assert.Null(dialog.SelectedRelativePath);
        }
        finally { dialog.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Worker_transaction_awaits_actual_modal_choice_without_releasing_authority(bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), "modal-install-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "Game");
        PortableExecutableChoiceDialog? dialog = null;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transaction = new Relicade.Core.Services.PortableInstallationTransaction(live, new()
        {
            ChooseExecutableAsync = async (choices, token) => await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                dialog = new PortableExecutableChoiceDialog("Synthetic package", choices);
                dialog.Opened += (_, _) => opened.TrySetResult();
                try { await GameDialogService.ShowWindowAsync(dialog, token); return dialog.SelectedRelativePath; }
                finally { dialog.Close(); }
            })
        });
        var operation = Task.Run(() => transaction.InstallAsync(path =>
        {
            File.WriteAllText(Path.Combine(path, "game.exe"), "non-runnable owned fixture");
            File.WriteAllText(Path.Combine(path, "editor.exe"), "non-runnable owned fixture");
            return Task.CompletedTask;
        }, "fixture-v1"));
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(dialog); Assert.False(operation.IsCompleted);
            Assert.ThrowsAny<IOException>(() => new Relicade.Core.Services.PortableInstallationTransaction(live).AcquireLease());
            Assert.False(File.Exists(Path.Combine(live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName)));
            if (cancel) dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else { dialog.ChoicesList.SelectedIndex = 1; dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            if (cancel)
                await Assert.ThrowsAsync<Relicade.Core.Services.PortableExecutableSelectionCancelledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
            else
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("game.exe", transaction.ReadCommitted()!.SelectedExecutable);
            }
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            dialog?.Close();
            try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Relicade.Core.Services.PortableExecutableSelectionCancelledException) { }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

}
