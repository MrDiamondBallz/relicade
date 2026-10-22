using System.Diagnostics;
using System.Runtime.InteropServices;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

public static class GameLaunchService
{
    public static Task<bool> LaunchAsync(GameInfo game, string gamesFolder) => LaunchAsync(game, gamesFolder, null);

    // Shortcut dispatch binds the generation it validated before entering this service.
    internal static async Task<bool> LaunchAsync(GameInfo game, string gamesFolder, string? expectedOperationId,
        ManagedShortcutChildLifetime? shortcutObserver = null)
    {
        if (string.IsNullOrEmpty(game.FolderName))
        {
            await GameDialogService.ShowMessageBoxAsync("Cannot launch game: folder name is not configured.", "Configuration Error");
            return false;
        }

        try
        {
            var gamePath = game.GetInstallPath(gamesFolder);
            var portableManifest = PortableInstallationRecovery.EnsureReadable(game, gamePath);
            if (expectedOperationId != null && portableManifest?.OperationId != expectedOperationId)
                throw new IOException("The managed generation changed before launch. Refresh and retry.");
            if (portableManifest != null) game.SelectedExecutable = PortableInstallationTransaction.SafePath(gamePath, portableManifest.SelectedExecutable);
            if (expectedOperationId != null) ManagedShortcutLaunch.EnsureSupportedLifetime(game.SelectedExecutable!);
            if (expectedOperationId != null && shortcutObserver == null)
                throw new NotSupportedException("Managed shortcut child-tree observation must be established before launch.");

            if (FlatpakService.HasReceipt(gamePath))
                return await LaunchFlatpakAsync(game, gamePath);

            if (WindowsInstallerService.HasReceipt(gamePath))
                return await LaunchWindowsInstallerAppAsync(game, gamePath);

            if (!Directory.Exists(gamePath))
            {
                await GameDialogService.ShowMessageBoxAsync($"App directory not found: {gamePath}", "Directory Not Found");
                return false;
            }

            if (expectedOperationId == null) GameInstallationService.EnsureExecutableAtRoot(gamePath, game.GetInstallationOptions());

            var executables = GameInstallationService.FindExecutableCandidates(
                gamePath,
                // A committed generation's validated target may be nested even when
                // root-level helper executables exist. Never discard it or substitute
                // a helper merely because legacy discovery prefers the root.
                portableManifest != null ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly,
                game.GetInstallationOptions(),
                out var needsWine);

            if (executables.Count == 0)
            {
                executables = GameInstallationService.FindExecutableCandidates(
                    gamePath,
                    SearchOption.AllDirectories,
                    game.GetInstallationOptions(),
                    out needsWine);
            }

            if (portableManifest != null && !executables.Contains(game.SelectedExecutable!))
                throw new IOException("The committed executable target is not launchable with the current platform/options. Repair or explicitly select a new target; no helper was substituted.");

            if (executables.Count == 0)
            {
                await GameDialogService.ShowMessageBoxAsync(
                    $"No executable found for {game.Name} in:\n{gamePath}\n\nThe game may not have installed correctly.",
                    "Executable Not Found");
                return false;
            }

            var settings = AppSettings.Load();

            game.AvailableExecutables = executables;

            if (string.IsNullOrEmpty(game.SelectedExecutable))
                game.SelectedExecutable = game.LoadSelectedExecutable(gamesFolder);

            if (executables.Count > 1 &&
                (string.IsNullOrEmpty(game.SelectedExecutable) || !executables.Contains(game.SelectedExecutable)))
            {
                game.SelectedExecutable = null;
                game.NotifyMultipleExecutablesChanged();
                return false;
            }

            var executablePath = !string.IsNullOrEmpty(game.SelectedExecutable) &&
                                 executables.Contains(game.SelectedExecutable)
                ? game.SelectedExecutable
                : executables[0];

            // A mixed release can contain both .exe files and shell launchers.
            // Runner selection belongs to the chosen file, not the candidate list.
            needsWine = OperatingSystem.IsLinux() && executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (needsWine && !WindowsRunnerService.IsWindowsRunnerAvailable(settings, game))
            {
                await GameDialogService.ShowMessageBoxAsync(
                    "The selected Windows executable needs a Linux Windows-runner, but none is configured or detected.\n\n" +
                    "Install Wine/Proton, or open this app’s menu (⋯) → Launch Options → Windows Runner to pick a runner or custom command.",
                    "Windows Runner Not Found");
                return false;
            }

            if (OperatingSystem.IsMacOS() && executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                await GameDialogService.ShowMessageBoxAsync(
                    "This is a Windows app, and Relicade can't run Windows apps on macOS yet.\n\n" +
                    "Open this app's folder to run it with CrossOver or another compatibility tool, " +
                    "or set the platform in Settings → Platform & Cache to Automatic or MacOS to download a Mac build if one exists.",
                    "Windows App on macOS");
                return false;
            }

            using var portableLifetime = PortableInstallationLifetime.Begin(gamePath, expectedOperationId ?? portableManifest?.OperationId);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !executablePath.EndsWith(".app") &&
                !needsWine)
            {
                await MakeExecutableAsync(executablePath);
            }

            var processEnvBefore = LaunchDebugReport.SnapshotProcessEnvironment();
            var gameName = game.Name ?? game.FolderName;
            var startInfo = new ProcessStartInfo();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && executablePath.EndsWith(".app"))
            {
                ConfigureMacAppBundleStart(startInfo, executablePath, gamePath);
            }
            else if (needsWine && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var runnerCommand = WindowsRunnerService.GetWindowsRunnerCommand(settings, executablePath, gamePath, game);
                if (runnerCommand == null)
                {
                    await GameDialogService.ShowMessageBoxAsync(
                        "A Linux Windows-runner was detected earlier but is no longer available.",
                        "Windows Runner Error");
                    return false;
                }

                startInfo.UseShellExecute = false;
                startInfo.WorkingDirectory = gamePath;
                startInfo.FileName = runnerCommand.FileName;

                foreach (var argument in runnerCommand.Arguments)
                    startInfo.ArgumentList.Add(argument);

                HostProcessEnvironment.SanitizeThenApply(startInfo, runnerCommand.EnvironmentVariables);
            }
            else
            {
                startInfo.FileName = executablePath;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? gamePath;
                startInfo.UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                if (!startInfo.UseShellExecute)
                    HostProcessEnvironment.Sanitize(startInfo);
            }

