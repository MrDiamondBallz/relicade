using System.Net;
using System.Text.Json;
using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using static Relicade.Tests.ReleaseIdentityRegressionTests;

namespace Relicade.Tests;

public sealed class ExactBuildCacheTests : IDisposable
{
    const string Pin = "v1.0.0-prerelease+37", Other = "v1.0.0-prerelease+39";
    readonly string root = Path.Combine(Path.GetTempPath(), "relicade-build-cache-" + Guid.NewGuid().ToString("N"));
    readonly string? prior = RelicadePaths.OverrideUserDataRoot;
    public ExactBuildCacheTests() { Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root; GitHubApiCache.Initialize(Path.Combine(root, "cache")); }
    public void Dispose() { RelicadePaths.OverrideUserDataRoot = prior; Directory.Delete(root, true); }
    GameInfo App() => new() { Repository = "fixture/" + Guid.NewGuid().ToString("N"), FolderName = "game", PreferredVersion = Pin };

    [Theory]
    [InlineData(Other)]
    [InlineData("V1.0.0-prerelease+37")]
    public async Task Warm_cache_requires_matching_version_and_release_metadata(string wrong)
    {
        var app = App(); var requests = 0;
        GitHubApiCache.SetCache("github", app.Repository!, Pin, "", Release(wrong, true), persist: false);
        using var client = new HttpClient(new Handler(r =>
        {
            r.RequestUri!.Host.Should().Be("api.github.com"); requests++;
            return new(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri.AbsolutePath.EndsWith("/latest")
                ? JsonSerializer.Serialize(Release(Pin, true)) : JsonSerializer.Serialize(new[] { Release(Pin, true) })) };
        }));
        await app.CheckLatestVersionAsync(client);
        app.GetLatestRelease()!.tag_name.Should().Be(Pin);
        app.PreferredVersion.Should().Be(Pin); requests.Should().Be(2);
    }

    [Theory]
    [InlineData("metadata-build")]
    [InlineData("metadata-case")]
    [InlineData("selected-version")]
    public async Task Primed_endpoint_304_uses_its_own_payload_not_a_replaced_version_cache(string change)
    {
        var app = App(); app.RepositorySource = "codeberg"; var requests = 0;
        using var client = new HttpClient(new Handler(r =>
        {
            r.RequestUri!.Host.Should().Be("codeberg.org"); requests++;
            if (requests == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { Release(Pin, true) })) };
                response.Headers.ETag = new("\"endpoint-fixture\""); return response;
            }
            r.Headers.IfNoneMatch.Should().ContainSingle(x => x.Tag == "\"endpoint-fixture\"");
            GitHubApiCache.SetCache("codeberg", app.Repository!, change == "selected-version" ? Other : Pin, "\"unrelated-version-cache\"",
                Release(change == "metadata-case" ? "V1.0.0-prerelease+37" : Other, true), persist: false);
            return new(HttpStatusCode.NotModified);
        }));
        await app.CheckLatestVersionAsync(client, forceCheck: true);
        await app.CheckLatestVersionAsync(client, forceCheck: true);
        app.GetLatestRelease()!.tag_name.Should().Be(Pin); app.PreferredVersion.Should().Be(Pin);
        app.RepositoryCheckError.Should().BeNull(); requests.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Matching_warm_and_primed_endpoint_cache_remain_usable(bool notModified)
    {
        var app = App(); app.RepositorySource = "codeberg"; var requests = 0;
        GitHubApiCache.SetCache("codeberg", app.Repository!, Pin, "\"version-fixture\"", Release(Pin, true), persist: false);
        using var client = new HttpClient(new Handler(_ =>
        {
            requests++;
            if (requests == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { Release(Pin, true) })) };
                response.Headers.ETag = new("\"endpoint-fixture\""); return response;
            }
            return new(HttpStatusCode.NotModified);
        }));
        await app.CheckLatestVersionAsync(client, forceCheck: notModified);
        if (notModified) await app.CheckLatestVersionAsync(client, forceCheck: true);
        app.GetLatestRelease()!.tag_name.Should().Be(Pin); app.PreferredVersion.Should().Be(Pin);
        requests.Should().Be(notModified ? 2 : 0);
    }
}
