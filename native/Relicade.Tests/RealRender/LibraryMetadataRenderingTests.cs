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
public sealed class LibraryMetadataRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(1280, 860, 700, 540, false, 100)][InlineData(1280, 860, 700, 540, true, 100)]
    [InlineData(750, 490, 580, 440, false, 100)][InlineData(750, 490, 580, 440, true, 100)]
    [InlineData(1100, 760, 580, 440, false, 200)][InlineData(1100, 760, 580, 440, true, 200)]
    public async Task Actual_export_preview_keeps_exact_fields_exclusions_and_file_consent_readable(int width, int height,
        int dialogWidth, int dialogHeight, bool light, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "library-export-render-" + Guid.NewGuid().ToString("N"));
            var priorChrome = GamepadFocusChrome.IsActive;
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            MainView? view = null; Window? window = null; LibraryMetadataExportDialog? dialog = null;
            GameManager? manager = null; HttpClient? http = null; DesktopInterfaceScaling? scaling = null;
            LauncherSession? session = null; LauncherDialogLifetime? dialogs = null;
            Task? opening = null;
            try
            {
                Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
                var store = new Store(root, light, percent); SettingsStoreProvider.Default = store;
                var handler = new Offline(); http = new HttpClient(handler);
                var catalog = new AppCatalogService(dataDirectory: root);
                await File.WriteAllTextAsync(Path.Combine(root, "apps.json"), "{\"apps\":[{\"name\":\"Authored library entry\",\"folderName\":\"Fixture\"}]}");
                manager = new GameManager(store, http, catalog);
                await manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
                view = new MainView(RealRenderTestApp.Dependencies(store, manager)); view.Shell.Mode = MainViewMode.Library;
                window = new Window { Content = view, Width = width, Height = height };
                scaling = new DesktopInterfaceScaling(window, view.SettingsModel, () => new Size(3840, 2160));
                window.Show(); await view.Library.SetupHistory.Work; await Frame(window);
                var before = Snapshot(root); var saves = store.Saves; var choices = 0;
                session = new LauncherSession(); dialogs = new LauncherDialogLifetime(session.Token, action => Dispatcher.UIThread.Post(action));
                var flow = new LibraryMetadataExportFlow(manager, view.SettingsModel, session, dialogs,
                    () => { choices++; throw new InvalidOperationException("This renderer must never open a file picker."); });
                var library = view.FindControl<LibraryView>("LibraryPanel")!;
                library.ConfigureMetadataExport(origin => opening = flow.OpenAsync(origin));
                var export = library.ExportLibraryMetadataButton;
                Assert.True(export.IsEffectivelyVisible); Assert.True(export.IsEnabled); Inside(export, window);
                await LabelContrast(export, window);
                using (var frame = await DetailsHeaderContrastRenderingTests.Frame(window)) Save(frame, $"library-{width}x{height}-{light}-{percent}");
                export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dialog = flow.Dialog; Assert.NotNull(dialog);
                // Headless has no desktop application lifetime; register the actual
                // owner scaling that GameDialogService applies in the desktop host.
                DesktopInterfaceScaling.PrepareDialog(dialog, window);
                await Frame(dialog); dialog.Width = dialogWidth * percent / 100d; dialog.Height = dialogHeight * percent / 100d;
                await Frame(dialog);
                var transform = Assert.IsType<LayoutTransformControl>(dialog.Content);
                Assert.Equal(percent / 100d, Assert.IsType<ScaleTransform>(transform.LayoutTransform).ScaleX);
                Assert.Equal(((ISolidColorBrush)view.Resources["ThemeBase"]!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color);
                Assert.True(dialog.CancelButton.IsDefault); Assert.False(dialog.SaveButton.IsDefault); Assert.True(dialog.SaveButton.IsEnabled);
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(viewport.Viewport.Width > 0 && viewport.Viewport.Height > 0);
                GamepadFocusChrome.SetActive(true, dialog);
                foreach (var button in new[] { dialog.CancelButton, dialog.PreviousButton, dialog.NextButton, dialog.SaveButton }) Inside(button, dialog);
                foreach (var button in new[] { dialog.CancelButton, dialog.NextButton, dialog.SaveButton }) await LabelContrast(button, dialog);
                Assert.False(dialog.PreviousButton.IsEnabled);
                var disabled = Assert.Single(dialog.PreviousButton.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Previous");
                var enabled = Assert.Single(dialog.NextButton.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Next");
                Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(disabled.Foreground).Color, Assert.IsAssignableFrom<ISolidColorBrush>(enabled.Foreground).Color);
                Assert.InRange(dialog.Model.Pages.Count, 3, 8);
                for (var page = 0; page < dialog.Model.Pages.Count; page++)
                {
                    var chunks = dialog.Details.Children.OfType<Button>().ToArray(); Assert.NotEmpty(chunks);
                    Assert.Equal(dialog.Model.Pages[page], string.Concat(chunks.Select(b => Assert.IsType<TextBlock>(b.Content).Text)));
                    foreach (var chunk in chunks)
                    {
                        Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(chunk)); await Frame(dialog);
                        Assert.Same(chunk, dialog.FocusManager!.GetFocusedElement());
                        var body = Assert.IsType<TextBlock>(chunk.Content);
                        Assert.Equal(FontWeight.Normal, body.FontWeight);
                        var presenter = Assert.Single(chunk.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>(), c => c.Name == "PART_ContentPresenter");
                        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, presenter.HorizontalAlignment);
                        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, presenter.HorizontalContentAlignment);
                        Assert.InRange(body.TranslatePoint(default, chunk)!.Value.X, 0, 16);
                        var point = chunk.TranslatePoint(default, viewport)!.Value;
                        Assert.True(chunk.Bounds.Width > 0 && chunk.Bounds.Height > 0);
                        Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - chunk.Bounds.Height + 0.1);
                    }
                    var text = Assert.IsType<TextBlock>(chunks[0].Content); chunks[0].BringIntoView(); await Frame(dialog);
                    using (var normal = await DetailsHeaderContrastRenderingTests.Frame(dialog))
                    {
                        text.Opacity = 0; using var background = await DetailsHeaderContrastRenderingTests.Frame(dialog); text.Opacity = 1;
                        DetailsHeaderContrastRenderingTests.CheckContrast(dialog, text, normal, background, 4.5, requireOpaqueCore: false);
                        Assert.Equal(new PixelSize(dialogWidth * percent / 100, dialogHeight * percent / 100), normal.PixelSize);

                    }
                    viewport.ScrollToHome(); await Frame(dialog);
                    using (var capture = await DetailsHeaderContrastRenderingTests.Frame(dialog))
                        Save(capture, $"preview-{dialogWidth}x{dialogHeight}-{light}-{percent}-page-{page + 1}");
                    if (page + 1 < dialog.Model.Pages.Count) { Assert.True(dialog.NextButton.IsEnabled); dialog.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Frame(dialog); }
                }
                Assert.False(dialog.NextButton.IsEnabled); Assert.True(dialog.PreviousButton.IsEnabled);
                await LabelContrast(dialog.PreviousButton, dialog);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.CancelButton)); await Frame(dialog);
                Assert.Equal(((ISolidColorBrush)view.Resources["ThemeFocusRing"]!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(dialog.CancelButton.BorderBrush).Color);
                Assert.True(dialog.CancelButton.BorderThickness.Left >= 2);
                Assert.Equal(0, choices); Assert.False(dialog.Model.Saved);
                dialog.Close(); if (opening != null) await opening;
                Assert.Equal(before, Snapshot(root)); Assert.Equal(saves, store.Saves); Assert.Equal(0, handler.Requests);
            }
            finally
            {
                try
                {
                    try { dialog?.Close(); if (opening != null) await opening; }
                    finally { try { if (session != null) await session.DisposeAsync(); } finally { dialogs?.Dispose(); } }
                }
                finally
                {
                    try { if (view != null) await view.ShutdownAsync(); }
                    finally
                    {
                        try { scaling?.Dispose(); }
                        finally
                        {
                            try { window?.Close(); }
                            finally
                            {
                                try { manager?.Dispose(); }
                                finally
                                {
                                    try { http?.Dispose(); }
                                    finally { try { GamepadFocusChrome.SetActive(priorChrome); } finally { SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot; } }
                                }
                            }
                        }
                    }
                }
            }
            return 0;
        }, TestContext.Current.CancellationToken);
    }
    static async Task LabelContrast(Button button, Window window)
    {
        Assert.True(button.IsEnabled);
        var text = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == (string)button.Content!);
        Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);
        using var normal = await DetailsHeaderContrastRenderingTests.Frame(window);
        text.Opacity = 0; using var background = await DetailsHeaderContrastRenderingTests.Frame(window); text.Opacity = 1;
        DetailsHeaderContrastRenderingTests.CheckContrast(window, text, normal, background, 4.5, requireOpaqueCore: false);
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
        var a = control.TranslatePoint(default, window)!.Value;
        var b = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(a.X, 0, window.ClientSize.Width); Assert.InRange(a.Y, 0, window.ClientSize.Height);
        Assert.InRange(b.X, 0, window.ClientSize.Width); Assert.InRange(b.Y, 0, window.ClientSize.Height);
    }
    static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => Path.GetRelativePath(root, p) + "|" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var output = new FileStream(Path.Combine(directory, "35-library-export-" + name + ".png"), FileMode.CreateNew); frame.Save(output);
    }
    sealed class Store(string root, bool light, int percent) : ISettingsStore
    {
        internal int Saves;
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps"), Platform = TargetOS.LinuxX64,
            FirstStartup = false, LocalFirstCatalogMigrationComplete = true, ListScope = AppListScope.InstalledOnly,
            PrimaryColor = light ? "#e5e5e5" : "#18181b", InterfaceScalePercent = percent };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
    sealed class Offline : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests++; throw new InvalidOperationException("No transport in metadata preview renderer."); }
    }
}
