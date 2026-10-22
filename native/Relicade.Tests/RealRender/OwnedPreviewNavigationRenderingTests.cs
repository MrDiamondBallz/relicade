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
using Direction = Relicade.Services.NavigationDirection;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class OwnedPreviewNavigationRenderingTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Theory]
    [InlineData(760, 660, 100, false)][InlineData(320, 360, 100, false)][InlineData(320, 360, 200, false)]
    [InlineData(760, 660, 100, true)][InlineData(320, 360, 100, true)][InlineData(320, 360, 200, true)]
    public async Task Native_preview_navigation_keeps_each_detail_and_confirmation_visible(int width, int height, int percent, bool repair)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            await using var session = new LauncherSession();
            var source = "/synthetic/original/" + string.Concat(Enumerable.Repeat(new string('遊', 40) + "/", 3)) + "freedoom2.wad";
            var plan = new OwnedDataPreview(new string('a', 64), source, "/synthetic/owned-data/freedoom2.wad",
                new string('b', 32), new string('c', 64), repair ? 8 : 4, false,
                repair ? new OwnedDataRepair("/synthetic/owned-data/retained-" + new string('d', 64) + "-freedoom2.wad", 4, new string('d', 64), false) : null);
            var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic preview only. No game installed; four-byte test data, not a real WAD.",
                (_, _) => plan, (_, _) => throw new InvalidOperationException("Rendering must not copy."), () => Task.CompletedTask);
            model.ConfigureFreedoom(() => throw new InvalidOperationException("Rendering must not launch."), false);
            model.ConfigureFreeDataDownload((_, _) => throw new InvalidOperationException("Rendering must not download."));
            var store = new Store(); store.Current.InterfaceScalePercent = percent;
            var owner = new Window { Width = 1200, Height = 800, Content = new TextBlock { Text = "Synthetic owner" } };
            using var scaling = new DesktopInterfaceScaling(owner, new SettingsViewModel(store), () => new Size(3840, 2160));
            var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
            model.PickFile = () => Task.FromResult<string?>(source); Task? closed = null;
            void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            try
            {
                owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                DesktopInterfaceScaling.PrepareDialog(dialog, owner); closed = dialog.ShowDialog(owner);
                await model.ChooseAsync(); Layout();
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                var inspections = dialog.GetVisualDescendants().OfType<Button>()
                    .Where(button => AutomationProperties.GetName(button)?.StartsWith("Inspect ", StringComparison.Ordinal) == true).ToArray();
                var complete = string.Concat(inspections.Select(x => ((TextBlock)x.Content!).Text));
                Assert.Contains("Source: " + source, complete); Assert.Contains("Destination: " + plan.Destination, complete);
                if (repair) Assert.Contains("Retained copy: " + plan.Repair!.RetainedDestination, complete);
                var visited = new HashSet<Button>(); var captured = new HashSet<string>();
                GamepadFocusChrome.SetActive(true, dialog);
                foreach (var direction in new[] { Direction.Up, Direction.Down })
                    for (var i = 0; i < inspections.Length + 10; i++)
                    {
                        var before = dialog.FocusManager?.GetFocusedElement();
                        GamepadModalDialogNavigation.Instance.TryHandleNavigation(direction); Layout();
                        if (dialog.FocusManager?.GetFocusedElement() is Button button && inspections.Contains(button))
                        {
                            Assert.True(button.Bounds.Height >= 44);
                            visited.Add(button); var text = Assert.IsType<TextBlock>(button.Content);
                            var point = text.TranslatePoint(default, viewport)!.Value;
                            Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - text.Bounds.Height + 0.1);
                            var state = text.Text!.StartsWith("Source:", StringComparison.Ordinal) ? "source"
                                : text.Text.StartsWith("Retained copy:", StringComparison.Ordinal) ? "retained" : null;
                            if (state != null && captured.Add(state))
                            {
                                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                                Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize);
                                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                                if (!string.IsNullOrWhiteSpace(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                    using var file = new FileStream(Path.Combine(directory, $"17-preview-{state}-{width}x{height}-{percent}-repair-{repair}.png"), FileMode.CreateNew, FileAccess.Write);
                                    frame.Save(file);
                                }
                            }
                        }
                        if (ReferenceEquals(before, dialog.FocusManager?.GetFocusedElement())) break;
                    }
                Assert.True(inspections.All(visited.Contains)); Assert.Contains("source", captured);
                if (repair) Assert.Contains("retained", captured);
                foreach (var button in new[] { dialog.ConfirmButton, dialog.CloseButton })
                {
                    var start = button.TranslatePoint(default, dialog)!.Value;
                    var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), dialog)!.Value;
                    Assert.InRange(start.X, 0, dialog.ClientSize.Width); Assert.InRange(start.Y, 0, dialog.ClientSize.Height);
                    Assert.InRange(end.X, 0, dialog.ClientSize.Width); Assert.InRange(end.Y, 0, dialog.ClientSize.Height);
                }
                output.WriteLine("Actual native modal render, natural controller navigation and synthetic paths; no copy, network, engine execution or physical-platform acceptance.");
            }
            finally { dialog.Close(); if (closed != null) await closed; await dialog.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); owner.Close(); }
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
