using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Relicade.Models;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class PortableRollbackBaselineTests
{
    [AvaloniaTheory]
    [InlineData("ListViewTemplate")][InlineData("GridViewTemplate")][InlineData("CompactGridViewTemplate")]
    public async Task Actual_native_library_offers_previous_payload_for_readable_retained_generation(string template)
    {
        using var s = new PortableBackupCleanupBaselineScope(windowsPayload: OperatingSystem.IsWindows());
        await s.Install("v1"); await s.Install("v2");
        var current = s.Tx.ReadCommitted()!;
        Assert.Equal("v2", current.SelectedVersion);
        var retained = Path.Combine(s.Tx.OperationsPath, current.OperationId, "previous");
        Assert.Equal("v1", File.ReadAllText(Path.Combine(retained, "version.txt")));
        File.WriteAllText(Path.Combine(s.Live, "LastPlayed.txt"), "CURRENT_DATA_NOT_PREVIOUS");
        var before = s.Snapshot();
        var view = new LibraryView();
        var card = ((IDataTemplate)view.Resources[template]!).Build(s.Game)!; card.DataContext = s.Game;
        var host = new Window { Content = card, Width = 1000, Height = 750 };
        try
        {
            host.Show(); host.UpdateLayout();
            var items = card.GetVisualDescendants().OfType<Control>().Prepend(card)
                .Select(c => c.ContextMenu).OfType<ContextMenu>().SelectMany(m => m.Items.OfType<MenuItem>()).ToArray();
            Assert.NotEmpty(items);
            Assert.Contains(items, i => i.Header?.ToString() == "Clean redundant backups…");
            Assert.Equal(before, s.Snapshot());
            Assert.Contains(items, i => i.Header?.ToString() == "Restore previous portable version…");
        }
        finally { host.Close(); }
    }
}
