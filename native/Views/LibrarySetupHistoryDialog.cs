using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class LibrarySetupHistoryDialog : Window
{
    readonly LibrarySetupHistoryViewModel model;
    readonly Action<LibrarySetupHistoryRow> show;
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    readonly TextBlock entryName = new() { TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeight.SemiBold };
    readonly TextBlock entryMessage = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    readonly TextBlock page = new() { FontSize = 14 };
    internal Button ContinueButton { get; } = new() { Content = "Continue inspection", MinHeight = 44 };
    internal Button RefreshButton { get; } = new() { Content = "Refresh", MinHeight = 44 };
    internal Button ShowButton { get; } = new() { Content = "Show in Library", MinHeight = 44 };
    internal Button PreviousButton { get; } = new() { Content = "Previous", MinHeight = 44 };
    internal Button NextButton { get; } = new() { Content = "Next", MinHeight = 44 };
    internal Button CloseButton { get; } = new() { Content = "Close", MinHeight = 44, IsCancel = true, IsDefault = true };
    int selected;
    bool closing, drained;
    readonly CancellationTokenRegistration shutdown;
    internal LibrarySetupHistoryDialog(LibrarySetupHistoryViewModel model, LauncherSession session, Action<LibrarySetupHistoryRow> show)
    {
        this.model = model; this.show = show;
        Styles.Add(new StyleInclude(new Uri("avares://Relicade/"))
        { Source = new Uri("avares://Relicade/Themes/LauncherStyles.axaml") });
        Title = "Saved setup history"; Width = 700; Height = 520; MinWidth = 580; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        var grid = new Grid { Margin = new Thickness(24), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16 };
        grid.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(status);
        var inspect = new WrapPanel { Orientation = Orientation.Horizontal };
        inspect.Children.Add(ContinueButton); inspect.Children.Add(RefreshButton); content.Children.Add(inspect);
        content.Children.Add(page); content.Children.Add(entryName); content.Children.Add(entryMessage);
        content.Children.Add(new TextBlock { Text = "Showing a game only navigates to its current library entry. Review its setup or recovery controls separately. No download, repair or launch starts here.", TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var footer = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { PreviousButton, NextButton, CloseButton, ShowButton })
        { button.Margin = new Thickness(4, 0); footer.Children.Add(button); }
        Grid.SetRow(footer, 2); grid.Children.Add(footer); Content = grid;
        foreach (var button in new[] { ContinueButton, RefreshButton, PreviousButton, NextButton, CloseButton, ShowButton })
        {
            button.Classes.Add("options");
            button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        }
        ContinueButton.Click += (_, _) => model.Continue(); RefreshButton.Click += (_, _) => { selected = 0; model.Refresh(); };
        PreviousButton.Click += (_, _) => { selected--; Render(); }; NextButton.Click += (_, _) => { selected++; Render(); };
        CloseButton.Click += async (_, _) => await CloseAfterDrain();
        ShowButton.Click += async (_, _) =>
        {
            if (closing || drained || selected < 0 || selected >= model.Rows.Count) return;
            var row = model.Rows[selected];
            if (!model.TryGetCurrent(row, out _)) { model.Refresh(); return; }
            await CloseAfterDrain();
            if (!session.IsClosed && model.TryGetCurrent(row, out _)) show(row);
        };
        model.PropertyChanged += Changed;
        Closing += async (_, e) => { if (drained) return; e.Cancel = true; await CloseAfterDrain(); };
        Closed += (_, _) => { model.PropertyChanged -= Changed; shutdown.Dispose(); };
        shutdown = session.Token.Register(() => Dispatcher.UIThread.Post(async () => await CloseAfterDrain()));
        GamepadModalDialogNavigation.Attach(this);
        Render();
    }
    void Changed(object? sender, PropertyChangedEventArgs e) => Render();
    void Render()
    {
        status.Text = model.Message;
        ContinueButton.IsVisible = model.HasMore; ContinueButton.IsEnabled = !model.IsBusy;
        RefreshButton.IsEnabled = !model.IsBusy;
        selected = Math.Clamp(selected, 0, Math.Max(0, model.Rows.Count - 1));
        var row = model.Rows.Count == 0 ? null : model.Rows[selected];
        entryName.Text = row?.Name ?? ""; entryMessage.Text = row?.Message ?? "";
        page.Text = row == null ? "" : $"Entry {selected + 1} of {model.Rows.Count} inspected entries needing attention";
        PreviousButton.IsEnabled = selected > 0; NextButton.IsEnabled = selected + 1 < model.Rows.Count;
        ShowButton.IsEnabled = row != null && !model.IsBusy;
        AutomationProperties.SetName(ShowButton, row == null ? "Show in Library" : "Show " + row.Name + " in Library; navigation only");
    }
    async Task CloseAfterDrain()
    {
        if (closing || drained) return; closing = true;
        try { await model.CancelAndDrain(); }
        finally { drained = true; Close(); }
    }
}
