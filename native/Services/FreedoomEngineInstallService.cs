using System.Runtime.InteropServices;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

internal static class FreedoomEngineInstallService
{
    internal static string ConfirmationMessage(OwnedDataRequirement? definition = null)
    {
        var requirement = definition ?? OwnedGameSetupService.FreedoomPhase2;
        var asset = requirement.AssetIdentity.Split("|sha256:", StringSplitOptions.None)[0];
        return $"Install UZDoom {requirement.ReleaseIdentity} for Linux x64 in this library entry?\n\nThis explicit setup uses the reviewed {asset} package. After installation, you can download and confirm the free Freedoom Phase 2 data. Nothing is played automatically. Existing engines, versions and personal game files are not replaced.";
    }
    internal sealed record PreparedInstall(OwnedDataRequirement Requirement, string AssetName,
        OwnedGameSetupSession Context, FirstInstallPackageConstraint Constraint);

    internal static PreparedInstall PrepareFirstInstall(GameInfo game, GameManager manager,
        CancellationToken token, OwnedDataRequirement? definition = null)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IOException("Guided Freedoom installation currently supports Linux x64 only. Ordinary engine installation and manual game setup remain available on their supported platforms.");
        if (!OwnedGameSetupService.OffersFreedoom(game) || game.Status != GameStatus.NotInstalled || game.IsLoading ||
            !string.IsNullOrEmpty(game.InstalledVersion))
            throw new IOException("First-time Freedoom setup will not replace an existing or busy engine. Use ordinary version controls for an existing installation.");
        var requirement = definition ?? OwnedGameSetupService.FreedoomPhase2;
        var split = requirement.AssetIdentity.Split("|sha256:", StringSplitOptions.None);
        if (split.Length != 2 || split[1] != requirement.ExecutableSha256)
            throw new IOException("The guided engine package definition is invalid.");
        var context = new OwnedGameSetupSession(manager, game.InstanceKey, requirement);
        var constraint = new FirstInstallPackageConstraint(requirement.ReleaseIdentity, split[0], split[1])
        { MaximumBytes = 34_560_504, ValidateContext = () => context.ValidateDownloadRequest(token) };
        constraint.ValidateSelection(requirement.ReleaseIdentity, split[0]);
        var destination = game.GetInstallPath(manager.GamesFolder);
        new PortableInstallationTransaction(destination).RequireFirstInstallationTarget();
        return new PreparedInstall(requirement, split[0], context, constraint);
    }

    internal static async Task<bool> InstallAsync(GameInfo game, GameManager manager, AppSettings settings,
        CancellationToken token, Func<string, string, Task<bool>> confirm,
        IGameDownloadDialogs? dialogs = null, OwnedDataRequirement? definition = null)
    {
        var prepared = PrepareFirstInstall(game, manager, token, definition);
        var requirement = prepared.Requirement; var assetName = prepared.AssetName;
        var context = prepared.Context; var constraint = prepared.Constraint;
        game.IsLoading = true;
        try
        {
            if (!await confirm(ConfirmationMessage(requirement), "Set up Freedoom Phase 2")) return false;
            token.ThrowIfCancellationRequested();
            context.ValidateDownloadRequest(token);
            using var fresh = ReleaseRequestCoordinator.AllowCachedMetadata(TimeSpan.Zero);
            var release = await CatalogReleaseSelection.FetchSelectedAsync(manager.HttpClient, "github", "UZDoom/UZDoom",
                requirement.ReleaseIdentity, game.GetReleaseApiToken(settings), token);
            token.ThrowIfCancellationRequested();
            context.ValidateDownloadRequest(token);
            if (release == null || release.tag_name != requirement.ReleaseIdentity)
                throw new IOException("The reviewed engine release is unavailable. No newer or alternative release was selected.");
            var assets = release.assets.Where(asset => asset.name == assetName).ToArray();
            var expectedUrl = $"https://github.com/UZDoom/UZDoom/releases/download/{requirement.ReleaseIdentity}/{assetName}";
            if (assets.Length != 1 || assets[0].browser_download_url != expectedUrl)
                throw new IOException("The reviewed engine download is missing or its source changed. No alternative asset was selected.");
            GameDownloadService.SelectExplicit(game, release, settings, assets[0]);
            await GameDownloadInstallService.DownloadAndInstallAsync(game, manager.HttpClient, manager.GamesFolder,
                release, settings, GameStatus.NotInstalled, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease,
                firstInstall: constraint);
            token.ThrowIfCancellationRequested();
            context.ValidateDownloadRequest(token);
            if (!game.IsInstalled || game.InstalledVersion != requirement.ReleaseIdentity ||
                game.InstalledBuild?.Sha256 != constraint.Sha256) return false;
            // The existing data-setup flow independently revalidates the committed
            // package with its non-recovering owned-data authority before showing readiness.
            return true;
        }
        finally { game.IsLoading = false; }
    }
}
