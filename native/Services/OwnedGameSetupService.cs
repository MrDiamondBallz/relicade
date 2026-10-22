using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

internal static class OwnedGameSetupService
{
    // Official release asset 512564020; source prerequisite documentation is pinned
    // in the setup guide. Metadata feeds cannot supply or override these rules.
    internal static readonly OwnedDataRequirement PerfectDark = new("perfect-dark-us-11-linux-x64",
        "Perfect Dark US 1.1 (.z64)", "ci-dev-build",
        "pd-x86_64-linux.tar.gz|sha256:0f28fd59b488e0a919aae4350083059faebd462fce52df7bfdc9a4aec26f92ae",
        "40e8e1317a712a0b29ebd715d25ab7ae366aa601444eb8dec45b97c308ddf9a5", 33554432,
        "e03b088b6ac9e0080440efed07c1e40f");

    internal static readonly OwnedDataRequirement FreedoomPhase2 = new(
        "uzdoom-5-0-3-freedoom-0-13-phase2-linux-x64", "Freedoom Phase 2 v0.13.0 (.wad)", "5.0.3",
        "Linux-UZDoom-Release-x86_64.AppImage|sha256:9c5166febc09cbf80c24612a78829037c953f38c4d986e36f8475ddd507e0228",
        "9c5166febc09cbf80c24612a78829037c953f38c4d986e36f8475ddd507e0228", 28787748,
        "cd666466759b5e5f63af93c5f0ffd0a1", "freedoom2.wad",
        "a8772e088847032510d97ba2312406a6998f21cbab44d4ff10696faa9c0ecd4b");

    // Optional, explicit play choice. Ordinary engine Launch remains unchanged.
    internal static bool OffersFreedoom(GameInfo game) => !game.IsManuallyManaged &&
        RepositorySourceHelper.IsGitHub(game.RepositorySource) &&
        string.Equals(game.Repository, "UZDoom/UZDoom", StringComparison.OrdinalIgnoreCase);

    internal sealed record InstallTarget(string RelativePath, long Length, string Sha256)
    {
        internal void Validate(string candidate)
        {
            var path = PortableInstallationTransaction.SafePath(candidate, RelativePath);
            PortableInstallationTransaction.RequireRegularEvidenceFile(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length != Length || Convert.ToHexStringLower(SHA256.HashData(file)) != Sha256)
                throw new InvalidDataException("The staged setup executable does not match this supported package. No generation was activated.");
        }
    }

