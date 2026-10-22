namespace Relicade.Core.Services;

public sealed class GameInstallationOptions
{
    public static GameInstallationOptions Default { get; } = new();

    public IReadOnlyCollection<string> AdditionalMetadataFileNames { get; init; } = Array.Empty<string>();

    public Action<string>? Log { get; init; }

    /// <summary>
    /// Optional 0–1 progress for archive extraction (after the download has finished).
    /// </summary>
    public IProgress<double>? ExtractProgress { get; init; }

    /// <summary>
    /// The install folder isn't one Relicade made for this app (the player located it, or it's shared with other
    /// programs), so an AppImage update removes only <see cref="PreviousInstallFileName"/>, never the folder's
    /// other AppImages.
    /// </summary>
    public bool IsSharedFolder { get; init; }

    /// <summary>The file Relicade last installed this app from, when it recorded one.</summary>
    public string? PreviousInstallFileName { get; init; }

    /// <summary>Explicit project-adapter data locations, relative to this root. Unknown additions block updates.</summary>
    public IReadOnlyCollection<string> PreservedDataPaths { get; init; } = ["LastPlayed.txt"];
    public string? SelectedExecutableRelativePath { get; init; }
    /// <summary>Trusted adapter default for a first installation only; never replaces an existing launch selection.</summary>
    public string? InitialExecutableRelativePath { get; init; }
    public string? ReleaseIdentity { get; init; }
    public string? AssetIdentity { get; init; }
    public string? InstalledBuildText { get; init; }
    public Action<string>? PrepareCandidate { get; init; }
    /// <summary>Deterministic diagnostic/fault boundary; never supplies success or bypasses validation.</summary>
    public Action<string>? TransactionBoundary { get; init; }
    public long MaximumExpandedBytes { get; init; } = 16L * 1024 * 1024 * 1024;
    public int MaximumFileCount { get; init; } = 100000;

    /// <summary>Platform disk-image license consent, separate from portable activation.</summary>
    public Func<string, string, Task<bool>>? AcceptLicenseAsync { get; init; }
}
