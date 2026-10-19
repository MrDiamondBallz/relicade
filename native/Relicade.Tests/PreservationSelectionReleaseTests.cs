using System.Runtime.InteropServices;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.Tests;

public class PreservationSelectionReleaseTests
{
    private static GitHubRelease Release(string tag, params string[] names) => new()
    {
        tag_name = tag,
        assets = names.Select(name => new GitHubAsset { name = name, browser_download_url = "https://example.invalid/fixture" }).ToArray()
    };

    [Fact]
    public void Missing_explicit_pin_never_falls_back_to_latest_or_installed()
    {
        var latest = Release("v2.0", "app-windows-x64.zip");
        Assert.Null(CatalogReleaseSelection.SelectLatestRelease([latest], "v1.0", "v2.0", "v2.0"));
        Assert.Null(ReleaseSelection.SelectLatestRelease([latest], "v1.0", "v2.0", "v2.0"));
    }

    [Fact]
    public void Available_pin_preserves_the_exact_release_and_asset_identity()
    {
        var pin = Release("v1.0", "app-windows-x64.zip");
        var latest = Release("v2.0", "other-windows-x64.zip");
        Assert.Same(pin, CatalogReleaseSelection.SelectLatestRelease([latest, pin], "v1.0", githubLatestTag: "v2.0"));
        Assert.Same(pin.assets[0], DownloadAssetPolicy.Select(pin, "Windows", machine: Architecture.X64).Automatic);
        Assert.Same(latest, CatalogReleaseSelection.SelectLatestRelease([latest, pin]));
    }

    [Theory]
    [InlineData("Windows", "app-windows-arm64.zip", Architecture.X64)]
    [InlineData("macOS", "app-macos-arm64.zip", Architecture.X64)]
    [InlineData("Linux-X64", "app-windows-arm64.zip", Architecture.X64)]
    [InlineData("Windows", "app-windows-x64.zip", Architecture.Arm64)]
    [InlineData("Android", "app-android-arm64.apk", Architecture.X64)]
    [InlineData("Windows", "app-windows-armv7.zip", Architecture.X64)]
    [InlineData("Windows", "app-windows-arm64.zip", Architecture.Arm)]
    [InlineData("Windows", "app-windows-x64.zip", Architecture.X86)]
    [InlineData("Linux-X86", "app-linux-x64.zip", Architecture.X86)]
    [InlineData("Linux-ARM", "app-linux-arm64.zip", Architecture.Arm)]
    public void Known_wrong_processor_never_becomes_automatic_even_as_the_only_asset(string platform, string name, Architecture cpu)
    {
        var choices = DownloadAssetPolicy.Select(Release("v2.0", name), platform, machine: cpu);
        Assert.Empty(choices.Eligible);
        Assert.Null(choices.Automatic);
        Assert.Null(DownloadAssetPolicy.MatchInstalled(choices, name, "v1.0", "v2.0"));
        Assert.NotNull(choices.EmptyReason);
    }

    [Theory]
    [InlineData("app-windows-universal.zip")]
    [InlineData("app-windows-x64-arm64.zip")]
    [InlineData("app-windows.zip")]
    public void Universal_or_unspecified_processor_remains_distinct_from_known_wrong_processor(string name)
    {
        Assert.NotNull(DownloadAssetPolicy.Select(Release("v2.0", name), "Windows", machine: Architecture.X64).Automatic);
    }

    [Fact]
    public void Unknown_platform_is_manual_and_does_not_reintroduce_incompatible_cpu()
    {
        var choices = DownloadAssetPolicy.Select(Release("v2.0", "app-aarch64.tar.zst"), "Windows", machine: Architecture.X64);
        Assert.Empty(choices.Eligible);
        Assert.Empty(choices.Uncertain);
        Assert.Null(choices.Automatic);
        var unknown = DownloadAssetPolicy.Select(Release("v2.0", "app.tar.zst"), "Windows", machine: Architecture.X64);
        Assert.Empty(unknown.Eligible);
        Assert.Single(unknown.Uncertain);
        Assert.True(unknown.NeedsChoice);
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            const string latest = "{\"tag_name\":\"v2.0\",\"assets\":[{\"name\":\"fixture-windows-x64.zip\",\"browser_download_url\":\"https://example.invalid/not-fetched\"}]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/latest") ? latest : "[" + latest + "]") });
        }
    }

    [Fact]
    public async Task Missing_pin_is_a_visible_failed_check_without_replacing_preference_or_release_identity()
    {
        using var client = new HttpClient(new ReleaseHandler());
        var app = new Relicade.Models.GameInfo { Name = "Pinned fixture", Repository = "fixture/pinned", FolderName = "Pinned", PreferredVersion = "v1.0" };
        var result = await new LibraryUpdateChecker(client, new AppSettings()).CheckAsync([app], true, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        Assert.False(result.Complete);
        Assert.Equal(AppCheckOutcome.Failed, Assert.Single(result.Apps).Outcome);
        Assert.Contains("preferred version", app.RepositoryCheckError!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v1.0", app.PreferredVersion);
        Assert.NotEqual("v2.0", app.LatestVersion);
    }

    [Fact]
    public void Explicit_linux_windows_runner_policy_keeps_x64_but_never_infers_arm64_support()
    {
        var choices = DownloadAssetPolicy.Select(Release("v2.0", "app-windows-x64.zip", "app-windows-arm64.zip"), "Linux-X64", machine: Architecture.X64);
        Assert.Equal("app-windows-x64.zip", Assert.Single(choices.Eligible).name);
    }
}
