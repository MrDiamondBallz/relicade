using System.Net;
using System.Text.Json;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;
public sealed class CatalogHistoryTagIdentityTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Relicade.HistoryTagTests", Guid.NewGuid().ToString("N"));
    public void Dispose() => TestFixtures.CleanupDirectory(root);
    sealed class Handler(object[] rows, string verified) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t)
        {
            var path = r.RequestUri!.AbsolutePath;
            object[] items = path.EndsWith("/release-status") ? [new { id = "id-alpha", slug = "alpha", provider = "github", repository = "fixture/alpha", verified = new { version = verified } }]
                : path.EndsWith("/apps") ? [new { id = "id-alpha", slug = "alpha", name = "Alpha", launcher = new { folderName = "Alpha" } }]
                : rows;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items, isDone = true })) });
        }
    }
    static object Release(string tag, string state, char digest) => new { releaseId = tag, version = tag, state, reasons = Array.Empty<string>(), assets = new[] { new { filename = "game.zip", url = "https://assets.example.test/game.zip", checksum = "sha256:" + new string(digest,64) } } };
    static GameInfo App() => new() { Name = "Alpha", FolderName = "Alpha", Repository = "fixture/alpha", RepositorySource = "github", CatalogEntryId = "id-alpha" };
    [Theory]
    [InlineData("v1.0.0-prerelease+39", "v1.0.0-prerelease+37")]
    [InlineData("v1", "1")]
    [InlineData("v1", "V1")]
    public async Task Exact_requested_tag_keeps_its_own_state_files_and_pin(string first, string second)
    {
        using var handler = new Handler([Release(first,"verified",'a'), Release(second,"blocked",'b')],first); using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root); var app = App(); var token = TestContext.Current.CancellationToken;
        var result = await service.CheckAsync(app,second,token); result!.State.Should().Be(ReleaseCheckState.Blocked); result.ChecksumFor("game.zip").Should().Be(new string('b',64));
        (await service.CheckAsync(app,first,token))!.State.Should().Be(ReleaseCheckState.Verified);
        var cached = service.Cached(app)!.Value; cached.Releases.Should().HaveCount(2); cached.States.Should().HaveCount(2);
        app.PreferredVersion = second; (await service.SelectAsync(app,token))!.tag_name.Should().Be(second);
    }
    [Fact]
    public async Task Ambiguous_legacy_alias_does_not_borrow_a_digest_or_pick_a_different_tag()
    {
        using var handler = new Handler([Release("v1","verified",'a'),Release("1","verified",'b')],"v1"); using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root); var app = App(); var token = TestContext.Current.CancellationToken;
        var result = await service.CheckAsync(app,"01",token); result!.State.Should().Be(ReleaseCheckState.Unverified); result.Checksums.Should().BeEmpty();
        app.PreferredVersion = "01"; (await service.SelectAsync(app,token)).Should().BeNull();
    }
    [Fact]
    public async Task Single_legacy_v_prefix_alias_keeps_working()
    {
        using var handler = new Handler([Release("v1","verified",'a')],"v1"); using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root); var app = App(); var token = TestContext.Current.CancellationToken;
        (await service.CheckAsync(app,"1",token))!.State.Should().Be(ReleaseCheckState.Verified);
        app.PreferredVersion = "1"; (await service.SelectAsync(app,token))!.tag_name.Should().Be("v1");
    }
    [Theory]
    [InlineData("v1+37")]
    [InlineData("")]
    public async Task Absent_build_or_empty_request_cannot_borrow_status_approval(string requested)
    {
        using var handler = new Handler([Release("v1+39","verified",'a')],"v1+39"); using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root);
        var result = await service.CheckAsync(App(),requested,TestContext.Current.CancellationToken); result!.State.Should().Be(ReleaseCheckState.Unverified); result.Checksums.Should().BeEmpty();
    }
    [Fact]
    public async Task Empty_source_tag_is_not_a_complete_history()
    {
        using var handler = new Handler([Release("","verified",'a')],"v1"); using var http = new HttpClient(handler);
        var client = new QuiverCatalogClient(http,"https://catalog.example.test/api/v1");
        await ((Func<Task>)(async () => await client.GetReleaseHistoryAsync("alpha",TestContext.Current.CancellationToken))).Should().ThrowAsync<QuiverCatalogClient.IncompleteHistoryException>();
    }

    [Fact]
    public async Task Conflicting_exact_tag_keeps_file_warnings_and_menu_state_consistent()
    {
        object Record(char digest) => new { version = "v1", state = "verified", reasons = Array.Empty<string>(), assets = new[]
        {
            new { filename = "windows.zip", url = "https://assets.example.test/windows.zip", checksum = "sha256:" + new string(digest,64), scan = new { verdict = "flagged" } },
            new { filename = "linux.zip", url = "https://assets.example.test/linux.zip", checksum = "sha256:" + new string(digest,64), scan = new { verdict = "clean" } },
        } };
        using var handler = new Handler([Record('a'),Record('b')],"v1"); using var http = new HttpClient(handler);
        var service = new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root); var app = App();
        var result = await service.CheckAsync(app,"v1",TestContext.Current.CancellationToken);
        result!.State.Should().Be(ReleaseCheckState.Unverified); result.Checksums.Should().BeEmpty();
        result.ForFile("windows.zip").Flagged.Should().BeTrue(); result.ForFile("linux.zip").Flagged.Should().BeFalse();
        service.Cached(app)!.Value.Releases.Should().HaveCount(2).And.OnlyContain(r => r.assets.Length == 2);
        service.Cached(app)!.Value.States["v1"].Should().Be(ReleaseCheckState.Unverified);
    }
}
