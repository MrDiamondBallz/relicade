using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    [Fact]
    public void Cover_columns_keep_a_readable_single_card_on_a_narrow_desktop()
    {
        var panel = new CardColumnsPanel { MinColumnWidth = 280, MinColumns = 1, Spacing = 20 };
        Assert.Equal(1, panel.ColumnsFor(440)); Assert.Equal(440, panel.ColumnWidthFor(440));
        Assert.Equal(3, panel.ColumnsFor(974)); Assert.True(panel.ColumnWidthFor(974) >= 280);
    }
}
