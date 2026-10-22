using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Relicade.Views;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
namespace Relicade.Tests;
public sealed class BrowseReleaseRetryTests
{
    sealed class Transport : HttpMessageHandler
    {
        internal int HistoryCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/release-history"))
            {
                HistoryCalls++;
                return Task.FromResult(HistoryCalls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : Json("""{"items":[{"version":"fixture-v1","notes":"Recovered notes"}],"isDone":true}"""));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/reviews")) return Task.FromResult(Json("""{"items":[],"isDone":true}"""));
            return Task.FromResult(Json("{}"));
        }
        static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Explicit_reentry_after_transient_failure_loads_release_notes_without_reopening_project(bool catalog)
    {
        using var transport = new Transport(); using var http = new HttpClient(transport);
        var app = new GameInfo { Name = "Synthetic project", Repository = "fixture/project", FolderName = "Fixture" };
        var repositoryCalls = 0;
        using var model = new BrowseDetailsViewModel(new QuiverCatalogClient(http, "https://catalog.test/api/v1"),
            (_, _) => app, (_, _) => Task.FromResult(new DocumentContent("Synthetic README")), (_, _) =>
            {
                repositoryCalls++;
                if (repositoryCalls == 1) throw new HttpRequestException("Synthetic offline failure");
                return Task.FromResult<IReadOnlyList<QuiverCatalogRelease>>([new() { Version = "fixture-v1", Notes = "Recovered notes" }]);
            });
        var item = catalog ? BrowseItem.FromCatalog(new() { Id = "fixture", Slug = "fixture", Name = "Synthetic project" }, new Dictionary<string, string>()) : BrowseItem.FromList(app);
        await model.OpenAsync(item, CancellationToken.None);
        await model.LoadReleasesAsync(CancellationToken.None);
        Assert.Null(model.Releases); Assert.Contains("Couldn't load", model.ReleasesStatus);
        await model.LoadReleasesAsync(CancellationToken.None);
        Assert.Equal(2, catalog ? transport.HistoryCalls : repositoryCalls);
        Assert.Equal("fixture-v1", Assert.Single(model.Releases!).Version); Assert.Equal("", model.ReleasesStatus);
        Assert.Same(item, model.Item); Assert.Equal("Synthetic project", model.Entry!.Name);
        await model.LoadReleasesAsync(CancellationToken.None);
        Assert.Equal(2, catalog ? transport.HistoryCalls : repositoryCalls);
    }
    [Theory]
    [InlineData(404)][InlineData(410)][InlineData(0)]
    public async Task Missing_or_successfully_empty_history_does_not_offer_repeat_requests(int status)
    {
        using var http = new HttpClient(new Transport()); var calls = 0;
        using var model = Manual(http, (_, _) =>
        {
            calls++;
            if (status > 0) throw new HttpRequestException("Missing fixture", null, (HttpStatusCode)status);
            return Task.FromResult<IReadOnlyList<QuiverCatalogRelease>>([]);
        });
        await model.OpenAsync(BrowseItem.FromList(new() { Name = "Synthetic missing project", Repository = "fixture/project" }), CancellationToken.None);
        await model.LoadReleasesAsync(CancellationToken.None); await model.LoadReleasesAsync(CancellationToken.None);
        Assert.Equal(1, calls); Assert.False(model.CanRetryReleases); Assert.False(model.IsLoadingReleases);
        if (status == 0) Assert.Empty(model.Releases!); else Assert.Null(model.Releases);
    }

    [Theory]
    [InlineData("close")][InlineData("switch")][InlineData("cancel")][InlineData("duplicate")]
    public async Task Pending_history_cannot_duplicate_or_publish_into_a_closed_changed_or_cancelled_page(string action)
    {
        using var http = new HttpClient(new Transport()); var calls = 0;
        var response = new TaskCompletionSource<IReadOnlyList<QuiverCatalogRelease>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = Manual(http, (_, _) => { calls++; return response.Task; });
        await model.OpenAsync(BrowseItem.FromList(new() { Name = "First", Repository = "fixture/first" }), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = model.LoadReleasesAsync(cancellation.Token);
        await model.LoadReleasesAsync(cancellation.Token); Assert.Equal(1, calls); Assert.True(model.IsLoadingReleases);
        if (action == "close") model.Close();
        if (action == "switch") await model.OpenAsync(BrowseItem.FromList(new() { Name = "Second", Repository = "fixture/second" }), CancellationToken.None);
        if (action == "cancel") cancellation.Cancel();
        response.SetResult([new() { Version = "old" }]);
        if (action == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); else await pending;
        Assert.False(model.IsLoadingReleases);
        if (action == "duplicate") Assert.Equal("old", Assert.Single(model.Releases!).Version);
        else Assert.Null(model.Releases);
        if (action == "switch") Assert.Equal("Second", model.Entry!.Name);
        if (action == "close") Assert.Null(model.Item);
        if (action == "cancel")
        {
            Assert.True(model.CanRetryReleases); await model.LoadReleasesAsync(CancellationToken.None);
            Assert.Equal(2, calls); Assert.Equal("old", Assert.Single(model.Releases!).Version);
        }
    }

    sealed class RepositoryTransport(int status) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Null(request.Headers.Authorization);
            Assert.True(request.RequestUri!.Host is "api.github.com" or "gitlab.com");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        }
    }
    [Theory]
    [InlineData("github",404)][InlineData("github",410)]
    [InlineData("gitlab",404)][InlineData("gitlab",410)]
    public async Task Actual_repository_provider_preserves_missing_status_for_retry_policy(string provider, int status)
    {
        using var transport = new RepositoryTransport(status); using var http = new HttpClient(transport);
        using var model = Manual(http, (game, token) => RepositoryReleaseNotes.FetchAsync(game, new AppSettings(), http, token));
        await model.OpenAsync(BrowseItem.FromList(new() { Name = "Synthetic missing project", Repository = "fixture/project", RepositorySource = provider }), CancellationToken.None);
        await model.LoadReleasesAsync(CancellationToken.None); Assert.False(model.CanRetryReleases);
        await model.LoadReleasesAsync(CancellationToken.None); Assert.Equal(1, transport.Calls);
    }

    static BrowseDetailsViewModel Manual(HttpClient http, Func<GameInfo, CancellationToken, Task<IReadOnlyList<QuiverCatalogRelease>>> releases) =>
        new(new QuiverCatalogClient(http, "https://catalog.test/api/v1"), (_, _) => throw new Exception("Not catalog"),
            (_, _) => Task.FromResult(new DocumentContent("Synthetic README")), releases);

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_details_retry_control_recovers_notes_with_controller_or_keyboard(bool keyboard)
    {
        using var http = new HttpClient(new Transport()); var calls = 0;
        using var model = Manual(http, (_, _) =>
        {
            calls++;
            if (calls == 1) throw new HttpRequestException("Synthetic offline failure");
            return Task.FromResult<IReadOnlyList<QuiverCatalogRelease>>([new() { Version = "recovered", Notes = "Recovered notes" }]);
        });
        await using var session = new LauncherSession();
        var host = new Host(); var view = new BrowseDetailsView();
        var browse = new BrowseViewModel(new QuiverCatalogClient(http, "https://catalog.test/api/v1"), () => [], () => "",
            _ => throw new Exception("No list load requested"));
        view.Configure(session, host, new MarkdownRenderer(_ => throw new Exception("No link requested")), model, _ => null, browse);
        var window = new Window { Content = view, Width = 1000, Height = 760 };
        try
        {
            window.Show();
            view.Open(BrowseItem.FromList(new() { Name = "Synthetic offline project", Repository = "fixture/project" }), releases: true);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var retry = view.FindControl<Button>("BrowseDetailsReleaseRetryButton")!;
            Assert.True(retry.IsVisible); Assert.True(retry.IsEnabled); Assert.Equal(1, calls);
            if (keyboard)
            {
                Assert.True(retry.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else
            {
                view.RestoreFocus();
                for (var i = 0; i < 8 && !retry.Classes.Contains("gamepad-focused"); i++) view.Navigate(Relicade.Services.NavigationDirection.Down);
                Assert.Contains("gamepad-focused", retry.Classes); Assert.True(view.Confirm());
            }
            Dispatcher.UIThread.RunJobs(); Assert.Equal(2, calls);
            Assert.Equal("recovered", Assert.Single(model.Releases!).Version); Assert.False(retry.IsVisible);
            Assert.Equal("Synthetic offline project", model.Entry!.Name);
        }
        finally { window.Close(); }
    }
    sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new() { ActiveZone = GamepadNavigationZone.BrowseDetailsOverlay };
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.BrowseDetailsOverlay;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) { Navigation.ActiveZone = transition.Zone; return true; }
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }

}
