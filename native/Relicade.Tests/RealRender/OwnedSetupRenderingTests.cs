using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class OwnedSetupRenderingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Actual_setup_dialog_renders_requirements_before_file_selection()
    {
        await using var renderer = HeadlessUnitTestSession.StartNew(typeof(RealRenderTestApp));
        await renderer.Dispatch<int>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "setup-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            RelicadePaths.OverrideUserDataRoot = root;
            var store = new FileSettingsStore(Path.Combine(root, "settings.json")); store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Save(store.Current); SettingsStoreProvider.Default = store;
            using var http = new HttpClient(new Offline());
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            var game = new GameInfo { Name = "Synthetic setup fixture, no game installed", Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Fixture", IsCustom = true };
            manager.Games = new ObservableCollection<GameInfo> { game }; await manager.CatalogService.SaveLocalAppsAsync([game]);
            await using var session = new LauncherSession();
            var model = new OwnedGameSetupViewModel(session, game.InstanceKey, new OwnedGameSetupSession(manager, game.InstanceKey));
            var dialog = new OwnedGameSetupDialog(model) { Width = 760, Height = 660 };
            try
            {
                dialog.Show(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                Assert.True(dialog.ChooseButton.IsEnabled); Assert.False(dialog.ConfirmButton.IsVisible); Assert.True(dialog.CloseButton.IsDefault);
                using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(760, 660), frame.PixelSize);
                using var locked = frame.Lock(); var bytes = new byte[locked.RowBytes * 660]; Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
                var colors = new HashSet<int>(); for (var i = 0; i < bytes.Length; i += 4) colors.Add(BitConverter.ToInt32(bytes, i));
                Assert.True(colors.Count > 32);
                var directory = Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "04-owned-game-setup-before-selection.png");
                    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); frame.Save(file);
                }
                output.WriteLine("Actual native setup dialog, 760x660 CPU framebuffer; synthetic local library identity, no installed game, no user input, no external request. Not a desktop-host or gameplay acceptance test.");
            }
            finally
            {
                dialog.Close(); await dialog.Work; model.DisposeAfterDrain();
                SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot;
                Directory.Delete(root, true);
            }
            return 0;
        }, CancellationToken.None);
    }
    sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Unexpected network request in native setup render fixture.");
    }
}