            // No-ops unless Relicade itself is running inside a Flatpak sandbox.
            HostProcessEnvironment.RouteToHostIfSandboxed(startInfo);

            var startInfoEnvAfter = startInfo.UseShellExecute
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo);
            var selectedExecutableFile = TryReadSelectedExecutableFile(gamePath);

            System.Diagnostics.Process? gameProcess;
            try
            {
                gameProcess = shortcutObserver == null ? HostGameSession.Start(startInfo) : shortcutObserver.Start(startInfo);
                if (gameProcess != null && shortcutObserver == null) TrackStartedProcess(portableLifetime, gameProcess, false);
            }
            catch { portableLifetime.PreserveUncertain(); throw; }
            if (gameProcess == null)
            {
                WriteLaunchReport(
                    gameName,
                    gamePath,
                    executablePath,
                    executables,
                    selectedExecutableFile,
                    startInfo,
                    processEnvBefore,
                    startInfoEnvAfter,
                    pid: null,
                    liveProcEnviron: null);
                await GameDialogService.ShowMessageBoxAsync(
                    $"Failed to start {game.Name}. The operating system did not create a process.",
                    "Launch Error");
                return false;
            }

            // A launch attempt is not play history. Record only after the OS
            // returned a process and the existing lifetime tracking accepted it.
            game.UpdateLastPlayedTime(game.IsManagedPortable || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && executablePath.EndsWith(".app")
                ? gamePath
                : (Path.GetDirectoryName(executablePath) ?? gamePath));

            try
            {
                var liveProcEnviron = OperatingSystem.IsLinux()
                    ? LaunchDebugReport.TryReadProcEnviron(gameProcess.Id)
                    : null;

                WriteLaunchReport(
                    gameName,
                    gamePath,
                    executablePath,
                    executables,
                    selectedExecutableFile,
                    startInfo,
                    processEnvBefore,
                    startInfoEnvAfter,
                    gameProcess.Id,
                    liveProcEnviron);

                ScheduleLaunchExitFollowUp(
                    gameName,
                    gamePath,
                    executablePath,
                    executables,
                    selectedExecutableFile,
                    startInfo,
                    processEnvBefore,
                    startInfoEnvAfter,
                    gameProcess,
                    liveProcEnviron);

                game.RaiseGameProcessStarted(gameProcess);

                if (game.GameManager != null && Avalonia.Application.Current != null)
                    game.GameManager.OnPropertyChanged(nameof(GameManager.Games));

                // Keep the existing writer lease and Launching receipt owned by this
                // stack until the kernel proves the entire owned child tree is gone.
                // NEVER Attach the direct PID on this path: stale-PID cleanup could
                // otherwise unlock an orphaned/daemonized descendant after a crash.
                if (shortcutObserver != null)
                    return await CompleteManagedShortcutAsync(portableLifetime, gameProcess, shortcutObserver);
                return true;
            }
            catch
            {
                if (shortcutObserver != null) portableLifetime.PreserveUncertain();
                throw;
            }
        }
        catch (Exception ex)
        {
            Telemetry.Current.CaptureException(ex, handled: true, "app_launch");
            if (Avalonia.Application.Current != null)
                await GameDialogService.ShowMessageBoxAsync($"Error launching {game.Name}: {ex.Message}", "Launch Error");

            return false;
        }
    }

    // Shared production completion boundary, exercised with benign OWN test
    // workers rather than executing any installed/synthetic game payload.
    internal static async Task<bool> CompleteManagedShortcutAsync(PortableInstallationLifetime lifetime,
        Process process, ManagedShortcutChildLifetime observer)
    {
        try
        {
            await observer.ObserveAsync(process);
            return process.ExitCode == 0;
        }
        catch { lifetime.PreserveUncertain(); throw; }
    }

    // Without an owned child-tree observer, a native file format is not authority.
    // This conservative helper also remains the unchanged ordinary UI Attach path.
    internal static void TrackStartedProcess(PortableInstallationLifetime lifetime, Process process, bool managedShortcut)
    {
        if (managedShortcut) lifetime.PreserveUncertain();
        else lifetime.Attach(process);
    }

    private static async Task<bool> LaunchWindowsInstallerAppAsync(GameInfo game, string gamePath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This installation requires Windows desktop.");
        WindowsInstallerService.ApplyState(game, gamePath);
        var executable = game.SelectedExecutable;
        if (!WindowsInstallerService.IsExecutable(executable))
        {
            await GameDialogService.ShowMessageBoxAsync("The installed executable is missing. Select it again or run the installer again from the app menu.", "Executable Not Found");
            return false;
        }
        var info = new ProcessStartInfo(executable!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        var process = Process.Start(info) ?? throw new InvalidOperationException("Windows could not start the installed app.");
        game.UpdateLastPlayedTime(gamePath);
        game.RaiseGameProcessStarted(process);
        return true;
    }

    /// <summary>
    /// Starts a macOS .app through LaunchServices. Plain <c>open</c> exits as soon as the app
    /// launches; <c>-W</c> keeps it alive until the app quits, because the returned process is
    /// what hands gamepad input back to the launcher and what <c>--run</c> waits on.
    /// </summary>
    internal static void ConfigureMacAppBundleStart(ProcessStartInfo startInfo, string appPath, string workingDirectory)
    {
        startInfo.FileName = "open";
        startInfo.ArgumentList.Add("-W");
        startInfo.ArgumentList.Add(appPath);
        startInfo.UseShellExecute = false;
        startInfo.WorkingDirectory = workingDirectory;
    }

    private static async Task<bool> LaunchFlatpakAsync(GameInfo game, string gamePath)
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException("Flatpak apps can only be launched on Linux desktops.");
        // Resume on the caller's UI context before changing bound state or raising
        // launch/library events. Flatpak's process and metadata work remains async.
        var state = await FlatpakService.Current.GetStateAsync(gamePath);
        if (state?.Installed != true)
            throw new InvalidOperationException("This Flatpak app is no longer installed. Install it again from Relicade.");
        game.IsFlatpak = true;
        var startInfo = FlatpakService.StartInfo(FlatpakService.LaunchArguments(state.Receipt), capture: false);
        var before = LaunchDebugReport.SnapshotProcessEnvironment();
        var after = LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not launch Flatpak.");
        game.UpdateLastPlayedTime(gamePath);
        var name = game.Name ?? game.FolderName ?? state.Receipt.ApplicationId;
        WriteLaunchReport(name, gamePath, state.Receipt.Reference, [], null, startInfo, before, after,
            process.Id, LaunchDebugReport.TryReadProcEnviron(process.Id));
        ScheduleLaunchExitFollowUp(name, gamePath, state.Receipt.Reference, [], null, startInfo,
            before, after, process, null);
        game.RaiseGameProcessStarted(process);
        if (game.GameManager != null && Avalonia.Application.Current != null)
            game.GameManager.OnPropertyChanged(nameof(GameManager.Games));
        return true;
    }

    private static async Task MakeExecutableAsync(string executablePath)
    {
        var chmodProcess = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{executablePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(chmodProcess);
        if (process != null)
            await process.WaitForExitAsync();
    }

    static string? TryReadSelectedExecutableFile(string gamePath)
    {
        try
        {
            var path = Path.Combine(gamePath, "selected_executable.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    static string BuildLaunchReport(
        string gameName,
        string gamePath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        int? pid,
        IReadOnlyDictionary<string, string>? liveProcEnviron,
        bool? hasExited = null,
        int? exitCode = null) =>
        LaunchDebugReport.Build(
            DateTimeOffset.UtcNow,
            gameName,
            gamePath,
            chosenExecutable,
            candidates,
            selectedExecutableFile,
            startInfo,
            processEnvBefore,
            startInfoEnvAfter,
            pid,
            liveProcEnviron,
            hasExited,
            exitCode);

    static void WriteLaunchReport(
        string gameName,
        string gamePath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        int? pid,
        IReadOnlyDictionary<string, string>? liveProcEnviron,
        bool? hasExited = null,
        int? exitCode = null)
    {
        var report = BuildLaunchReport(
            gameName,
            gamePath,
            chosenExecutable,
            candidates,
            selectedExecutableFile,
            startInfo,
            processEnvBefore,
            startInfoEnvAfter,
            pid,
            liveProcEnviron,
            hasExited,
            exitCode);
        LaunchDebugReport.TryWrite(gamePath, report);
    }

    static void ScheduleLaunchExitFollowUp(
        string gameName,
        string gamePath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        Process gameProcess,
        IReadOnlyDictionary<string, string>? liveProcEnviron)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                gameProcess.Refresh();
                var hasExited = gameProcess.HasExited;
                int? exitCode = null;
                if (hasExited)
                {
                    try
                    {
                        exitCode = gameProcess.ExitCode;
                    }
                    catch
                    {
                    }
                }

                var report = BuildLaunchReport(
                    gameName,
                    gamePath,
                    chosenExecutable,
                    candidates,
                    selectedExecutableFile,
                    startInfo,
                    processEnvBefore,
                    startInfoEnvAfter,
                    gameProcess.Id,
                    liveProcEnviron,
                    hasExited,
                    exitCode);
                LaunchDebugReport.TryRewriteGameFolder(gamePath, report);
                LaunchDebugReport.TryAppendUserData(
                    hasExited
                        ? $"[{DateTimeOffset.UtcNow:O}] pid={gameProcess.Id} HasExited=true ExitCode={exitCode}{Environment.NewLine}"
                        : $"[{DateTimeOffset.UtcNow:O}] pid={gameProcess.Id} HasExited=false{Environment.NewLine}");
            }
            catch
            {
            }
        });
    }
}
