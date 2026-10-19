using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Relicade.Core.Models;

namespace Relicade.Core.Services;

public sealed record DownloadAssetSelection(IReadOnlyList<GitHubAsset> Eligible,
    IReadOnlyList<GitHubAsset> Uncertain, string? EmptyReason)
{
    public GitHubAsset? Automatic => Eligible.Count == 1 ? Eligible[0] : null;
    public bool NeedsChoice => Eligible.Count > 1 || Eligible.Count == 0 && Uncertain.Count > 0;
}

public static class DownloadAssetPolicy
{
    public static bool IsAuxiliary(string name) =>
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        // Electron's update feeds (latest-linux.yml), delta maps and AppImage zsync files describe a download.
        Regex.IsMatch(name, @"\.(?:ya?ml|blockmap|zsync)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        // Only trailing, separated archive labels: app names such as Resource or SourceGame remain valid.
        Regex.IsMatch(name, @"(?:^|[._\s-])(?:notices?|sources?|source[._\s-]code|src)\.(?:zip|7z|rar|tar(?:\.(?:gz|xz|bz2|zst))?|tgz|tbz2|txz)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(name, @"(?:\.(?:sha(?:1|224|256|384|512)?|md5|sig|asc|minisig|signature|debug)(?:\.txt)?$)|(?:^|[._-])(?:checksums?|sha(?:1|224|256|384|512)?sums?|md5sums?|pdb|dsym|symbols|debugsymbols|debug[._-]symbols)(?:$|[._-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="machine">The processor the download will run on; by default the one in this computer.</param>
    public static DownloadAssetSelection Select(GitHubRelease release, string platform, string? filter = null,
        Architecture? machine = null)
    {
        var all = GitHubReleaseService.GetDownloadableAssets(release);
        var filtered = GitHubReleaseService.GetDownloadableAssets(release, filter);
        var linux = platform.StartsWith("Linux", StringComparison.OrdinalIgnoreCase);
        var processor = machine ?? (platform.Equals("Linux-ARM64", StringComparison.OrdinalIgnoreCase) ? Architecture.Arm64
            : platform.Equals("Linux-ARM", StringComparison.OrdinalIgnoreCase) ? Architecture.Arm
            : platform.Equals("Linux-X86", StringComparison.OrdinalIgnoreCase) ? Architecture.X86
            : platform.Equals("Linux-X64", StringComparison.OrdinalIgnoreCase) ? Architecture.X64 : DefaultProcessor(platform));
        var compatible = ForProcessor(filtered, processor);
        var eligible = compatible.Where(a => PlatformAssetMatcher.MatchesPlatform(a.name, platform) ||
            linux && PlatformAssetMatcher.IsWindowsAsset(a.name))
            .OrderByDescending(a => PlatformAssetMatcher.MatchesPlatform(a.name, platform)).ToList();
        var uncertain = compatible.Where(a => !HasKnownPlatform(a.name)).ToList();
        var reason = eligible.Count > 0 ? null : all.Count == 0 ? "This release has no installable download files." :
            filtered.Count == 0 ? $"No download files match the release asset filter \"{filter}\"." :
            $"This release has no recognized download for {platform}.";
        return new(eligible, uncertain, reason);
    }

    /// <summary>
    /// The file in <paramref name="choices"/> that is the same build as the installed file, its version aside:
    /// Game-1.2.0-linux-x64.zip installed under 1.2.0 matches Game-1.3.0-linux-x64.zip under 1.3.0. Null unless exactly
    /// one file matches, so an update never guesses between builds.
    /// </summary>
    public static GitHubAsset? MatchInstalled(DownloadAssetSelection choices, string? installedFileName, string? installedTag, string? releaseTag)
    {
        if (string.IsNullOrWhiteSpace(installedFileName)) return null;
        var installed = WithoutVersion(installedFileName.Trim(), installedTag);
        foreach (var candidates in new[] { choices.Eligible, choices.Uncertain })
        {
            var matches = candidates.Where(a => string.Equals(WithoutVersion(a.name, releaseTag), installed, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToList();
            if (matches.Count > 0) return matches.Count == 1 ? matches[0] : null;
        }
        return null;
    }

    private const string VersionMark = "\u0001";

    // A dotted version such as 1.2.0 or 1.2.0-beta.3. A single number isn't one: it may be x64, win32 or arm64.
    private static readonly Regex DottedVersion = new(@"(?<![0-9.])\d+(?:\.\d+)+(?:[-.]?(?:alpha|beta|rc|pre|preview)(?:[-.]?\d+)?)?(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The file name with its release's tag (v1.2.0, r23, nightly-2026-10-11) and any dotted version replaced by one mark.</summary>
    internal static string WithoutVersion(string name, string? tag)
    {
        var tagText = tag?.Trim() ?? "";
        if (tagText.Any(char.IsAsciiDigit))
        {
            foreach (var form in new[] { tagText, tagText.TrimStart('v', 'V') }.Distinct())
                name = Regex.Replace(name, $@"(?<![0-9A-Za-z]){Regex.Escape(form)}(?![0-9A-Za-z]|\.[0-9])", VersionMark,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return DottedVersion.Replace(name, VersionMark);
    }

    /// <summary>Exclude known incompatible processors. An absent/universal marker is not a known mismatch.
    /// Emulation requires a separate deliberate runner choice, never a sole-asset fallback.</summary>
    internal static List<GitHubAsset> ForProcessor(IEnumerable<GitHubAsset> assets, Architecture machine)
    {
        return assets.Where(a => ProcessorOf(a.name) is not { } processor || processor == machine).ToList();
    }

    private static readonly Regex ArmName = new(@"(?:^|[^a-z0-9])(?:arm64|aarch64|armv8)(?:$|[^a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Arm32Name = new(@"(?:^|[^a-z0-9])(?:arm|arm32|armv7[al]?|armhf)(?:$|[^a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex X64Name = new(@"(?:^|[^a-z0-9])(?:x64|x86[_-]64|amd64|win64)(?:$|[^a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex X86Name = new(@"(?:^|[^a-z0-9])(?:x86(?![_-]64)|i386|i586|i686)(?:$|[^a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Explicit CPU identity; null when unspecified or the name declares multiple architectures.</summary>
    internal static Architecture? ProcessorOf(string name)
    {
        var processors = new List<Architecture>();
        if (ArmName.IsMatch(name)) processors.Add(Architecture.Arm64);
        if (Arm32Name.IsMatch(name)) processors.Add(Architecture.Arm);
        if (X64Name.IsMatch(name)) processors.Add(Architecture.X64);
        if (X86Name.IsMatch(name)) processors.Add(Architecture.X86);
        return processors.Count == 1 ? processors[0] : null;
    }

    /// <summary>
    /// This computer's processor, except for a Windows download on a Mac or Linux PC: Wine, Proton and CrossOver
    /// run x64 Windows apps, also on Apple silicon, but not ARM64 ones.
    /// </summary>
    private static Architecture DefaultProcessor(string platform) =>
        platform.Equals("Windows", StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows()
            ? Architecture.X64
            : RuntimeInformation.OSArchitecture;

    private static bool HasKnownPlatform(string name) => PlatformAssetMatcher.IsIosAsset(name) ||
        PlatformAssetMatcher.IsDedicatedDeviceAsset(name) || PlatformAssetMatcher.IsWindowsAsset(name) ||
        PlatformAssetMatcher.MatchesPlatform(name, "macOS") ||
        Regex.IsMatch(name, @"(?:linux|appimage|flatpak|android|arm64-v8a|switch|\.apk$|\.deb$|\.rpm$|\.tar\.(?:gz|xz)$)", RegexOptions.IgnoreCase);
}
