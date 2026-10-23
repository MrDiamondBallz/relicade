using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class LibraryMetadataPreviewTests
{
    [AvaloniaFact]
    public async Task Inspection_prose_preserves_every_character_without_splitting_ordinary_words()
    {
        await using var f = await LibraryMetadataExportTests.ExportScope.Create();
        using var model = new LibraryMetadataExportViewModel(f.Manager, f.Settings, f.Session,
            () => throw new InvalidOperationException("Inspection must not open a picker."));
        var dialog = new LibraryMetadataExportDialog(model);
        try
        {
            var chunks = dialog.Details.Children.OfType<Button>().Select(b => Assert.IsType<TextBlock>(b.Content)).ToArray();
            Assert.True(chunks.Length > 1);
            Assert.Equal(model.Pages[0], string.Concat(chunks.Select(t => t.Text)));
            for (var i = 1; i < chunks.Length; i++)
                Assert.True(char.IsWhiteSpace(chunks[i - 1].Text![^1]) || char.IsWhiteSpace(chunks[i].Text![0]),
                    "Ordinary disclosure words must not break across separate inspection rows.");
            Assert.All(chunks, text => Assert.Equal(FontWeight.Normal, text.FontWeight));
            Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally { dialog.Close(); model.Close(); await model.Work; }
    }
    [Theory]
    [InlineData("long")][InlineData("combining")][InlineData("whitespace")]
    public void Bounded_inspection_chunks_preserve_long_tokens_and_graphemes(string kind)
    {
        var text = kind switch
        {
            "long" => new string('x', 150),
            "combining" => string.Concat(Enumerable.Repeat("e\u0301", 100)),
            _ => "  One\n\tsecond phrase with spaces   " + new string('z', 80) + " end."
        };
        var chunks = LibraryMetadataExportDialog.InspectionChunks(text).ToArray();
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(System.Globalization.StringInfo.ParseCombiningCharacters(chunk).Length, 1, 64);
            Assert.False(char.GetUnicodeCategory(chunk[0]) == System.Globalization.UnicodeCategory.NonSpacingMark);
        });
    }

}
