using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>A catalog's game-cover reference, separate from the project's own icon.</summary>
public sealed record CatalogCover(string EntryId, string Url, string? IconUrl, string Variant)
{
    // Only the established public image provider's static grid files are eligible.
    // This is URL validation, not a grant of artwork rights or publisher verification.
    private static readonly Regex Grid = new(
        @"\Ahttps://cdn2\.steamgriddb\.com/grid/[a-fA-F0-9]{32}\.(?:png|jpg|jpeg|webp)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(EntryId) && EntryId.Length <= 256 &&
        EntryId == EntryId.Trim() && Variant is "header" or "capsule" &&
        Url is { Length: <= 256 } && Grid.IsMatch(Url);

    public bool Matches(GameInfo app) => IsValid &&
        string.Equals(EntryId, app.CatalogEntryId, StringComparison.Ordinal) &&
        string.Equals(Text(IconUrl), Text(app.GameIconUrl), StringComparison.Ordinal);

    public static CatalogCover? FromEntry(QuiverCatalogApp entry)
    {
        foreach (var (url, variant) in new[] { (entry.LibraryArt?.Header, "header"), (entry.LibraryArt?.Capsule, "capsule") })
        {
            if (url == null) continue;
            var cover = new CatalogCover(entry.Id, url, entry.ArtworkFromGame ? null : entry.Artwork, variant);
            if (cover.IsValid) return cover;
        }
        return null;
    }

    public JsonObject ToJson() => new()
    {
        ["entryId"] = EntryId, ["url"] = Url, ["iconUrl"] = IconUrl, ["variant"] = Variant
    };

    public static CatalogCover? Read(JsonElement app, bool strict)
    {
        if (!app.TryGetProperty("catalogCover", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        try
        {
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count() || value.EnumerateObject().Any(p =>
                p.Name is not ("entryId" or "url" or "iconUrl" or "variant"))) throw new JsonException("Unknown catalog cover shape.");
            string? ReadText(string key) => value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Null ? null : value.GetProperty(key).GetString();
            var cover = new CatalogCover(ReadText("entryId")!, ReadText("url")!, ReadText("iconUrl"), ReadText("variant")!);
            if (!cover.IsValid) throw new JsonException("Invalid catalog cover reference.");
            return cover;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if (strict) throw new JsonException("The library contains unsupported catalog cover metadata; it was not rewritten.", ex);
            return null;
        }
    }

    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
