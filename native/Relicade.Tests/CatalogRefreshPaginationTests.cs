using System.Net;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogRefreshPaginationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Relicade.CatalogRefreshTests", Guid.NewGuid().ToString("N"));
    public void Dispose() => TestFixtures.CleanupDirectory(_root);
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static object Status(string version) => new { id = "id-alpha", slug = "alpha", provider = "github", repository = "fixture/alpha", verified = new { version }, latestUpstream = new { version } };
    private static object Listed(string folder) => new { id = "id-alpha", slug = "alpha", name = "Alpha", launcher = new { folderName = folder } };
    private static object Page(object item, string? cursor) => new { items = new[] { item }, nextCursor = cursor, isDone = cursor == null };
    private static GameInfo App() => new() { Name = "Alpha", FolderName = "Alpha", Repository = "fixture/alpha", RepositorySource = "github", CatalogEntryId = "id-alpha" };
    private void Seed()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "catalog-release-status.json"), JsonSerializer.Serialize(new[] { Status("v1") }));
        File.WriteAllText(Path.Combine(_root, "catalog-release-links.json"), "{\"alpha\":{\"folderName\":\"Alpha\",\"releaseAssetFilter\":null}}");
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(false, true, "")]
    [InlineData(true, false, "")]
    [InlineData(true, true, "")]
    [InlineData(true, false, " ")]
    [InlineData(true, false, "\u2003")]
    public async Task Repeating_refresh_cursors_stop_without_replacing_saved_metadata(bool listing, bool cycle, string padding)
    {
        Seed();
        var before = Directory.GetFiles(_root).ToDictionary(Path.GetFileName, File.ReadAllBytes);
        var requests = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(listing ? "/apps" : "/release-status", StringComparison.Ordinal))
            {
                var n = ++requests;
                if (n > 4) throw new InvalidOperationException("Bounded fixture stopped a repeated refresh cursor.");
                var cursor = cycle && n == 2 ? "b" : "a";
                cursor = string.Concat(Enumerable.Repeat(padding, n)) + cursor + string.Concat(Enumerable.Repeat(padding, n));
                return Task.FromResult(Json(Page(listing ? Listed("Changed") : Status("v99"), cursor)));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Offline fixture") });
        });
        using var http = new HttpClient(handler);
        var releases = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), _root);
        var app = App();
        // Mirrors background refresh's uncancelled caller, but the fake handler has a hard request bound.
        await releases.RefreshAsync([app], CancellationToken.None);
        requests.Should().Be(cycle ? 3 : 2);
        foreach (var pair in before) File.ReadAllBytes(Path.Combine(_root, pair.Key!)).Should().Equal(pair.Value);
        app.CatalogSlug.Should().Be("alpha");
        app.CatalogVerifiedVersion.Should().Be("v1");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_refresh_keeps_backoff_then_allows_a_valid_same_instance_retry(bool listing)
    {
        Seed();
        var healthy = false;
        var calls = 0;
        using var handler = new Handler((request, _) =>
        {
            var apps = request.RequestUri!.AbsolutePath.EndsWith("/apps", StringComparison.Ordinal);
            calls++;
            if (calls > 10) throw new InvalidOperationException("Bounded refresh fixture.");
            return Task.FromResult(Json(Page(apps ? Listed("Alpha") : Status("v2"), apps == listing && !healthy ? "repeat" : null)));
        });
        using var http = new HttpClient(handler);
        var releases = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), _root);
        var app = App();
        var token = TestContext.Current.CancellationToken;
        await releases.RefreshAsync([app], token);
        var failedCalls = calls;
        healthy = true;
        await releases.RefreshAsync([app], token);
        calls.Should().Be(failedCalls, "failure backoff is not an immediate retry loop");
        // Advance only the existing cache-age boundary, without sleeps or changing responses mid-operation.
        typeof(CatalogReleases).GetField(listing ? "_linksAt" : "_statusAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(releases, DateTime.MinValue);
        await releases.RefreshAsync([app], token).WaitAsync(TimeSpan.FromSeconds(5), token);
        calls.Should().Be(failedCalls + 1);
        app.CatalogVerifiedVersion.Should().Be("v2");
        releases.ListedApp(app)!.Launcher.FolderName.Should().Be("Alpha");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_preserves_partial_feed_and_releases_refresh_authority_for_retry(bool listing)
    {
        Seed();
        var path = Path.Combine(_root, listing ? "catalog-release-links.json" : "catalog-release-status.json");
        var before = File.ReadAllBytes(path);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = false;
        var targetCalls = 0;
        using var handler = new Handler(async (request, token) =>
        {
            var apps = request.RequestUri!.AbsolutePath.EndsWith("/apps", StringComparison.Ordinal);
            if (apps == listing)
            {
                targetCalls++;
                if (!healthy && targetCalls == 2)
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return Json(Page(apps ? Listed("Alpha") : Status("v2"), healthy ? null : "next"));
            }
            return Json(Page(apps ? Listed("Alpha") : Status("v1"), null));
        });
        using var http = new HttpClient(handler);
        var releases = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), _root);
        var app = App();
        var token = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = releases.RefreshAsync([app], cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        File.ReadAllBytes(path).Should().Equal(before);
        healthy = true;
        await releases.RefreshAsync([app], token).WaitAsync(TimeSpan.FromSeconds(5), token);
        targetCalls.Should().Be(3);
        app.CatalogVerifiedVersion.Should().Be(listing ? "v1" : "v2");
        releases.ListedApp(app)!.Launcher.FolderName.Should().Be("Alpha");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Long_empty_page_sequence_preserves_endpoint_specific_cursor_identity(bool listing)
    {
        Seed();
        var cursors = new List<string?>();
        using var handler = new Handler((request, _) =>
        {
            var apps = request.RequestUri!.AbsolutePath.EndsWith("/apps", StringComparison.Ordinal);
            if (apps != listing) return Task.FromResult(Json(Page(apps ? Listed("Alpha") : Status("v1"), null)));
            var raw = request.RequestUri.Query.TrimStart('?').Split('&').FirstOrDefault(value => value.StartsWith("cursor=", StringComparison.Ordinal));
            cursors.Add(raw == null ? null : Uri.UnescapeDataString(raw[7..]));
            var n = cursors.Count;
            if (n > 52) throw new InvalidOperationException("Unexpected extra page.");
            if (n == 52) return Task.FromResult(Json(Page(apps ? Listed("Alpha") : Status("v2"), null)));
            var next = n switch { 1 => " A ", 2 => "a", 3 => "a b", _ => $"page{n}" };
            return Task.FromResult(Json(new { items = Array.Empty<object>(), nextCursor = next, isDone = false }));
        });
        using var http = new HttpClient(handler);
        var releases = new CatalogReleases(new QuiverCatalogClient(http, "https://catalog.example.test/api/v1"), _root);
        var app = App();
        await releases.RefreshAsync([app], TestContext.Current.CancellationToken);
        cursors.Should().HaveCount(52);
        cursors.Take(4).Should().Equal(new string?[] { null, listing ? "A" : " A ", "a", "a b" });
        app.CatalogVerifiedVersion.Should().Be(listing ? "v1" : "v2");
        releases.ListedApp(app)!.Launcher.FolderName.Should().Be("Alpha");
    }

}
