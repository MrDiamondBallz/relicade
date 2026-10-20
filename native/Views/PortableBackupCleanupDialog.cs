using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Views;

internal sealed class PortableBackupCleanupDialog : Window
{
    internal const string RetentionNotice = "Keep: ALL previous generations, data snapshots, current saves, journals, metadata and the entire current-operation backup. Only verified redundant immutable backup payload copies are eligible. This is not uninstall, general retention, credential removal or secure erasure. Deleted copies are not rolled back.";
    internal PortableBackupCleanupViewModel Model { get; }
    internal Button CancelButton { get; } = new() { Content = "Cancel", IsDefault = true, IsCancel = true, MinHeight = 44, MinWidth = 96 };
    internal Button ApplyButton { get; } = new() { Content = "Clean displayed plan", MinHeight = 44 };
    internal Button ResumeButton { get; } = new() { Content = "Resume approved plan", MinHeight = 44 };
    internal Button PreviewButton { get; } = new() { Content = "Preview again", MinHeight = 44 };
    internal TextBlock Summary { get; } = Text("");
    internal TextBlock Hash { get; } = Text("");
    internal TextBlock Status { get; } = Text("");
    internal ItemsControl Files { get; } = new();
    internal Task Work { get; private set; } = Task.CompletedTask;
    BackupCleanupPreview? renderedPlan;

    internal PortableBackupCleanupDialog(PortableBackupCleanupViewModel model)
    {
        Model = model;
        Title = "Clean redundant backups"; Width = 760; Height = 640; MinWidth = 320; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        AutomationProperties.SetName(this, Title);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 16 };
        layout.Children.Add(Text("Clean redundant backups", 22));
        Files.ItemTemplate = new FuncDataTemplate<BackupCleanupFile>((file, _) => Text(
            $"{file!.OperationId}/{file.BackupRelativePath}\nRetained: {file.OperationId}/{file.RetainedRelativePath}\n{file.Length} bytes · SHA256 {file.Sha256}"));
        var detail = new StackPanel { Spacing = 12, Children = {
            Text("Selected InstanceKey: " + model.InstanceKey), Text(RetentionNotice), Status, Summary, Hash, Files } };
        var scroll = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Focusable = true };
        AutomationProperties.SetName(scroll, "Complete cleanup plan and retained-file details");
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { CancelButton, PreviewButton, ResumeButton, ApplyButton } };
        foreach (var button in buttons.Children.OfType<Button>())
        {
            button.Margin = new Thickness(0, 0, 8, 8);
            button.SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        }
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons);
        Content = layout;
        CancelButton.Click += (_, _) => { Model.Cancel(); if (Model.IsClosed) Close(); };
        ApplyButton.Click += (_, _) => { if (Model.CanApply) Work = Model.ConfirmAsync(); };
        ResumeButton.Click += (_, _) => { if (Model.CanResume) Work = Model.ResumeAsync(); };
        PreviewButton.Click += (_, _) => { if (Model.CanPreview) Work = Model.PreviewAsync(); };
        model.PropertyChanged += Changed;
        Closed += (_, _) => { model.Close(); model.PropertyChanged -= Changed; };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && !Model.IsClosed) { GamepadModalDialogNavigation.Instance.FocusDialogControl(CancelButton); CancelButton.Focus(); }
        }, DispatcherPriority.Loaded);
        GamepadModalDialogNavigation.Attach(this);
        Render();
    }

    internal void BeginPreview() => Work = Model.PreviewAsync();
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (!Model.IsClosed) Render(); }
    void Render()
    {
        Status.Text = Model.Message;
        Summary.Text = Model.Plan is { } p ? $"Eligible files: {p.Files.Count} · Reclaimable bytes: {p.ReclaimableBytes}" : "No approved plan is available.";
        Hash.Text = Model.Plan is { } plan ? "Plan SHA256: " + plan.PlanSha256 : "Plan SHA256: not available";
        if (!ReferenceEquals(renderedPlan, Model.Plan)) { renderedPlan = Model.Plan; Files.ItemsSource = renderedPlan?.Files; }
        ApplyButton.IsEnabled = Model.CanApply;
        ApplyButton.IsVisible = Model.State is PortableBackupCleanupUiState.Ready or PortableBackupCleanupUiState.Empty or PortableBackupCleanupUiState.Previewing;
        ResumeButton.IsVisible = Model.State == PortableBackupCleanupUiState.Interrupted;
        ResumeButton.IsEnabled = Model.CanResume;
        PreviewButton.IsVisible = Model.CanPreview;
        CancelButton.Content = Model.State == PortableBackupCleanupUiState.Completed ? "Close" : "Cancel";
        GamepadModalDialogNavigation.Instance.RefreshDialogButtons();
    }
    static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
}
