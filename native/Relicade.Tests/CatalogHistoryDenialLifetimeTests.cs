using System.Net;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;
public sealed class CatalogHistoryDenialLifetimeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Relicade.HistoryLifetimeTests", Guid.NewGuid().ToString("N"));
    public void Dispose() => TestFixtures.CleanupDirectory(root);
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    static object Page(object[] items, string? next = null) => new { items, nextCursor = next, isDone = next == null };
    static object Release(string state) => new { version = "v1", state, reasons = Array.Empty<string>(), assets = Array.Empty<object>() };
    static GameInfo App(string slug) => new() { Name = slug, FolderName = slug, Repository = slug == "gamma" ? "fixture/unrelated" : "fixture/shared", RepositorySource = "github", CatalogEntryId = "id-" + slug, ReleaseAssetFilter = slug == "beta" ? "linux" : "windows" };
    static HttpResponseMessage? Metadata(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        string[] slugs = ["alpha", "beta", "gamma"];
        if (path.EndsWith("/release-status")) return Json(Page(slugs.Select(slug => (object)new { id = "id-" + slug, slug, provider = "github", repository = App(slug).Repository, verified = new { version = "v1" } }).ToArray()));
        if (path.EndsWith("/apps")) return Json(Page(slugs.Select(slug => (object)new { id = "id-" + slug, slug, name = slug, launcher = new { folderName = slug, releaseAssetFilter = App(slug).ReleaseAssetFilter } }).ToArray()));
        return null;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_preserves_only_blocks_actually_received_before_it(bool beforeAnyPage)
    {
        var phase = 0; var requests = 0; var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (r, token) =>
        {
            if (Metadata(r) is { } m) return m;
            if (phase == 1) throw new HttpRequestException("Synthetic offline retry.");
            if (!beforeAnyPage && ++requests == 1) return Json(Page([Release("blocked")], "more"));
            pending.SetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Expected cancellation.");
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root); var app = App("alpha");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var check = service.CheckAsync(app, "v1", cancel.Token);
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); cancel.Cancel();
        await ((Func<Task>)(async () => await check)).Should().ThrowAsync<OperationCanceledException>();
        service.Cached(app).Should().BeNull(); phase = 1;
        (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(beforeAnyPage ? ReleaseCheckState.Verified : ReleaseCheckState.Blocked);
    }
    [Fact]
    public async Task Denial_identity_is_the_catalog_app_and_version_not_a_shared_repository_or_other_platform_entry()
    {
        var phase = 0; var requests = 0;
        using var handler = new Handler((r, _) =>
        {
            if (Metadata(r) is { } m) return Task.FromResult(m);
            var alpha = r.RequestUri!.AbsolutePath.Contains("/alpha/");
            if (phase == 2 && !alpha || phase == 3) return Task.FromResult(Json(Page([Release("verified")])));
            if (phase == 0 && alpha && ++requests == 1) return Task.FromResult(Json(Page([Release("blocked")], "more")));
            throw new HttpRequestException("Synthetic offline continuation.");
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root);
        var alpha = App("alpha"); var beta = App("beta"); var gamma = App("gamma"); var token = TestContext.Current.CancellationToken;
        (await service.CheckAsync(alpha, "v1", token))!.State.Should().Be(ReleaseCheckState.Blocked); phase = 1;
        (await service.CheckAsync(beta, "v1", token))!.State.Should().Be(ReleaseCheckState.Verified);
        (await service.CheckAsync(gamma, "v1", token))!.State.Should().Be(ReleaseCheckState.Verified);
        (await service.CheckAsync(alpha, "v2", token))!.State.Should().Be(ReleaseCheckState.Unverified);
        phase = 2; (await service.CheckAsync(beta, "v1", token))!.State.Should().Be(ReleaseCheckState.Verified);
        (await service.CheckAsync(alpha, "v1", token))!.State.Should().Be(ReleaseCheckState.Blocked);
        phase = 3; (await service.CheckAsync(alpha, "v1", token))!.State.Should().Be(ReleaseCheckState.Verified);
    }
    [Theory]
    [InlineData("missing-items")]
    [InlineData("null-items")]
    [InlineData("missing-done")]
    [InlineData("null-json")]
    [InlineData("not-found")]
    public async Task Malformed_or_missing_history_is_not_a_complete_refresh_that_clears_a_known_block(string kind)
    {
        var phase = 0; var requests = 0;
        using var handler = new Handler((r, _) =>
        {
            if (Metadata(r) is { } m) return Task.FromResult(m);
            if (phase == 0 && ++requests == 1) return Task.FromResult(Json(Page([Release("blocked")], "more")));
            if (phase == 0) throw new HttpRequestException("Synthetic incomplete history.");
            if (phase == 2) return Task.FromResult(Json(Page([Release("verified")])));
            return Task.FromResult(kind switch
            {
                "missing-items" => Json(new { isDone = true }),
                "null-items" => Json(new { items = (object?)null, isDone = true }),
                "missing-done" => Json(new { items = Array.Empty<object>() }),
                "null-json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root); var app = App("alpha"); var token = TestContext.Current.CancellationToken;
        (await service.CheckAsync(app, "v1", token))!.State.Should().Be(ReleaseCheckState.Blocked); phase = 1;
        (await service.CheckAsync(app, "v1", token))!.State.Should().Be(ReleaseCheckState.Blocked);
        service.Cached(app).Should().BeNull(); phase = 2;
        (await service.CheckAsync(app, "v1", token))!.State.Should().Be(ReleaseCheckState.Verified);
    }
}
