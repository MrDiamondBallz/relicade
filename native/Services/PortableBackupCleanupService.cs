using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>Cleanup-only typed composition. Never loads status, recovers, saves or contacts a provider.</summary>
internal static class PortableBackupCleanupService
{
    internal static BackupCleanupPreview Preview(GameManager manager, string instanceKey)
    {
        var (tx, context, validate, defaultRoot) = Prepare(manager, instanceKey);
        return tx.PreviewRedundantBackups(context, validate, alternativeRoot: defaultRoot);
    }

    internal static BackupCleanupResult Apply(GameManager manager, string instanceKey, string expectedPlanSha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedPlanSha256);
        if (!ValidHash(expectedPlanSha256)) throw new ArgumentException("Expected an exact lower-case SHA256 cleanup plan.", nameof(expectedPlanSha256));
        cancellationToken.ThrowIfCancellationRequested();
        var (tx, context, validate, defaultRoot) = Prepare(manager, instanceKey);
        return tx.CleanupRedundantBackups(expectedPlanSha256, context, validate, cancellationToken, alternativeRoot: defaultRoot);
    }

    internal static bool ValidHash(string hash) => hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static StringComparer RootComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    static bool Same(string a, string b) => RootComparer.Equals(a, b);
    static string Full(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    static bool Exists(string p)
    {
        PortableInstallationTransaction.CheckLinks(p);
        try { _ = File.GetAttributes(p); return true; }
        catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; }
    }

    // Same read-only path policy as the removal loader, without its Games mutation.
    static void ValidateRemovalAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) ||
            path.Replace('\\', '/').Split('/').Any(p => p is "." or ".." ||
                p.Equals(PortableInstallationTransaction.DirectoryName, StringComparison.OrdinalIgnoreCase)) ||
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) == null)
            throw new IOException("Unsafe or ambiguous local removal path. Repair the library first.");
        for (var item = new DirectoryInfo(path); item != null; item = item.Parent)
        {
            try
            {
                if ((File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked removal paths require explicit repair; no files were removed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    static (PortableInstallationTransaction Tx, string Context, Action Validate, string DefaultRoot) Prepare(GameManager manager, string instanceKey)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        manager.VerifyBackupCleanupSettingsRecovery();
        if (instanceKey != instanceKey.Trim() || instanceKey.StartsWith('-') || instanceKey.Contains('=') || instanceKey.Any(char.IsControl))
            throw new ArgumentException("Expected an exact case-sensitive InstanceKey.", nameof(instanceKey));
        var game = manager.Games.Where(g => string.Equals(g.InstanceKey, instanceKey, StringComparison.Ordinal)).ToArray();
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
        var libraryPath = manager.CatalogService.AppsConfigPath;
        PortableInstallationTransaction.CheckLinks(libraryPath);
        var library = File.ReadAllBytes(libraryPath);
        // Unlike the early CLI, the already-initialized UI has no removal-loader
        // snapshot. Validate its persisted identities without replacing Games or
        // invoking the general loader (which saves, recovers and prefetches).
        ValidateLibrary();
        void ValidateLibrary()
        {
            ManagedShortcutLaunch.ValidateLocalIdentities(library);
            using var doc = JsonDocument.Parse(library);
            var sections = new Dictionary<string, JsonElement>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array) sections.Add("apps", doc.RootElement);
            else foreach (var field in doc.RootElement.EnumerateObject())
            {
                if (field.Name is not ("apps" or "standard" or "experimental" or "custom"))
                    throw new IOException("Unknown local library format; repair before cleanup.");
                sections.Add(field.Name, field.Value);
            }
            var apps = manager.CatalogService.ParseAppsFromDictionary(sections);
            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.FolderName))
                    throw new IOException("Incomplete local cleanup identity; repair the library first.");
                PortableInstallationTransaction.ValidateRelative(app.FolderName);
                ValidateRemovalAbsolutePath(Path.Combine(manager.GamesFolder, app.FolderName));
                if (app.InstallPath != null) ValidateRemovalAbsolutePath(app.InstallPath);
            }
            var matches = apps.Where(g => string.Equals(g.InstanceKey, instanceKey, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !Same(Full(matches[0].GetInstallPath(manager.GamesFolder)), effective) ||
                matches[0].IsManuallyManaged || matches[0].IsWindowsInstaller || matches[0].IsFlatpak)
                throw new IOException("The persisted library no longer identifies this exact portable instance. Reopen the library before cleanup.");
            foreach (var other in apps.Where(g => g.InstanceKey != instanceKey))
                foreach (var root in new[] { other.GetInstallPath(manager.GamesFolder), Path.Combine(manager.GamesFolder, other.FolderName!) })
                {
                    var path = Full(root);
                    if (Same(effective, path) || effective.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.StartsWith(effective + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Shared or overlapping persisted library roots cannot authorize cleanup.");
                }
        }
        byte[] SettingsEvidence()
        {
            manager.VerifyBackupCleanupSettingsRecovery();
            // Refuse effective UI policy BEFORE a default-store reload can
            // replace a shared store's Current object with persisted values.
            if (KioskLaunch.IsStartupLocked(manager.CurrentSettings) ||
                !Same(GameManager.ValidateRemovalAppsPath(manager.CurrentSettings), Full(manager.GamesFolder)))
                throw new IOException("Effective kiosk policy or changed apps directory blocks cleanup. Reopen the library.");
            var global = SettingsStoreProvider.Default.Load();
            manager.VerifyBackupCleanupSettingsRecovery();
            if (SettingsStoreProvider.Default is ISettingsRecoveryState { IsReadOnly: true } || KioskLaunch.IsStartupLocked(global) || KioskLaunch.IsStartupLocked(manager.CurrentSettings))
                throw new IOException("Settings recovery or kiosk policy blocks backup cleanup.");
            if (!Same(GameManager.ValidateRemovalAppsPath(manager.CurrentSettings), Full(manager.GamesFolder)))
                throw new IOException("The configured apps directory changed. Reopen before cleanup.");
            var path = RelicadePaths.SettingsJsonPath;
            PortableInstallationTransaction.CheckLinks(path);
            var raw = Exists(path) ? Digest(File.ReadAllBytes(path)) : "absent";
            return JsonSerializer.SerializeToUtf8Bytes(new { global, manager = manager.CurrentSettings, path, raw });
        }
        var settings = SettingsEvidence();
        // Exact BC01 canonicalization retained, shared by CLI and UI.
        var context = Digest(JsonSerializer.SerializeToUtf8Bytes(new { instanceKey, effective, defaultRoot,
            libraryPath, librarySha256 = Digest(library), settingsSha256 = Digest(settings) }));
        void Validate()
        {
            manager.VerifyBackupCleanupSettingsRecovery();
            var currentSettings = SettingsEvidence();
            manager.VerifyRemovalLibrary(); ValidateRoots();
            PortableInstallationTransaction.CheckLinks(libraryPath);
            if (!File.ReadAllBytes(libraryPath).AsSpan().SequenceEqual(library) || !currentSettings.AsSpan().SequenceEqual(settings) ||
                manager.Games.Count(g => string.Equals(g.InstanceKey, instanceKey, StringComparison.Ordinal)) != 1 ||
                !manager.Games.Any(g => ReferenceEquals(g, selected)) || selected.InstanceKey != instanceKey ||
                selected.IsManuallyManaged || selected.IsWindowsInstaller || selected.IsFlatpak)
                throw new IOException("Library/settings changed after preview; no newly expanded cleanup is authorized.");
        }
        return (new PortableInstallationTransaction(effective), context, Validate, defaultRoot);
    }
}
