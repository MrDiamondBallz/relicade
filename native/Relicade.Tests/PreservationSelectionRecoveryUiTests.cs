using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

public class PreservationSelectionRecoveryUiTests
{
    [AvaloniaFact]
    public async Task Corrupt_settings_startup_shows_recovery_instead_of_first_run_and_preserves_bytes()
    {
        var previousRoot = RelicadePaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "startup-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{broken-settings");
        MainView? view = null;
        Window? window = null;
        try
        {
            RelicadePaths.OverrideUserDataRoot = root;
            var store = new FileSettingsStore(path);
            view = new MainView(new() { SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
            window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("{broken-settings", File.ReadAllText(path));
            Assert.True(view.Shell.SettingsOpen);
            var panel = view.FindControl<SettingsView>("SettingsPanel")!;
            var error = panel.FindControl<TextBlock>("SettingsRecoveryMessage");
            Assert.NotNull(error);
            Assert.Contains("recover", error.Text!, StringComparison.OrdinalIgnoreCase);
            Assert.True(error.IsVisible);
            Assert.False(panel.FindControl<TabControl>("SettingsTabControl")!.IsEnabled);
        }
        finally
        {
            window?.Close();
            if (view != null) await view.ShutdownAsync();
            RelicadePaths.OverrideUserDataRoot = previousRoot;
            Directory.Delete(root, true);
        }
    }
}
