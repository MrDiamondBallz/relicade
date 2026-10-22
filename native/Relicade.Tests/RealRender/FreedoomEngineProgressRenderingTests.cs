using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Core.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class FreedoomEngineProgressRenderingTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Theory]
    [InlineData("confirmation", 700, 510)][InlineData("confirmation", 580, 440)]
    [InlineData("progress", 700, 510)][InlineData("progress", 580, 440)]
    [InlineData("error-lines", 580, 440)][InlineData("error-short-lines", 580, 440)]
    [InlineData("canceling", 580, 440)][InlineData("retained", 700, 510)][InlineData("error", 580, 440)]
    public async Task Setup_progress_and_cancellation_keep_status_and_footer_readable(string state, int width, int height)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            await using var session = new LauncherSession();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var isError = state.StartsWith("error", StringComparison.Ordinal);
            var game = new GameInfo { Name = "Synthetic engine progress" };
            var model = new FreedoomEngineProgressViewModel(session, game, async _ =>
            {
                entered.TrySetResult(); await release.Task;
                if (state == "error-lines" || state == "error-short-lines") throw new IOException(string.Concat(Enumerable.Repeat("detail\n", state == "error-lines" ? 100 : 30)));
                if (state == "error") throw new IOException("Synthetic destination refusal: /fixture/" + new string('x', 1200));
                return false;
            });
            var window = new FreedoomEngineProgressDialog(model) { Width = width, Height = height };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                if (state != "confirmation")
                {
                    _ = model.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    game.DownloadProgress = 65; Dispatcher.UIThread.RunJobs();
                    if (state == "canceling" || state == "retained") model.Cancel();
                    if (state == "retained") game.Status = GameStatus.Installed;
                    if (state == "retained" || isError) { release.TrySetResult(); await model.Work; }
                }
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                // Await the composition batch after operation-driven layout changes.
                var visual = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window.Content as Visual);
                Assert.NotNull(visual);
                var committed = visual.Compositor.RequestCommitAsync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                await committed.WaitAsync(TimeSpan.FromSeconds(10));
                foreach (var button in new[] { window.CancelButton, window.InstallButton })
                {
                    Assert.True(button.IsEffectivelyVisible); Assert.True(button.Bounds.Height >= 44);
                    var point = button.TranslatePoint(default, window)!.Value;
                    Assert.InRange(point.X, 0, width - button.Bounds.Width);
                    Assert.InRange(point.Y, 0, height - button.Bounds.Height);
                }
                if (isError)
                {
                    Assert.StartsWith(window.DiagnosticText.Text!, model.Message);
                    var viewport = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>());
                    var diagnosticPoint = window.DiagnosticText.TranslatePoint(default, viewport)!.Value;
                    Assert.InRange(diagnosticPoint.Y, 0, viewport.Bounds.Height - window.DiagnosticText.Bounds.Height);
                    if (state == "error-short-lines") Assert.True(model.Message.Length <= 280);
                    foreach (var control in new Control[] { window.DiagnosticText, window.PreviousDetailsButton, window.NextDetailsButton })
                    {
                        Assert.True(control.IsEffectivelyVisible);
                        var point = control.TranslatePoint(default, window)!.Value;
                        Assert.InRange(point.X, 0, width - control.Bounds.Width);
                        Assert.InRange(point.Y, 0, height - control.Bounds.Height);
                    }
                }
                else Assert.Equal(model.Message, window.StatusText.Text);
                Assert.False(model.Succeeded); Assert.Equal(state == "confirmation", window.InstallButton.IsEnabled);
                Assert.True(window.IsVisible);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(width, height), frame.PixelSize);
                using var pixels = frame.Lock(); var bytes = new byte[pixels.RowBytes * height]; Marshal.Copy(pixels.Address, bytes, 0, bytes.Length);
                var heading = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == window.Title);
                var headingPoint = heading.TranslatePoint(default, window)!.Value;
                var headingPixels = 0;
                for (var y = (int)headingPoint.Y; y < Math.Min(height, headingPoint.Y + heading.Bounds.Height); y++)
                    for (var x = (int)headingPoint.X; x < Math.Min(width, headingPoint.X + heading.Bounds.Width); x++)
                    {
                        var offset = y * pixels.RowBytes + x * 4;
                        if (bytes[offset] > 100 && bytes[offset + 1] > 100 && bytes[offset + 2] > 100) headingPixels++;
                    }
                Assert.True(headingPixels > 50, $"Native title must be present in the captured frame; found {headingPixels} light pixels.");
                var colors = new HashSet<int>(); for (var i = 0; i < bytes.Length; i += 4) colors.Add(BitConverter.ToInt32(bytes, i)); Assert.True(colors.Count > 32);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, $"16-engine-setup-{state}-{width}x{height}.png"), FileMode.CreateNew, FileAccess.Write); frame.Save(file);
                }
                output.WriteLine("Actual headless native setup dialog with controlled synthetic operation state. No downloaded engine, network request, gameplay or desktop-host certification.");
            }
            finally { release.TrySetResult(); await model.Work; window.Close(); model.DisposeAfterDrain(); }
            return 0;
        }, CancellationToken.None);
    }
}
