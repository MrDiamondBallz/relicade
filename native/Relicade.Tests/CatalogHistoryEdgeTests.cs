using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogHistoryEdgeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Relicade.HistoryEdgeTests", Guid.NewGuid().ToString("N"));
    public void Dispose() => TestFixtures.CleanupDirectory(root);
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    static object Page(object[] items, string? next = null) => new { items, nextCursor = next, isDone = next == null };
    static object Release(string version, string state, char digest = 'a') => new { version, state, reasons = Array.Empty<string>(), assets = new[] { new { filename = "game.zip", checksum = "sha256:" + new string(digest,64) } } };
    static GameInfo App() => new() { Name = "Alpha", FolderName = "Alpha", Repository = "fixture/alpha", RepositorySource = "github", CatalogEntryId = "id-alpha" };
    static HttpResponseMessage? Metadata(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/release-status")) return Json(Page([new { id = "id-alpha", slug = "alpha", provider = "github", repository = "fixture/alpha", verified = new { version = "v1" } }]));
        if (path.EndsWith("/apps")) return Json(Page([new { id = "id-alpha", slug = "alpha", name = "Alpha", launcher = new { folderName = "Alpha" } }]));
        path.Should().Be("/api/v1/apps/alpha/release-history"); return null;
    }
    static IReadOnlyList<QuiverCatalogRelease> StoredHistory(CatalogReleases service) =>
        ((Dictionary<string, (DateTime At, IReadOnlyList<QuiverCatalogRelease> Releases)>)typeof(CatalogReleases).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!)["alpha"].Releases;
    [Theory]
    [InlineData("verified", "verified", 'a', ReleaseCheckState.Verified)]
    [InlineData("verified", "verified", 'b', ReleaseCheckState.Unverified)]
    [InlineData("verified", "blocked", 'a', ReleaseCheckState.Blocked)]
    [InlineData("blocked", "verified", 'a', ReleaseCheckState.Blocked)]
    [InlineData("verified", "unverified", 'a', ReleaseCheckState.Unverified)]
    public async Task Overlapping_exact_tags_cannot_hide_conflicting_state_or_files(string first, string second, char digest, ReleaseCheckState expected)
    {
        var requests = 0;
        using var handler = new Handler((r, _) =>
        {
            if (Metadata(r) is { } m) return Task.FromResult(m);
            requests++; requests.Should().BeLessThanOrEqualTo(2);
            return Task.FromResult(Json(requests == 1 ? Page([Release("v1", first)], "more") : Page([Release("v1", second, digest)])));
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root); var app = App();
        var result = await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken);
        result!.State.Should().Be(expected); requests.Should().Be(2);
        StoredHistory(service).Should().HaveCount(expected == ReleaseCheckState.Verified ? 1 : 2);
        StoredHistory(service).Should().OnlyContain(r => r.Version == "v1" && r.Assets.Count == 1);
        if (expected == ReleaseCheckState.Verified) result.ChecksumFor("game.zip").Should().Be(new string('a',64));
        else result.Checksums.Should().BeEmpty();
    }
    [Fact]
    public async Task Empty_and_short_pages_continue_with_exact_case_sensitive_opaque_cursors()
    {
        string[] cursors = ["next", "NEXT", " next ", "\u2003next\u2003", "雪 /?&+"];
        var requests = 0;
        using var handler = new Handler((r, _) =>
        {
            var index = requests++; index.Should().BeLessThanOrEqualTo(cursors.Length);
            r.RequestUri!.Query.Should().Be("?limit=100" + (index == 0 ? "" : "&cursor=" + Uri.EscapeDataString(cursors[index - 1])));
            return Task.FromResult(Json(Page(index == cursors.Length ? [Release("v1", "blocked")] : [], index < cursors.Length ? cursors[index] : null)));
        });
        using var http = new HttpClient(handler);
        var result = await new QuiverCatalogClient(http, "https://catalog.example.test/api/v1").GetReleaseHistoryAsync("alpha", TestContext.Current.CancellationToken);
        requests.Should().Be(6); result.IsDone.Should().BeTrue(); result.NextCursor.Should().BeNull(); result.Items.Should().ContainSingle().Which.State.Should().Be("blocked");
    }
    [Theory]
    [InlineData("repeat", 2)]
    [InlineData("cycle", 3)]
    [InlineData("missing", 1)]
    [InlineData("empty", 1)]
    [InlineData("gone", 2)]
    public async Task Invalid_continuation_is_refused_without_retrying_a_cycle(string mode, int expectedRequests)
    {
        var requests = 0;
        using var handler = new Handler((_, _) =>
        {
            requests++; requests.Should().BeLessThanOrEqualTo(expectedRequests + 1);
            if (mode == "gone" && requests == 2) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(Json(new { items = Array.Empty<object>(), isDone = false, nextCursor = mode switch { "missing" => null, "empty" => "", "cycle" when requests == 2 => "b", _ => "a" } }));
        });
        using var http = new HttpClient(handler);
        var act = () => new QuiverCatalogClient(http, "https://catalog.example.test/api/v1").GetReleaseHistoryAsync("alpha", TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<QuiverCatalogClient.IncompleteHistoryException>(); requests.Should().Be(expectedRequests);
    }
    [Fact]
    public async Task Cancellation_during_a_later_page_preserves_complete_cache_and_allows_fresh_retry()
    {
        var phase = 0; var pageRequests = 0; var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (r, token) =>
        {
            if (Metadata(r) is { } m) return m;
            if (phase == 0) return Json(Page([Release("v0", "blocked")]));
            if (phase == 2) return Json(Page([Release("v1", "verified")]));
            if (++pageRequests == 1) return Json(Page([Release("v1", "verified")], "more"));
            pending.SetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Cancellation was not observed.");
        });
        using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), root); var app = App();
        (await service.CheckAsync(app, "v0", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Blocked);
        var cache = (Dictionary<string, (DateTime At, IReadOnlyList<QuiverCatalogRelease> Releases)>)typeof(CatalogReleases).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        cache["alpha"] = (DateTime.MinValue, cache["alpha"].Releases); phase = 1;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var read = service.CheckAsync(app, "v1", cancel.Token);
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); cancel.Cancel();
        await ((Func<Task>)(async () => await read)).Should().ThrowAsync<OperationCanceledException>();
        pageRequests.Should().Be(2); StoredHistory(service).Should().ContainSingle().Which.Version.Should().Be("v0");
        phase = 2; (await service.CheckAsync(app, "v1", TestContext.Current.CancellationToken))!.State.Should().Be(ReleaseCheckState.Verified);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task History_byte_budget_is_shared_across_page_bodies(int limit)
    {
        var budget = new QuiverCatalogClient.HistoryReadBudget(limit);
        using var first = new StringContent("12345", Encoding.UTF8);
        if (limit < 5)
        {
            await ((Func<Task>)(async () => await budget.ReadAsync(first, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
            return;
        }
        (await budget.ReadAsync(first, TestContext.Current.CancellationToken)).Should().Be("12345");
        using var empty = new StringContent(""); (await budget.ReadAsync(empty, TestContext.Current.CancellationToken)).Should().BeEmpty();
        using var second = new StringContent("6");
        await ((Func<Task>)(async () => await budget.ReadAsync(second, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
    }
}
