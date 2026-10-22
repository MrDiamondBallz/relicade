using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
namespace Relicade.Tests;
public sealed class OwnedDataRecoveryRenderingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(760, 660, 100)][InlineData(320, 360, 100)][InlineData(320, 360, 200)]
    public async Task Native_replacement_preview_keeps_confirmation_accessible(int width, int height, int percent)
    {
        await using var renderer = HeadlessUnitTestSession.StartNew(typeof(RealRenderTestApp));
        await renderer.Dispatch<int>(async () =>
        {
            await using var session = new LauncherSession();
            var plan = new OwnedDataPreview(new string('a', 64), "/synthetic/original/freedoom2.wad",
                "/synthetic/owned-data/freedoom2.wad", new string('b', 32), new string('c', 64), 8, false,
                new OwnedDataRepair("/synthetic/owned-data/retained-" + new string('d', 64) + "-freedoom2.wad", 4, new string('d', 64), false));
            var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic repair preview, no game installed. Four-byte test data, not a real WAD.",
                (_, _) => plan, (_, _) => throw new Exception("Render must not copy"), () => Task.CompletedTask);
            model.ConfigureFreedoom(() => throw new Exception("Render must not launch"), false);
            var store = new Store(); store.Current.InterfaceScalePercent = percent;
            var owner = new Window { Content = new TextBlock { Text = "Synthetic owner" }, Width = 1200, Height = 800 };
            using var scaling = new DesktopInterfaceScaling(owner, new SettingsViewModel(store), () => new Size(3840, 2160));
            var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
            model.PickFile = () => Task.FromResult<string?>(plan.Source);
            try
            {
                owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                DesktopInterfaceScaling.PrepareDialog(dialog, owner); dialog.Show();
                await model.ChooseAsync(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                Assert.Equal(percent, scaling.AppliedPercent);
                foreach (var button in new[] { dialog.ConfirmButton, dialog.CloseButton })
                {
                    var top = button.TranslatePoint(default, dialog)!.Value;
                    var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), dialog)!.Value;
                    Assert.InRange(top.X, 0, dialog.ClientSize.Width); Assert.InRange(top.Y, 0, dialog.ClientSize.Height);
                    Assert.InRange(end.X, 0, dialog.ClientSize.Width); Assert.InRange(end.Y, 0, dialog.ClientSize.Height);
                }
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.ConfirmButton));
                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize);
                using var locked = frame.Lock(); var bytes = new byte[locked.RowBytes * frame.PixelSize.Height];
                Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
                var colors = new HashSet<int>(); for (var i = 0; i < bytes.Length; i += 4) colors.Add(BitConverter.ToInt32(bytes, i));
                Assert.True(colors.Count > 32);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, $"10-data-repair-{width}x{height}-{percent}.png"), FileMode.CreateNew, FileAccess.Write); frame.Save(file);
                }
                output.WriteLine("Actual native CPU render; synthetic preview and paths, no real game data, installation, network, copy or execution. Not desktop-host or platform acceptance.");
            }
            finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); owner.Close(); }
            return 0;
        }, CancellationToken.None);
    }
    sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
