using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using Relicade.Services;
using Relicade.ViewModels;
using SDL;
using static SDL.SDL3;

namespace Relicade.Tests;

/// <summary>
/// On a Steam Deck one press of A can reach the launcher twice: SDL reports the button, and Steam's
/// desktop layout (or the Deck's lizard mode) also types Enter for it, Escape for B. These tests press a
/// real SDL3 virtual pad and type the matching key, the way the Deck delivers them, in both orders.
/// </summary>
public class SteamDeckDoublePressTests : IDisposable
{
    public SteamDeckDoublePressTests()
    {
        GamepadTextInput.Reset();
        PressEchoFilter.Instance.Reset();
    }

    public void Dispose()
    {
        GamepadTextInput.Reset();
        PressEchoFilter.Instance.Reset();
    }

    [AvaloniaFact]
    public void Checkbox_toggles_once_when_A_arrives_before_its_Enter()
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.PressA();
        h.TypeEnter();
        h.ReleaseA();

        h.CheckBox.IsChecked.Should().BeTrue();
        h.Confirms.Should().Be(1);
    }

    [AvaloniaFact]
    public void Checkbox_toggles_once_when_Enter_arrives_before_A()
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.TypeEnter();
        h.PressA();
        h.ReleaseA();

        h.CheckBox.IsChecked.Should().BeTrue();
        h.Confirms.Should().Be(1);
    }

    [AvaloniaFact]
    public void Dropdown_stays_open_after_one_press()
    {
        using var h = new Harness();
        h.ComboBox.Focus();

        h.PressA();
        h.TypeEnter();
        h.ReleaseA();

        h.ComboBox.IsDropDownOpen.Should().BeTrue();
        h.Confirms.Should().Be(1);
    }

    [AvaloniaFact]
    public void Text_box_stays_in_edit_after_one_press()
    {
        using var h = new Harness();
        GamepadTextInput.Highlight(h.TextBox);

        h.PressA();
        h.TypeEnter();
        h.ReleaseA();

        GamepadTextInput.IsEditing.Should().BeTrue();
        h.TextBox.IsFocused.Should().BeTrue();
    }

    [AvaloniaFact]
    public void B_and_its_Escape_cancel_once()
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.Press(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST);
        h.Type(Key.Escape, PhysicalKey.Escape);
        h.Release(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST);

        h.Cancels.Should().Be(1);
    }

    [AvaloniaFact]
    public void Enter_repeating_while_A_is_held_does_not_confirm_again()
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.PressA();
        h.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        h.Advance(TimeSpan.FromMilliseconds(600));
        // The key repeat a held key gets once the repeat delay has passed.
        h.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        h.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        h.ReleaseA();

        h.Confirms.Should().Be(1);
        h.CheckBox.IsChecked.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Separate_presses_from_pad_and_keyboard_both_count()
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.PressA();
        h.ReleaseA();
        h.Advance(TimeSpan.FromMilliseconds(400));
        h.TypeEnter();

        h.Confirms.Should().Be(2);
        h.CheckBox.IsChecked.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(299, 1, true)]
    [InlineData(300, 2, false)]
    public void Pad_then_keyboard_uses_the_echo_window_boundary(int milliseconds, int expectedConfirms, bool expectedChecked)
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.PressA();
        h.ReleaseA();
        h.Advance(TimeSpan.FromMilliseconds(milliseconds));
        h.TypeEnter();

        h.Confirms.Should().Be(expectedConfirms);
        h.CheckBox.IsChecked.Should().Be(expectedChecked);
    }

    [AvaloniaTheory]
    [InlineData(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, GamepadAction.Confirm)]
    [InlineData(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, GamepadAction.Cancel)]
    [InlineData(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH, GamepadAction.Options)]
    public void Released_gamepad_action_can_be_pressed_again_without_a_cooldown(SDL_GamepadButton button, GamepadAction action)
    {
        using var h = new Harness();
        h.CheckBox.Focus();
        int Count() => action switch
        {
            GamepadAction.Confirm => h.Confirms,
            GamepadAction.Cancel => h.Cancels,
            GamepadAction.Options => h.Options,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        h.Press(button);
        Count().Should().Be(1);
        // Repeated polls of a held button must not create another logical press.
        h.Press(button);
        h.Press(button);
        Count().Should().Be(1);
        h.Release(button);
        h.Press(button);
        elapsed.Stop();

        Count().Should().Be(2, $"a sampled release separates the presses (sequence took {elapsed.Elapsed.TotalMilliseconds:F3} ms)");
        h.Release(button);
    }

    [AvaloniaFact]
    public void Axis_bound_confirm_counts_new_edges_but_not_held_samples()
    {
        using var h = new Harness();
        h.CheckBox.Focus();
        h.BindConfirmToHorizontalAxis();
        h.MoveHorizontalAxis(0);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        h.MoveHorizontalAxis(20000);
        h.Confirms.Should().Be(1);
        h.MoveHorizontalAxis(20000);
        h.Confirms.Should().Be(1);
        h.MoveHorizontalAxis(0);
        h.MoveHorizontalAxis(20000);
        elapsed.Stop();

        h.Confirms.Should().Be(2, $"neutral separates the axis presses (sequence took {elapsed.Elapsed.TotalMilliseconds:F3} ms)");
        h.CheckBox.IsChecked.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Independent_gamepads_each_confirm_once_while_held()
    {
        using var h = new Harness();
        h.CheckBox.Focus();
        h.AttachOtherPad();

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        h.PressA();
        h.PressA();
        h.Confirms.Should().Be(1);
        h.PressOtherA();
        elapsed.Stop();
        h.Confirms.Should().Be(2, $"each pad has its own press edge (sequence took {elapsed.Elapsed.TotalMilliseconds:F3} ms)");
        h.PressA();
        h.PressOtherA();
        h.Confirms.Should().Be(2);
        h.CheckBox.IsChecked.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Keyboard_alone_still_confirms_on_every_press()
    {
        using var h = new Harness();
        h.CheckBox.Focus();

        h.TypeEnter();
        h.TypeEnter();

        h.Confirms.Should().Be(2);
    }

    [AvaloniaFact]
    public void Enter_echo_is_dropped_in_other_windows_before_the_focused_button_sees_it()
    {
        using var h = new Harness();
        var clicks = 0;
        var button = new Button { Content = "OK" };
        button.Click += (_, _) => clicks++;
        var dialog = new Window { Content = button };
        dialog.Show();
        try
        {
            button.Focus();
            PressEchoFilter.Instance.TryAccept(GamepadAction.Confirm, PressSource.Gamepad).Should().BeTrue();

            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            clicks.Should().Be(0);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Fact]
    public void Echo_is_only_dropped_from_the_other_source_inside_the_window()
    {
        var now = new DateTime(2026, 10, 13, 9, 0, 0);
        var filter = new PressEchoFilter(() => now);

        filter.TryAccept(GamepadAction.Confirm, PressSource.Gamepad).Should().BeTrue();
        filter.TryAccept(GamepadAction.Confirm, PressSource.Keyboard).Should().BeFalse();
        filter.TryAccept(GamepadAction.Cancel, PressSource.Keyboard).Should().BeTrue();

        now += PressEchoFilter.EchoWindow;
        filter.TryAccept(GamepadAction.Confirm, PressSource.Keyboard).Should().BeTrue();
        filter.TryAccept(GamepadAction.Confirm, PressSource.Keyboard).Should().BeTrue();
        filter.TryAccept(GamepadAction.Confirm, PressSource.Gamepad).Should().BeFalse();
    }

    /// <summary>
    /// A window with the three kinds of field from the report, wired to the launcher's real input
    /// controller and SDL3 input service. Confirm does what the shell does for each field.
    /// </summary>
    private sealed unsafe class Harness : IDisposable
    {
        private readonly LauncherInputController _controller;
        private readonly InputBindingsViewModel _bindings;
        private readonly SDL_JoystickID _padId;
        private readonly SDL_Joystick* _pad;
        private SDL_JoystickID _otherPadId;
        private SDL_Joystick* _otherPad;
        private bool _otherPadAttached;
        private DateTime _now = new(2026, 10, 13, 9, 0, 0);

        public Window Window { get; }
        public CheckBox CheckBox { get; } = new() { Content = "Start in fullscreen", IsChecked = false };
        public ComboBox ComboBox { get; } = new() { Items = { new ComboBoxItem { Content = "Dark" }, new ComboBoxItem { Content = "Light" } }, SelectedIndex = 0 };
        public TextBox TextBox { get; } = new() { Text = "search" };
        public int Confirms { get; private set; }
        public int Cancels { get; private set; }
        public int Options { get; private set; }

        public Harness()
        {
            PressEchoFilter.Instance.Clock = () => _now;
            GamepadComboBoxNavigation.Attach(ComboBox);
            // Like MainView inside MainWindow: the controller also listens on the window itself.
            var view = new UserControl { Content = new StackPanel { Children = { CheckBox, ComboBox, TextBox } } };
            Window = new Window { Content = view };
            Window.Show();

            var settings = new SettingsViewModel(new MemoryStore());
            settings.Current.EnableGamepadInput = false;
            _bindings = new InputBindingsViewModel(settings, () => null, action => action());
            _controller = new LauncherInputController(view, () => settings.Current, _bindings, () => true, () => false, () => { },
                new(_ => true, Confirm, () => Cancels++, () => Options++, _ => { }, () => { }), enableInput: true);

            SDL_VirtualJoystickDesc desc;
            SDL_INIT_INTERFACE(out desc);
            desc.type = (ushort)SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD;
            desc.naxes = (ushort)SDL_GamepadAxis.SDL_GAMEPAD_AXIS_COUNT;
            desc.nbuttons = (ushort)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT;
            _padId = SDL_AttachVirtualJoystick(&desc);
            _pad = SDL_OpenJoystick(_padId);
            ((nint)_pad).Should().NotBe(0, SDL_GetError());
            Poll();
            _controller.Service!.ConnectedGamepadCount.Should().Be(1);
        }

        private void Confirm()
        {
            Confirms++;
            if (GamepadTextInput.IsEditing)
            {
                GamepadTextInput.TryEndEdit();
                return;
            }

            if (GamepadComboBoxNavigation.Instance.TryHandleConfirm())
                return;

            switch (Window.FocusManager?.GetFocusedElement())
            {
                case CheckBox check:
                    GamepadControlActivation.ActivateCheckBox(check);
                    break;
                case ComboBox combo:
                    GamepadComboBoxNavigation.Open(combo);
                    break;
                case TextBox text:
                    GamepadControlActivation.ActivateTextBox(text);
                    break;
            }
        }

        public void PressA() => Press(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);

        public void ReleaseA() => Release(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);

        public void BindConfirmToHorizontalAxis() => _controller.Service!.ApplyBindings(
            new Dictionary<GamepadAction, List<GamepadBinding>>
            {
                [GamepadAction.Confirm] = [GamepadBinding.AxisPositive(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX)]
            });

        public void MoveHorizontalAxis(short value)
        {
            ((bool)SDL_SetJoystickVirtualAxis(_pad, (int)SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, value)).Should().BeTrue();
            Poll();
        }

        public void AttachOtherPad()
        {
            SDL_VirtualJoystickDesc desc;
            SDL_INIT_INTERFACE(out desc);
            desc.type = (ushort)SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD;
            desc.naxes = (ushort)SDL_GamepadAxis.SDL_GAMEPAD_AXIS_COUNT;
            desc.nbuttons = (ushort)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT;
            _otherPadId = SDL_AttachVirtualJoystick(&desc);
            _otherPadAttached = true;
            _otherPad = SDL_OpenJoystick(_otherPadId);
            ((nint)_otherPad).Should().NotBe(0, SDL_GetError());
            Poll();
            _controller.Service!.ConnectedGamepadCount.Should().Be(2);
        }

        public void PressOtherA()
        {
            ((bool)SDL_SetJoystickVirtualButton(_otherPad, (int)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, true)).Should().BeTrue();
            Poll();
        }

        public void Press(SDL_GamepadButton button)
        {
            SDL_SetJoystickVirtualButton(_pad, (int)button, true);
            Poll();
        }

        public void Release(SDL_GamepadButton button)
        {
            SDL_SetJoystickVirtualButton(_pad, (int)button, false);
            Poll();
        }

        public void TypeEnter() => Type(Key.Enter, PhysicalKey.Enter);

        public void Type(Key key, PhysicalKey physical)
        {
            Window.KeyPress(key, RawInputModifiers.None, physical, null);
            Window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        public void Advance(TimeSpan by) => _now += by;

        private void Poll()
        {
            _controller.Service!.PollGamepads();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            ComboBox.IsDropDownOpen = false;
            if (_otherPad != null) SDL_CloseJoystick(_otherPad);
            if (_otherPadAttached) SDL_DetachVirtualJoystick(_otherPadId);
            SDL_CloseJoystick(_pad);
            SDL_DetachVirtualJoystick(_padId);
            _controller.Dispose();
            _bindings.Dispose();
            Window.Close();
            PressEchoFilter.Instance.Clock = () => DateTime.Now;
        }
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
