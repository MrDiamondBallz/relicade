using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
namespace Relicade.Tests;

public sealed class DiscoverVisualTests
{
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public void Cover_action_remains_large_and_routes_exactly_once(bool inLibrary)
    {
        var item = BrowseItem.FromCatalog(new() { Id = "fixture", Name = "Synthetic project", ProjectType = "ports" }, new Dictionary<string,string>());
        item.InLibrary = inLibrary;
        var card = new BrowseCard { DataContext = item }; var window = new Window { Content = card, Width = 320, Height = 420 };
        var calls = 0; card.AddHandler(BrowseCard.AddRequestedEvent, (_, e) => { Assert.Same(card, e.Source); calls++; });
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var button = card.FindControl<Button>("BrowseCardAddButton")!;
            Assert.True(button.IsVisible); Assert.True(button.IsEnabled); Assert.True(button.Bounds.Height >= 44);
            Assert.Equal(inLibrary ? "Open in Library" : "+ Add", button.Content);
            Assert.Equal(Color.Parse("#111211"), Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, calls);
            Assert.Equal(inLibrary, item.InLibrary);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false, false)][InlineData(false, true)]
    [InlineData(true, false)][InlineData(true, true)]
    public void Feedback_text_keeps_readable_contrast_and_explicit_verdict(bool light, bool broken)
    {
        var background = Color.Parse(light ? "#e5e5e5" : "#18181b");
        var item = BrowseItem.FromCatalog(new() { Id = "fixture", Name = "Synthetic project", ProjectType = "ports",
            Recommended = broken ? 0 : 4, ReportBroken = broken ? 4 : 0 }, new Dictionary<string,string>());
        var card = new BrowseCard { DataContext = item };
        new ThemeEditor(_ => background, (_, _) => { }, new LauncherDialogLifetime(CancellationToken.None, action => action()))
            .ApplyResources(card.Resources, background, Color.Parse("#333333"));
        var window = new Window { Content = card, Width = 320, Height = 420 };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var score = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("browse-score-text"));
            var run = score.Inlines!.OfType<Run>().First();
            Assert.Equal(broken ? "Doesn't run" : "Runs well", run.Text);
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(run.Foreground).Color;
            static double L(Color c)
            {
                static double C(byte b) { var v = b / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
                return 0.2126 * C(c.R) + 0.7152 * C(c.G) + 0.0722 * C(c.B);
            }
            var ratio = (Math.Max(L(foreground), L(background)) + 0.05) / (Math.Min(L(foreground), L(background)) + 0.05);
            Assert.True(ratio >= 4.5, $"Feedback contrast is {ratio:F2}: {foreground} on {background}.");
        }
        finally { window.Close(); }
    }
    [Fact]
    public void Cover_columns_keep_a_readable_single_card_on_a_narrow_desktop()
    {
        var panel = new CardColumnsPanel { MinColumnWidth = 280, MinColumns = 1, Spacing = 20 };
        Assert.Equal(1, panel.ColumnsFor(440)); Assert.Equal(440, panel.ColumnWidthFor(440));
        Assert.Equal(3, panel.ColumnsFor(974)); Assert.True(panel.ColumnWidthFor(974) >= 280);
    }
}
