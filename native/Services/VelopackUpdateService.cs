using System.Diagnostics;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

namespace Relicade.Services;

/// <summary>
/// Relicade self-updates via Velopack + GitHub Releases.
/// </summary>
public sealed class VelopackUpdateService
{
    public const string GitHubRepoUrl = "https://github.com/MrDiamondBallz/relicade";

    private UpdateInfo? _lastUpdateInfo;
    private bool _lastIncludePrerelease;

    public bool IsInstalled
    {
        get
        {
            // No Relicade signing trust root or update feed is accepted yet.
            return false;
        }
    }

    public string? CurrentVersion
    {
        get
        {
            try
            {
                return VelopackLocator.Current?.CurrentlyInstalledVersion?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>The folder Relicade is installed in (the one holding Update.exe and current).</summary>
    public string? RootAppDir
    {
        get
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
    }

    public string? AppId
    {
        get
        {
            try
            {
                return VelopackLocator.Current?.AppId;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>A downloaded release newer than the running one that hasn't been installed yet.</summary>
    public string? PendingRestartVersion
    {
        get
        {
            try
            {
                if (!IsInstalled)
                    return null;
                return CreateManager(_lastIncludePrerelease).UpdatePendingRestart?.Version.ToString();
            }
            catch
            {
                return null;
            }
        }
    }

    public bool IsUpdatePendingRestart
    {
        get
        {
            try
            {
                if (!IsInstalled)
                    return false;
                var mgr = CreateManager(_lastIncludePrerelease);
                return mgr.UpdatePendingRestart != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public UpdateInfo? LastUpdateInfo => _lastUpdateInfo;

    /// <summary>
    /// Prereleases are included when the setting is on, or when the installed version is already a prerelease (contains '-').
    /// </summary>
    public static bool EffectiveIncludePrerelease(string? currentVersion, bool allowPrereleaseLauncherUpdates)
    {
        if (allowPrereleaseLauncherUpdates)
            return true;

        return !string.IsNullOrWhiteSpace(currentVersion)
            && currentVersion.Contains('-', StringComparison.Ordinal);
    }

    public static bool ShouldSkipAutomaticSelfUpdate()
    {
        return true;
    }

    public async Task<VelopackCheckResult> CheckForUpdatesAsync(
        string? gitHubToken = null,
        bool? includePrerelease = null,
        bool allowPrereleaseLauncherUpdates = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return VelopackCheckResult.NotInstalled(
                LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory));
        }

        try
        {
            var current = CurrentVersion
                ?? LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory);
            var prerelease = includePrerelease
                ?? EffectiveIncludePrerelease(current, allowPrereleaseLauncherUpdates);
            _lastIncludePrerelease = prerelease;
            var mgr = CreateManager(prerelease, gitHubToken);
            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            _lastUpdateInfo = update;

            if (update == null)
            {
                return new VelopackCheckResult
                {
                    CheckSucceeded = true,
                    InstalledVersion = current,
                    UpdateAvailable = false,
                    IncludedPrerelease = prerelease,
                };
            }

            return new VelopackCheckResult
            {
                CheckSucceeded = true,
                InstalledVersion = current,
                UpdateAvailable = true,
                AvailableVersion = update.TargetFullRelease.Version.ToString(),
                UpdateInfo = update,
                IncludedPrerelease = prerelease,
            };
        }
        catch (NotInstalledException)
        {
            return VelopackCheckResult.NotInstalled(
                LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Velopack update check failed: {ex.Message}");
            return new VelopackCheckResult
            {
                CheckSucceeded = false,
                ErrorMessage = ex.Message,
                InstalledVersion = CurrentVersion
                    ?? LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory),
            };
        }
    }

    public async Task DownloadUpdatesAsync(
        UpdateInfo updateInfo,
        Action<int>? progress = null,
        string? gitHubToken = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            throw new NotSupportedException("Self-update unavailable in this Relicade development build.");
        _lastIncludePrerelease = includePrerelease;
        cancellationToken.ThrowIfCancellationRequested();
        // Velopack can apply a staged download on the next startup, so protect data
        // before staging as well as immediately before the explicit restart.
        await LauncherUpdateBackup.BeforeUpdateAsync(async () =>
        {
            var mgr = CreateManager(includePrerelease, gitHubToken);
            await mgr.DownloadUpdatesAsync(updateInfo, progress).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo, bool includePrerelease = false)
    {
        if (!IsInstalled)
            throw new NotSupportedException("Self-update unavailable in this Relicade development build.");
        _lastIncludePrerelease = includePrerelease;
        LauncherUpdateBackup.BeforeUpdate(() =>
        {
            var mgr = CreateManager(includePrerelease);
            mgr.ApplyUpdatesAndRestart(updateInfo);
        });
    }

    private static UpdateManager CreateManager(bool includePrerelease, string? gitHubToken = null)
    {
        throw new NotSupportedException("Self-update is unavailable in this Relicade development build.");
    }
}

public sealed class VelopackCheckResult
{
    public bool CheckSucceeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string InstalledVersion { get; init; } = "0.0";
    public bool UpdateAvailable { get; init; }
    public string? AvailableVersion { get; init; }
    public UpdateInfo? UpdateInfo { get; init; }
    public bool IsNotInstalled { get; init; }
    public bool IncludedPrerelease { get; init; }

    public static VelopackCheckResult NotInstalled(string installedVersion) => new()
    {
        CheckSucceeded = false,
        IsNotInstalled = true,
        InstalledVersion = installedVersion,
        UpdateAvailable = false,
        ErrorMessage = "Self-update unavailable in this development build. No signed Relicade update feed is configured.",
    };
}
