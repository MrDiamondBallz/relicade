using Avalonia;
using Avalonia.Headless;
using Relicade.Services;

namespace Relicade.Tests;

// This project runs in its own process; never mix with the dummy-drawing test assembly.
// Avalonia's renderer/font resources are process-wide, not isolated by headless sessions.
public static class RealRenderTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.SuppressDesktopHost = true;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true
            });
    }

    // Use the existing native-view composition seam, never the eager MainWindow XAML host.
    // Caller owns injected store/manager; the view owns only its session and subscriptions.
    public static MainViewDependencies Dependencies(ISettingsStore store, GameManager manager) => new()
    {
        SettingsStore = store,
        GameManager = manager,
        EnableInput = false,
        EnableMusic = false,
        InitializeOnOpen = false
    };
}
