using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

/// <summary>Baseline-compatible functional missing-journey assertion, not a missing-type test.</summary>
public sealed class PortableBackupCleanupUiBaselineTests
{
    [AvaloniaTheory]
    [InlineData("ListViewTemplate")]
    [InlineData("GridViewTemplate")]
    [InlineData("CompactGridViewTemplate")]
    public async Task Actual_library_layout_offers_explicit_cleanup_for_real_redundant_history(string template)
    {
        var fixture = new PortableBackupCleanupFixture();
        await fixture.Generations();
        var plan = fixture.Tx().PreviewRedundantBackups();
        Assert.NotEmpty(plan.Files);
        Assert.True(plan.ReclaimableBytes > 0);
        var before = PortableBackupCleanupFixture.Snapshot(fixture.Root);
        var view = new LibraryView();
        var game = new GameInfo { Name = "Synthetic selected instance", Repository = "fixture/cleanup", FolderName = "live", InstallPath = fixture.Live };
        var card = ((IDataTemplate)view.Resources[template]!).Build(game)!;
        card.DataContext = game;
        var host = new Window { Content = card, Width = 1000, Height = 750 };
        try
        {
            host.Show(); host.UpdateLayout();
            var menus = card.GetVisualDescendants().OfType<Control>().Prepend(card).Select(c => c.ContextMenu).OfType<ContextMenu>().ToArray();
            var items = menus.SelectMany(m => m.Items.OfType<MenuItem>()).ToArray();
            Assert.NotEmpty(items); // Actual existing menu was built, not an empty/missing-control failure.
            Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(fixture.Root));
            Assert.Contains(items, i => i.Header?.ToString() == "Clean redundant backups…");
        }
        finally { host.Close(); }
    }
}
