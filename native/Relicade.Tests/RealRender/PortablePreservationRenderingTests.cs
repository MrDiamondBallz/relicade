using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class PortablePreservationRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(700, 520, false, 100)][InlineData(700, 520, true, 100)]
    [InlineData(580, 440, false, 100)][InlineData(580, 440, true, 100)]
    [InlineData(580, 440, false, 200)][InlineData(580, 440, true, 200)]
    public async Task Actual_observation_dialog_keeps_preservation_limits_and_actions_readable(int width, int height, bool light, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "preservation-render-" + Guid.NewGuid().ToString("N"));
            var previousRoot = RelicadePaths.OverrideUserDataRoot; var previousStore = SettingsStoreProvider.Default;
            MainView? themeView = null; Window? owner = null; PortablePreservationDialog? dialog = null;
            PortablePreservationViewModel? model = null; LauncherSession? session = null; GameManager? manager = null;
            HttpClient? http = null; DesktopInterfaceScaling? scaling = null; Task? closed = null;
            using var game = new GameInfo { Name = "Synthetic preservation fixture", Repository = "fixture/preservation", FolderName = "Fixture", IsCustom = true };
            try
            {
                Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
                var store = new Store(root, light, percent); SettingsStoreProvider.Default = store;
                var handler = new Offline(); http = new HttpClient(handler);
                manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
                manager.Games = new ObservableCollection<GameInfo> { game };
                var live = game.GetInstallPath(manager.GamesFolder);
                // Authored non-runnable ELF-shaped fixture. Never launch or acquire a game.
                var payload = new byte[64]; payload[0] = 0x7f; payload[1] = (byte)'E'; payload[2] = (byte)'L'; payload[3] = (byte)'F';
                payload[4] = 2; payload[5] = 1; payload[6] = 1; payload[16] = 2; payload[24] = 1;
                await new PortableInstallationTransaction(live).InstallAsync(stage =>
                { File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), payload); return Task.CompletedTask; }, "synthetic-v1");
                File.WriteAllText(Path.Combine(live, "LastPlayed.txt"), "current synthetic save");
                File.WriteAllText(Path.Combine(live, "unlisted-user-file.txt"), "preserve unknown bytes");
                game.InstalledVersion = "synthetic-v1"; game.Status = GameStatus.Installed;
                // Actual host theme, not a rendered Library/card: avoids artwork and
                // provider paths while testing the real dialog and real observer.
                themeView = new MainView(RealRenderTestApp.Dependencies(store, manager));
                owner = new Window { Content = new Border(), Width = 1280, Height = 860 };
                scaling = new DesktopInterfaceScaling(owner, themeView.SettingsModel, () => new Size(3840, 2160));
                session = new LauncherSession(); model = new PortablePreservationViewModel(session, manager, game.InstanceKey);
                dialog = new PortablePreservationDialog(model);
                foreach (var key in new[] { "ThemeBase", "ThemeLighter", "ThemeDarker", "ThemeBorder", "ThemeText", "ThemeTextSecondary", "ThemeFocusRing" })
                    dialog.Resources[key] = themeView.Resources[key];
                dialog.Background = (IBrush)themeView.Resources["ThemeBase"]!; dialog.Foreground = (IBrush)themeView.Resources["ThemeText"]!;
                // Drain actual host setup events before measuring the inspection. The retained
                // diagnostic receipt identifies a queued SettingsView TextChanged save here.
                owner.Show(); await FrameCommit(owner);
                var before = Snapshot(root); var saves = store.Saves;
                DesktopInterfaceScaling.PrepareDialog(dialog, owner);
                closed = dialog.ShowDialog(owner); await dialog.BeginInspection();
                dialog.Width = width * percent / 100d; dialog.Height = height * percent / 100d; await FrameCommit(dialog);
                Assert.NotNull(model.Assessment); Assert.False(model.Assessment.IsPartial); Assert.True(dialog.CloseButton.IsDefault);
                Assert.Equal(percent / 100d, Assert.IsType<ScaleTransform>(Assert.IsType<LayoutTransformControl>(dialog.Content).LayoutTransform).ScaleX);
                Assert.False(dialog.PreviousButton.IsEnabled); Assert.True(dialog.NextButton.IsEnabled);
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(viewport.Viewport.Width > 0 && viewport.Viewport.Height > 0);
                var enabled = Label(dialog.CloseButton, "Close"); var disabled = Label(dialog.PreviousButton, "Previous");
                Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(enabled.Foreground).Color, Assert.IsAssignableFrom<ISolidColorBrush>(disabled.Foreground).Color);
                dialog.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await FrameCommit(dialog);
                foreach (var button in new[] { dialog.CloseButton, dialog.RefreshButton, dialog.PreviousButton, dialog.NextButton })
                {
                    Assert.True(button.IsEnabled); Inside(button, dialog);
                    var label = Label(button, button.Content!.ToString()!); Assert.True(label.Bounds.Width > 0 && label.Bounds.Height > 0);
                    using var normal = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                    label.Opacity = 0; using var background = await DetailsHeaderContrastRenderingTests.Frame(dialog); label.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(dialog, label, normal, background, 4.5, requireOpaqueCore: false);
                }
                GamepadFocusChrome.SetActive(true, dialog);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.CloseButton)); await FrameCommit(dialog);
                Assert.Equal(((ISolidColorBrush)themeView.Resources["ThemeFocusRing"]!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(dialog.CloseButton.BorderBrush).Color);
                Assert.True(dialog.CloseButton.BorderThickness.Left >= 2);
                dialog.PreviousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await FrameCommit(dialog);
                var inspection = Assert.IsType<Button>(dialog.Details.Children[0]);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(inspection)); await FrameCommit(dialog);
                Assert.True(inspection.IsFocused); var text = Assert.IsType<TextBlock>(inspection.Content);
                text.BringIntoView(); await FrameCommit(dialog);
                Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);
                var point = text.TranslatePoint(default, viewport)!.Value;
                Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - text.Bounds.Height + 0.1);
                using (var normal = await DetailsHeaderContrastRenderingTests.Frame(dialog))
                {
                    text.Opacity = 0; using var background = await DetailsHeaderContrastRenderingTests.Frame(dialog); text.Opacity = 1;
                    DetailsHeaderContrastRenderingTests.CheckContrast(dialog, text, normal, background, 4.5, requireOpaqueCore: false);
                }
                viewport.ScrollToHome(); await FrameCommit(dialog);
                using (var frame = await DetailsHeaderContrastRenderingTests.Frame(dialog))
                { Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize); Save(frame, $"{width}x{height}-{light}-{percent}"); }
                Assert.Equal(before, Snapshot(root)); Assert.Equal(saves, store.Saves); Assert.Equal(0, handler.Requests);
            }
            finally
            {
                try
                {
                    try { dialog?.Close(); if (model != null) await model.Work; if (closed != null) await closed; }
                    finally { try { model?.DisposeAfterDrain(); } finally { if (session != null) await session.DisposeAsync(); } }
                }
                finally
                {
                    try { if (themeView != null) await themeView.ShutdownAsync(); }
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
                                        finally { SettingsStoreProvider.Default = previousStore; RelicadePaths.OverrideUserDataRoot = previousRoot; }
                                    }
                                }
                            }
                        }
                    }
                }
                // Retain synthetic payload/history and every failed input, no root deletion.
            }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    static TextBlock Label(Button button, string text) => Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == text);
    static async Task FrameCommit(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
    }
    static void Inside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height >= 44);
        var a = control.TranslatePoint(default, window)!.Value; var b = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(a.X, 0, window.ClientSize.Width); Assert.InRange(a.Y, 0, window.ClientSize.Height);
        Assert.InRange(b.X, 0, window.ClientSize.Width); Assert.InRange(b.Y, 0, window.ClientSize.Height);
    }
    static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var output = new FileStream(Path.Combine(directory, "32-preservation-" + name + ".png"), FileMode.CreateNew); frame.Save(output);
    }
    static string[] Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + (Directory.Exists(path) ? "directory" : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();
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
        { Requests++; throw new InvalidOperationException("No external transport in preservation renderer."); }
    }
}
