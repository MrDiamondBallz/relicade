using System.Net;
using FluentAssertions;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class ProductBoundaryTests
{
    private sealed class Requests : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public void Identity_is_owned_and_updates_are_not_implicitly_enabled()
    {
        RelicadeProfile.Instance.DisplayName.Should().Be("Relicade");
        RelicadeProfile.Instance.Repository.Should().Be("MrDiamondBallz/relicade");
        Relicade.Core.Services.LauncherProfile.Default.ApplicationId.Should().Be("Relicade");
        Relicade.Core.Services.LauncherProfile.Default.Repository.Should().Be("MrDiamondBallz/relicade");
        RelicadePaths.AppName.Should().Be("Relicade");
        VelopackUpdateService.ShouldSkipAutomaticSelfUpdate().Should().BeTrue();
    }

    [Fact]
    public async Task Download_problem_reporting_never_sends_even_with_explicit_catalog_url()
    {
        using var handler = new Requests();
        using var http = new HttpClient(handler);
        var catalog = new QuiverCatalogClient(http, "https://catalog.example.test/api/v1");
        await catalog.ReportDownloadProblemAsync("fixture", "v1", "fixture.zip", "mismatch", TestContext.Current.CancellationToken);
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task Announcements_have_no_default_remote_service()
    {
        using var handler = new Requests();
        using var http = new HttpClient(handler);
        (await AnnouncementService.TryFetchAsync(http)).Should().BeNull();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task Production_telemetry_is_unavailable_even_when_given_a_token()
    {
        using var telemetry = new Telemetry("synthetic-canary-token", "https://collector.example.test");
        telemetry.Configure(true, "fixture");
        telemetry.Available.Should().BeFalse();
        telemetry.Enabled.Should().BeFalse();
        telemetry.Track("canary");
        await telemetry.FlushAsync(TestContext.Current.CancellationToken);
        Telemetry.Current.Available.Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_self_updates_are_unavailable_not_a_successful_check()
    {
        var result = await new VelopackUpdateService().CheckForUpdatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        result.CheckSucceeded.Should().BeFalse();
        result.UpdateAvailable.Should().BeFalse();
        result.ErrorMessage.Should().Contain("unavailable");
        using var requests = new Requests();
        using var http = new HttpClient(requests);
        var directory = Path.Combine(Path.GetTempPath(), "relicade-disabled-updates-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var android = new AndroidLauncherUpdater(http, directory, new UnusedInstaller(), () => new());
            await android.CheckAsync(manual: true, TestContext.Current.CancellationToken);
            await android.UpdateAsync();
            android.CanCheck.Should().BeFalse();
            android.CanUpdate.Should().BeFalse();
            android.AvailableText.Should().Contain("unavailable");
            requests.Count.Should().Be(0);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class UnusedInstaller : IAndroidLauncherInstaller
    {
        public LauncherApkIdentity Installed => new("io.github.mrdiamondballz.relicade", 100, "0.1.0");
        public Task<LauncherApkIdentity> ValidateAsync(string path, string expectedVersion) => throw new InvalidOperationException("Unexpected validation");
        public Task InstallAsync(string path) => throw new InvalidOperationException("Unexpected install");
    }

    [Fact]
    public async Task Disabled_updater_rejects_staging_and_restart_before_backup_or_other_mutations()
    {
        var previous = RelicadePaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "relicade-no-updater-writes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        RelicadePaths.OverrideUserDataRoot = root;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "apps.json"), "{\"apps\":[]}", TestContext.Current.CancellationToken);
            var updater = new VelopackUpdateService();
            var download = () => updater.DownloadUpdatesAsync(null!, cancellationToken: TestContext.Current.CancellationToken);
            await download.Should().ThrowAsync<NotSupportedException>();
            Action apply = () => updater.ApplyUpdatesAndRestart(null!);
            apply.Should().Throw<NotSupportedException>();
            Directory.Exists(Path.Combine(root, "Backups")).Should().BeFalse("disabled updates must fail before any mutation");
            Directory.GetFiles(root).Should().ContainSingle().Which.Should().EndWith("apps.json");
        }
        finally { RelicadePaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }

    [Fact]
    public void Development_build_does_not_adopt_a_package_sidecar()
    {
        var previousRoot = RelicadePaths.OverrideUserDataRoot;
        var previousPackage = RelicadePaths.VelopackPackageDirectoryProvider;
        var root = Path.Combine(Path.GetTempPath(), "relicade-product-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"GitHubApiToken\":\"synthetic-canary\"}");
        try
        {
            RelicadePaths.OverrideUserDataRoot = null;
            RelicadePaths.VelopackPackageDirectoryProvider = () => root;
            RelicadePaths.UserDataRoot.Should().NotBe(root);
            Path.GetFileName(RelicadePaths.UserDataRoot).Should().Be("Relicade");
        }
        finally
        {
            RelicadePaths.OverrideUserDataRoot = previousRoot;
            RelicadePaths.VelopackPackageDirectoryProvider = previousPackage;
            Directory.Delete(root, true);
        }
    }
}
