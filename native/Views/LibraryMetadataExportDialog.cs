using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Styling;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class LibraryMetadataExportDialog : Window
{
    internal LibraryMetadataExportViewModel Model { get; }
    internal Button CancelButton { get; } = new() { Content = "Close", IsCancel = true, IsDefault = true, MinHeight = 44 };
    internal Button SaveButton { get; } = new() { Content = "Choose new file…", MinHeight = 44 };
    internal Button PreviousButton { get; } = new() { Content = "Previous", MinHeight = 44 };
    internal Button NextButton { get; } = new() { Content = "Next", MinHeight = 44 };
    internal StackPanel Details { get; } = new() { Spacing = 8 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock page = new() { TextWrapping = TextWrapping.Wrap };
    int position;
    internal LibraryMetadataExportDialog(LibraryMetadataExportViewModel model)
    {
        Model = model; Title = "Export loaded library metadata"; Width = 700; Height = 540; MinWidth = 580; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        Styles.Add(new StyleInclude(new Uri("avares://Relicade/")) { Source = new Uri("avares://Relicade/Themes/LauncherStyles.axaml") });
        Styles.Add(new Style(selector => selector.OfType<Button>().Class("metadata-inspection").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(ContentPresenter.HorizontalContentAlignmentProperty, HorizontalAlignment.Left) }
        });
        AutomationProperties.SetName(this, Title + "; export only, not backup or restore");
        var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = "Metadata preview — not a backup", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        var scroll = new ScrollViewer { Content = new StackPanel { Spacing = 10, Children = { status, page, Details } }, Focusable = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        AutomationProperties.SetName(scroll, "Exact exported metadata and exclusions"); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new WrapPanel { Children = { CancelButton, PreviousButton, NextButton, SaveButton } };
        foreach (var button in buttons.Children.OfType<Button>())
        { button.Classes.Add("options"); button.Margin = new Thickness(0, 0, 8, 8); button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true); }
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons); Content = grid;
        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += (_, _) => { if (Model.CanSave) _ = Model.SaveAsync(); };
        PreviousButton.Click += (_, _) => { if (position > 0) { position--; Render(); } };
        NextButton.Click += (_, _) => { if (position + 1 < Model.Pages.Count) { position++; Render(); } };
        Model.PropertyChanged += Changed;
        Closed += (_, _) => { Model.Close(); Model.PropertyChanged -= Changed; };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (IsVisible && !Model.IsClosed) { GamepadModalDialogNavigation.Instance.FocusDialogControl(CancelButton); CancelButton.Focus(); } }, DispatcherPriority.Loaded);
        GamepadModalDialogNavigation.Attach(this); Render();
    }
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (!Model.IsClosed) Render(); }
    void Render()
    {
        status.Text = Model.Message; SaveButton.IsEnabled = Model.CanSave;
        page.Text = $"Preview page {position + 1} of {Model.Pages.Count}";
        Details.Children.Clear(); AddMetadataInspection(Model.Pages[position]);
        PreviousButton.IsEnabled = position > 0; NextButton.IsEnabled = position + 1 < Model.Pages.Count;
        GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
    }
    internal static IEnumerable<string> InspectionChunks(string text)
    {
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        for (var start = 0; start < boundaries.Length;)
        {
            var end = Math.Min(start + 64, boundaries.Length);
            if (end < boundaries.Length && !char.IsWhiteSpace(text, boundaries[end]) && !char.IsWhiteSpace(text, boundaries[end - 1]))
            {
                // Prefer a complete word, but keep oversized tokens bounded and grapheme-safe.
                for (var split = end - 1; split > start; split--)
                    if (char.IsWhiteSpace(text, boundaries[split])) { end = split + 1; break; }
            }
            yield return text[boundaries[start]..(end < boundaries.Length ? boundaries[end] : text.Length)];
            start = end;
        }
    }
    void AddMetadataInspection(string text)
    {
        foreach (var part in InspectionChunks(text))
        {
            var body = new TextBlock { Text = part, FontSize = 14, FontWeight = FontWeight.Normal, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap };
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = body };
            button.Classes.Add("options"); button.Classes.Add("metadata-inspection");
            button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
            AutomationProperties.SetName(button, "Inspect " + part);
            button.GotFocus += (_, _) => button.BringIntoView();
            Details.Children.Add(button);
        }
    }

}
