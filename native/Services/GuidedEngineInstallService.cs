using System.Runtime.InteropServices;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

internal static class GuidedEngineInstallService
{
    internal static bool OffersRequiredFirstInstall(GameInfo game) => game.Status == GameStatus.NotInstalled &&
        (OwnedGameSetupService.Requirement(game) == OwnedGameSetupService.Z2rs ||
         (string.IsNullOrEmpty(game.InstallPath) && OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
          OwnedGameSetupService.Requirement(game) == OwnedGameSetupService.PerfectDark));

    internal sealed record Project(string Repository, string Title, OwnedDataRequirement Requirement, long MaximumBytes)
    {
        internal string Confirmation => Repository == "perfect-dark-pc-port/perfect_dark"
            ? $"Install the reviewed Perfect Dark {Requirement.ReleaseIdentity} package for Linux x64 in this library entry?\n\nAfter installation, select your own lawful Perfect Dark US 1.1 (.z64) file and confirm the separate local copy. No ROM is downloaded. Nothing is played automatically. Existing engines, versions and personal game files are not replaced. A changed rolling-release package is refused, not substituted."
            : Requirement.NesBody != null
            ? $"Install z2rs {Requirement.ReleaseIdentity} for Linux x64 in this library entry?\n\nThis explicit setup uses the reviewed z2rs package. After installation, select your own lawful Zelda II USA ROM and confirm the separate file copy. No ROM is downloaded. Nothing is played automatically. Existing engines, versions and personal game files are not replaced."
            : ConfirmationMessage(Requirement);
    }
    internal static Project ProjectFor(GameInfo game, OwnedDataRequirement? definition = null)
    {
        if (OwnedGameSetupService.OffersFreedoom(game))
            return new("UZDoom/UZDoom", "Set up Freedoom Phase 2", definition ?? OwnedGameSetupService.FreedoomPhase2, 34_560_504);
        if (OwnedGameSetupService.Requirement(game) == OwnedGameSetupService.Z2rs)
            return new("troyedwardsjr/z2rs", "Set up z2rs", definition ?? OwnedGameSetupService.Z2rs, 20_928_166);
        if (OwnedGameSetupService.Requirement(game) == OwnedGameSetupService.PerfectDark)
            return new("perfect-dark-pc-port/perfect_dark", "Set up Perfect Dark", definition ?? OwnedGameSetupService.PerfectDark, 22_213_594);
        throw new IOException("No reviewed first-install setup is available for this project.");
    }

    internal static string ConfirmationMessage(OwnedDataRequirement? definition = null)
    {
        var requirement = definition ?? OwnedGameSetupService.FreedoomPhase2;
        var asset = requirement.AssetIdentity.Split("|sha256:", StringSplitOptions.None)[0];
        return $"Install UZDoom {requirement.ReleaseIdentity} for Linux x64 in this library entry?\n\nThis explicit setup uses the reviewed {asset} package. After installation, you can download and confirm the free Freedoom Phase 2 data. Nothing is played automatically. Existing engines, versions and personal game files are not replaced.";
    }
    internal sealed record PreparedInstall(OwnedDataRequirement Requirement, string AssetName,
        OwnedGameSetupSession Context, FirstInstallPackageConstraint Constraint, Project Project);

