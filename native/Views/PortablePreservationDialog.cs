using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class PortablePreservationDialog : Window
{
    internal PortablePreservationViewModel Model { get; }
    internal Button CloseButton { get; } = new() { Content = "Close", IsDefault = true, IsCancel = true, MinHeight = 44 };
    internal Button RefreshButton { get; } = new() { Content = "Refresh inspection", MinHeight = 44 };
    internal Button PreviousButton { get; } = new() { Content = "Previous", MinHeight = 44 };
    internal Button NextButton { get; } = new() { Content = "Next", MinHeight = 44 };
    internal StackPanel Details { get; } = new() { Spacing = 6 };
    internal Task Work => Model.Work;
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock page = new() { TextWrapping = TextWrapping.Wrap };
    string[] pages = []; int position; PortablePreservationAssessment? rendered;
    internal PortablePreservationDialog(PortablePreservationViewModel model)
    {
        Model = model; Title = "Preservation inspection"; Width = 700; Height = 520; MinWidth = 580; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        Styles.Add(new StyleInclude(new Uri("avares://Relicade/")) { Source = new Uri("avares://Relicade/Themes/LauncherStyles.axaml") });
        AutomationProperties.SetName(this, Title);
        var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = Title + " — nothing is removed", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        var content = new StackPanel { Spacing = 10, Children = { status, page, Details } };
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Focusable = true };
        AutomationProperties.SetName(scroll, "Observed preservation paths, bytes and uncertainty"); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { CloseButton, RefreshButton, PreviousButton, NextButton } };
        foreach (var button in buttons.Children.OfType<Button>())
        { button.Classes.Add("options"); button.Margin = new Thickness(0, 0, 8, 8); button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true); }
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons); Content = grid;
        CloseButton.Click += (_, _) => Close();
        RefreshButton.Click += (_, _) => { if (Model.CanRefresh) _ = Model.RefreshAsync(); };
        PreviousButton.Click += (_, _) => { if (position > 0) { position--; RenderPage(); } };
        NextButton.Click += (_, _) => { if (position + 1 < pages.Length) { position++; RenderPage(); } };
        Model.PropertyChanged += Changed;
        Closed += (_, _) => { Model.Close(); Model.PropertyChanged -= Changed; };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (IsVisible && !Model.IsClosed) { GamepadModalDialogNavigation.Instance.FocusDialogControl(CloseButton); CloseButton.Focus(); } }, DispatcherPriority.Loaded);
        GamepadModalDialogNavigation.Attach(this); Render();
    }
    internal Task BeginInspection() => Model.RefreshAsync();
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (!Model.IsClosed) Render(); }
    void Render()
    {
        status.Text = Model.Message; RefreshButton.IsEnabled = Model.CanRefresh;
        if (pages.Length == 0 || !ReferenceEquals(rendered, Model.Assessment))
        {
            rendered = Model.Assessment; position = 0;
            var text = new List<string> { "Managed uninstall remains unavailable. All files stay in place. This is not a deletion plan or reclaimable-space estimate." };
            if (rendered is { } p)
            {
                text.Add($"Observed at {p.ObservedAt:O}. External writers can change files after observation. Refresh explicitly to inspect again.");
                text.Add($"Installation: {p.Root}\nHistory: {p.HistoryRoot}\nCurrent version: {p.Version}\nCommitted operation: {p.OperationId}");
                foreach (var group in p.Rows.GroupBy(r => r.Classification))
                    text.Add($"{group.Key}: {group.Count()} observed files, {group.Where(r => r.ObservedBytes.HasValue).Sum(r => r.ObservedBytes!.Value)} observed bytes. Uncertain/unobserved sizes are excluded.");
                text.AddRange(p.Warnings);
                text.AddRange(p.Rows.Select(r => $"{r.Area}/{r.Path}\n{r.Classification}\nObserved bytes: {r.ObservedBytes?.ToString() ?? "unknown"}\nSHA256: {r.Sha256 ?? "not verified"}\n{r.Note}"));
            }
            // Bounded one-message pages keep long paths reachable without thousands
            // of realized controls. Every character is retained by inspection chunks.
            pages = text.ToArray(); RenderPage();
        }
    }
    void RenderPage()
    {
        Details.Children.Clear(); page.Text = pages.Length == 0 ? "No observation yet" : $"Inspection page {position + 1} of {pages.Length}";
        if (pages.Length != 0)
        {
            PortableRollbackDialog.AddInspection(Details, pages[position]);
            foreach (var button in Details.Children.OfType<Button>()) button.Classes.Add("options");
        }
        PreviousButton.IsEnabled = position > 0; NextButton.IsEnabled = position + 1 < pages.Length;
        GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
    }
}
