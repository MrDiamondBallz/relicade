using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AsyncImageLoader;
using Relicade.Services;

namespace Relicade.Tests;

public class MarkdownFenceTests
{
    [AvaloniaTheory]
    [InlineData("```text\nkeep me", "keep me")]
    [InlineData("```text\r\nfirst\r\n\r\nlast\r\n", "first\n\nlast\n")]
    [InlineData("```\n", "")]
    [InlineData("```", "")]
    public void Unterminated_fence_flushes_once_as_selectable_literal_code(string markdown, string expected)
    {
        var blocks = new MarkdownRenderer(_ => throw new InvalidOperationException("No navigation")).Render(markdown);
        var code = Assert.IsType<SelectableTextBlock>(Assert.IsType<Border>(Assert.Single(blocks)).Child);
        Assert.Equal(expected, code.Text);
        code.SelectionStart = 0; code.SelectionEnd = expected.Length;
        Assert.Equal(expected, code.SelectedText);
    }

    [AvaloniaFact]
    public void Closed_then_unclosed_code_preserves_order_without_duplicating_either_block()
    {
        var blocks = new MarkdownRenderer(_ => { }).Render("Before\n\n```cs\nfirst\n```\n\nAfter\n\n```\nsecond");
        Assert.Equal(4, blocks.Count);
        Assert.IsType<SelectableTextBlock>(blocks[0]);
        Assert.Equal("first", Assert.IsType<SelectableTextBlock>(Assert.IsType<Border>(blocks[1]).Child).Text);
        Assert.IsType<SelectableTextBlock>(blocks[2]);
        Assert.Equal("second", Assert.IsType<SelectableTextBlock>(Assert.IsType<Border>(blocks[3]).Child).Text);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fenced_markup_is_literal_and_creates_no_image_or_link_actions(bool close)
    {
        const string payload = "<img src=\"https://fixture.invalid/tracker\">\n[link](https://fixture.invalid/)\n![cover](https://fixture.invalid/cover)\n<a href=\"file:///private\">html</a>\n**bold** &amp;";
        var opened = 0;
        var blocks = new MarkdownRenderer(_ => opened++).Render("```html\n" + payload + (close ? "\n```" : ""));
        var border = Assert.IsType<Border>(Assert.Single(blocks));
        var code = Assert.IsType<SelectableTextBlock>(border.Child);
        Assert.Equal(payload, code.Text);
        Assert.True(code.Inlines == null || code.Inlines.Count == 0);
        code.RaiseEvent(new PointerReleasedEventArgs(code, new Pointer(1, PointerType.Mouse, true), code,
            default, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
        Assert.Equal(0, opened);
    }
}