    internal static InstallTarget? PinnedInstallTarget(GameInfo game, string release, string asset)
    {
        var requirement = Requirement(game);
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            requirement == null || requirement.ReleaseIdentity != release || requirement.AssetIdentity != asset)
            return null;
        // This is the post-extraction relative path in the exact pinned archive,
        // not a preferred executable supplied by remote metadata.
        return new("pd.x86_64", 15329872, requirement.ExecutableSha256);
    }

    internal static OwnedDataRequirement? Requirement(GameInfo game) =>
        RepositorySourceHelper.IsGitHub(game.RepositorySource) &&
        (string.Equals(game.Repository, "perfect-dark-pc-port/perfect_dark", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(game.Repository, "fgsfdsfgs/perfect_dark", StringComparison.OrdinalIgnoreCase)) ? PerfectDark : null;

    internal static async Task RefreshAsync(GameInfo game, string gamesFolder)
    {
        var requirement = Requirement(game);
        if (requirement == null) return;
        game.OwnedDataReady = false;
        if (!game.IsInstalled) { game.OwnedDataSetupMessage = "Install a supported package before game-data setup."; return; }
        try
        {
            await Task.Run(() => new PortableInstallationTransaction(game.GetInstallPath(gamesFolder))
                .ReadOwnedData(requirement, RelicadePaths.UserDataRoot));
            game.OwnedDataSetupMessage = "Required game data verified locally; gameplay compatibility still depends on this system.";
            game.OwnedDataReady = true;
        }
        catch (FileNotFoundException) { game.OwnedDataSetupMessage = "Needs game files: select your lawful Perfect Dark US 1.1 .z64 file."; }
        catch (DirectoryNotFoundException) { game.OwnedDataSetupMessage = "Needs game files: select your lawful Perfect Dark US 1.1 .z64 file."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { game.OwnedDataSetupMessage = "Requirements not confirmed: " + ex.Message; }
    }
}

// A single selected instance and unchanged local library/settings authorize this
// dialog. Selecting another card during the picker never retargets the operation.
internal sealed class OwnedGameSetupSession
{
    readonly GameManager manager;
    readonly GameInfo game;
    readonly string instance, install, dataRoot, appsRoot, repository, libraryPath;
    readonly byte[] library;
    readonly OwnedDataRequirement requirement;
    internal string Label => requirement.Label;
    internal long RequiredBytes => requirement.Length;
    internal string VariantMd5 => requirement.Md5;
    internal string? ExpectedSha256 => requirement.ExpectedSha256;

    internal OwnedGameSetupSession(GameManager manager, string instance) : this(manager, instance, null) { }
    // A definition is injectable only by trusted in-process compositions/tests;
    // the production constructor always uses the compiled project registry.
    internal OwnedGameSetupSession(GameManager manager, string instance, OwnedDataRequirement? definition)
    {
        this.manager = manager; this.instance = instance;
        game = ManagedShortcutLaunch.Resolve(manager.Games, instance);
        requirement = definition ?? OwnedGameSetupService.Requirement(game) ?? throw new IOException("No reviewed setup adapter is available for this project.");
        if (string.IsNullOrWhiteSpace(game.FolderName) || !string.IsNullOrEmpty(game.InstallPath) || game.IsManuallyManaged || game.IsFlatpak || game.IsWindowsInstaller)
            throw new IOException("This setup adapter requires a dedicated managed portable installation, not a linked or OS-managed folder.");
        PortableInstallationTransaction.ValidateRelative(game.FolderName);
        appsRoot = Path.GetFullPath(manager.GamesFolder); install = game.GetInstallPath(appsRoot);
        dataRoot = RelicadePaths.UserDataRoot; repository = game.Repository!;
        libraryPath = manager.CatalogService.AppsConfigPath;
        library = ReadLibrary();
        ManagedShortcutLaunch.ValidateLocalIdentities(library);
        Validate();
    }

    byte[] ReadLibrary(int? expectedLength = null)
    {
        PortableInstallationTransaction.RequireRegularEvidenceFile(libraryPath);
        using var file = new FileStream(libraryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = file.Length;
        if (length > 8 * 1024 * 1024) throw new IOException("The local library exceeds the setup inspection limit.");
        if (expectedLength is { } expected && length != expected)
            throw new IOException("The saved library changed. Reopen game setup before continuing.");
        // Allocate only the bounded opened-file length, never ReadAllBytes after a
        // separate path-size check. Growth cannot expand this read's allocation.
        var bytes = new byte[checked((int)length)];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new IOException("The saved library changed during inspection. Reopen game setup.");
        return bytes;
    }

    void Validate()
    {
        manager.VerifyRollbackSettingsRecovery();
        var store = SettingsStoreProvider.RollbackCurrent;
        if (store is ISettingsRecoveryState { IsReadOnly: true } || store is not IPortableRollbackSettingsEvidence evidence)
            throw new IOException("Initialized writable settings evidence is required. Reopen Relicade before setup.");
        var (persistedSettings, _, _) = evidence.ReadRollbackSettings();
        if (KioskLaunch.IsStartupLocked(persistedSettings) || Path.GetFullPath(persistedSettings.AppsPath) != appsRoot)
            throw new IOException("Saved setup permissions or apps directory changed. Reopen game setup.");
        if (KioskLaunch.IsStartupLocked(manager.CurrentSettings) ||
            Path.GetFullPath(manager.CurrentSettings.AppsPath) != appsRoot || RelicadePaths.UserDataRoot != dataRoot ||
            !manager.Games.Any(g => ReferenceEquals(g, game)) || game.InstanceKey != instance || game.Repository != repository ||
            game.GetInstallPath(manager.GamesFolder) != install || !string.IsNullOrEmpty(game.InstallPath) ||
            game.IsManuallyManaged || game.IsFlatpak || game.IsWindowsInstaller)
            throw new IOException("Library or setup permissions changed. Reopen game setup before continuing.");
        if (!ReadLibrary(library.Length).AsSpan().SequenceEqual(library))
            throw new IOException("The saved library changed. Reopen game setup before continuing.");
        // The persisted row, not a potentially stale card alone, must identify the
        // same root and project. Strict identity validation already ran above.
        using var doc = JsonDocument.Parse(library);
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array
            ? new Dictionary<string, JsonElement> { ["apps"] = doc.RootElement }
            : doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        if (rows.Keys.Any(k => k is not ("apps" or "standard" or "experimental" or "custom"))) throw new IOException("Unknown library format.");
        var apps = manager.CatalogService.ParseAppsFromDictionary(rows);
        var persisted = ManagedShortcutLaunch.Resolve(apps, instance);
        if (persisted.Repository != repository || persisted.GetInstallPath(appsRoot) != install || !string.IsNullOrEmpty(persisted.InstallPath))
            throw new IOException("The saved library does not identify this dedicated installation.");
        foreach (var other in apps.Where(g => g.InstanceKey != instance))
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other.GetInstallPath(appsRoot)));
            if (path == install || path.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                install.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("Shared or overlapping installations require separate setup support.");
        }
    }

    internal OwnedDataPreview Preview(string source, CancellationToken token)
    {
        Validate();
        var result = new PortableInstallationTransaction(install).PreviewOwnedData(requirement, dataRoot, source, token);
        Validate(); return result;
    }
    internal OwnedDataImportResult Apply(OwnedDataPreview plan, CancellationToken token)
    {
        Validate();
        return new PortableInstallationTransaction(install).ImportOwnedData(requirement, dataRoot, plan, token,
            point => { if (point != "Copying") Validate(); });
    }
    internal void ValidateReady()
    {
        Validate();
        new PortableInstallationTransaction(install).ReadOwnedData(requirement, dataRoot);
        Validate();
    }
    internal Task RefreshAsync() => OwnedGameSetupService.RefreshAsync(game, appsRoot);
}
