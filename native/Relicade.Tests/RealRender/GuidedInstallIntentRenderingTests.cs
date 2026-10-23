using System.Collections.ObjectModel;
using System.Net;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class GuidedInstallIntentRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(700, 510, 100)][InlineData(580, 440, 100)][InlineData(580, 440, 200)]
    public async Task Reopened_retry_confirmation_keeps_consent_and_history_accessible_without_transfer(int width, int height, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "guided-intent-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            try
            {
                RelicadePaths.OverrideUserDataRoot = root;
                var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
                store.Current.AppsPath = Path.Combine(root, "Apps"); store.Current.InterfaceScalePercent = percent;
                store.Save(store.Current); SettingsStoreProvider.Default = store;
                using var handler = new Offline(); using var http = new HttpClient(handler);
                using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
                using var game = new GameInfo { Name = "Synthetic interrupted setup", RepositorySource = "github", Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Synthetic", IsCustom = true };
                manager.Games = new ObservableCollection<GameInfo> { game }; await manager.CatalogService.SaveLocalAppsAsync([game]);
                await using var session = new LauncherSession();
                var prepared = GuidedEngineInstallService.PrepareFirstInstall(game, manager, session.Token);
                var identity = GuidedInstallIntent.For(game, manager, prepared);
                using (var attempt = GuidedInstallIntent.Begin(identity)) attempt.Advance("Downloading");
                var record = Assert.Single(Directory.GetFiles(Path.Combine(root, "DownloadIntents"), "*.json"));
                var bytes = File.ReadAllBytes(record);
                var model = OwnedGameSetupFlow.CreateEngineProgress(game, manager, store.Current, session);
                var owner = new Window { Width = 1200, Height = 800, Content = new TextBlock { Text = "Synthetic owner; no game installed" } };
                using var scaling = new DesktopInterfaceScaling(owner, new SettingsViewModel(store), () => new Size(3840, 2160));
                var dialog = new FreedoomEngineProgressDialog(model) { Width = width, Height = height };
                Task? closed = null;
                void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
                try
                {
                    owner.Show(); DesktopInterfaceScaling.PrepareDialog(dialog, owner); closed = dialog.ShowDialog(owner); Layout();
                    Assert.Equal(percent, scaling.AppliedPercent); Assert.True(dialog.CancelButton.IsFocused);
                    Assert.True(dialog.CancelButton.IsDefault); Assert.False(dialog.InstallButton.IsDefault);
                    Assert.Equal("Retry engine install", dialog.InstallButton.Content); Assert.True(model.CanStart);
                    Assert.Contains("Downloading", model.Message); Assert.Contains("nothing resumes automatically", model.Message);
                    Assert.DoesNotContain("Engine setup stopped", dialog.StatusText.Text);
                    var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                    Assert.True(viewport.Viewport.Width > 0); Assert.True(viewport.Viewport.Height > 0);
                    var introduction = Assert.Single(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == model.Confirmation);
                    Assert.Contains("No ROM is downloaded", introduction.Text);
                    Assert.InRange(introduction.TranslatePoint(default, viewport)!.Value.Y, 0, viewport.Bounds.Height);
                    var initialStatus = dialog.StatusText.TranslatePoint(default, viewport)!.Value;
                    Assert.InRange(initialStatus.Y, -0.1, viewport.Bounds.Height - dialog.StatusText.Bounds.Height + 0.1);
                    viewport.ScrollToEnd(); Layout();
                    var status = dialog.StatusText.TranslatePoint(default, viewport)!.Value;
                    Assert.True(dialog.StatusText.Bounds.Height > 0);
                    Assert.InRange(status.Y, -0.1, viewport.Bounds.Height - dialog.StatusText.Bounds.Height + 0.1);
                    if (dialog.DiagnosticText.IsVisible)
                    {
                        var complete = dialog.DiagnosticText.Text;
                        for (var page = 0; page < 8 && dialog.NextDetailsButton.IsEnabled; page++)
                        {
                            dialog.NextDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
                            complete += dialog.DiagnosticText.Text;
                        }
                        Assert.False(dialog.NextDetailsButton.IsEnabled); Assert.Equal(model.Message, complete);
                        for (var page = 0; page < 8 && dialog.PreviousDetailsButton.IsEnabled; page++)
                        { dialog.PreviousDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); }
                        Assert.False(dialog.PreviousDetailsButton.IsEnabled);
                    }
                    foreach (var button in new[] { dialog.CancelButton, dialog.InstallButton })
                    {
                        Assert.True(button.IsEffectivelyVisible); Assert.True(button.Bounds.Width > 0); Assert.True(button.Bounds.Height >= 44);
                        var top = button.TranslatePoint(default, dialog)!.Value;
                        var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), dialog)!.Value;
                        Assert.InRange(top.X, 0, dialog.ClientSize.Width); Assert.InRange(top.Y, 0, dialog.ClientSize.Height);
                        Assert.InRange(end.X, 0, dialog.ClientSize.Width); Assert.InRange(end.Y, 0, dialog.ClientSize.Height);
                    }
                    GamepadFocusChrome.SetActive(true, dialog);
                    Assert.True(GamepadModalDialogNavigation.Instance.TryHandleNavigation(NavigationDirection.Right));
                    Assert.True(dialog.InstallButton.IsFocused);
                    Assert.True(GamepadModalDialogNavigation.Instance.TryHandleNavigation(NavigationDirection.Left));
                    Assert.True(dialog.CancelButton.IsFocused);
                    var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(dialog)!.Compositor.RequestCommitAsync();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
                    using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                    Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize);
                    using var locked = frame.Lock(); var pixels = new byte[locked.RowBytes * frame.PixelSize.Height];
                    Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
                    Assert.True(Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToInt32(pixels, i * 4)).Distinct().Count() > 32);
                    var output = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        using var file = new FileStream(Path.Combine(output, $"30-guided-intent-retry-{width}x{height}-{percent}.png"), FileMode.CreateNew, FileAccess.Write);
                        frame.Save(file);
                    }
                    Assert.Equal(0, handler.Calls); Assert.True(model.Work.IsCompleted); Assert.False(model.IsBusy);
                    Assert.False(game.IsInstalled); Assert.False(game.OwnedDataReady); Assert.Equal(bytes, File.ReadAllBytes(record));
                }
                finally { dialog.Close(); if (closed != null) await closed; await model.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); owner.Close(); }
            }
            finally { SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot; Directory.Delete(root, true); }
            return 0;
        }, CancellationToken.None);
    }
    sealed class Offline : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("A render must never request metadata or payloads."); }
    }
}
