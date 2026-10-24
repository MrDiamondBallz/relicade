using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Services;

namespace Relicade.Views;

// A bounded offline original-game index, separate from downloadable project cards.
internal sealed class GameDiscoveryDialog : Window
{
    internal TextBox SearchBox { get; } = new() { PlaceholderText = "Search game titles or a Wikidata ID…", MaxLength = 128, MinHeight = 44 };
    internal ListBox Results { get; } = new() { Focusable = true, MinHeight = 70 };
    internal Button PreviousButton { get; } = new() { Content = "Previous", MinHeight = 44 };
    internal Button NextButton { get; } = new() { Content = "Next", MinHeight = 44 };
    internal Button FindProjectsButton { get; } = new() { Content = "Find community projects", MinHeight = 44, IsDefault = true };
    internal Button SourceButton { get; } = new() { Content = "Wikidata source", MinHeight = 44 };
    internal Button CloseButton { get; } = new() { Content = "Close", IsCancel = true, MinHeight = 44 };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBlock Detail { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    internal ScrollViewer SummaryScroll { get; }
    readonly GameDiscoveryIndex index;
    readonly Action<GameWork> findProjects;
    readonly Func<string, Task> openSource;
    GameWork? selected;
    int offset;
    bool closed, sourceBusy;
    internal GameDiscoveryPage Page { get; private set; } = null!;
    internal Task SourceWork { get; private set; } = Task.CompletedTask;
    internal GameDiscoveryDialog(GameDiscoveryIndex index, Action<GameWork> findProjects, Func<string, Task>? openSource = null)
    {
        this.index = index; this.findProjects = findProjects; this.openSource = openSource ?? UrlLauncher.OpenAsync;
        Title = "All games · original-game discovery"; Width = 820; Height = 680; MinWidth = 540; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        Styles.Add(new StyleInclude(new Uri("avares://Relicade/")) { Source = new Uri("avares://Relicade/Themes/LauncherStyles.axaml") });
        this.Bind(BackgroundProperty, this.GetResourceObservable("ThemeBase"));
        this.Bind(ForegroundProperty, this.GetResourceObservable("ThemeText"));
        AutomationProperties.SetName(SearchBox, "Search the offline original-game catalog");
        AutomationProperties.SetName(Results, "Original-game records; metadata, not verified play paths");
        var header = new StackPanel { Spacing = 6, Children =
        {
            new TextBlock { Text = "More games to rediscover.", FontSize = 25, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"{index.Count:N0} game records · offline snapshot · Wikidata CC0", FontSize = 12, TextWrapping = TextWrapping.Wrap }
        } };
        Results.ItemTemplate = new FuncDataTemplate<GameWork>((work, _) => new StackPanel { Margin = new Thickness(8, 5), Spacing = 3, Children =
        {
            new TextBlock { Text = work?.Title, FontSize = 16, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = work?.Id + " · " + GameWork.Readiness, FontSize = 11, TextWrapping = TextWrapping.Wrap }
        } });
        var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 10 };
        grid.Children.Add(header); grid.Children.Add(SearchBox); Grid.SetRow(SearchBox, 1);
        var body = new Grid { RowDefinitions = new("3*,2*"), RowSpacing = 8 };
        body.Children.Add(Results);
        var summary = new StackPanel { Spacing = 6, Children = { Status, Detail } };
        SummaryScroll = new ScrollViewer { Content = summary, Focusable = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        AutomationProperties.SetName(SummaryScroll, "Selected game source and readiness; scroll for details");
        body.Children.Add(SummaryScroll); Grid.SetRow(SummaryScroll, 1);
        grid.Children.Add(body); Grid.SetRow(body, 2);
        var buttons = new WrapPanel { Children = { CloseButton, PreviousButton, NextButton, SourceButton, FindProjectsButton } };
        foreach (var button in buttons.Children.OfType<Button>())
        {
            button.Classes.Add("options"); button.Margin = new Thickness(0, 0, 8, 8);
            button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        }
        grid.Children.Add(buttons); Grid.SetRow(buttons, 3); Content = grid;
        SearchBox.TextChanged += (_, _) => { offset = 0; Refresh(); };
        Results.SelectionChanged += (_, _) =>
        {
            // Modal controller navigation clears the row cursor on button focus.
            // Keep the visibly summarized source identity until another row is chosen.
            if (Results.SelectedItem is not GameWork work || !Page.Items.Contains(work)) return;
            selected = work; Detail.Text = $"{GameWork.Readiness}.\nSource: {work.SourceUrl}\nProject search uses this title; matches are not verified links.";
            FindProjectsButton.IsEnabled = true; SourceButton.IsEnabled = !sourceBusy;
        };
        PreviousButton.Click += (_, _) => { if (Page.HasPrevious) { offset = Math.Max(0, offset - GameDiscoveryIndex.PageSize); Refresh(); } };
        NextButton.Click += (_, _) => { if (Page.HasNext) { offset += GameDiscoveryIndex.PageSize; Refresh(); } };
        FindProjectsButton.Click += (_, _) =>
        {
            if (closed || selected == null) return;
            var work = selected; Close(); findProjects(work); // A search, never an install or inferred mapping.
        };
        SourceButton.Click += (_, _) => { if (!closed && !sourceBusy && selected != null) SourceWork = OpenSourceAsync(selected.Id, selected.SourceUrl); };
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => closed = true;
        Opened += (_, _) => { GamepadModalDialogNavigation.Attach(this); SearchBox.Focus(); };
        Refresh();
    }
    async Task OpenSourceAsync(string id, string url)
    {
        sourceBusy = true; SourceButton.IsEnabled = false;
        try { await openSource(url); }
        catch (Exception) { if (!closed && selected?.Id == id) Detail.Text += "\nThe browser could not open. The source address above remains available."; }
        finally { sourceBusy = false; if (!closed) SourceButton.IsEnabled = selected != null; }
    }
    void Refresh()
    {
        if (closed) return;
        Page = index.Search(SearchBox.Text ?? "", offset);
        selected = null; Results.ItemsSource = Page.Items;
        Detail.Text = "A starting collection, not every game. Editions may be separate records. Choose a record to inspect its source or search community projects. No download starts here.";
        SourceButton.IsEnabled = FindProjectsButton.IsEnabled = false;
        PreviousButton.IsEnabled = Page.HasPrevious; NextButton.IsEnabled = Page.HasNext;
        Status.Text = Page.Total == 0 ? "No matching records in this snapshot. Try another title; this collection is incomplete."
            : $"{Page.Offset + 1:N0}–{Page.Offset + Page.Items.Count:N0} of {Page.Total:N0} matching records · 50 per page";
        GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
    }
}
