using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Core.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
namespace Relicade.Tests;
public sealed class ProjectSetupGuidanceTests
{
    [AvaloniaTheory]
    [InlineData("troyedwardsjr/z2rs", "Zelda II USA ROM")]
    [InlineData("perfect-dark-pc-port/perfect_dark", "Perfect Dark US 1.1")]
    [InlineData("taisei-project/taisei", "No original-game ROM")]
    [InlineData("fixture/unknown", "Requirements not reviewed")]
    public async Task Details_explain_game_files_before_adding_or_downloading(string repository, string expected)
    {
        await using var fixture = new Fixture();
        using var app = new GameInfo { Name = "Synthetic project", FolderName = "Fixture", Repository = repository,
            RepositorySource = "github", CatalogEntryId = repository == "taisei-project/taisei" ? "k177mxt12q61yv9fsvf6wvh1pn8f4n2n" : null };
        fixture.Open(app);
        Assert.Same(app, fixture.Details.Model.Entry);
        Assert.True(fixture.Details.BrowseDetailsAddButton.IsVisible);
        Assert.False(app.IsInstalled); Assert.Empty(fixture.Manager.LibraryApps);
        var visible = string.Join("\n", fixture.Details.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        Assert.Contains(expected, visible, StringComparison.Ordinal);
        Assert.Empty(fixture.Manager.LibraryApps);
    }

    [Theory]
    [InlineData("gitlab", "taisei-project/taisei")]
    [InlineData("unsupported-provider", "taisei-project/taisei")]
    [InlineData("github", "other/taisei")]
    [InlineData("github", "troyedwardsjr/z2rs-unknown")]
    public void Unreviewed_sources_cannot_borrow_project_guidance(string source, string repository)
    {
        using var app = new GameInfo { RepositorySource = source, Repository = repository };
        Assert.Equal("Requirements not reviewed", ProjectSetupGuidance.For(app).Heading);
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Setup_guide_is_an_explicit_keyboard_or_controller_action_without_installation(bool keyboard)
    {
        await using var fixture = new Fixture();
        using var app = new GameInfo { Name = "Synthetic z2rs", FolderName = "Fixture", Repository = "troyedwardsjr/z2rs", RepositorySource = "github" };
        fixture.Open(app);
        var guide = fixture.Details.FindControl<Button>("BrowseDetailsSetupGuideButton")!;
        var urls = new List<string>(); fixture.Details.OpenUrlRequested += urls.Add;
        Assert.True(guide.IsVisible); Assert.True(guide.IsEnabled);
        fixture.Details.Confirm(); // First confirm enters the primary action row.
        for (var i = 0; i < 8 && !guide.Classes.Contains("gamepad-focused"); i++) fixture.Details.Navigate(NavigationDirection.Right);
        Assert.Contains("gamepad-focused", guide.Classes);
        Assert.Same(guide, fixture.Window.FocusManager!.GetFocusedElement());
        if (keyboard)
        {
            fixture.Window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            fixture.Window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        }
        else fixture.Details.Confirm();
        Assert.Equal(ProjectSetupGuidance.GuideUrl, Assert.Single(urls));
        Assert.Empty(fixture.Manager.LibraryApps); Assert.False(app.IsInstalled);
    }

    [AvaloniaFact]
    public async Task Switching_project_replaces_advice_without_borrowing_previous_requirements()
    {
        await using var fixture = new Fixture();
        using var owned = new GameInfo { Name = "Owned data", Repository = "troyedwardsjr/z2rs", FolderName = "One" };
        using var unknown = new GameInfo { Name = "Unknown", Repository = "fixture/unknown", FolderName = "Two" };
        fixture.Open(owned);
        Assert.Contains("Zelda II USA ROM", fixture.Details.FindControl<TextBlock>("BrowseDetailsSetupDescription")!.Text);
        fixture.Open(unknown);
        Assert.Equal("Requirements not reviewed", fixture.Details.FindControl<TextBlock>("BrowseDetailsSetupHeading")!.Text);
        Assert.DoesNotContain("Zelda II", fixture.Details.FindControl<TextBlock>("BrowseDetailsSetupDescription")!.Text);
        Assert.Empty(fixture.Manager.LibraryApps);
        var guide = fixture.Details.FindControl<Button>("BrowseDetailsSetupGuideButton")!;
        Assert.False(guide.IsVisible);
        var urls = new List<string>(); fixture.Details.OpenUrlRequested += urls.Add;
        guide.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(urls);
    }

    [Theory]
    [InlineData("fgsfdsfgs/perfect_dark", "Original game files required")]
    [InlineData("UZDoom/UZDoom", "Choose your game data")]
    [InlineData("TROYEDWARDSJR/Z2RS", "Original game files required")]
    public void Recognized_repository_aliases_keep_their_distinct_advice(string repository, string heading)
    {
        using var game = new GameInfo { Repository = repository, RepositorySource = "github" };
        Assert.Equal(heading, ProjectSetupGuidance.For(game).Heading);
        Assert.True(ProjectSetupGuidance.For(game).HasSpecificGuide);
        Assert.False(game.IsInstalled);
    }

    sealed class Fixture : IAsyncDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "details-library-" + Guid.NewGuid().ToString("N"));
        readonly string? previous = RelicadePaths.OverrideUserDataRoot;
        readonly HttpClient http;
        internal readonly Offline Network = new();
        internal readonly Store Settings = new();
        internal readonly GameManager Manager;
        internal readonly MainView View;
        internal readonly Window Window;
        internal readonly BrowseDetailsView Details;
        internal Fixture()
        {
            Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
            var store = Settings; store.Current.AppsPath = Path.Combine(root, "Apps");
            http = new HttpClient(Network);
            Manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root)) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
            View = new MainView(new() { SettingsStore = store, GameManager = Manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
            Window = new Window { Content = View, Width = 1280, Height = 860 };
            Details = View.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
            Window.Show(); Dispatcher.UIThread.RunJobs();
        }
        internal Task Add(GameInfo app) => (Task)typeof(MainView).GetMethod("AddFromBrowseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(View, [app, "fixture", null])!;
        internal void Open(GameInfo app)
        {
            View.Shell.Mode = MainViewMode.Browse;
            typeof(MainView).GetMethod("OpenBrowseDetails", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(BrowseItem), typeof(bool)])!.Invoke(View, [BrowseItem.FromList(app), false]);
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        }
        public async ValueTask DisposeAsync()
        {
            Window.Close(); await View.ShutdownAsync(); Manager.Dispose(); http.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false); RelicadePaths.OverrideUserDataRoot = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        internal int Saves;
        public void Save(AppSettings settings) { Saves++; }
    }
    sealed class Offline : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("Unexpected network in local details navigation fixture."); }
    }
}
