using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Relicade.Services;

// Original-game records have source identity; they are not installed apps or verified play paths.
internal sealed record GameWork(string Id, string Title)
{
    internal string SourceUrl => "https://www.wikidata.org/wiki/" + Id;
    internal const string Readiness = "Metadata only · play path not verified";
}
internal sealed record GameDiscoveryPage(IReadOnlyList<GameWork> Items, int Total, int Offset, int PageSize)
{
    internal bool HasPrevious => Offset > 0;
    internal bool HasNext => Offset + Items.Count < Total;
}
internal sealed class GameDiscoveryIndex
{
    internal const string SnapshotSha256 = "5b9d6c569e2a29f15fe4e7ac1adb24611bedac0d566a02d427c8b262aba839fb";
    internal const int PageSize = 50;
    static readonly Lazy<GameDiscoveryIndex> bundled = new(LoadBundled);
    internal static GameDiscoveryIndex Bundled => bundled.Value;
    readonly (GameWork Work, string Search)[] rows;
    internal int Count => rows.Length;
    internal GameDiscoveryIndex(IEnumerable<GameWork> records)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        rows = records.Select(work =>
        {
            if (!ValidId(work.Id) || string.IsNullOrWhiteSpace(work.Title) || work.Title.Length > 500 ||
                work.Title.Any(char.IsControl) || !ids.Add(work.Id))
                throw new InvalidDataException("Invalid or repeated game source identity.");
            return (Work: work, Search: Fold(work.Title));
        }).OrderBy(row => row.Search, StringComparer.Ordinal).ThenBy(row => row.Work.Id, StringComparer.Ordinal).ToArray();
        if (rows.Length > 100_000) throw new InvalidDataException("The game index exceeds its supported record limit.");
    }
    static bool ValidId(string id) => id.Length is >= 2 and <= 18 && id[0] == 'Q' && id[1] is >= '1' and <= '9' && id.Skip(2).All(c => c is >= '0' and <= '9');
    static string Fold(string text) => text.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    internal GameDiscoveryPage Search(string query, int offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (query.Length > 128) throw new ArgumentException("Search is limited to 128 characters.", nameof(query));
        var terms = Fold(query.Trim()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var page = new List<GameWork>(PageSize); var total = 0;
        foreach (var (work, search) in rows)
        {
            if (!terms.All(term => search.Contains(term, StringComparison.Ordinal) || work.Id.Equals(term, StringComparison.Ordinal))) continue;
            if (total >= offset && page.Count < PageSize) page.Add(work);
            total++;
        }
        return new(page.AsReadOnly(), total, offset, PageSize);
    }
    static GameDiscoveryIndex LoadBundled()
    {
        using var stream = typeof(GameDiscoveryIndex).Assembly.GetManifestResourceStream("Relicade.CatalogData.wikidata-games-20261024.jsonl")
            ?? throw new IOException("The bundled game catalog is missing. Project discovery remains available.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            if (bytes.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Bundled game catalog exceeds its size limit.");
            bytes.Write(buffer, 0, read);
        }
        var data = bytes.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(data)) != SnapshotSha256)
            throw new InvalidDataException("Bundled game catalog integrity check failed. No network replacement was requested.");
        using var text = new StringReader(new UTF8Encoding(false, true).GetString(data));
        var records = new List<GameWork>(10_000); string? line;
        while ((line = text.ReadLine()) != null)
        {
            if (records.Count >= 10_000 || line.Length > 2048) throw new InvalidDataException("Invalid bundled game catalog bounds.");
            using var row = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
            var root = row.RootElement;
            var id = root.GetProperty("qid").GetString()!;
            if (root.GetProperty("sourceType").GetString() != "wdt:P31/wd:Q7889" ||
                root.GetProperty("entityUri").GetString() != "http://www.wikidata.org/entity/" + id)
                throw new InvalidDataException("Unexpected bundled game source assertion.");
            records.Add(new(id, root.GetProperty("labelEn").GetString()!));
        }
        if (records.Count != 10_000) throw new InvalidDataException("The bundled game catalog is incomplete.");
        return new(records);
    }
}
