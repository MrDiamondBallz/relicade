using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class Z2rsSetupRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory][InlineData(760, 660, 100)][InlineData(320, 360, 100)][InlineData(320, 360, 200)]
    public async Task Native_setup_requirements_and_privacy_disclosure_remain_readable(int width, int height, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            await using var session = new LauncherSession();
            var model = new OwnedGameSetupViewModel(session, "synthetic-no-installed-game",
                OwnedGameSetupService.Describe(OwnedGameSetupService.Z2rs),
                (_, _) => throw new InvalidOperationException("Rendering must not read game data."),
                (_, _) => throw new InvalidOperationException("Rendering must not copy game data."), () => Task.CompletedTask);
            var store = new Store(); store.Current.InterfaceScalePercent = percent;
            var owner = new Window { Width = 1200, Height = 800, Content = new TextBlock { Text = "Synthetic owner, no installed game" } };
            using var scaling = new DesktopInterfaceScaling(owner, new SettingsViewModel(store), () => new Size(3840, 2160));
            var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
            void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            Task? closed = null;
            try
            {
                owner.Show(); Layout(); DesktopInterfaceScaling.PrepareDialog(dialog, owner); closed = dialog.ShowDialog(owner); Layout();
                Assert.False(dialog.DownloadButton.IsVisible); Assert.False(dialog.PlayButton.IsVisible);
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                var rows = dialog.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetName(b)?.StartsWith("Inspect ", StringComparison.Ordinal) == true).ToArray();
                var complete = string.Concat(rows.Select(b => Assert.IsType<TextBlock>(b.Content).Text));
                Assert.Contains("262,144", complete); Assert.Contains("BA322865", complete); Assert.Contains("engine may remember the ROM path", complete);
                var visited = new HashSet<Button>(); GamepadFocusChrome.SetActive(true, dialog);
                foreach (var direction in new[] { NavigationDirection.Up, NavigationDirection.Down })
                    for (var i = 0; i < rows.Length + 8; i++)
                    {
                        var before = dialog.FocusManager?.GetFocusedElement();
                        GamepadModalDialogNavigation.Instance.TryHandleNavigation(direction); Layout();
                        if (dialog.FocusManager?.GetFocusedElement() is Button button && rows.Contains(button))
                        {
                            visited.Add(button); var text = Assert.IsType<TextBlock>(button.Content);
                            var point = text.TranslatePoint(default, viewport)!.Value;
                            Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - text.Bounds.Height + 0.1);
                        }
                        if (ReferenceEquals(before, dialog.FocusManager?.GetFocusedElement())) break;
                    }
                Assert.True(rows.All(visited.Contains));
                dialog.CloseButton.Focus(); Layout();
                // Render the top of the requirements, not an invented installed library.
                viewport.Offset = default; Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize);
                var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    using var file = new FileStream(Path.Combine(output, $"19-z2rs-setup-{width}x{height}-{percent}.png"), FileMode.CreateNew, FileAccess.Write);
                    frame.Save(file);
                }
            }
            finally { dialog.Close(); if (closed != null) await closed; await dialog.Work; model.DisposeAfterDrain(); owner.Close(); GamepadFocusChrome.SetActive(false); }
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
