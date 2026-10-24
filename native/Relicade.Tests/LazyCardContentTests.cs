using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Relicade.Views;

namespace Relicade.Tests;

public class LazyCardContentTests
{
    [AvaloniaTheory]
    [InlineData(0)][InlineData(double.NaN)]
    public void A_cold_auto_height_card_inside_a_clipped_parent_materializes_its_visible_menu(double placeholder)
    {
        var card = new LazyCardContent
        {
            Group = "cold-visible-" + Guid.NewGuid().ToString("N"), PlaceholderHeight = placeholder,
            DataContext = "visible app",
            ContentTemplate = new FuncDataTemplate<string>((_, _) => new Button
            {
                Width = 160, Height = 100, Content = "Options",
                ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "Restore previous portable version…" } } }
            })
        };
        var parent = new Border { ClipToBounds = true, Width = 200, Child = card };
        var window = new Window { Content = new StackPanel { Children = { parent } }, Width = 400, Height = 300 };
        try
        {
            window.Show(); window.UpdateLayout();
            for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            Assert.True(card.IsBuilt, "Visible cold content must not depend on a previous card warming the height cache.");
            var button = Assert.IsType<Button>(card.Presenter!.Child);
            Assert.Contains(button.ContextMenu!.Items.OfType<MenuItem>(), item => item.Header?.ToString() == "Restore previous portable version…");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Cards_are_built_near_the_screen_and_dropped_far_from_it_without_moving()
    {
        var cards = Enumerable.Range(0, 30).Select(i => new LazyCardContent
        {
            Group = nameof(Cards_are_built_near_the_screen_and_dropped_far_from_it_without_moving),
            PlaceholderHeight = 100,
            DataContext = $"app {i}",
            ContentTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 100 }),
        }).ToList();
        var stack = new StackPanel();
        stack.Children.AddRange(cards);
        var scroller = new ScrollViewer { Content = stack };
        var window = new Window { Content = scroller, Width = 400, Height = 300 };
        window.Show();
        void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }
        Settle();

        // On screen (0-300) and within half a screen below it are built; the rest are not.
        cards.Take(4).Should().OnlyContain(c => c.IsBuilt);
        cards.Skip(6).Should().OnlyContain(c => !c.IsBuilt);
        stack.Bounds.Height.Should().Be(3000, "cards not built yet still take their place");

        scroller.Offset = new Vector(0, 2000);
        Settle();
        cards.Skip(20).Take(3).Should().OnlyContain(c => c.IsBuilt);
        cards.Take(10).Should().OnlyContain(c => !c.IsBuilt, "cards well above the screen are let go");
        stack.Bounds.Height.Should().Be(3000);
    }

    [AvaloniaFact]
    public void An_auto_placeholder_height_lays_out_as_empty_instead_of_failing()
    {
        // Compact grid cards on a phone bind PlaceholderHeight to an auto (NaN) card size.
        var cards = Enumerable.Range(0, 30).Select(i => new LazyCardContent
        {
            Group = nameof(An_auto_placeholder_height_lays_out_as_empty_instead_of_failing),
            PlaceholderHeight = double.NaN,
            DataContext = $"app {i}",
            ContentTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 100 }),
        }).ToList();
        var wrap = new WrapPanel();
        wrap.Children.AddRange(cards);
        var window = new Window { Content = new ScrollViewer { Content = wrap }, Width = 400, Height = 300 };

        var show = () =>
        {
            window.Show();
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        };

        show.Should().NotThrow();
        cards.Should().OnlyContain(c => double.IsFinite(c.DesiredSize.Height));
    }
}
