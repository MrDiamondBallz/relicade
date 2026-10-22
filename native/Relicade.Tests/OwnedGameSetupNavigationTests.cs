using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
using Direction = Relicade.Services.NavigationDirection;

namespace Relicade.Tests;

public sealed class OwnedGameSetupNavigationTests
{
    [AvaloniaTheory]
    [InlineData(760, 660, "keyboard")]
    [InlineData(760, 660, "controller")]
    [InlineData(320, 360, "keyboard")]
    [InlineData(320, 360, "controller")]
    public async Task Preview_details_are_readable_through_natural_navigation_before_confirmation(int width, int height, string input)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        f.Definition = f.Definition with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(f.Data)) };
        var folder = f.Root;
        for (var i = 0; i < 3; i++) folder = Path.Combine(folder, new string('遊', 40) + i);
        Directory.CreateDirectory(folder); var source = Path.Combine(folder, "freedoom2.wad"); File.WriteAllBytes(source, f.Data);
        var setup = f.Setup(); await using var session = new LauncherSession(); var copies = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, "Synthetic fixture, not game data", setup.Preview,
            (plan, token) => { copies++; return setup.Apply(plan, token); }, () => Task.CompletedTask);
        model.ConfigureFreedoom(() => throw new InvalidOperationException("No engine may execute in this fixture."), false);
        var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
        model.PickFile = () => Task.FromResult<string?>(source);
        model.OpenDownload = () => throw new InvalidOperationException("No browser may open in this fixture.");
        void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        try
        {
            dialog.Show(); Layout();
            dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work; Layout();
            Assert.True(model.CanConfirm); Assert.True(dialog.CloseButton.IsFocused);
            var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
            var inspections = dialog.GetVisualDescendants().OfType<Button>()
                .Where(button => AutomationProperties.GetName(button)?.StartsWith("Inspect ", StringComparison.Ordinal) == true).ToArray();
            Assert.NotEmpty(inspections); var visited = new HashSet<Button>();
            void InspectFocused()
            {
                if (dialog.FocusManager?.GetFocusedElement() is Button button && inspections.Contains(button))
                {
                    Assert.True(button.Bounds.Height >= 44);
                    visited.Add(button); var text = Assert.IsType<TextBlock>(button.Content);
                    var point = text.TranslatePoint(default, viewport)!.Value;
                    Assert.True(point.Y >= -0.1 && point.Y + text.Bounds.Height <= viewport.Bounds.Height + 0.1,
                        $"{input} {width}x{height}: preview text at {point.Y} + {text.Bounds.Height} exceeds viewport {viewport.Bounds.Height}: {text.Text}");
                }
                Assert.Equal(0, copies); Assert.False(File.Exists(model.Plan!.Destination));
            }
            if (input == "keyboard")
            {
                for (var i = 0; i < (inspections.Length + 8) * 2; i++)
                {
                    dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Layout(); InspectFocused();
                }
            }
            else
            {
                GamepadFocusChrome.SetActive(true, dialog);
                foreach (var direction in new[] { Direction.Up, Direction.Down })
                {
                    for (var i = 0; i < inspections.Length + 8; i++)
                    {
                        var before = dialog.FocusManager?.GetFocusedElement();
                        GamepadModalDialogNavigation.Instance.TryHandleNavigation(direction); Layout(); InspectFocused();
                        if (ReferenceEquals(before, dialog.FocusManager?.GetFocusedElement())) break;
                    }
                }
            }
            Assert.True(inspections.All(visited.Contains),
                "Unreachable preview rows: " + string.Join(" | ", inspections.Where(x => !visited.Contains(x)).Select(AutomationProperties.GetName)));
            var completeText = string.Concat(inspections.Select(x => ((TextBlock)x.Content!).Text));
            Assert.Contains("Source: " + source, completeText); Assert.Contains("Destination: " + model.Plan!.Destination, completeText);
            ConfirmNaturally(dialog, input, inspections.Length + 8, Layout);
            await dialog.Work; Assert.Equal(1, copies); Assert.Equal(OwnedGameSetupUiState.Completed, model.State);
            Assert.Equal(f.Data, File.ReadAllBytes(model.Plan.Destination));
            Assert.Equal(f.Data, File.ReadAllBytes(source)); Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); }
    }

    static void ConfirmNaturally(OwnedGameSetupDialog dialog, string input, int bound, Action layout)
    {
        if (input == "keyboard")
        {
            for (var i = 0; i < bound && !dialog.ConfirmButton.IsFocused; i++)
            { dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); layout(); }
            Assert.True(dialog.ConfirmButton.IsFocused);
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        }
        else
        {
            foreach (var direction in new[] { Direction.Right, Direction.Down })
                for (var i = 0; i < bound && !dialog.ConfirmButton.IsFocused; i++)
                { GamepadModalDialogNavigation.Instance.TryHandleNavigation(direction); layout(); }
            Assert.True(dialog.ConfirmButton.IsFocused); Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm());
        }
    }

    [AvaloniaTheory]
    [InlineData("keyboard", false)][InlineData("controller", false)]
    [InlineData("keyboard", true)][InlineData("controller", true)]
    public async Task Repair_paths_remain_inspectable_and_original_bytes_survive_natural_confirmation(string input, bool resize)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        f.Definition = f.Definition with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(f.Data)) };
        var setup = f.Setup(); var original = setup.Preview(f.Source, CancellationToken.None); setup.Apply(original, CancellationToken.None);
        byte[] changed = [40, 30, 20, 10]; File.WriteAllBytes(original.Destination, changed);
        await using var session = new LauncherSession(); var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        await OwnedGameSetupFlow.InitializeFreedoomAsync(model, setup, session.Token,
            _ => throw new InvalidOperationException("No engine execution is allowed."), null);
        model.ConfigureFreeDataDownload((_, _) => throw new InvalidOperationException("No download is requested."));
        var dialog = new OwnedGameSetupDialog(model) { Width = resize ? 760 : 320, Height = resize ? 660 : 360 };
        model.PickFile = () => Task.FromResult<string?>(f.Source);
        void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        try
        {
            dialog.Show(); Layout(); dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work; Layout();
            if (resize) { dialog.Width = 320; dialog.Height = 360; Layout(); }
            var repair = Assert.IsType<Relicade.Core.Services.OwnedDataRepair>(model.Plan!.Repair);
            var viewport = Assert.Single(dialog.GetVisualDescendants().OfType<ScrollViewer>());
            var inspections = dialog.GetVisualDescendants().OfType<Button>()
                .Where(button => AutomationProperties.GetName(button)?.StartsWith("Inspect ", StringComparison.Ordinal) == true).ToArray();
            var visited = new HashSet<Button>(); var bound = inspections.Length + 8;
            void Inspect()
            {
                if (dialog.FocusManager?.GetFocusedElement() is Button button && inspections.Contains(button))
                {
                    Assert.True(button.Bounds.Height >= 44);
                    visited.Add(button); var text = Assert.IsType<TextBlock>(button.Content); var point = text.TranslatePoint(default, viewport)!.Value;
                    Assert.InRange(point.Y, -0.1, viewport.Bounds.Height - text.Bounds.Height + 0.1);
                }
                Assert.Equal(changed, File.ReadAllBytes(original.Destination)); Assert.False(File.Exists(repair.RetainedDestination));
            }
            if (input == "keyboard")
                for (var i = 0; i < bound * 2; i++)
                { dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Layout(); Inspect(); }
            else
            {
                GamepadFocusChrome.SetActive(true, dialog);
                foreach (var direction in new[] { Direction.Up, Direction.Down })
                    for (var i = 0; i < bound; i++)
                    {
                        var before = dialog.FocusManager?.GetFocusedElement();
                        GamepadModalDialogNavigation.Instance.TryHandleNavigation(direction); Layout(); Inspect();
                        if (ReferenceEquals(before, dialog.FocusManager?.GetFocusedElement())) break;
                    }
            }
            Assert.True(inspections.All(visited.Contains), "Every repair detail must be reachable before confirmation.");
            var completeText = string.Concat(inspections.Select(x => ((TextBlock)x.Content!).Text));
            Assert.Contains("Source: " + f.Source, completeText); Assert.Contains("Destination: " + original.Destination, completeText);
            Assert.Contains("Retained copy: " + repair.RetainedDestination, completeText); Assert.Contains(repair.PreviousSha256, completeText);
            ConfirmNaturally(dialog, input, bound, Layout); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Completed, model.State);
            Assert.Equal(changed, File.ReadAllBytes(repair.RetainedDestination)); Assert.Equal(f.Data, File.ReadAllBytes(original.Destination));
            Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); }
    }
    [AvaloniaTheory]
    [InlineData("keyboard")][InlineData("controller")]
    public async Task Confirming_an_inspection_is_inert_and_cancel_preserves_files(string input)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        var setup = f.Setup(); await using var session = new LauncherSession(); var copies = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, "Synthetic inspection only", setup.Preview,
            (plan, token) => { copies++; return setup.Apply(plan, token); }, () => Task.CompletedTask);
        var dialog = new OwnedGameSetupDialog(model) { Width = 320, Height = 360 };
        model.PickFile = () => Task.FromResult<string?>(f.Source);
        void Layout() { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        try
        {
            dialog.Show(); Layout(); await model.ChooseAsync(); Layout();
            if (input == "controller") GamepadFocusChrome.SetActive(true, dialog);
            var count = dialog.GetVisualDescendants().OfType<Button>().Count();
            for (var i = 0; i < count + 1; i++)
            {
                if (input == "keyboard")
                { dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); }
                else GamepadModalDialogNavigation.Instance.TryHandleNavigation(Direction.Up);
                Layout();
                if (dialog.FocusManager?.GetFocusedElement() is Button button && AutomationProperties.GetName(button)?.StartsWith("Inspect ", StringComparison.Ordinal) == true) break;
            }
            var focused = Assert.IsType<Button>(dialog.FocusManager?.GetFocusedElement());
            Assert.StartsWith("Inspect ", AutomationProperties.GetName(focused));
            if (input == "keyboard")
            { dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); }
            else Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm());
            Layout(); Assert.True(dialog.IsVisible); Assert.Equal(OwnedGameSetupUiState.Confirm, model.State);
            Assert.Equal(0, copies); Assert.False(File.Exists(model.Plan!.Destination));
            if (input == "keyboard")
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Assert.True(GamepadModalDialogNavigation.Instance.TryHandleCancel());
            Layout(); await dialog.Work; Assert.False(dialog.IsVisible); Assert.True(model.IsClosed);
            Assert.Equal(0, copies); Assert.False(File.Exists(model.Plan.Destination));
            Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); GamepadFocusChrome.SetActive(false); }
    }

    [AvaloniaTheory]
    [InlineData(760, 660, false)][InlineData(320, 360, false)]
    [InlineData(760, 660, true)][InlineData(320, 360, true)]
    public async Task Inspection_word_breaks_preserve_complete_graphemes(int width, int height, bool combinedSpaces)
    {
        var requirement = combinedSpaces ? string.Concat(Enumerable.Repeat(" \u0301", 200))
            : string.Concat(Enumerable.Repeat("word e\u0301 \u0301 👨‍👩‍👧‍👦 ", 24));
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, "synthetic", requirement,
            (_, _) => throw new InvalidOperationException("No preview requested."),
            (_, _) => throw new InvalidOperationException("No copy requested."), () => Task.CompletedTask);
        var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var rows = dialog.GetVisualDescendants().OfType<Button>()
                .Where(button => AutomationProperties.GetName(button)?.StartsWith("Inspect ", StringComparison.Ordinal) == true)
                .Select(button => ((TextBlock)button.Content!).Text!).ToArray();
            var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(requirement).Append(requirement.Length).ToHashSet();
            var offset = 0;
            foreach (var row in rows)
            {
                if (offset >= requirement.Length) break;
                Assert.Equal(requirement.Substring(offset, row.Length), row); offset += row.Length;
                Assert.Contains(offset, boundaries);
            }
            Assert.Equal(requirement.Length, offset);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

}
