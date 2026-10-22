using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class PortableChoiceRenderingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Actual_staged_file_choice_renders_before_install_confirmation()
    {
        await using var renderer = HeadlessUnitTestSession.StartNew(typeof(RealRenderTestApp));
        await renderer.Dispatch<int>(() =>
        {
            var dialog = new PortableExecutableChoiceDialog("Synthetic package preview (no game installed)",
                ["game/game.x86_64", "tools/editor.x86_64"]) { Width = 720, Height = 540 };
            try
            {
                dialog.Show(); dialog.ChoicesList.SelectedIndex = 0;
                dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                Assert.True(dialog.ConfirmButton.IsEnabled); Assert.True(dialog.CancelButton.IsDefault);
                Assert.Null(dialog.SelectedRelativePath);
                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(720, 540), frame.PixelSize);
                using var locked = frame.Lock(); var bytes = new byte[locked.RowBytes * 540]; Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
                var colors = new HashSet<int>(); for (var i = 0; i < bytes.Length; i += 4) colors.Add(BitConverter.ToInt32(bytes, i));
                Assert.True(colors.Count > 32);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, "05-staged-launch-file-choice.png"), FileMode.CreateNew, FileAccess.Write);
                    frame.Save(file);
                }
                output.WriteLine("Actual native staged-file dialog, 720x540 CPU framebuffer; synthetic paths, no installed game, no external request or executable launch. Not desktop-host or gameplay acceptance.");
            }
            finally { dialog.Close(); }
            return Task.FromResult(0);
        }, CancellationToken.None);
    }
}
