using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>Cleanup-only, strictly local command composition. No status/recovery/updater work.</summary>
internal static class PortableBackupCleanupCommand
{
    internal const string Preview = "--preview-portable-backups", Cleanup = "--cleanup-portable-backups";
    internal static bool IsRequest(IEnumerable<string> args) => args.Any(a =>
        a.StartsWith(Preview, StringComparison.OrdinalIgnoreCase) || a.StartsWith(Cleanup, StringComparison.OrdinalIgnoreCase));
    internal static bool ValidArguments(string[] args) =>
        (args.Length == 2 && args[0] == Preview || args.Length == 4 && args[0] == Cleanup && args[2] == "--confirm" && ValidHash(args[3])) &&
        !string.IsNullOrWhiteSpace(args[1]) && !args[1].StartsWith('-') && !args[1].Contains('=') && !args[1].Any(char.IsControl) && args[1] == args[1].Trim();
    internal static bool ValidOriginalArguments(string[] original, string[] dispatched) =>
        !IsRequest(original.Skip(1)) || ValidArguments(original.Skip(1).ToArray()) && original.Skip(1).SequenceEqual(dispatched);
    static bool ValidHash(string hash) => hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    // Match the Core writer-authority identity: case-sensitive outside Windows.
    // In particular, Linux default live and located LIVE must not share a lease.
    static StringComparer RootComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    static bool Same(string a, string b) => RootComparer.Equals(a, b);
    static string Full(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    static bool Exists(string p)
    {
        PortableInstallationTransaction.CheckLinks(p);
        try { _ = File.GetAttributes(p); return true; }
        catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; }
    }
    internal static int Execute(GameManager manager, string[] args)
    {
        var game = manager.Games.Where(g => string.Equals(g.InstanceKey, args[1], StringComparison.Ordinal)).ToArray();
        if (game.Length != 1) throw new IOException("The exact case-sensitive InstanceKey is missing or ambiguous. No name or repository fallback is supported.");
        var selected = game[0];
        if (selected.IsManuallyManaged || string.IsNullOrWhiteSpace(selected.Repository) || selected.IsWindowsInstaller || selected.IsFlatpak)
            throw new IOException("Only a uniquely managed portable instance can clean redundant backups.");
        PortableInstallationTransaction.ValidateRelative(selected.FolderName!);
        var effective = Full(selected.GetInstallPath(manager.GamesFolder));
        var defaultRoot = Full(Path.Combine(manager.GamesFolder, selected.FolderName!));
        void ValidateRoots()
        {
            if (Full(selected.GetInstallPath(manager.GamesFolder)) != effective || Full(Path.Combine(manager.GamesFolder, selected.FolderName!)) != defaultRoot)
                throw new IOException("The selected installation location changed.");
            if (!Same(defaultRoot, effective) && Exists(defaultRoot)) throw new IOException("Distinct default and located roots are ambiguous; no cleanup is permitted.");
            foreach (var root in new[] { defaultRoot, effective }.Distinct(RootComparer))
                if (Exists(Path.Combine(root, WindowsInstallerService.ReceiptFileName)) || Exists(Path.Combine(root, FlatpakService.ReceiptFileName)))
                    throw new IOException("Conflicting OS package evidence blocks portable cleanup.");
            foreach (var other in manager.Games.Where(g => !ReferenceEquals(g, selected)))
                foreach (var root in new[] { other.GetInstallPath(manager.GamesFolder), Path.Combine(manager.GamesFolder, other.FolderName!) })
                {
                    var path = Full(root);
                    if (Same(effective, path) || effective.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.StartsWith(effective + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Shared or overlapping library roots cannot authorize cleanup.");
                }
        }
        ValidateRoots();
        // Core owns one invocation-local collection spanning both roots and their
        // common ancestors; a separate outer lease would conflict with itself.
        var libraryPath = manager.CatalogService.AppsConfigPath;
        PortableInstallationTransaction.CheckLinks(libraryPath);
        var library = File.ReadAllBytes(libraryPath);
        byte[] SettingsEvidence()
        {
            var global = SettingsStoreProvider.Default.Load();
            if (SettingsStoreProvider.Default is ISettingsRecoveryState { IsReadOnly: true } || KioskLaunch.IsStartupLocked(global) || KioskLaunch.IsStartupLocked(manager.CurrentSettings))
                throw new IOException("Settings recovery or kiosk policy blocks backup cleanup.");
            // Hash effective settings AND the persisted default document identity;
            // injected stores are also bound by their complete effective values.
            var path = RelicadePaths.SettingsJsonPath;
            PortableInstallationTransaction.CheckLinks(path);
            var raw = Exists(path) ? Digest(File.ReadAllBytes(path)) : "absent";
            return JsonSerializer.SerializeToUtf8Bytes(new { global, manager = manager.CurrentSettings, path, raw });
        }
        var settings = SettingsEvidence();
        var context = Digest(JsonSerializer.SerializeToUtf8Bytes(new { instanceKey = args[1], effective, defaultRoot,
            libraryPath, librarySha256 = Digest(library), settingsSha256 = Digest(settings) }));
        void Validate()
        {
            manager.VerifyRemovalLibrary(); ValidateRoots();
            PortableInstallationTransaction.CheckLinks(libraryPath);
            if (!File.ReadAllBytes(libraryPath).AsSpan().SequenceEqual(library) || !SettingsEvidence().AsSpan().SequenceEqual(settings))
                throw new IOException("Library/settings changed after preview; no newly expanded cleanup is authorized.");
        }
        var tx = new PortableInstallationTransaction(effective);
        if (args[0] == Preview)
        {
            var plan = tx.PreviewRedundantBackups(context, Validate, alternativeRoot: defaultRoot);
            Console.WriteLine("Plan SHA256: " + plan.PlanSha256);
            foreach (var f in plan.Files) Console.WriteLine($"{f.OperationId}/{f.BackupRelativePath} -> retained {f.OperationId}/{f.RetainedRelativePath} | {f.Length} bytes | SHA256 {f.Sha256}");
            Console.WriteLine($"Reclaimable bytes: {plan.ReclaimableBytes}; duplicate payload files: {plan.Files.Count}.");
            Console.WriteLine("ALL previous generations, data snapshots, journals, current saves and the entire current-operation backup remain. This is not uninstall, history pruning or secure erasure.");
            return 0;
        }
        var result = tx.CleanupRedundantBackups(args[3], context, Validate, alternativeRoot: defaultRoot);
        Console.WriteLine($"Plan SHA256: {result.PlanSha256}; removed THIS invocation: {result.DeletedFiles} files / {result.ReclaimedBytes} bytes; completed: {result.Completed}.");
        Console.WriteLine("All retained previous generations, declared data and current-operation backups remain; redundant copies are not rolled back or securely erased.");
        return result.Completed ? 0 : 1;
    }
}
