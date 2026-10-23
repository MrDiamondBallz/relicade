using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class LibrarySetupHistoryRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(1280, 860, 700, 520, false, 100)]
    [InlineData(1280, 860, 700, 520, true, 100)]
    [InlineData(750, 490, 580, 440, false, 100)]
    [InlineData(750, 490, 580, 440, true, 100)]
    [InlineData(1100, 760, 580, 440, false, 200)]
    [InlineData(1100, 760, 580, 440, true, 200)]
    public async Task Actual_library_notice_and_review_keep_navigation_readable_without_setup(int width, int height,
        int dialogWidth, int dialogHeight, bool light, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "library-history-render-" + Guid.NewGuid().ToString("N"));
            var previousRoot = RelicadePaths.OverrideUserDataRoot; var previousStore = SettingsStoreProvider.Default;
            MainView? view = null; Window? window = null; LibrarySetupHistoryDialog? dialog = null;
            GameManager? manager = null; HttpClient? http = null; DesktopInterfaceScaling? scaling = null;
            using var game = new GameInfo { Name = "Synthetic saved Perfect Dark setup", RepositorySource = "github",
                Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Synthetic", IsCustom = true };
            try
            {
                Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
                var store = new Store(root, light, percent); SettingsStoreProvider.Default = store;
                var handler = new DenyTransport(); http = new HttpClient(handler);
                manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
                manager.SetCatalogAppsAndFilter([game], store.Current);
                var identity = GuidedInstallIntent.ForCaptured(game.InstanceKey, root, game.GetInstallPath(manager.GamesFolder),
                    GuidedEngineInstallService.ProjectFor(game));
                using (var attempt = GuidedInstallIntent.Begin(identity)) { }
                var before = Snapshot(root);
                view = new MainView(RealRenderTestApp.Dependencies(store, manager)); view.Shell.Mode = MainViewMode.Library;
                window = new Window { Content = view, Width = width, Height = height };
                scaling = new DesktopInterfaceScaling(window, view.SettingsModel, () => new Size(3840, 2160));
                window.Show(); await view.Library.SetupHistory.Work;
                var library = view.FindControl<LibraryView>("LibraryPanel")!;
                await FrameCommit(window);
                var review = library.LibrarySetupHistoryReviewButton;
                Assert.True(review.IsEffectivelyVisible); Assert.True(review.IsEnabled); Inside(review, window);
                Assert.Empty(manager.Games); Assert.Single(view.Library.SetupHistory.Rows);
                var notice = library.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == view.Library.SetupHistory.Message);
                using (var normal = await DetailsHeaderContrastRenderingTests.Frame(window))
                {
                    notice.Opacity = 0;
                    using var background = await DetailsHeaderContrastRenderingTests.Frame(window);
                    notice.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(window, notice, normal, background, 4.5, requireOpaqueCore: false);
                    Save(normal, $"library-{width}x{height}-{light}-{percent}");
                }
                var reviewLabel = Assert.Single(review.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Text == "Review saved setup"));
                Assert.True(reviewLabel.Bounds.Width > 0 && reviewLabel.Bounds.Height > 0);
                using (var normal = await DetailsHeaderContrastRenderingTests.Frame(window))
                {
                    reviewLabel.Opacity = 0;
                    using var background = await DetailsHeaderContrastRenderingTests.Frame(window);
                    reviewLabel.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(window, reviewLabel, normal, background, 4.5, requireOpaqueCore: false);
                }
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dialog = library.SetupHistoryDialog;
                Assert.NotNull(dialog); await view.Library.SetupHistory.Work; await FrameCommit(dialog);
                // Exercise the supported minimum by resizing the actual opened modal, not a mock.
                dialog.Width = dialogWidth * percent / 100d; dialog.Height = dialogHeight * percent / 100d;
                await FrameCommit(dialog);
                var transform = Assert.IsType<LayoutTransformControl>(dialog.Content);
                Assert.Equal(percent / 100d, Assert.IsType<ScaleTransform>(transform.LayoutTransform).ScaleX);
                Assert.Equal(((ISolidColorBrush)view.Resources["ThemeBase"]!).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color);
                Assert.True(dialog.CloseButton.IsDefault); Assert.False(dialog.ShowButton.IsDefault);
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(viewport.Viewport.Width > 0 && viewport.Viewport.Height > 0);
                GamepadFocusChrome.SetActive(true, dialog);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.ShowButton));
                Assert.Same(dialog.ShowButton, dialog.FocusManager?.GetFocusedElement());
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.CloseButton));
                foreach (var button in new[] { dialog.CloseButton, dialog.ShowButton, dialog.PreviousButton, dialog.NextButton })
                    Inside(button, dialog);
                foreach (var button in new[] { dialog.RefreshButton, dialog.CloseButton, dialog.ShowButton })
                {
                    Assert.True(button.IsEnabled);
                    var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(),
                        t => t.Text == (string)button.Content!);
                    label.BringIntoView(); await FrameCommit(dialog);
                    using var normal = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    label.Opacity = 0;
                    using var background = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    label.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(dialog, label, normal, background, 4.5, requireOpaqueCore: false);
                }
                await FrameCommit(dialog);
                Assert.Equal(((ISolidColorBrush)view.Resources["ThemeFocusRing"]!).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(dialog.CloseButton.BorderBrush).Color);
                Assert.True(dialog.CloseButton.BorderThickness.Left >= 2);
                var enabledLabel = Assert.Single(dialog.ShowButton.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Show in Library");
                var disabledLabel = Assert.Single(dialog.NextButton.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Next");
                Assert.False(dialog.NextButton.IsEnabled);
                Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(enabledLabel.Foreground).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(disabledLabel.Foreground).Color);
                var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Where(t =>
                    t.Text == game.Name || t.Text == view.Library.SetupHistory.Message || t.Text == view.Library.SetupHistory.Rows[0].Message).ToArray();
                Assert.Equal(3, texts.Length);
                foreach (var text in texts)
                {
                    text.BringIntoView(); await FrameCommit(dialog);
                    Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);
                    var point = text.TranslatePoint(default, viewport)!.Value;
                    Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - text.Bounds.Height + 0.1);
                    using var normal = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    text.Opacity = 0;
                    using var background = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    text.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(dialog, text, normal, background, 4.5, requireOpaqueCore: false);
                }
                viewport.ScrollToHome(); await FrameCommit(dialog);
                using (var frame = await DetailsHeaderContrastRenderingTests.Frame(dialog))
                {
                    Assert.Equal(new PixelSize(dialogWidth * percent / 100, dialogHeight * percent / 100), frame.PixelSize);
                    Save(frame, $"review-{dialogWidth}x{dialogHeight}-{light}-{percent}");
                }
                Assert.Equal(0, handler.Requests); Assert.Empty(manager.Games); Assert.False(game.IsInstalled);
                Assert.False(game.OwnedDataReady); Assert.Equal(before, Snapshot(root));
            }
            finally
            {
                try
                {
                    try { dialog?.Close(); }
                    finally { if (view != null) await view.ShutdownAsync(); }
                }
                finally
                {
                    try
                    {
                        try { scaling?.Dispose(); }
                        finally
                        {
                            try { window?.Close(); }
                            finally
                            {
                                try { manager?.Dispose(); }
                                finally { http?.Dispose(); }
                            }
                        }
                    }
                    finally
                    {
                        try { GamepadFocusChrome.SetActive(false); }
                        finally
                        {
                            SettingsStoreProvider.Default = previousStore;
                            RelicadePaths.OverrideUserDataRoot = previousRoot;
                        }
                    }
                }
                // Retain this fixture's authored history, including failed inputs.
            }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    static async Task FrameCommit(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
    }
    static void Inside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height >= 44);
        var a = control.TranslatePoint(default, window)!.Value;
        var b = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(a.X, 0, window.ClientSize.Width); Assert.InRange(a.Y, 0, window.ClientSize.Height);
        Assert.InRange(b.X, 0, window.ClientSize.Width); Assert.InRange(b.Y, 0, window.ClientSize.Height);
    }
    static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var file = new FileStream(Path.Combine(directory, "31-setup-history-" + name + ".png"), FileMode.CreateNew);
        frame.Save(file);
    }
    static string[] Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + "|" + File.GetLastWriteTimeUtc(p).Ticks + "|" +
            (Directory.Exists(p) ? "directory" : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))))).ToArray();
    sealed class Store(string root, bool light, int percent) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps"), Platform = TargetOS.LinuxX64,
            FirstStartup = false, LocalFirstCatalogMigrationComplete = true, ListScope = AppListScope.InstalledOnly,
            PrimaryColor = light ? "#e5e5e5" : "#18181b", InterfaceScalePercent = percent };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    sealed class DenyTransport : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests++; throw new InvalidOperationException("No transport permitted in the history renderer."); }
    }
}
