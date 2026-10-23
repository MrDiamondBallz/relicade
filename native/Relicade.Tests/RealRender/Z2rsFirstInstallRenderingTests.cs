using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class Z2rsFirstInstallRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory][InlineData(700, 510, 100)][InlineData(580, 440, 100)][InlineData(580, 440, 200)]
    public async Task First_install_confirmation_is_readable_without_starting_work(int width, int height, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            await using var session = new LauncherSession();
            var game = new GameInfo { Name = "Synthetic z2rs entry", Repository = "troyedwardsjr/z2rs", FolderName = "Synthetic" };
            var project = GuidedEngineInstallService.ProjectFor(game);
            var model = new FreedoomEngineProgressViewModel(session, game,
                _ => throw new InvalidOperationException("A render must never start a download."), project.Title, project.Confirmation);
            var store = new Store(); store.Current.InterfaceScalePercent = percent;
            var owner = new Window { Width = 1200, Height = 800, Content = new TextBlock { Text = "Synthetic owner, no installed game" } };
            using var scaling = new DesktopInterfaceScaling(owner, new SettingsViewModel(store), () => new Size(3840, 2160));
            var dialog = new FreedoomEngineProgressDialog(model) { Width = width, Height = height };
            Task? closed = null;
            void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            try
            {
                owner.Show(); DesktopInterfaceScaling.PrepareDialog(dialog, owner); closed = dialog.ShowDialog(owner); Layout();
                Assert.Equal("Set up z2rs", dialog.Title); Assert.Equal("Set up game", game.ButtonText);
                Assert.True(dialog.CancelButton.IsFocused); Assert.True(model.CanStart); Assert.False(model.IsBusy);
                var intro = Assert.Single(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == project.Confirmation);
                Assert.Contains("No ROM is downloaded", intro.Text);
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                var point = intro.TranslatePoint(default, viewport)!.Value;
                Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - intro.Bounds.Height + 0.1);
                foreach (var button in new[] { dialog.CancelButton, dialog.InstallButton })
                {
                    Assert.True(button.IsEffectivelyVisible); Assert.True(button.Bounds.Height >= 44);
                    var location = button.TranslatePoint(default, dialog)!.Value;
                    Assert.InRange(location.Y, 0, dialog.Bounds.Height - button.Bounds.Height);
                }
                GamepadFocusChrome.SetActive(true, dialog);
                Assert.True(GamepadModalDialogNavigation.Instance.TryHandleNavigation(NavigationDirection.Right));
                Assert.True(dialog.InstallButton.IsFocused);
                Assert.True(GamepadModalDialogNavigation.Instance.TryHandleNavigation(NavigationDirection.Left));
                Assert.True(dialog.CancelButton.IsFocused);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize);
                var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    using var file = new FileStream(Path.Combine(output, $"20-z2rs-first-install-{width}x{height}-{percent}.png"), FileMode.CreateNew, FileAccess.Write);
                    frame.Save(file);
                }
            }
            finally { dialog.Close(); if (closed != null) await closed; await model.Work; model.DisposeAfterDrain(); owner.Close(); GamepadFocusChrome.SetActive(false); }
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
