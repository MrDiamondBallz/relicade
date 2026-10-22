using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Relicade.Services;

namespace Relicade.Tests;

public class SteamTextInputLifetimeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Activation_fixtures_release_edit_on_owner_before_next_application()
    {
        Dispatcher? previousOwner = null;
        foreach (var activate in new Action[]
        {
            new SteamOnScreenKeyboardTests().ActivateTextBox_does_not_throw_for_enabled_textbox,
            new SteamOnScreenKeyboardTests().ActivateTextBox_moves_caret_to_end
        })
        {
            await using (var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp)))
            {
                await session.Dispatch(() =>
                {
                    var owner = Dispatcher.UIThread;
                    if (previousOwner != null)
                        Assert.NotSame(previousOwner, owner);
                    previousOwner = owner;
                    // Observe the incoming state before any fixture/cleanup can mask a leak.
                    AssertReleased();
                    try
                    {
                        activate();
                        AssertReleased();
                        output.WriteLine($"{activate.Method.Name}: released on owner {owner.Thread.ManagedThreadId}");
                    }
                    finally { GamepadTextInput.Reset(); }
                }, CancellationToken.None);
            }
            output.WriteLine("activation application disposed after owner-thread assertions");
        }

        await using var next = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        await next.Dispatch(() =>
        {
            Assert.NotSame(previousOwner, Dispatcher.UIThread);
            AssertReleased();
            // This is the edit-end path invoked by the victim's Enter key.
            Assert.False(GamepadTextInput.TryEndEdit());
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_restores_highlight_and_static_state_even_when_fixture_throws(bool fail)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        await session.Dispatch(() =>
        {
            AssertReleased();
            var box = new TextBox { Text = "n64, recomp", CaretBrush = Brushes.Lime };
            var owner = box.Dispatcher;
            var expected = new InvalidOperationException("synthetic assertion failure");
            try
            {
                var error = Record.Exception(() => SteamOnScreenKeyboardTests.WithTextInputCleanup(() =>
                {
                    Assert.True(owner.CheckAccess());
                    GamepadTextInput.SkipNativeFocusOverride = () => true;
                    GamepadTextInput.Highlight(box);
                    Assert.True(box.IsReadOnly);
                    Assert.Same(Brushes.Transparent, box.CaretBrush);
                    GamepadControlActivation.ActivateTextBox(box);
                    Assert.Same(box, GamepadTextInput.Active);
                    Assert.True(GamepadTextInput.IsEditing);
                    Assert.False(box.IsReadOnly);
                    Assert.Same(Brushes.Lime, box.CaretBrush);
                    Assert.Equal(box.Text.Length, box.CaretIndex);
                    Assert.True(GamepadTextInput.TryEndEdit());
                    Assert.True(box.IsReadOnly);
                    if (fail)
                        throw expected;
                }));
                if (fail)
                    Assert.Same(expected, error);
                else
                    Assert.Null(error);
                Assert.True(owner.CheckAccess());
                AssertReleased();
                Assert.False(box.IsReadOnly);
                Assert.Same(Brushes.Lime, box.CaretBrush);
                Assert.Equal("n64, recomp", box.Text);
                output.WriteLine($"cleanup fail={fail}: visuals/static state released on owner {owner.Thread.ManagedThreadId}");
            }
            finally { GamepadTextInput.Reset(); }
        }, CancellationToken.None);
    }

    private static void AssertReleased()
    {
        Assert.Null(GamepadTextInput.Active);
        Assert.False(GamepadTextInput.IsEditing);
        Assert.Null(GamepadTextInput.SkipNativeFocusOverride);
    }
}
