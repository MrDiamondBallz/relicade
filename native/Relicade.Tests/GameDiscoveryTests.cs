using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.Views;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Relicade.Models;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class GameDiscoveryTests
{
    [Fact]
    public void Offline_provenance_is_copied_to_the_runtime_output_and_matches_the_compiled_snapshot()
    {
        using var provenance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "CatalogData", "snapshot.json")));
        var root = provenance.RootElement;
        Assert.Equal(GameDiscoveryIndex.SnapshotSha256, root.GetProperty("jsonlSha256").GetString());
        Assert.Equal(10_000, root.GetProperty("recordCount").GetInt32());
        Assert.Equal("CC0-1.0", root.GetProperty("license").GetString());
        Assert.False(root.GetProperty("playabilityVerified").GetBoolean());
        Assert.Contains("CURSOR_CLAUSE", root.GetProperty("queryTemplate").GetString());
    }

    [AvaloniaTheory]
    [InlineData(1280, false)][InlineData(700, false)][InlineData(700, true)]
    public async Task Configured_catalog_controller_opens_offline_discovery_and_cancel_restores_focus(int width, bool shutdown)
    {
        var transport = new NoNetwork(); using var http = new HttpClient(transport);
        var model = new BrowseViewModel(new QuiverCatalogClient(http), () => Array.Empty<GameInfo>(), () => "",
            _ => throw new Exception("No custom-list load"));
        await using var session = new LauncherSession(); var host = new Host(); var view = new BrowseView();
        view.Configure(model, session, host, () => true);
        var window = new Window { Content = view, Width = width, Height = 760 };
        GameDiscoveryDialog? dialog = null;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var action = view.FindControl<Button>("AllGamesButton")!;
            Assert.Contains(action, view.Navigation.ToolbarControls());
            Assert.True(view.Navigation.SynchronizePointer(action));
            Assert.Equal(GamepadNavigationZone.BrowseToolbar, host.Navigation.ActiveZone);
            Assert.True(view.Navigation.Confirm());
            for (var i = 0; i < 100 && GamepadModalDialogNavigation.Instance.ActiveDialog is not GameDiscoveryDialog; i++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            dialog = Assert.IsType<GameDiscoveryDialog>(GamepadModalDialogNavigation.Instance.ActiveDialog);
            Assert.Equal(10_000, dialog.Page.Total); Assert.Equal(0, transport.Calls);
            if (shutdown) await session.DisposeAsync();
            else Assert.True(GamepadModalDialogNavigation.Instance.TryHandleCancel());
            for (var i = 0; i < 5; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.False(dialog.IsVisible);
            if (!shutdown) Assert.Same(action, window.FocusManager!.GetFocusedElement());
            Assert.Equal(0, transport.Calls);
        }
        finally { dialog?.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Source_open_is_single_flight_and_close_retains_the_pending_task_without_late_mutation()
    {
        var before = GamepadModalDialogNavigation.Instance.DialogStackCount;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var dialog = new GameDiscoveryDialog(GameDiscoveryIndex.Bundled, _ => throw new Exception("No project search"),
            _ => { calls++; return release.Task; });
        Assert.Equal(before, GamepadModalDialogNavigation.Instance.DialogStackCount); // Unshown windows do not register input ownership.
        try
        {
            dialog.Show(); dialog.SearchBox.Text = "Q100000040"; dialog.Results.SelectedIndex = 0;
            dialog.SourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.SourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls); Assert.False(dialog.SourceWork.IsCompleted);
            var detail = dialog.Detail.Text; dialog.Close();
            release.SetException(new IOException("Synthetic browser handoff failure")); await dialog.SourceWork;
            Assert.Equal(detail, dialog.Detail.Text); Assert.Equal(before, GamepadModalDialogNavigation.Instance.DialogStackCount);
        }
        finally { release.TrySetResult(); dialog.Close(); await dialog.SourceWork; }
    }

    [AvaloniaFact]
    public async Task Failure_of_an_old_source_open_does_not_change_the_newly_selected_game_details()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var index = new GameDiscoveryIndex([new("Q1", "First game"), new("Q2", "Second game")]);
        var dialog = new GameDiscoveryDialog(index, _ => throw new Exception("No project search"), _ => release.Task);
        try
        {
            dialog.Show(); dialog.SearchBox.Text = "Q1"; Dispatcher.UIThread.RunJobs();
            dialog.Results.SelectedItem = Assert.Single(dialog.Page.Items); Dispatcher.UIThread.RunJobs();
            Assert.Contains("/Q1", dialog.Detail.Text);
            dialog.SourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.SearchBox.Text = "Q2"; Dispatcher.UIThread.RunJobs();
            dialog.Results.SelectedItem = Assert.Single(dialog.Page.Items); Dispatcher.UIThread.RunJobs();
            var expected = dialog.Detail.Text; Assert.Contains("/Q2", expected);
            release.SetException(new IOException("Synthetic browser failure for Q1")); await dialog.SourceWork;
            Assert.Equal(expected, dialog.Detail.Text);
        }
        finally { release.TrySetResult(); dialog.Close(); await dialog.SourceWork; }
    }

    sealed class NoNetwork : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new Exception("Offline game discovery must not request metadata or payloads."); }
    }
    sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new() { ActiveZone = GamepadNavigationZone.BrowseToolbar };
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.BrowseGrid;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) { Navigation.ActiveZone = transition.Zone; return true; }
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }

    [Fact]
    public void Bundled_snapshot_contains_ten_thousand_pinned_real_source_identities_and_bounded_pages()
    {
        var index = GameDiscoveryIndex.Bundled; Assert.Equal(10_000, index.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < index.Count; offset += GameDiscoveryIndex.PageSize)
        {
            var page = index.Search("", offset);
            Assert.Equal(10_000, page.Total); Assert.Equal(50, page.Items.Count);
            foreach (var row in page.Items) { Assert.True(ids.Add(row.Id)); Assert.False(string.IsNullOrWhiteSpace(row.Title)); }
        }
        var sourceOrder = string.Join('\n', ids.Order(StringComparer.Ordinal)) + "\n";
        Assert.Equal("a71671a47ad6bdcf839c251e1399c89135c9cc3361993827ba360112b8b9edeb",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceOrder))));
        var pikmin = Assert.Single(index.Search("Q100000040").Items);
        Assert.Equal("Pikmin 3 Deluxe", pikmin.Title);
        Assert.Equal("https://www.wikidata.org/wiki/Q100000040", pikmin.SourceUrl);
        Assert.Contains("not verified", GameWork.Readiness);
    }

    [Fact]
    public void Search_keeps_same_title_editions_distinct_and_does_not_infer_a_play_path()
    {
        var index = new GameDiscoveryIndex([new("Q1", "Same Title"), new("Q2", "Same Title"), new("Q3", "Different Game")]);
        Assert.Equal(new[] { "Q1", "Q2" }, index.Search("SAME title").Items.Select(row => row.Id));
        Assert.Empty(index.Search("not present").Items);
        Assert.Throws<InvalidDataException>(() => new GameDiscoveryIndex([new("Q1", "A"), new("Q1", "B")]));
        Assert.Throws<InvalidDataException>(() => new GameDiscoveryIndex([new("../../bad", "A")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search("", -1));
        Assert.Throws<ArgumentException>(() => index.Search(new string('x', 129)));
    }

    [Fact]
    public void Synthetic_hundred_thousand_record_workload_keeps_search_results_paged()
    {
        // Synthetic scale check, never counted as real catalog records or a timing claim.
        var index = new GameDiscoveryIndex(Enumerable.Range(1, 100_000).Select(i => new GameWork("Q" + i, "Synthetic game " + i)));
        var page = index.Search("synthetic", 99_950);
        Assert.Equal(100_000, page.Total); Assert.Equal(50, page.Items.Count); Assert.False(page.HasNext); Assert.True(page.HasPrevious);
    }

    [AvaloniaFact]
    public async Task Native_dialog_search_pages_source_and_project_search_are_explicit_and_do_not_install()
    {
        GameWork? requested = null; string? source = null;
        var dialog = new GameDiscoveryDialog(GameDiscoveryIndex.Bundled, work => requested = work,
            url => { source = url; return Task.CompletedTask; });
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            Assert.Equal(50, dialog.Results.ItemCount); Assert.Null(requested); Assert.Null(source);
            dialog.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(50, dialog.Page.Offset);
            dialog.SearchBox.Text = "Pikmin 3 Deluxe"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, dialog.Page.Offset); Assert.Equal("Q100000040", Assert.Single(dialog.Page.Items).Id);
            dialog.Results.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.Contains("not verified", dialog.Detail.Text); Assert.Contains("Project search uses this title", dialog.Detail.Text);
            dialog.SourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.SourceWork;
            Assert.Equal("https://www.wikidata.org/wiki/Q100000040", source); Assert.Null(requested);
            dialog.FindProjectsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Q100000040", requested?.Id); Assert.False(dialog.IsVisible);
        }
        finally { dialog.Close(); }
    }
    [AvaloniaFact]
    public void Source_catalog_exposes_game_discovery_without_a_custom_list_or_network()
    {
        var view = new BrowseView();
        var window = new Window { Content = view, Width = 1000, Height = 760 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var action = view.FindControl<Button>("AllGamesButton");
            Assert.NotNull(action);
            Assert.True(action.IsEffectivelyVisible);
            Assert.True(action.Bounds.Height >= 44);
            Assert.Contains("games", action.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }
}
