using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>Offline, exact tile dispatch. No name, repository, executable or root fallback.</summary>
internal static class ManagedShortcutLaunch
{
    internal const string Command = "--launch-instance";
    internal static bool IsRequest(IEnumerable<string> args) => args.Any(a => string.Equals(a, Command, StringComparison.OrdinalIgnoreCase) || a.StartsWith(Command + "=", StringComparison.OrdinalIgnoreCase));
    internal static bool ValidArguments(string[] args) => args.Length == 2 && args[0] == Command &&
        !string.IsNullOrWhiteSpace(args[1]) && !args[1].StartsWith('-') && !args[1].Any(char.IsControl);

    // Program consumes --kiosk before dispatch. Check the original process arguments too,
    // without changing ordinary --run or treating test-host arguments as launcher input.
    internal static bool ValidOriginalArguments(string[] original, string[] dispatched) =>
        !IsRequest(original.Skip(1)) || ValidArguments(original.Skip(1).ToArray()) && original.Skip(1).SequenceEqual(dispatched);

    internal static GameInfo Resolve(IReadOnlyList<GameInfo> library, string key)
    {
        var matches = library.Where(g => string.Equals(g.InstanceKey, key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("The shortcut's exact library instance is missing or ambiguous. Recreate it from the intended library tile.");
        return matches[0];
    }

    // The general library parser deduplicates rows for display. Shortcuts must not
    // inherit its select-first behavior. Validate every raw identity before that load.
    internal static void ValidateLocalIdentities(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var rows = new List<JsonElement>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array) rows.AddRange(doc.RootElement.EnumerateArray());
        else
        {
            var sections = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!sections.Add(p.Name)) throw new InvalidDataException("Ambiguous local library properties; repair the library first.");
                if (p.Name is "apps" or "standard" or "experimental" or "custom") rows.AddRange(p.Value.EnumerateArray());
            }
        }
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in row.EnumerateObject())
                if (!names.Add(p.Name)) throw new InvalidDataException("Ambiguous local library properties; repair the library first.");
            string? Value(string name) => row.TryGetProperty(name, out var p) ? p.GetString() : null;
            var key = RepositorySourceHelper.GetInstanceKey(Value("repositorySource"), Value("repository"), Value("folderName"));
            if (!keys.Add(key)) throw new InvalidDataException("Duplicate library instance identity; repair duplicate tiles before launching a shortcut.");
        }
    }

    internal static string ValidateLauncher(string? path, string gamesFolder, string? installationRoot = null)
    {
        const string refusal = "A stable installed Relicade launcher is required for the running-game lease. Open an installed Relicade build and recreate this shortcut; no direct game shortcut was created.";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new NotSupportedException(refusal);
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full);
        var supported = name.Equals("Relicade.Desktop", StringComparison.OrdinalIgnoreCase) || name.Equals("Relicade.Desktop.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Relicade", StringComparison.OrdinalIgnoreCase) || name.Equals("Relicade.exe", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Relicade", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
        var normalized = full.Replace('\\', '/');
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var library = Path.GetFullPath(gamesFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var installation = installationRoot == null ? null : Path.GetFullPath(installationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!supported || ShortcutHelper.IsAppImageMountPath(full) || normalized.Contains("/.mount_", StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("/tmp/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/var/tmp/", StringComparison.OrdinalIgnoreCase) || full.StartsWith(library, StringComparison.OrdinalIgnoreCase) ||
            installation != null && full.StartsWith(installation, StringComparison.OrdinalIgnoreCase) ||
            normalized.Split('/').Contains(PortableInstallationTransaction.DirectoryName, StringComparer.OrdinalIgnoreCase)) throw new NotSupportedException(refusal);
        for (FileSystemInfo? item = new FileInfo(full); item != null; item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException(refusal);
        return full;
    }

    internal static string? ResolveOwnLauncher() => ShortcutHelper.ResolveLauncherPath();

    internal static void EnsureSupportedLifetime(string target)
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || HostProcessEnvironment.IsSandboxed() ||
            target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Managed shortcuts currently require a directly tracked Linux native process outside a sandbox. Runner, external-start and other native package lifetimes are not established; use Relicade's supported interface instead.");
    }

    internal static async Task<(bool Launched, Task? Lifetime)> LaunchAsync(GameInfo game, string gamesFolder, string operation)
    {
        Process? process = null;
        void Started(Process? value) => process = value;
        game.GameProcessStarted += Started;
        try
        {
            var launched = await GameLaunchService.LaunchAsync(game, gamesFolder, operation);
            if (!launched || process == null) return (false, null);
            return (true, HostGameSession.ExitTask(process) ?? process.WaitForExitAsync());
        }
        finally { game.GameProcessStarted -= Started; }
    }

    internal static async Task<int> ExecuteAsync(GameManager manager, string key,
        Func<GameInfo, string, Task<(bool Launched, Task? Lifetime)>>? launch = null)
    {
        await manager.LoadGamesForManagedShortcutAsync();
        var game = Resolve(manager.LibraryApps, key);
        var root = game.GetInstallPath(manager.GamesFolder);
        var manifest = PortableInstallationRecovery.EnsureReadable(game, root) ?? throw new NotSupportedException("This tile is not a managed portable installation. Recreate its shortcut from Relicade.");
        await GameStatusService.CheckStatusAsync(game, manager.HttpClient, manager.GamesFolder, checkRemoteVersion: false, applyCachedRelease: false);
        if (game.PortableRecoveryError != null || !game.CanLaunch) throw new IOException("The exact installation is not launchable. Resolve local recovery/setup in Relicade first.");
        var target = PortableInstallationTransaction.SafePath(root, manifest.SelectedExecutable);
        EnsureSupportedLifetime(target);
        if (!GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories, game.GetInstallationOptions(), out _).Contains(target))
            throw new IOException("The committed executable is not launchable; no helper or other generation was substituted.");
        manager.VerifyManagedShortcutLibrary();
        var result = await (launch == null ? LaunchAsync(game, manager.GamesFolder, manifest.OperationId) : launch(game, manifest.OperationId));
        if (!result.Launched || result.Lifetime == null) return 1;
        await result.Lifetime;
        return 0;
    }
}
