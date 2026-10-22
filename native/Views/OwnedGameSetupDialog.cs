using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class OwnedGameSetupDialog : Window
{
    internal OwnedGameSetupViewModel Model { get; }
    internal Button ChooseButton { get; } = new() { Content = "Choose my game file…", MinHeight = 44 };
    internal Button ConfirmButton { get; } = new() { Content = "Confirm lawful use and copy", MinHeight = 44 };
    internal Button PlayButton { get; } = new() { Content = "Play Freedoom Phase 2", MinHeight = 44 };
    internal Button DownloadButton { get; } = new() { Content = "Official download", MinHeight = 44 };
    internal Button WebsiteButton { get; } = new() { Content = new TextBlock { Text = "Open official Freedoom release page", TextWrapping = TextWrapping.Wrap }, MinHeight = 44 };
    internal Button CloseButton { get; } = new() { Content = "Cancel", IsCancel = true, IsDefault = true, MinHeight = 44 };
    internal Task Work { get; private set; } = Task.CompletedTask;
    readonly StackPanel details = new() { Spacing = 8 };
    internal OwnedGameSetupDialog(OwnedGameSetupViewModel model)
    {
        Model = model; Title = model.Title; Background = new SolidColorBrush(Color.Parse("#14171C"));
        Width = 760; Height = 660; MinWidth = 320; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true); AutomationProperties.SetName(this, Title);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        layout.Children.Add(new TextBlock { Text = Title, FontSize = 24, Foreground = new SolidColorBrush(Color.Parse("#FF8A32")) });
        var scroll = new ScrollViewer { Content = details, Focusable = true, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(scroll, "Game setup requirements and local copy preview"); Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { CloseButton, DownloadButton, ChooseButton, ConfirmButton, PlayButton } };
        foreach (var button in buttons.Children.OfType<Button>())
        { button.Margin = new Thickness(0, 0, 8, 8); button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true); }
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons); Content = layout;
        model.PickFile = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose your lawful game-data file", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(model.FileTypeLabel) { Patterns = [model.FilePattern] }, FilePickerFileTypes.All] });
            return files.FirstOrDefault()?.TryGetLocalPath();
        };
        ChooseButton.Click += (_, _) => { if (model.CanChoose) Work = model.ChooseAsync(); };
        ConfirmButton.Click += (_, _) => { if (model.CanConfirm) Work = model.ConfirmAsync(); };
        PlayButton.Click += (_, _) => { if (model.CanPlay) Work = model.PlayAsync(); };
        model.OpenDownload = () => Launcher.LaunchUriAsync(new Uri(OwnedGameSetupViewModel.OfficialFreedoomRelease));
        DownloadButton.Click += (_, _) => { if (model.CanChoose) Work = model.HasFreeDataDownload ? model.DownloadAndPreviewAsync() : model.OpenDownloadAsync(); };
        WebsiteButton.Click += (_, _) => { if (model.CanChoose) Work = model.OpenDownloadAsync(); };
        WebsiteButton.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        WebsiteButton.GotFocus += (_, _) => WebsiteButton.BringIntoView();
        CloseButton.Click += (_, _) => Close();
        model.PropertyChanged += Changed;
        Closed += (_, _) => { model.Close(); model.PropertyChanged -= Changed; };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (IsVisible && !model.IsClosed) { GamepadModalDialogNavigation.Instance.FocusDialogControl(CloseButton); CloseButton.Focus(); } }, DispatcherPriority.Loaded);
        GamepadModalDialogNavigation.Attach(this); Render();
    }
    void AddInspection(string text)
    {
        // Keep readable, bounded focus targets for keyboard/controller inspection.
        // Split at words where possible, never dropping whitespace or characters.
        while (text.Length > 0)
        {
            var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
            var end = boundaries.Length > 140 ? boundaries[140] : text.Length;
            if (end < text.Length)
            {
                var space = text.LastIndexOf(' ', end - 1, end);
                if (space > end / 2) end = space + 1;
            }
            var part = text[..end]; text = text[end..];
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent, Padding = new Thickness(8), MinHeight = 40,
                Content = new TextBlock { Text = part, FontSize = 15, TextWrapping = TextWrapping.Wrap }
            };
            button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
            AutomationProperties.SetName(button, "Inspect " + part);
            button.GotFocus += (_, _) => button.BringIntoView();
            details.Children.Add(button);
        }
    }
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (!Model.IsClosed) Render(); }
    void Render()
    {
        details.Children.Clear();
        foreach (var text in new[] { Model.Requirement, Model.Message, Model.DataPolicy }.SelectMany(text => text.Split('\n')))
            AddInspection(text);
        if (Model.HasFreeDataDownload && Model.State != OwnedGameSetupUiState.Completed)
        { WebsiteButton.IsEnabled = Model.CanChoose; details.Children.Add(WebsiteButton); }
        if (Model.Plan is { } plan)
            foreach (var text in new[] { "Source: " + plan.Source, "Destination: " + plan.Destination,
                "Additional space needed: " + plan.BytesToCopy.ToString("N0") + " bytes, plus a small local receipt.",
                plan.ReuseExisting ? "An identical retained copy exists; confirmation adopts it for this generation without overwriting it." : "The selected file will be copied, not moved or linked.",
                "Local copy SHA256: " + plan.SourceSha256 }) AddInspection(text);
        PlayButton.IsVisible = Model.IsFreedoom && Model.State == OwnedGameSetupUiState.Completed; PlayButton.IsEnabled = Model.CanPlay;
        DownloadButton.Content = Model.HasFreeDataDownload ? "Get free game data" : "Official download";
        DownloadButton.IsVisible = Model.IsFreedoom && Model.State != OwnedGameSetupUiState.Completed; DownloadButton.IsEnabled = Model.CanChoose;
        ChooseButton.IsVisible = !Model.IsFreedoom || Model.State != OwnedGameSetupUiState.Completed;
        ChooseButton.IsEnabled = Model.CanChoose; ConfirmButton.IsEnabled = Model.CanConfirm;
        ConfirmButton.IsVisible = Model.State == OwnedGameSetupUiState.Confirm;
        CloseButton.Content = Model.State == OwnedGameSetupUiState.Completed ? "Close" : "Cancel";
        GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
    }
}
