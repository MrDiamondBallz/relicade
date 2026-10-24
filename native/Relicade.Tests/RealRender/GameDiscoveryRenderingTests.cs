using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class GameDiscoveryRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(820, 680, false, false)]
    [InlineData(540, 480, false, false)]
    [InlineData(540, 480, true, false)]
    [InlineData(540, 480, true, true)]
    public async Task Real_catalog_dialog_keeps_bounded_results_and_explicit_actions_visible(int width, int height, bool light, bool longTitle)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var dialog = new GameDiscoveryDialog(GameDiscoveryIndex.Bundled, _ => throw new Exception("Render never searches online"),
                _ => throw new Exception("Render never opens a browser")) { Width = width, Height = height };
            dialog.Resources["ThemeBase"] = new SolidColorBrush(Color.Parse(light ? "#E5E5E5" : "#111211"));
            dialog.Resources["ThemeText"] = new SolidColorBrush(Color.Parse(light ? "#111211" : "#F1F2EA"));
            try
            {
                dialog.Show(); dialog.SearchBox.Text = longTitle ? "Q111540656" : "Pikmin 3 Deluxe";
                for (var i = 0; i < 3; i++) { dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
                Assert.Single(dialog.Page.Items); Assert.Equal(1, dialog.Results.ItemCount);
                dialog.Results.SelectedIndex = 0; dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var searchBottom = dialog.SearchBox.TranslatePoint(new Point(0, dialog.SearchBox.Bounds.Height), dialog)!.Value.Y;
                var resultsTop = dialog.Results.TranslatePoint(default, dialog)!.Value.Y;
                var resultsBottom = dialog.Results.TranslatePoint(new Point(0, dialog.Results.Bounds.Height), dialog)!.Value.Y;
                var summaryTop = dialog.SummaryScroll.TranslatePoint(default, dialog)!.Value.Y;
                var summaryBottom = dialog.SummaryScroll.TranslatePoint(new Point(0, dialog.SummaryScroll.Bounds.Height), dialog)!.Value.Y;
                Assert.True(searchBottom <= resultsTop, $"Search/result overlap: {searchBottom}/{resultsTop}");
                Assert.True(resultsBottom <= summaryTop, $"Result/summary overlap: {resultsBottom}/{summaryTop}");
                Assert.True(dialog.SummaryScroll.Viewport.Height > 0);
                foreach (var button in new[] { dialog.CloseButton, dialog.PreviousButton, dialog.NextButton, dialog.FindProjectsButton, dialog.SourceButton })
                {
                    var point = button.TranslatePoint(default, dialog)!.Value;
                    Assert.InRange(point.X, -0.1, dialog.ClientSize.Width - button.Bounds.Width + 0.1);
                    Assert.InRange(point.Y, -0.1, dialog.ClientSize.Height - button.Bounds.Height + 0.1);
                    Assert.True(summaryBottom <= point.Y, $"Summary/button overlap: {summaryBottom}/{point.Y}");
                    Assert.True(button.Bounds.Height >= 44);
                }
                Assert.True(dialog.Results.Bounds.Height >= 70);
                Assert.Contains("not verified", dialog.Detail.Text);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(dialog.FindProjectsButton));
                var output = Path.Combine(Path.GetTempPath(), "relicade-game-discovery-render"); Directory.CreateDirectory(output);
                using var bitmap = await DetailsHeaderContrastRenderingTests.Frame(dialog);
                using var file = new FileStream(Path.Combine(output, $"games-{width}-{height}-{light}-{longTitle}-{Guid.NewGuid():N}.png"), FileMode.CreateNew);
                bitmap.Save(file);
            }
            finally { dialog.Close(); }
            return 0;
        }, CancellationToken.None);
    }
}
