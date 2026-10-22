using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class FreedoomEngineProgressDialog : Window
{
    internal FreedoomEngineProgressViewModel Model { get; }
    internal Button InstallButton { get; } = new() { Content = "Install engine", MinHeight = 44 };
    internal Button CancelButton { get; } = new() { Content = "Cancel", IsCancel = true, IsDefault = true, MinHeight = 44 };
    internal TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    internal ProgressBar TransferProgress { get; } = new() { Minimum = 0, Maximum = 100, Height = 12 };
    internal Button PreviousDetailsButton { get; } = new() { Content = "Previous", MinHeight = 44, IsVisible = false };
    internal Button NextDetailsButton { get; } = new() { Content = "Next", MinHeight = 44, IsVisible = false };
    internal TextBlock DiagnosticText { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    readonly TextBlock diagnosticPage = new() { FontSize = 14 };
    readonly TextBlock introduction = new() { Text = FreedoomEngineInstallService.ConfirmationMessage(), FontSize = 16, TextWrapping = TextWrapping.Wrap };
    readonly List<string> diagnosticPages = new();
    int pageIndex;
    string? displayedDiagnostic;

    internal FreedoomEngineProgressDialog(FreedoomEngineProgressViewModel model)
    {
        Model = model; Title = "Set up Freedoom Phase 2";
        Width = 700; Height = 510; MinWidth = 580; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 16 };
        layout.Children.Add(new TextBlock { Text = Title, FontSize = 23, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var details = new StackPanel { Spacing = 16 };
        details.Children.Add(introduction);
        details.Children.Add(StatusText); details.Children.Add(diagnosticPage); details.Children.Add(DiagnosticText); details.Children.Add(TransferProgress);
        details.Children.Add(new TextBlock { Text = "Cancel affects this setup only. The window waits for its active work to stop; a committed engine and recovery history are retained.", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        var scroll = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12 };
        buttons.Children.Add(PreviousDetailsButton); buttons.Children.Add(NextDetailsButton);
        buttons.Children.Add(CancelButton); buttons.Children.Add(InstallButton);
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons); Content = layout;
        AutomationProperties.SetName(TransferProgress, "Engine download progress; verification and installation must also finish");
        InstallButton.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        CancelButton.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        PreviousDetailsButton.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        NextDetailsButton.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        PreviousDetailsButton.Click += (_, _) => { if (pageIndex > 0) { pageIndex--; RenderPage(); } };
        NextDetailsButton.Click += (_, _) => { if (pageIndex + 1 < diagnosticPages.Count) { pageIndex++; RenderPage(); } };
        InstallButton.Click += (_, _) => { if (Model.CanStart) _ = Model.StartAsync(); };
        CancelButton.Click += (_, _) => { if (Model.IsBusy) Model.Cancel(); else Close(); };
        Closing += (_, args) =>
        {
            if (Model.IsBusy) { args.Cancel = true; Model.Cancel(); }
            else if (!Model.Succeeded) Model.Cancel();
        };
        model.PropertyChanged += Changed;
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible) { GamepadModalDialogNavigation.Instance.FocusDialogControl(CancelButton); CancelButton.Focus(); }
        }, DispatcherPriority.Loaded);
        GamepadModalDialogNavigation.Attach(this); Render();
    }
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Render();
    void Render()
    {
        var installFocused = InstallButton.IsFocused;
        var diagnostic = Model.Message.Length > 280 ? Model.Message : "";
        StatusText.Text = diagnostic.Length == 0 ? Model.Message : "Engine setup stopped. Review the details with Previous and Next.";
        AutomationProperties.SetName(StatusText, StatusText.Text);
        var pagesChanged = diagnostic != displayedDiagnostic;
        if (pagesChanged)
        {
            displayedDiagnostic = diagnostic; diagnosticPages.Clear(); pageIndex = 0;
            // Bound untrusted diagnostic display without changing the retained error.
            // Small pages keep controller and keyboard inspection out of scroll geometry.
            var length = Math.Min(diagnostic.Length, 8192);
            if (length < diagnostic.Length && char.IsHighSurrogate(diagnostic[length - 1]) && char.IsLowSurrogate(diagnostic[length])) length--;
            var text = diagnostic[..length] + (length < diagnostic.Length ? "\n[Diagnostic display truncated.]" : "");
            while (text.Length > 0)
            {
                var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
                var end = boundaries.Length > 140 ? boundaries[140] : text.Length;
                diagnosticPages.Add(text[..end]); text = text[end..];
            }
        }
        introduction.IsVisible = Model.CanStart;
        RenderPage();
        TransferProgress.Value = Model.Progress; TransferProgress.IsVisible = Model.IsBusy;
        TransferProgress.IsIndeterminate = Model.IsBusy && (Model.Progress <= 0 || Model.CancellationRequested);
        InstallButton.IsEnabled = Model.CanStart;
        CancelButton.IsEnabled = !Model.IsBusy || Model.CanCancel;
        CancelButton.Content = Model.IsBusy ? "Cancel setup" : Model.CanStart ? "Cancel" : "Close";
        if (pagesChanged && IsVisible) GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
        if ((installFocused || pagesChanged) && IsVisible && !InstallButton.IsEnabled && CancelButton.IsEnabled)
        {
            GamepadModalDialogNavigation.Instance.FocusDialogControl(CancelButton);
            CancelButton.Focus();
        }
        if (IsVisible && !Model.IsBusy && (Model.Succeeded || Model.SessionClosed)) Close();
    }
    void RenderPage()
    {
        var previousFocused = PreviousDetailsButton.IsFocused;
        var nextFocused = NextDetailsButton.IsFocused;
        diagnosticPage.IsVisible = DiagnosticText.IsVisible = diagnosticPages.Count > 0;
        diagnosticPage.Text = $"Detail {pageIndex + 1} of {diagnosticPages.Count}";
        DiagnosticText.Text = diagnosticPages.Count == 0 ? "" : diagnosticPages[pageIndex];
        AutomationProperties.SetName(DiagnosticText, diagnosticPage.Text + ": " + DiagnosticText.Text);
        PreviousDetailsButton.IsVisible = NextDetailsButton.IsVisible = diagnosticPages.Count > 1;
        PreviousDetailsButton.IsEnabled = pageIndex > 0;
        NextDetailsButton.IsEnabled = pageIndex + 1 < diagnosticPages.Count;
        var replacement = nextFocused && !NextDetailsButton.IsEnabled ? PreviousDetailsButton
            : previousFocused && !PreviousDetailsButton.IsEnabled ? NextDetailsButton : null;
        if (replacement is { IsEnabled: true } && IsVisible)
        {
            GamepadModalDialogNavigation.Instance.FocusDialogControl(replacement); replacement.Focus();
        }
    }
}
