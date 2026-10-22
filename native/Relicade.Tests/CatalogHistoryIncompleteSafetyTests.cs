using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogHistoryIncompleteSafetyTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Relicade.IncompleteHistoryTests", Guid.NewGuid().ToString("N"));
    public void Dispose() => TestFixtures.CleanupDirectory(root);
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    static object Page(object[] items, string? next = null) => new { items, nextCursor = next, isDone = next == null };
    static object Release(string version, string state) => new { version, state, reasons = new[] { "Synthetic review evidence" }, assets = Array.Empty<object>() };
    [Theory]
    [InlineData("blocked", false, ReleaseCheckState.Blocked)]
    [InlineData("verified", false, ReleaseCheckState.Unverified)]
    [InlineData("unverified", false, ReleaseCheckState.Unverified)]
    [InlineData("unrelated", true, ReleaseCheckState.Blocked)]
    public async Task Incomplete_history_does_not_promote_the_status_feed_over_known_denials(string partialState, bool cachedBlock, ReleaseCheckState expected)
    {
        var phase = cachedBlock ? 0 : 1; var requests = 0;
        using var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/release-status")) return Json(Page([new { id = "id-alpha", slug = "alpha", provider = "github", repository = "fixture/alpha", verified = new { version = "v1" } }]));
            if (path.EndsWith("/apps")) return Json(Page([new { id = "id-alpha", slug = "alpha", name = "Alpha", launcher = new { folderName = "Alpha" } }]));
            path.Should().Be("/api/v1/apps/alpha/release-history");
            if (phase == 0) return Json(Page([Release("v1", "blocked")]));
            if (phase == 2) return Json(Page([Release("v1", "verified")]));
            requests++;
            if (requests == 1) return Json(Page([Release(partialState == "unrelated" ? "v2" : "v1", partialState == "unrelated" ? "verified" : partialState)], "more"));
            throw new HttpRequestException("Synthetic continuation unavailable.");
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root);
        var app = new GameInfo { Name = "Alpha", FolderName = "Alpha", Repository = "fixture/alpha", RepositorySource = "github", CatalogEntryId = "id-alpha" };
        if (cachedBlock)
        {
            (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Blocked);
            var cache = (Dictionary<string, (DateTime At, IReadOnlyList<QuiverCatalogRelease> Releases)>)typeof(CatalogReleases).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            cache["alpha"] = (DateTime.MinValue, cache["alpha"].Releases);
            phase = 1;
        }
        var result = await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken);
        result!.State.Should().Be(expected);
        requests.Should().Be(2);
        if (cachedBlock)
        {
            // Cached() is the installable version-menu projection and deliberately hides asset-free records.
            var retained = (Dictionary<string, (DateTime At, IReadOnlyList<QuiverCatalogRelease> Releases)>)typeof(CatalogReleases).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            retained["alpha"].Releases.Should().ContainSingle().Which.State.Should().Be("blocked");
        }
        else service.Cached(app).Should().BeNull();
        phase = 2;
        (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Verified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observed_partial_blocks_survive_a_later_offline_retry_until_complete_history_arrives(bool firstReadIsSelection)
    {
        var phase = 0; var requests = 0;
        using var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/release-status")) return Json(Page([new { id = "id-alpha", slug = "alpha", provider = "github", repository = "fixture/alpha", verified = new { version = "v1" } }]));
            if (path.EndsWith("/apps")) return Json(Page([new { id = "id-alpha", slug = "alpha", name = "Alpha", launcher = new { folderName = "Alpha" } }]));
            path.Should().Be("/api/v1/apps/alpha/release-history");
            if (phase == 2) return Json(Page([Release("v1", "verified")]));
            if (phase == 0 && ++requests == 1) return Json(Page([Release("v1", "blocked")], "more"));
            throw new HttpRequestException("Synthetic unavailable continuation or retry.");
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root);
        var app = new GameInfo { Name = "Alpha", FolderName = "Alpha", Repository = "fixture/alpha", RepositorySource = "github", CatalogEntryId = "id-alpha" };
        if (firstReadIsSelection) (await service.SelectAsync(app, TestContext.Current.CancellationToken)).Should().BeNull();
        else (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Blocked);
        service.Cached(app).Should().BeNull(); requests.Should().Be(2);
        phase = 1;
        (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Blocked);
        service.Cached(app).Should().BeNull();
        phase = 2;
        (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Verified);
    }
}
