using System.IO.Compression;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using static Relicade.Tests.ReleaseIdentityRegressionTests;

namespace Relicade.Tests;

public sealed class ExactBuildPinTests : IDisposable
{
    private const string Newer = "v1.0.0-prerelease+39";
    private const string Pinned = "v1.0.0-prerelease+37";
    private readonly string root = Path.Combine(Path.GetTempPath(), "relicade-build-pin-" + Guid.NewGuid().ToString("N"));
    private readonly string? previous = RelicadePaths.OverrideUserDataRoot;
    public ExactBuildPinTests() { Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root; GitHubApiCache.Initialize(Path.Combine(root, "cache")); }
    public void Dispose() { RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_automatic_install_honors_build_pin_in_either_setter_order(bool pinFirst)
    {
        var app = new GameInfo { Name = "Build pin", FolderName = "game", Repository = "fixture/" + Guid.NewGuid().ToString("N") };
        var newer = Release(Newer, true); var pinned = Release(Pinned, true);
        if (pinFirst) app.PreferredVersion = Pinned;
        app.ApplyCachedRelease(Newer, newer);
        if (!pinFirst) app.PreferredVersion = Pinned;
        var downloaded = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com")
                return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(newer) : JsonSerializer.Serialize(new[] { newer, pinned })) };
            request.RequestUri.Host.Should().Be("example.test"); downloaded.Add(Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
            using var data = new MemoryStream();
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
            using (var file = zip.CreateEntry(OperatingSystem.IsMacOS() ? "game" : "game.exe").Open()) file.Write([0x4d, 0x5a, 0x90, 0]);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) };
        }));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, root, newer,
            new AppSettings { Platform = TargetOS.Windows }, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        downloaded.Should().Equal("/" + Pinned + ".zip");
        app.InstalledVersion.Should().Be(Pinned);
        app.PreferredVersion.Should().Be(Pinned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_pin_survives_json_order_and_later_latest_refresh(bool pinFirst)
    {
        var values = new Dictionary<string, object?>();
        if (pinFirst) values["PreferredVersion"] = Pinned;
        values["LatestVersion"] = Newer;
        if (!pinFirst) values["PreferredVersion"] = Pinned;
        var app = JsonSerializer.Deserialize<GameInfo>(JsonSerializer.Serialize(values))!;
        app.PreferredVersion.Should().Be(Pinned);
        app.LatestVersion = "v1.0.0-prerelease+40";
        app.PreferredVersion.Should().Be(Pinned);
        app.MatchesReleaseTarget(Newer).Should().BeFalse();
        app.MatchesReleaseTarget(Pinned).Should().BeTrue();
        var restored = JsonSerializer.Deserialize<GameInfo>(JsonSerializer.Serialize(new { app.PreferredVersion, app.LatestVersion }))!;
        restored.PreferredVersion.Should().Be(Pinned);
    }

    [Fact]
    public void Missing_exact_build_pin_does_not_select_another_build() =>
        CatalogReleaseSelection.SelectLatestRelease([Release(Newer, true)], preferredVersion: Pinned).Should().BeNull();

    [Theory]
    [InlineData("v2", "2.0.0")]
    [InlineData("V1.2", "1.2.0")]
    public void Existing_numeric_alias_pin_behavior_is_preserved(string pin, string latest)
    {
        var app = new GameInfo { LatestVersion = latest, PreferredVersion = pin };
        app.PreferredVersion.Should().BeNull();
        CatalogReleaseSelection.SelectLatestRelease([Release(latest)], preferredVersion: pin)!.tag_name.Should().Be(latest);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_metadata_never_attaches_another_builds_cached_assets(bool catalog)
    {
        var app = new GameInfo { Repository = "fixture/" + Guid.NewGuid().ToString("N"), FolderName = "game" };
        var other = Release(Newer, true);
        // Even inconsistent legacy cache metadata cannot lend another tag's assets.
        GitHubApiCache.SetCache("github", app.Repository!, Pinned, "", other, persist: false);
        if (catalog) { app.CatalogVerifiedVersion = Pinned; app.CatalogVerifiedAt = DateTimeOffset.UtcNow; }
        else app.PreferredVersion = Pinned;
        var evidence = StartupVersionResolver.Resolve(app);
        evidence.Should().NotBeNull();
        evidence!.Version.Should().Be(Pinned);
        evidence.Release.Should().BeNull();
    }

    [Fact]
    public void Startup_cache_for_another_build_does_not_satisfy_the_pin()
    {
        var app = new GameInfo { Repository = "fixture/" + Guid.NewGuid().ToString("N"), FolderName = "game", PreferredVersion = Pinned };
        GitHubApiCache.SetCache("github", app.Repository!, Newer, "", Release(Newer, true), persist: false);
        StartupVersionResolver.Resolve(app).Should().BeNull();
    }
    [Theory]
    [InlineData("v1+37", "v1+37", true)]
    [InlineData("v1+37", "v1+39", false)]
    [InlineData("v1+37", "1+37", false)]
    [InlineData("v1+Build", "v1+build", false)]
    [InlineData("v1+37", "v1", false)]
    public void Explicit_build_target_identity_does_not_use_numeric_ordering(string target, string candidate, bool expected) =>
        CatalogReleaseSelection.MatchesTarget(target, candidate).Should().Be(expected);

    [Fact]
    public void Exact_case_pin_precedes_another_case_and_missing_ambiguous_alias_is_refused()
    {
        var exact = Release("V1");
        CatalogReleaseSelection.SelectLatestRelease([Release("v1"), exact], preferredVersion: "V1").Should().BeSameAs(exact);
        CatalogReleaseSelection.SelectLatestRelease([Release("v1"), Release("1")], preferredVersion: "01").Should().BeNull();
    }
    [Fact]
    public async Task Actual_cached_refresh_fetches_the_pinned_build_instead_of_reusing_another_build()
    {
        var app = new GameInfo { Name = "Pinned", FolderName = "game", Repository = "fixture/" + Guid.NewGuid().ToString("N"), PreferredVersion = Pinned };
        GitHubApiCache.SetCache("github", app.Repository!, Newer, "", Release(Newer, true), persist: false);
        var requests = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            request.RequestUri!.Host.Should().Be("api.github.com");
            requests++;
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(Release(Newer, true)) : JsonSerializer.Serialize(new[] { Release(Newer, true), Release(Pinned, true) })) };
        }));
        await app.CheckLatestVersionAsync(client, cancellationToken: TestContext.Current.CancellationToken);
        requests.Should().Be(2);
        app.GetLatestRelease()!.tag_name.Should().Be(Pinned);
        app.PreferredVersion.Should().Be(Pinned);
    }

    [Fact]
    public async Task Actual_automatic_install_never_downloads_a_substitute_for_a_missing_build_pin()
    {
        var app = new GameInfo { Name = "Pinned", FolderName = "game", Repository = "fixture/" + Guid.NewGuid().ToString("N"), PreferredVersion = Pinned };
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(Release(Newer, true)) : JsonSerializer.Serialize(new[] { Release(Newer, true) })) };
        }));
        await GameDownloadInstallService.DownloadAndInstallAsync(app, client, root, Release(Newer, true),
            new AppSettings { Platform = TargetOS.Windows }, GameStatus.NotInstalled, HeadlessGameDownloadDialogs.Instance);
        requests.Should().Equal("api.github.com", "api.github.com");
        File.Exists(Path.Combine(root, "game", "version.txt")).Should().BeFalse();
        app.InstalledVersion.Should().Be("");
    }
    [Fact]
    public async Task Actual_library_projection_preserves_an_explicit_build_pin()
    {
        var app = new GameInfo { Name = "Pinned", FolderName = "game", Repository = "fixture/pinned", LatestVersion = Newer, PreferredVersion = Pinned };
        var catalog = new AppCatalogService(dataDirectory: Path.Combine(root, "library"));
        await catalog.SaveLocalAppsAsync([app]);
        var loaded = (await catalog.LoadLocalAppsAsync()).Should().ContainSingle().Subject;
        loaded.PreferredVersion.Should().Be(Pinned);
        loaded.LatestVersion = Pinned;
        loaded.PreferredVersion.Should().Be(Pinned);
        loaded.PreferredVersion = null;
        loaded.PreferredVersion.Should().BeNull();
    }
}
