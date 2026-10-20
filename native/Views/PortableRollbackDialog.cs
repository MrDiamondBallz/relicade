using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class PortableRollbackDialog : Window
{
    internal const string Limits = "Payload only; current saves/data retained; compatibility not guaranteed; external/undeclared data not covered. No previous-save restore. Only the immediate complete retained portable generation; no downloads, general history restore, uninstall or preview/reopen repair.";
    internal PortableRollbackViewModel Model { get; }
    internal Button CancelButton { get; } = new() { Content = "Cancel", IsDefault = true, IsCancel = true, MinHeight = 44, MinWidth = 96 };
    internal Button ApplyButton { get; } = new() { Content = "Restore displayed plan", MinHeight = 44 };
    internal Button PreviewButton { get; } = new() { Content = "Preview again", MinHeight = 44 };
    internal StackPanel Details { get; } = new() { Spacing = 4 };
    internal Task Work { get; private set; } = Task.CompletedTask;
    PortableRollbackPreview? rendered;
    readonly StackPanel status = new() { Spacing = 4 };
    internal PortableRollbackDialog(PortableRollbackViewModel model)
    {
        Model = model; Title = "Restore previous portable version";
        Width = 760; Height = 640; MinWidth = 320; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        AutomationProperties.SetName(this, Title);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        layout.Children.Add(Text(Title, 22));
        var contents = new StackPanel { Spacing = 12 };
        AddInspection(contents, "Selected InstanceKey: " + model.InstanceKey);
        AddInspection(contents, Limits);
        contents.Children.Add(status); contents.Children.Add(Details);
        var scroll = new ScrollViewer { Content = contents, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Focusable = true };
        AutomationProperties.SetName(scroll, "Complete previous-payload preview, identity, data limits and hash");
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { CancelButton, PreviewButton, ApplyButton } };
        foreach (var button in buttons.Children.OfType<Button>())
        { button.Margin = new Thickness(0, 0, 8, 8); button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true); }
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons); Content = layout;
        CancelButton.Click += (_, _) => { model.Cancel(); if (model.IsClosed) Close(); };
        ApplyButton.Click += (_, _) => { if (model.CanApply) Work = model.ConfirmAsync(); };
        PreviewButton.Click += (_, _) => { if (model.CanPreview) Work = model.PreviewAsync(); };
        model.PropertyChanged += Changed;
        Closed += (_, _) => { model.Close(); model.PropertyChanged -= Changed; };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (IsVisible && !model.IsClosed) { GamepadModalDialogNavigation.Instance.FocusDialogControl(CancelButton); CancelButton.Focus(); } }, DispatcherPriority.Loaded);
        GamepadModalDialogNavigation.Attach(this); Render();
    }
    internal void BeginPreview() => Work = Model.PreviewAsync();
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (!Model.IsClosed) Render(); }
    void Render()
    {
        // Reuse stable status targets while applying/cancelling to avoid focus loss.
        var message = string.Concat(status.Children.OfType<Button>().Select(b => ((TextBlock)b.Content!).Text));
        if (message != Model.Message) { status.Children.Clear(); AddInspection(status, Model.Message); }
        if (!ReferenceEquals(rendered, Model.Plan))
        {
            rendered = Model.Plan; Details.Children.Clear();
            if (rendered is { } p)
            {
                foreach (var text in new[] { "Current version: " + p.CurrentVersion, "Current operation: " + p.CurrentOperationId,
                    "Retained target version: " + p.TargetVersion, "Retained target operation: " + p.RetainedOperationId,
                    "Target executable: " + p.TargetExecutable, "Plan SHA256: " + p.PlanSha256 }) AddInspection(Details, text);
                AddInspection(Details, "Preserved CURRENT declared data paths (not older saves):");
                if (p.PreservedDataPaths.Count == 0) AddInspection(Details, "No project data paths declared. External or undeclared saves are not covered.");
                foreach (var path in p.PreservedDataPaths) AddInspection(Details, path);
            }
        }
        ApplyButton.IsEnabled = Model.CanApply; ApplyButton.IsVisible = Model.State is PortableRollbackUiState.Ready or PortableRollbackUiState.Previewing;
        PreviewButton.IsVisible = Model.CanPreview;
        CancelButton.Content = Model.State == PortableRollbackUiState.Completed ? "Close" : "Cancel";
        GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
    }
    internal static void AddInspection(StackPanel panel, string text)
    {
        // Every character, including oversized identities/warnings, has a bounded
        // focus rectangle reachable with existing real modal keyboard/controller
        // routing. Confirm on these inspection targets is deliberately inert.
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        for (var start = 0; start < boundaries.Length; start += 64)
        {
            int end = start + 64 < boundaries.Length ? boundaries[start + 64] : text.Length;
            var part = text[boundaries[start]..end];
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Text(part) };
            button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
            AutomationProperties.SetName(button, "Inspect " + part); button.GotFocus += (_, _) => button.BringIntoView();
            panel.Children.Add(button);
        }
    }
    static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
}
