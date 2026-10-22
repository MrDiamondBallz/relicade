using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogHistoryPaginationTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Relicade.HistoryTests", Guid.NewGuid().ToString("N"));
    public void Dispose() => TestFixtures.CleanupDirectory(root);
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    static object Page(object[] items, string? next = null) => new { items, nextCursor = next, isDone = next == null };
    static object Release(string version, string state) => new { version, state, reasons = new[] { "Fixture review" }, assets = new[] { new { filename = "game.zip", url = "https://assets.example.test/game.zip", checksum = "sha256:" + new string('a',64) } } };
    static GameInfo App() => new() { Name = "Alpha", FolderName = "Alpha", Repository = "fixture/alpha", RepositorySource = "github", CatalogEntryId = "id-alpha" };
    HttpResponseMessage? Metadata(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/release-status")) return Json(Page([new { id = "id-alpha", slug = "alpha", provider = "github", repository = "fixture/alpha", verified = new { version = "v9" } }]));
        if (path.EndsWith("/apps")) return Json(Page([new { id = "id-alpha", slug = "alpha", name = "Alpha", launcher = new { folderName = "Alpha" } }]));
        path.Should().Be("/api/v1/apps/alpha/release-history"); return null;
    }
    static void Expire(CatalogReleases service)
    {
        var cache = (Dictionary<string, (DateTime At, IReadOnlyList<QuiverCatalogRelease> Releases)>)typeof(CatalogReleases).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        cache["alpha"] = (DateTime.MinValue, cache["alpha"].Releases);
    }
    [Theory]
    [InlineData("blocked", ReleaseCheckState.Blocked, true)]
    [InlineData("verified", ReleaseCheckState.Verified, true)]
    [InlineData("unverified", ReleaseCheckState.Unverified, true)]
    [InlineData("blocked", ReleaseCheckState.Blocked, false)]
    [InlineData("verified", ReleaseCheckState.Verified, false)]
    [InlineData("unverified", ReleaseCheckState.Unverified, false)]
    public async Task Actual_release_check_reads_the_target_state_and_digest_on_later_pages(string state, ReleaseCheckState expected, bool later)
    {
        var requests = new List<string>();
        using var handler = new Handler((request, _) =>
        {
            if (Metadata(request) is { } metadata) return Task.FromResult(metadata);
            requests.Add(request.RequestUri!.Query);
            requests.Count.Should().BeLessThanOrEqualTo(2);
            return Task.FromResult(Json(later && requests.Count == 1 ? Page([Release("v2", "verified")], "next /雪") : Page([Release("v1", state)])));
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root);
        var result = await service.CheckAsync(App(), "v1", TestContext.Current.CancellationToken);
        result!.State.Should().Be(expected); result.ChecksumFor("game.zip").Should().Be(new string('a',64));
        requests.Should().HaveCount(later ? 2 : 1);
        if (later) requests[1].Should().Contain("cursor=" + Uri.EscapeDataString("next /雪"));
    }
    [Theory]
    [InlineData("offline")]
    [InlineData("cycle")]
    [InlineData("missing-cursor")]
    public async Task Incomplete_refresh_never_replaces_complete_history_and_can_retry(string failure)
    {
        var phase = 0; var requests = 0;
        using var handler = new Handler((request, _) =>
        {
            if (Metadata(request) is { } metadata) return Task.FromResult(metadata);
            requests++;
            if (phase == 0) return Task.FromResult(Json(Page([Release("v0", "blocked")])));
            if (phase == 2) return Task.FromResult(Json(Page([Release("v1", "verified")])));
            if (requests > 5) throw new IOException("Bounded fake-page failure.");
            if (failure == "missing-cursor") return Task.FromResult(Json(new { items = new[] { Release("v1", "verified") }, isDone = false }));
            if (requests == 2 || failure == "cycle") return Task.FromResult(Json(Page([Release("v1", "verified")], "repeat")));
            throw new HttpRequestException("Synthetic offline history.");
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root); var app = App();
        (await service.CheckAsync(app, "v0", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Blocked);
        Expire(service); phase = 1;
        var result = await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken);
        result!.State.Should().Be(ReleaseCheckState.Unverified); result.Reasons.Should().Contain(CatalogReleases.Unreachable);
        service.Cached(app)!.Value.States.Should().ContainKey("v0").WhoseValue.Should().Be(ReleaseCheckState.Blocked);
        service.Cached(app)!.Value.States.Should().NotContainKey("v1");
        phase = 2;
        (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Verified);
        service.Cached(app)!.Value.States.Should().NotContainKey("v0");
    }
}
