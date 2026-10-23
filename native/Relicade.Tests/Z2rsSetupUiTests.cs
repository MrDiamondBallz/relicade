using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class Z2rsSetupUiTests
{
    static uint Crc(byte[] bytes)
    {
        uint value = uint.MaxValue;
        foreach (var b in bytes) { value ^= b; for (var i = 0; i < 8; i++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0); }
        return value ^ uint.MaxValue;
    }
    [AvaloniaTheory][InlineData("keyboard")][InlineData("controller")]
    public async Task Actual_setup_labels_and_explicit_confirmation_preserve_original_data(string input)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        f.Game.Repository = "troyedwardsjr/z2rs"; await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]);
        f.Definition = f.Definition with { Label = "Synthetic NES fixture, not a ROM", FileName = "zelda2.nes", Md5 = "",
            NesBody = new(Convert.ToHexStringLower(SHA1.HashData(f.Data)), Crc(f.Data)) };
        await using var session = new LauncherSession();
        var setup = f.Setup(); var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        var dialog = new OwnedGameSetupDialog(model) { Width = 320, Height = 360 };
        model.PickFile = () => Task.FromResult<string?>(f.Source);
        void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Tab()
        { dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Layout(); }
        try
        {
            Assert.Equal("*.nes", model.FilePattern); Assert.Equal("NES game data", model.FileTypeLabel);
            Assert.Contains("engine may remember the ROM path", model.Requirement); Assert.Contains("disables Discord presence", model.Requirement);
            Assert.DoesNotContain("MD5", model.Requirement);
            dialog.Show(); Layout(); Assert.False(dialog.DownloadButton.IsVisible); Assert.False(dialog.PlayButton.IsVisible);
            dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work; Layout();
            Assert.True(model.CanConfirm); Assert.False(File.Exists(model.Plan!.Destination));
            if (input == "keyboard")
            {
                for (var i = 0; i < 80 && !dialog.ConfirmButton.IsFocused; i++) Tab();
                Assert.True(dialog.ConfirmButton.IsFocused);
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else
            {
                GamepadFocusChrome.SetActive(true, dialog);
                foreach (var direction in new[] { Relicade.Services.NavigationDirection.Right, Relicade.Services.NavigationDirection.Down })
                    for (var i = 0; i < 80 && !dialog.ConfirmButton.IsFocused; i++)
                    { GamepadModalDialogNavigation.Instance.TryHandleNavigation(direction); Layout(); }
                Assert.True(dialog.ConfirmButton.IsFocused); Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm());
            }
            await dialog.Work; Assert.Equal(OwnedGameSetupUiState.Completed, model.State);
            Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(f.Data, File.ReadAllBytes(model.Plan.Destination));
            Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); }
    }
}
