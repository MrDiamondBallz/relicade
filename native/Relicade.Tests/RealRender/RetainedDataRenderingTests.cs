using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
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
public sealed class RetainedDataRenderingTests(SharedCatalogRenderer renderer, ITestOutputHelper output)
{
    [Theory]
    [InlineData(760, 660, 100)][InlineData(320, 360, 100)][InlineData(320, 360, 200)]
    public async Task Actual_retained_reuse_preview_keeps_separate_consent_and_inspection_accessible(int width, int height, int percent)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "retained-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            RelicadePaths.OverrideUserDataRoot = root;
            try
            {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Current.AppsPath = Path.Combine(root, "Apps"); store.Current.InterfaceScalePercent = percent;
            store.Save(store.Current); SettingsStoreProvider.Default = store;
            using var http = new HttpClient(new Offline());
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            var game = new GameInfo { Name = "Synthetic retained-file fixture", Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Fixture", IsCustom = true };
            manager.Games = new ObservableCollection<GameInfo> { game }; await manager.CatalogService.SaveLocalAppsAsync([game]);
            var live = game.GetInstallPath(store.Current.AppsPath);
            // Authored header-shaped bytes for transaction discovery, never executed.
            var payload = new byte[64]; payload[0] = 0x7f; payload[1] = (byte)'E'; payload[2] = (byte)'L'; payload[3] = (byte)'F';
            payload[4] = 2; payload[5] = 1; payload[6] = 1; payload[16] = 2; payload[24] = 1;
            byte[] data = [10, 20, 30, 40];
            var definition = new OwnedDataRequirement("synthetic-render", "Synthetic four-byte fixture, not game data", "fixture", "fixture",
                Convert.ToHexStringLower(SHA256.HashData(payload)), data.Length, Convert.ToHexStringLower(MD5.HashData(data)));
            Task Install() => new PortableInstallationTransaction(live, new() { ReleaseIdentity = "fixture", AssetIdentity = "fixture" })
                .InstallAsync(stage => { File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), payload); return Task.CompletedTask; }, "fixture");
            await Install(); game.InstalledVersion = "fixture"; game.Status = GameStatus.Installed;
            var source = Path.Combine(root, "original.z64"); File.WriteAllBytes(source, data);
            var initial = new OwnedGameSetupSession(manager, game.InstanceKey, definition);
            var imported = initial.Preview(source, CancellationToken.None); initial.Apply(imported, CancellationToken.None);
            await Install(); File.Move(source, source + ".preserved");
            await using var session = new LauncherSession();
            var model = new OwnedGameSetupViewModel(session, game.InstanceKey, new OwnedGameSetupSession(manager, game.InstanceKey, definition));
            var owner = new Window { Width = 1200, Height = 800, Content = new TextBlock { Text = "Synthetic owner" } };
            using var scaling = new DesktopInterfaceScaling(owner, new SettingsViewModel(store), () => new Size(3840, 2160));
            var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
            Task? closed = null;
            void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            try
            {
                owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                DesktopInterfaceScaling.PrepareDialog(dialog, owner); closed = dialog.ShowDialog(owner);
                dialog.RetainedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work; Layout();
                Assert.Equal(percent, scaling.AppliedPercent); Assert.NotNull(model.RetainedPlan);
                Assert.Equal("Confirm lawful use and reuse", dialog.ConfirmButton.Content);
                Assert.True(dialog.CloseButton.IsDefault); Assert.False(dialog.ConfirmButton.IsDefault);
                var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(viewport.Bounds.Height > 0); Assert.True(viewport.Bounds.Width > 0);
                var inspections = dialog.GetVisualDescendants().OfType<Button>()
                    .Where(b => AutomationProperties.GetName(b)?.StartsWith("Inspect ", StringComparison.Ordinal) == true).ToArray();
                var complete = string.Concat(inspections.Select(b => ((TextBlock)b.Content!).Text));
                Assert.Contains(model.RetainedPlan.Sha256, complete); Assert.Contains(model.RetainedPlan.Destination, complete);
                GamepadFocusChrome.SetActive(true, dialog); Layout();
                foreach (var row in inspections)
                {
                    Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(row)); Layout();
                    Assert.Same(row, dialog.FocusManager?.GetFocusedElement());
                    var text = Assert.IsType<TextBlock>(row.Content); var point = text.TranslatePoint(default, viewport)!.Value;
                    Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - text.Bounds.Height + 0.1);
                }
                foreach (var button in new[] { dialog.CloseButton, dialog.ChooseButton, dialog.RetainedButton, dialog.ConfirmButton })
                {
                    Assert.True(button.IsVisible); Assert.True(button.IsEnabled); Assert.True(button.Bounds.Width > 0); Assert.True(button.Bounds.Height >= 44);
                    var top = button.TranslatePoint(default, dialog)!.Value;
                    var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), dialog)!.Value;
                    Assert.InRange(top.X, 0, dialog.ClientSize.Width); Assert.InRange(top.Y, 0, dialog.ClientSize.Height);
                    Assert.InRange(end.X, 0, dialog.ClientSize.Width); Assert.InRange(end.Y, 0, dialog.ClientSize.Height);
                    Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(button));
                }
                var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(dialog)!.Compositor.RequestCommitAsync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); await commit.WaitAsync(TimeSpan.FromSeconds(10));
                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                Assert.Equal(new PixelSize(width * percent / 100, height * percent / 100), frame.PixelSize);
                using var locked = frame.Lock(); var pixels = new byte[locked.RowBytes * frame.PixelSize.Height];
                Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
                var colors = new HashSet<int>(); var opaque = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                { colors.Add(BitConverter.ToInt32(pixels, i)); if (pixels[i + 3] == 255) opaque++; }
                Assert.True(colors.Count > 32); Assert.True(opaque > frame.PixelSize.Width * frame.PixelSize.Height / 2);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var file = new FileStream(Path.Combine(directory, $"29-retained-reuse-{width}x{height}-{percent}.png"), FileMode.CreateNew, FileAccess.Write);
                    frame.Save(file);
                }
                Assert.False(game.OwnedDataReady);
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(imported.Destination)!, model.RetainedPlan.OperationId + ".json")));
                Assert.Equal(data, File.ReadAllBytes(imported.Destination)); Assert.Equal(data, File.ReadAllBytes(source + ".preserved"));
                output.WriteLine("Actual native CPU-rendered retained preview using synthetic local engine/data. No consent click, acquired runtime, external request or gameplay/platform acceptance.");
            }
            finally
            {
                dialog.Close(); if (closed != null) await closed; await dialog.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); owner.Close();
            }
            return 0;
            }
            finally
            {
                SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot;
                Directory.Delete(root, true);
            }
        }, CancellationToken.None);
    }
    sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No transport is permitted in the retained-file render fixture.");
    }
}
