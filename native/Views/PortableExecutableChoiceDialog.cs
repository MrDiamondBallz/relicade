using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Relicade.Services;

namespace Relicade.Views;

internal sealed class PortableExecutableChoiceDialog : Window
{
    internal ListBox ChoicesList { get; }
    internal Button ConfirmButton { get; } = new() { Content = "Install", MinHeight = 44, Width = 140, IsDefault = false };
    internal Button CancelButton { get; } = new() { Content = "Cancel", MinHeight = 44, Width = 100, IsDefault = true, IsCancel = true };
    internal string? SelectedRelativePath { get; private set; }
    string? choice;
    bool closed;

    internal PortableExecutableChoiceDialog(string appName, IReadOnlyList<string> candidates)
    {
        var snapshot = Array.AsReadOnly(candidates.ToArray());
        Title = "Choose launch file"; Width = 720; Height = 540; MinWidth = 480; MinHeight = 420;
        Background = new SolidColorBrush(Color.Parse("#14171C"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var selected = new TextBlock { Text = "No launch file selected.", TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
        ChoicesList = new ListBox
        {
            ItemsSource = snapshot, MinHeight = 64, Focusable = true,
            ItemTemplate = new FuncDataTemplate<string>((path, _) => new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), FontSize = 15 }),
        };
        AutomationProperties.SetName(ChoicesList, "Staged launch files");
        AutomationProperties.SetName(ConfirmButton, "Install using selected launch file");
        ConfirmButton.IsEnabled = false;
        ChoicesList.SelectionChanged += (_, _) =>
        {
            // Controller navigation clears the list cursor when moving to buttons.
            // Keep the explicit visible selection summary until another row is chosen.
            if (ChoicesList.SelectedItem is not string next || !snapshot.Contains(next, StringComparer.Ordinal)) return;
            choice = next; selected.Text = "Selected: " + next; ConfirmButton.IsEnabled = true;
        };
        ConfirmButton.Click += (_, _) =>
        {
            if (closed || choice == null || !snapshot.Contains(choice, StringComparer.Ordinal)) return;
            SelectedRelativePath = choice; Close();
        };
        CancelButton.Click += (_, _) => Close();
        Closed += (_, _) => closed = true;
        var header = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "CHOOSE LAUNCH FILE", FontSize = 25, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#FF7A1A")) },
            new TextBlock { Text = appName, FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = "This package contains several launch files. Choose the game you want Relicade to open. Nothing will run now.", TextWrapping = TextWrapping.Wrap }
        } };
        var selectionScroll = new ScrollViewer
        {
            Content = selected, MaxHeight = 64,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12, Children = { CancelButton, ConfirmButton } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 16, Margin = new Thickness(24) };
        grid.Children.Add(header); grid.Children.Add(ChoicesList); Grid.SetRow(ChoicesList, 1);
        grid.Children.Add(selectionScroll); Grid.SetRow(selectionScroll, 2); grid.Children.Add(footer); Grid.SetRow(footer, 3);
        Content = grid;
        GamepadModalDialogNavigation.Attach(this);
    }
}
