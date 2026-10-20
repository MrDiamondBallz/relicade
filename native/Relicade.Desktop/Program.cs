using Avalonia;
using Avalonia.Controls.Platform;
using Relicade.Services;
using System.Runtime.InteropServices;
using Velopack;
using Velopack.Locators;

namespace Relicade.Desktop;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        // Cleanup must see ORIGINAL args before kiosk stripping, data-root setup
        // or any normal desktop initialization. Non-cleanup dispatch is unchanged.
        if (args.Any(a => a.StartsWith("--preview-portable-backups", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--cleanup-portable-backups", StringComparison.OrdinalIgnoreCase)))
            return PortableBackupCleanupConsole.Run(() => RunCli(args));

        AppInstallLaunch.Current = new DesktopAppInstallLaunchService();

        // Unpackaged foundation: never initialize inherited updater hooks, auto-apply,
        // rescue or sidecar migration. Relicade starts with its own OS-scoped data root.
        RelicadePaths.EnsureUserDataRootExists();

        var launch = KioskLaunch.Parse(args);
        args = launch.Args;

        if (args.Length > 0 && args[0].StartsWith("-"))
        {
            if (OperatingSystem.IsWindows())
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                }
            }

            int exitCode = RunCli(args);

            if (OperatingSystem.IsWindows())
                FreeConsole();

            return exitCode;
        }

#if DEBUG
        RegisterDebugExceptionHandlers();
#endif

        DefaultMenuInteractionHandler.MenuShowDelay = TimeSpan.Zero;
        if (launch.Kiosk)
            KioskLaunch.ArmProcess();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

#if DEBUG
    private static void RegisterDebugExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLog.Log("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
#endif

    private static string? ResolveVelopackRootAppDir()
    {
        try
        {
            return VelopackLocator.Current?.RootAppDir;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveVelopackPackageDirectory()
    {
        try
        {
            var locator = VelopackLocator.Current;
            if (locator is null)
                return null;

            if (OperatingSystem.IsLinux() &&
                locator is LinuxVelopackLocator linux &&
                !string.IsNullOrWhiteSpace(linux.AppImagePath))
            {
                return Path.GetDirectoryName(linux.AppImagePath);
            }

            if (OperatingSystem.IsMacOS())
            {
                return RelicadePaths.ResolveMacOsPackageDirectory(
                    locator.RootAppDir,
                    locator.AppContentDir,
                    AppDomain.CurrentDomain.BaseDirectory);
            }
        }
        catch
        {
            // Not a Velopack install, or locator unavailable.
        }

        return null;
    }

    private static int RunCli(string[] args)
    {
        try
        {
            var cliHandler = new CLIHandler();
            return cliHandler.Execute(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsLinux())
            builder = builder.With(new X11PlatformOptions { OverlayPopups = true });

        return builder;
    }
}
