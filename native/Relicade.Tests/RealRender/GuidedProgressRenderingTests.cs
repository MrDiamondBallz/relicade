using Relicade.Core.Models;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class GuidedProgressRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(700, 510, false, 100)][InlineData(700, 510, true, 100)]
    [InlineData(580, 440, false, 100)][InlineData(580, 440, true, 100)]
    [InlineData(580, 440, false, 200)][InlineData(580, 440, true, 200)]
    public async Task Guided_progress_keeps_transfer_and_safe_finish_readable(int width, int height, bool light, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "guided-progress-render-" + Guid.NewGuid().ToString("N"));
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            MainView? view = null; Window? owner = null; FreedoomEngineProgressDialog? dialog = null;
            FreedoomEngineProgressViewModel? model = null; LauncherSession? session = null;
            GameManager? manager = null; HttpClient? http = null; DesktopInterfaceScaling? scaling = null; Task? closed = null;
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var game = new GameInfo();
            try
            {
                Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
                var store = new Store(root, light, percent); SettingsStoreProvider.Default = store;
                var handler = new Offline(); http = new HttpClient(handler);
                manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
                view = new MainView(RealRenderTestApp.Dependencies(store, manager));
                owner = new Window { Width = 1280, Height = 860, Content = view };
                scaling = new DesktopInterfaceScaling(owner, view.SettingsModel, () => new Size(3840, 2160));
                owner.Show(); await Frame(owner); var saves = store.Saves;
                session = new LauncherSession();
                model = new FreedoomEngineProgressViewModel(session, game, _ => release.Task,
                    title: "Set up Perfect Dark", confirmation: "Synthetic progress display. No game or ROM is downloaded or played.");
                dialog = new FreedoomEngineProgressDialog(model);
                DesktopInterfaceScaling.PrepareDialog(dialog, owner); closed = dialog.ShowDialog(owner);
                dialog.Width = width * percent / 100d; dialog.Height = height * percent / 100d;
                await Frame(dialog); Assert.True(dialog.CancelButton.IsFocused);
                _ = model.StartAsync();
                foreach (var progress in new[] {
                    new GuidedEngineProgress(GuidedEnginePhase.Downloading, 32, 103),
                    new GuidedEngineProgress(GuidedEnginePhase.Downloading, 64),
                    new GuidedEngineProgress(GuidedEnginePhase.Verifying),
                    new GuidedEngineProgress(GuidedEnginePhase.Committing) })
                {
                    model.ReportProgress(progress); await Frame(dialog);
                    dialog.StatusText.BringIntoView(); await Frame(dialog);
                    using var normal = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    Save(normal, $"{width}x{height}-{light}-{percent}-{progress.Phase}-{progress.ReceivedBytes}");
                    Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), normal.PixelSize);
                    Assert.Equal(((ISolidColorBrush)view.Resources["ThemeBase"]!).Color,
                        Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color);
                    Assert.True(dialog.StatusText.Bounds.Width > 0 && dialog.StatusText.Bounds.Height > 0);
                    Assert.DoesNotContain("Engine setup stopped", dialog.StatusText.Text);
                    var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                    Assert.True(viewport.Viewport.Height > 0 && viewport.Viewport.Width > 0);
                    var point = dialog.StatusText.TranslatePoint(default, viewport)!.Value;
                    Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - dialog.StatusText.Bounds.Height + 0.1);
                    dialog.StatusText.Opacity = 0;
                    using var background = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    dialog.StatusText.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(dialog, dialog.StatusText, normal, background, 4.5, requireOpaqueCore: false);
                    Assert.Equal(progress.TotalBytes == null, dialog.TransferProgress.IsIndeterminate);
                    Assert.Equal(model.Message, AutomationProperties.GetName(dialog.TransferProgress));
                    Assert.False(model.Succeeded); Assert.False(dialog.InstallButton.IsEnabled);
                    Assert.True(dialog.CancelButton.IsEnabled); Inside(dialog.CancelButton, dialog); Inside(dialog.InstallButton, dialog);
                    var label = Assert.Single(dialog.CancelButton.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Cancel setup");
                    using var action = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    label.Opacity = 0; using var actionBackground = await DetailsHeaderContrastRenderingTests.Frame(dialog); label.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(dialog, label, action, actionBackground, 4.5, requireOpaqueCore: false);
                }
                GamepadFocusChrome.SetActive(true, dialog);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.CancelButton));
                Assert.Same(dialog.CancelButton, dialog.FocusManager?.GetFocusedElement());
                Assert.Equal(0, handler.Requests); Assert.Equal(saves, store.Saves); Assert.False(game.IsInstalled);
            }
            finally
            {
                release.TrySetResult(false);
                try
                {
                    try { if (model != null) await model.Work; dialog?.Close(); if (closed != null) await closed; }
                    finally { try { model?.DisposeAfterDrain(); } finally { if (session != null) await session.DisposeAsync(); } }
                }
                finally
                {
                    try { if (view != null) await view.ShutdownAsync(); }
                    finally
                    {
                        try { scaling?.Dispose(); }
                        finally
                        {
                            try { owner?.Close(); }
                            finally
                            {
                                try { manager?.Dispose(); }
                                finally
                                {
                                    try { http?.Dispose(); }
                                    finally
                                    {
                                        try { GamepadFocusChrome.SetActive(false); }
                                        finally { SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot; }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    static async Task Frame(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
    }
    static void Inside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height >= 44);
        var start = control.TranslatePoint(default, window)!.Value;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(start.X, 0, window.ClientSize.Width); Assert.InRange(start.Y, 0, window.ClientSize.Height);
        Assert.InRange(end.X, 0, window.ClientSize.Width); Assert.InRange(end.Y, 0, window.ClientSize.Height);
    }
    static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var output = new FileStream(Path.Combine(directory, "34-guided-progress-" + name + ".png"), FileMode.CreateNew); frame.Save(output);
    }
    sealed class Store(string root, bool light, int percent) : ISettingsStore
    {
        internal int Saves;
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps"), Platform = TargetOS.LinuxX64,
            FirstStartup = false, LocalFirstCatalogMigrationComplete = true, PrimaryColor = light ? "#e5e5e5" : "#18181b", InterfaceScalePercent = percent };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
    sealed class Offline : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests++; throw new InvalidOperationException("No external transport in progress renderer."); }
    }
}
