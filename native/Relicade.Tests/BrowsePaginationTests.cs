using System.Net;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public class BrowsePaginationTests
{
    private sealed class Handler(Func<int, CancellationToken, Task<string>> page) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public List<string?> Cursors { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/facets", StringComparison.Ordinal))
            {
                var cursor = request.RequestUri.Query.TrimStart('?').Split('&').FirstOrDefault(value => value.StartsWith("cursor=", StringComparison.Ordinal));
                Cursors.Add(cursor == null ? null : Uri.UnescapeDataString(cursor[7..]));
            }
            var body = request.RequestUri!.AbsolutePath.EndsWith("/facets", StringComparison.Ordinal)
                ? "{\"total\":511,\"consoles\":[]}" : await page(++Requests, token);
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
    private static string Page(string? cursor, params string[] ids) => JsonSerializer.Serialize(new
    {
        items = ids.Select(id => new { id, slug = id, name = id, launcher = new { folderName = id } }),
        nextCursor = cursor, isDone = cursor == null
    });
    private static BrowseViewModel Model(Handler handler, bool hide = true) => new(
        new QuiverCatalogClient(new HttpClient(handler), "https://catalog.example.test/api/v1"),
        () => Array.Empty<GameInfo>(), () => "", _ => Task.FromResult<(List<GameInfo>, string?)>(([], null)))
        { HideLibraryApps = hide, Platform = null };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_or_cyclic_cursor_stops_with_visible_error_without_repeating_requests(bool cycle)
    {
        var handler = new Handler((n, _) => Task.FromResult(n <= 4
            ? Page(cycle && n == 2 ? "b" : "a", $"app{n}")
            : throw new InvalidOperationException("Fixture stopped an unbounded pagination loop.")));
        var model = Model(handler);
        await model.ReloadAsync(TestContext.Current.CancellationToken);
        handler.Requests.Should().Be(cycle ? 3 : 2);
        model.IsLoading.Should().BeFalse();
        model.CanLoadMore.Should().BeFalse();
        model.Status.Should().Contain("pagination");
        model.Items.Should().HaveCount(cycle ? 3 : 2);
    }

    [Fact]
    public async Task Empty_and_short_intermediate_pages_do_not_truncate_the_catalog()
    {
        var handler = new Handler((n, _) => Task.FromResult(n switch
        {
            1 => Page("a"), 2 => Page("b", "first"), 3 => Page("c"),
            4 => Page(null, "last"), _ => throw new InvalidOperationException("Unexpected extra request.")
        }));
        var model = Model(handler);
        await model.ReloadAsync(TestContext.Current.CancellationToken);
        handler.Requests.Should().Be(4);
        model.Items.Select(i => i.FolderName).Should().Equal("first", "last");
        model.CanLoadMore.Should().BeFalse();
        model.Status.Should().BeEmpty();
    }

    [Fact]
    public async Task Full_511_entry_catalog_is_reachable_without_an_arbitrary_page_cap()
    {
        var handler = new Handler((n, _) => Task.FromResult(Page(n == 6 ? null : $"page{n+1}",
            Enumerable.Range((n-1)*100, n == 6 ? 11 : 100).Select(i => $"app{i}").ToArray())));
        var model = Model(handler, hide: false);
        await model.ReloadAsync(TestContext.Current.CancellationToken);
        while (model.CanLoadMore && handler.Requests < 7) await model.LoadMoreAsync(TestContext.Current.CancellationToken);
        handler.Requests.Should().Be(6);
        model.Items.Should().HaveCount(511);
        model.Items.Select(i => i.FolderName).Distinct().Should().HaveCount(511);
        model.CanLoadMore.Should().BeFalse();
    }
    [Fact]
    public async Task Load_more_cycle_preserves_cards_and_reload_resets_cursor_history()
    {
        var handler = new Handler((n, _) => Task.FromResult(n switch
        {
            1 or 3 => Page("next", "first"), 2 => Page("next", "second"),
            4 => Page(null, "last"), _ => throw new InvalidOperationException("Unexpected request.")
        }));
        var model = Model(handler, hide: false);
        var token = TestContext.Current.CancellationToken;
        await model.ReloadAsync(token);
        (await model.LoadMoreAsync(token)).Should().BeFalse();
        model.Items.Select(i => i.FolderName).Should().Equal("first", "second");
        model.Status.Should().Contain("pagination");
        model.CanLoadMore.Should().BeFalse();
        model.IsLoadingMore.Should().BeFalse();
        await model.ReloadAsync(token);
        model.Status.Should().BeEmpty();
        (await model.LoadMoreAsync(token)).Should().BeTrue();
        model.Items.Select(i => i.FolderName).Should().Equal("first", "last");
    }

    [Fact]
    public async Task Failed_next_page_can_be_retried_without_a_false_cursor_cycle()
    {
        var handler = new Handler((n, _) => Task.FromResult(n switch
        {
            1 => Page("next", "first"), 2 => throw new HttpRequestException("Offline fixture"),
            3 => Page(null, "last"), _ => throw new InvalidOperationException("Unexpected request.")
        }));
        var model = Model(handler, hide: false);
        var token = TestContext.Current.CancellationToken;
        await model.ReloadAsync(token);
        (await model.LoadMoreAsync(token)).Should().BeFalse();
        model.CanLoadMore.Should().BeTrue();
        (await model.LoadMoreAsync(token)).Should().BeTrue();
        model.Items.Select(i => i.FolderName).Should().Equal("first", "last");
        model.Status.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_page_drains_without_changing_cards_and_can_be_retried()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (n, token) =>
        {
            if (n == 2)
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return n == 1 ? Page("next", "first") : Page(null, "last");
        });
        var model = Model(handler, hide: false);
        await model.ReloadAsync(TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = model.LoadMoreAsync(cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        model.Items.Select(i => i.FolderName).Should().Equal("first");
        model.IsLoadingMore.Should().BeFalse();
        model.CanLoadMore.Should().BeTrue();
        (await model.LoadMoreAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        model.Items.Select(i => i.FolderName).Should().Equal("first", "last");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\u2003")]
    public async Task Cursor_identity_matches_the_query_value_actually_sent(string padding)
    {
        var handler = new Handler((n, _) => Task.FromResult(n <= 4
            ? Page(string.Concat(Enumerable.Repeat(padding, n)) + "next" + string.Concat(Enumerable.Repeat(padding, n)), $"app{n}")
            : throw new InvalidOperationException("Fixture stopped repeated wire requests.")));
        var model = Model(handler);
        await model.ReloadAsync(TestContext.Current.CancellationToken);
        handler.Requests.Should().Be(2);
        handler.Cursors.Should().Equal(new string?[] { null, "next" });
        model.Items.Should().HaveCount(2);
        model.CanLoadMore.Should().BeFalse();
        model.Status.Should().Contain("pagination");
    }

    [Fact]
    public async Task Case_distinct_and_interior_space_cursors_remain_opaque()
    {
        var handler = new Handler((n, _) => Task.FromResult(n switch
        {
            1 => Page(" A "), 2 => Page("a"), 3 => Page("a b"),
            4 => Page("ab"), 5 => Page(null, "last"),
            _ => throw new InvalidOperationException("Unexpected extra request.")
        }));
        var model = Model(handler);
        await model.ReloadAsync(TestContext.Current.CancellationToken);
        handler.Cursors.Should().Equal(new string?[] { null, "A", "a", "a b", "ab" });
        model.Items.Select(item => item.FolderName).Should().Equal("last");
        model.CanLoadMore.Should().BeFalse();
        model.Status.Should().BeEmpty();
    }

}
