using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using Relicade.Services;
using SDL;
using static SDL.SDL3;

namespace Relicade.Tests;

public sealed class InputServiceLifetimeTests
{
    [AvaloniaFact]
    public unsafe void Closing_previous_input_owner_keeps_replacement_virtual_pad_usable()
    {
        PressEchoFilter.Instance.Reset();
        var settings = new AppSettings { EnableGamepadInput = false };
        using var previous = new InputService(new UserControl(), settings);
        var initialGamepadCount = previous.ConnectedGamepadCount;
        SDL_VirtualJoystickDesc desc;
        SDL_INIT_INTERFACE(out desc);
        desc.type = (ushort)SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD;
        desc.naxes = (ushort)SDL_GamepadAxis.SDL_GAMEPAD_AXIS_COUNT;
        desc.nbuttons = (ushort)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT;
        var id = SDL_AttachVirtualJoystick(&desc);
        var pad = SDL_OpenJoystick(id);
        ((nint)pad).Should().NotBe(0, SDL_GetError());
        try
        {
            using var replacement = new InputService(new UserControl(), settings);
            replacement.PollGamepads();
            replacement.ConnectedGamepadCount.Should().Be(initialGamepadCount + 1);
            var confirms = 0;
            replacement.OnConfirm += () => confirms++;

            previous.Dispose();
            previous.Dispose();

            // A queued poll or a returning-game callback may outlive the old shell.
            previous.ReclaimGamepads(reinitIfEmpty: true);
            previous.PollGamepads();
            previous.GetConnectedGamepads().Should().BeEmpty();
            previous.ConnectedGamepadCount.Should().Be(0);

            SDL_WasInit(SDL_InitFlags.SDL_INIT_GAMEPAD).Should().Be(SDL_InitFlags.SDL_INIT_GAMEPAD,
                "closing the previous owner must release only its own subsystem reference");
            ((bool)SDL_SetJoystickVirtualButton(pad, 0, true)).Should().BeTrue();
            replacement.PollGamepads();
            confirms.Should().Be(1);
            replacement.ConnectedGamepadCount.Should().Be(initialGamepadCount + 1);
        }
        finally
        {
            // A pre-fix global SDL_Quit already invalidated the virtual device.
            if (SDL_WasInit(SDL_InitFlags.SDL_INIT_GAMEPAD) != 0)
            {
                SDL_CloseJoystick(pad);
                SDL_DetachVirtualJoystick(id);
            }
            PressEchoFilter.Instance.Reset();
        }
    }
}