    internal static PreparedInstall PrepareFirstInstall(GameInfo game, GameManager manager,
        CancellationToken token, OwnedDataRequirement? definition = null)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new IOException("Guided engine installation currently supports Linux x64 only. Ordinary engine installation and manual game setup remain available on their supported platforms.");
        var project = ProjectFor(game, definition);
        if (game.Status != GameStatus.NotInstalled || game.IsLoading ||
            !string.IsNullOrEmpty(game.InstalledVersion))
            throw new IOException("First-time engine setup will not replace an existing or busy engine. Use ordinary version controls for an existing installation.");
        var requirement = project.Requirement;
        var split = requirement.AssetIdentity.Split("|sha256:", StringSplitOptions.None);
        if (split.Length != 2 || split[1].Length != 64 || !split[1].All(Uri.IsHexDigit) ||
            (project.Repository == "UZDoom/UZDoom" && split[1] != requirement.ExecutableSha256))
            throw new IOException("The guided engine package definition is invalid.");
        var context = new OwnedGameSetupSession(manager, game.InstanceKey, requirement);
        var constraint = new FirstInstallPackageConstraint(requirement.ReleaseIdentity, split[0], split[1])
        { MaximumBytes = project.MaximumBytes, AllowTarGzip = project.Repository is "troyedwardsjr/z2rs" or "perfect-dark-pc-port/perfect_dark", ValidateContext = () => context.ValidateDownloadRequest(token) };
        constraint.ValidateSelection(requirement.ReleaseIdentity, split[0]);
        var destination = game.GetInstallPath(manager.GamesFolder);
        new PortableInstallationTransaction(destination).RequireFirstInstallationTarget();
        return new PreparedInstall(requirement, split[0], context, constraint, project);
    }

    internal static async Task<bool> InstallAsync(GameInfo game, GameManager manager, AppSettings settings,
        CancellationToken token, Func<string, string, Task<bool>> confirm,
        IGameDownloadDialogs? dialogs = null, OwnedDataRequirement? definition = null,
        Action<GuidedEngineProgress>? reportProgress = null)
    {
        reportProgress?.Invoke(new(GuidedEnginePhase.Resolving));
        var prepared = PrepareFirstInstall(game, manager, token, definition);
        var requirement = prepared.Requirement; var assetName = prepared.AssetName;
        var context = prepared.Context; var constraint = prepared.Constraint;
        GuidedInstallIntent? intent = null;
        game.IsLoading = true;
        try
        {
            if (!await confirm(prepared.Project.Confirmation, prepared.Project.Title)) return false;
            token.ThrowIfCancellationRequested();
            context.ValidateDownloadRequest(token);
            intent = GuidedInstallIntent.Begin(GuidedInstallIntent.For(game, manager, prepared));
            constraint = constraint with { PersistStage = intent.Advance, ReportProgress = reportProgress };
            using var fresh = ReleaseRequestCoordinator.AllowCachedMetadata(TimeSpan.Zero);
            var release = await CatalogReleaseSelection.FetchSelectedAsync(manager.HttpClient, "github", prepared.Project.Repository,
                requirement.ReleaseIdentity, game.GetReleaseApiToken(settings), token);
            token.ThrowIfCancellationRequested();
            context.ValidateDownloadRequest(token);
            if (release == null || release.tag_name != requirement.ReleaseIdentity)
                throw new IOException("The reviewed engine release is unavailable. No newer or alternative release was selected.");
            var assets = release.assets.Where(asset => asset.name == assetName).ToArray();
            var expectedUrl = $"https://github.com/{prepared.Project.Repository}/releases/download/{requirement.ReleaseIdentity}/{assetName}";
            if (assets.Length != 1 || assets[0].browser_download_url != expectedUrl)
                throw new IOException("The reviewed engine download is missing or its source changed. No alternative asset was selected.");
            GameDownloadService.SelectExplicit(game, release, settings, assets[0]);
            await GameDownloadInstallService.DownloadAndInstallAsync(game, manager.HttpClient, manager.GamesFolder,
                release, settings, GameStatus.NotInstalled, dialogs, releaseMode: ReleaseInstallMode.ExplicitRelease,
                firstInstall: constraint);
            if (intent.StatusUncertain || (!intent.Committed && intent.Stage == "Committing"))
                throw new IOException(intent.Committed
                    ? "The engine committed, but its saved download status could not be confirmed. Inspect the installed entry; do not repeat the transfer."
                    : "Setup reached the installation commit boundary. Inspect existing installation and recovery evidence before retrying; nothing was repaired automatically.");
            token.ThrowIfCancellationRequested();
            context.ValidateDownloadRequest(token);
            if (!game.IsInstalled || game.InstalledVersion != requirement.ReleaseIdentity ||
                game.InstalledBuild?.Sha256 != constraint.Sha256) return false;
            // The existing data-setup flow independently revalidates the committed
            // package with its non-recovering owned-data authority before showing readiness.
            return true;
        }
        finally
        {
            try { intent?.Finish(token.IsCancellationRequested); }
            finally { intent?.Dispose(); game.IsLoading = false; }
        }
    }
}
