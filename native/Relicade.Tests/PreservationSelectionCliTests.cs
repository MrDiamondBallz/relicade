using System.Reflection;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PreservationSelectionCliTests
{
    private sealed class Store(FileSettingsStore disk, Exception? failure) : ISettingsStore
    {
        private int loads;
        public int Saves;
        public AppSettings Current => disk.Current;
        public AppSettings Load()
        {
            if (++loads == 3 && failure != null) throw failure;
            return disk.Load();
        }
        public void Save(AppSettings settings) { Saves++; disk.Save(settings); }
    }

    [Theory]
    [InlineData("io")]
    [InlineData("cancel")]
    [InlineData("success")]
    public async Task Cli_scope_is_session_only_and_cannot_survive_success_io_or_cancellation(string scenario)
    {
        var previousRoot = RelicadePaths.OverrideUserDataRoot;
        var previousStore = SettingsStoreProvider.Default;
        var root = Path.Combine(Path.GetTempPath(), "cli-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        GameManager? manager = null;
        try
        {
            RelicadePaths.OverrideUserDataRoot = root;
            var path = Path.Combine(root, "settings.json");
            var disk = new FileSettingsStore(path);
            disk.Save(new AppSettings { ListScope = AppListScope.InstalledOnly, AppsPath = Path.Combine(root, "Apps") });
            Exception? expected = scenario switch { "io" => new IOException("Controlled load failure"), "cancel" => new OperationCanceledException("Controlled load cancellation"), _ => null };
            var store = new Store(disk, expected);
            SettingsStoreProvider.Default = store;
            var cli = new CLIHandler();
            var initialize = typeof(CLIHandler).GetMethod("InitializeGameManager", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var error = await Record.ExceptionAsync(() => (Task)initialize.Invoke(cli, null)!);
            manager = (GameManager?)typeof(CLIHandler).GetField("_gameManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cli);
            if (expected != null) Assert.Same(expected, error); else Assert.Null(error);
            Assert.Equal(AppListScope.InstalledOnly, new FileSettingsStore(path).Load().ListScope);
            Assert.Equal(AppListScope.InstalledOnly, store.Current.ListScope);
            Assert.Equal(0, store.Saves); // No temporary persistence, so restoration-save failure is impossible.
        }
        finally
        {
            manager?.Dispose();
            SettingsStoreProvider.Default = previousStore;
            RelicadePaths.OverrideUserDataRoot = previousRoot;
            Directory.Delete(root, true);
        }
    }
}
