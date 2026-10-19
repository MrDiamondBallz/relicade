using System.Runtime.InteropServices;
using Relicade.Core.Models;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PreservationSelectionCpuSetTests
{
    private static readonly Architecture[] Machines =
        [Architecture.X86, Architecture.X64, Architecture.Arm, Architecture.Arm64];

    // Every finite multi-CPU subset, plus aliases which must not create extra architectures.
    private static readonly (string Label, Architecture[] Cpus)[] Builds =
    [
        ("x86-x64", [Architecture.X86, Architecture.X64]),
        ("x86-arm", [Architecture.X86, Architecture.Arm]),
        ("x86-arm64", [Architecture.X86, Architecture.Arm64]),
        ("x64-arm", [Architecture.X64, Architecture.Arm]),
        ("x64-arm64", [Architecture.X64, Architecture.Arm64]),
        ("arm-arm64", [Architecture.Arm, Architecture.Arm64]),
        ("x86-x64-arm", [Architecture.X86, Architecture.X64, Architecture.Arm]),
        ("x86-x64-arm64", [Architecture.X86, Architecture.X64, Architecture.Arm64]),
        ("x86-arm-arm64", [Architecture.X86, Architecture.Arm, Architecture.Arm64]),
        ("x64-arm-arm64", [Architecture.X64, Architecture.Arm, Architecture.Arm64]),
        ("x86-x64-arm-arm64", Machines),
        ("i686-AMD64-ARMHF-AARCH64", Machines),
        ("x86_64-aarch64", [Architecture.X64, Architecture.Arm64]),
        ("x86-64-armv7l", [Architecture.X64, Architecture.Arm]),
        ("x64-amd64", [Architecture.X64])
    ];

    public static IEnumerable<object[]> PlatformMatrix()
    {
        foreach (var (label, cpus) in Builds)
        foreach (var machine in Machines)
        foreach (var platform in new[] { "Windows", "macOS", "Android", LinuxPlatform(machine) })
            yield return [platform, label, machine, cpus.Contains(machine)];
    }

    public static IEnumerable<object[]> ManualMatrix()
    {
        foreach (var (label, cpus) in Builds)
        foreach (var machine in Machines)
            yield return [label, machine, cpus.Contains(machine)];
    }

    private static string LinuxPlatform(Architecture machine) => machine switch
    {
        Architecture.X86 => "Linux-X86", Architecture.X64 => "Linux-X64",
        Architecture.Arm => "Linux-ARM", Architecture.Arm64 => "Linux-ARM64",
        _ => throw new ArgumentOutOfRangeException(nameof(machine))
    };

    private static GitHubRelease Release(string tag, string name) => new()
    {
        tag_name = tag,
        assets = [new GitHubAsset { name = name, browser_download_url = "https://example.invalid/fixture" }]
    };

    [Theory]
    [MemberData(nameof(PlatformMatrix))]
    public void Finite_cpu_set_must_contain_the_target_for_automatic_and_installed_match(
        string platform, string label, Architecture machine, bool compatible)
    {
        var os = platform.StartsWith("Linux") ? "linux" : platform.ToLowerInvariant();
        var extension = platform == "Android" ? ".apk" : ".zip";
        var name = $"app-2.0-{os}-{label}{extension}";
        var release = Release("v2.0", name);
        var choices = DownloadAssetPolicy.Select(release, platform, machine: machine);
        var installed = name.Replace("2.0", "1.0", StringComparison.Ordinal);
        Assert.Empty(choices.Uncertain);
        if (compatible)
        {
            Assert.Same(release.assets[0], Assert.Single(choices.Eligible));
            Assert.Same(release.assets[0], choices.Automatic);
            Assert.Same(release.assets[0], DownloadAssetPolicy.MatchInstalled(choices, installed, "v1.0", "v2.0"));
            Assert.Null(choices.EmptyReason);
        }
        else
        {
            Assert.Empty(choices.Eligible);
            Assert.Null(choices.Automatic);
            Assert.Null(DownloadAssetPolicy.MatchInstalled(choices, installed, "v1.0", "v2.0"));
            Assert.NotNull(choices.EmptyReason);
        }
        Assert.False(choices.NeedsChoice);
    }

    [Theory]
    [MemberData(nameof(ManualMatrix))]
    public void Unknown_platform_stays_manual_but_never_recovers_a_known_incompatible_cpu_set(
        string label, Architecture machine, bool compatible)
    {
        var name = $"app-2.0-{label}.tar.zst";
        var release = Release("v2.0", name);
        var choices = DownloadAssetPolicy.Select(release, "Windows", machine: machine);
        Assert.Empty(choices.Eligible);
        Assert.Null(choices.Automatic);
        Assert.Equal(compatible, choices.NeedsChoice);
        var match = DownloadAssetPolicy.MatchInstalled(choices, name.Replace("2.0", "1.0"), "v1.0", "v2.0");
        if (compatible)
        {
            Assert.Same(release.assets[0], Assert.Single(choices.Uncertain));
            Assert.Same(release.assets[0], match);
        }
        else
        {
            Assert.Empty(choices.Uncertain);
            Assert.Null(match);
        }
    }

    [Theory]
    [InlineData("app-windows.zip")]
    [InlineData("app-windows-universal.zip")]
    [InlineData("app-windows-universal2.zip")]
    [InlineData("myarmory-windows.zip")]
    public void Unspecified_or_universal_labels_are_not_finite_cpu_mismatches(string name)
    {
        foreach (var machine in Machines)
        {
            var release = Release("v2.0", name);
            Assert.Same(release.assets[0], DownloadAssetPolicy.Select(release, "Windows", machine: machine).Automatic);
        }
    }

    [Fact]
    public void Runner_policy_does_not_infer_support_for_a_target_absent_from_a_finite_set()
    {
        var wrong = Release("v2.0", "app-windows-x86-arm64.zip");
        var choices = DownloadAssetPolicy.Select(wrong, "Linux-X64", machine: Architecture.X64);
        Assert.Empty(choices.Eligible);
        Assert.Empty(choices.Uncertain);
        Assert.Null(choices.Automatic);
        Assert.Null(DownloadAssetPolicy.MatchInstalled(choices, wrong.assets[0].name, "v1.0", "v2.0"));

        var supported = Release("v2.0", "app-windows-x64-arm64.zip");
        Assert.Same(supported.assets[0], DownloadAssetPolicy.Select(supported, "Linux-X64", machine: Architecture.X64).Automatic);
    }
}
