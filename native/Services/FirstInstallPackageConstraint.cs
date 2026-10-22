using Relicade.Core.Services;

namespace Relicade.Services;

/// <summary>An explicit first-install request bound to reviewed bytes, not mutable release metadata.</summary>
public sealed record FirstInstallPackageConstraint(string Release, string FileName, string Sha256)
{
    public long MaximumBytes { get; init; } = 64L * 1024 * 1024;
    internal Action? ValidateContext { get; init; }

    internal void ValidateSelection(string release, string fileName)
    {
        if (MaximumBytes <= 0 || MaximumBytes > 64L * 1024 * 1024 || PublishedAssetDigest.Normalize(Sha256) != Sha256 || release != Release || fileName != FileName ||
            Path.GetFileName(FileName) != FileName || FileName.Contains('\\') ||
            !GameInstallationService.IsAppImageAsset(FileName))
            throw new IOException("The selected download is not the reviewed first-install package. No alternative version will be installed.");
    }

    internal void ValidateLength(long length)
    {
        if (length < 0 || length > MaximumBytes)
            throw new IOException("The engine download exceeds the pinned setup package size limit.");
    }

    internal void ValidateBytes(string actual)
    {
        if (!string.Equals(actual, Sha256, StringComparison.Ordinal))
            throw new IOException("The downloaded engine differs from the reviewed setup package. No installation was activated.");
    }
}
