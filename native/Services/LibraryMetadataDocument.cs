using Relicade.Core.Services;
using Relicade.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Relicade.Services;

// A separate export-only format: never feed this into the live apps.json parser.
internal sealed record LibraryMetadataEntry(string Provider, string? Repository, string FolderKey,
    string Name, string? Project, string? CatalogEntryId, string? PreferredRelease,
    string? SkippedRelease, string? AssetFilter);
internal sealed record LibraryMetadataDocument(string Format, int SchemaVersion, string Scope,
    DateTimeOffset ObservedAt, LibraryMetadataEntry[] Entries)
{
    internal const string FormatName = "relicade.library-metadata";
    internal const string ScopeName = "loaded-metadata-only-no-restore";
    internal const int MaxEntries = 10000, MaxBytes = 8 * 1024 * 1024;
    internal const string Disclosure = "This exports a list of currently loaded saved entries, including hidden entries. It is not a backup and cannot restore your library. Original duplicate records already discarded by loading cannot be recovered here. Names, project/repository names, logical folder keys and release labels are included exactly as previewed and may reveal personal information. No files, credentials/settings fields, local installation paths, custom commands, artwork/mod URLs, play history, installation receipts, custom labels/tags or automatic-update preferences are included. Manual entries require relinking; nothing is installed or downloaded.";
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12,
        RespectRequiredConstructorParameters = true,
    };
    internal static LibraryMetadataEntry Capture(GameInfo game) => new(
        game.IsManuallyManaged ? "manual" : game.RepositorySource?.Trim().ToLowerInvariant() is { Length: > 0 } p ? p : "github",
        game.IsManuallyManaged ? null : game.Repository?.Trim(), game.FolderName ?? "", game.Name ?? "", game.Project,
        game.CatalogEntryId, game.IsManuallyManaged ? null : game.PreferredVersion,
        game.IsManuallyManaged ? null : game.SkippedUpdateVersion, game.IsManuallyManaged ? null : game.ReleaseAssetFilter);

    internal static LibraryMetadataDocument Create(IEnumerable<LibraryMetadataEntry> entries, DateTimeOffset observed)
    {
        var rows = entries.Take(MaxEntries + 1).ToArray();
        var document = new LibraryMetadataDocument(FormatName, 1, ScopeName, observed, rows);
        document.Validate(); return document;
    }
    internal byte[] Encode()
    {
        Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(this, Options);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Metadata exceeds the 8 MiB export safety limit; nothing was truncated or saved.");
        return bytes;
    }
    internal static LibraryMetadataDocument Decode(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new InvalidDataException("Invalid metadata document size.");
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        static void Unique(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in value.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate metadata property."); Unique(p.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
        }
        Unique(parsed.RootElement);
        var result = JsonSerializer.Deserialize<LibraryMetadataDocument>(bytes, Options) ?? throw new InvalidDataException("Missing metadata document.");
        result.Validate(); return result;
    }
    void Validate()
    {
        if (Format != FormatName || SchemaVersion != 1 || Scope != ScopeName || ObservedAt == default || Entries == null || Entries.Length > MaxEntries)
            throw new InvalidDataException("Unsupported or incomplete export-only metadata document; no import was attempted.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hostedSelections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catalogOrigins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Entries)
        {
            if (row == null || row.Provider is not ("github" or "gitlab" or "codeberg" or "manual")) throw new InvalidDataException("Unknown metadata provider; no fallback is allowed.");
            Text(row.FolderKey, 120, true); PortableInstallationTransaction.ValidateRelative(row.FolderKey);
            if (row.FolderKey.Contains('/') || row.FolderKey.Contains('\\')) throw new InvalidDataException("A logical folder key must be one component, not a path.");
            Text(row.Name, 240, true); Text(row.Project, 240); Text(row.CatalogEntryId, 240);
            Text(row.PreferredRelease, 240); Text(row.SkippedRelease, 240); Text(row.AssetFilter, 240);
            if (row.Provider == "manual")
            {
                if (row.Repository != null || row.PreferredRelease != null || row.SkippedRelease != null || row.AssetFilter != null)
                    throw new InvalidDataException("Manual metadata cannot claim a hosted release.");
            }
            else
            {
                Text(row.Repository, 480, true);
                var parts = row.Repository!.Split('/');
                if (parts.Length < 2 || parts.Length > 8 || row.Provider != "gitlab" && parts.Length != 2 ||
                    parts.Any(p => p is "." or ".." || !Regex.IsMatch(p, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)))
                    throw new InvalidDataException("Repository identity must contain provider path segments, not a URL or local path.");
            }
            var origin = row.Provider + ":" + row.Repository;
            if (row.Provider != "manual" && !hostedSelections.Add(origin + ":" + (row.AssetFilter ?? "").Trim()))
                throw new InvalidDataException("Ambiguous repository/filter selection; inspect the loaded library before exporting.");
            if (!string.IsNullOrWhiteSpace(row.CatalogEntryId))
            {
                if (catalogOrigins.TryGetValue(row.CatalogEntryId, out var previous) && !string.Equals(previous, origin, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Conflicting catalog provenance; inspect the loaded library before exporting.");
                catalogOrigins[row.CatalogEntryId] = origin;
            }
            var key = row.Provider + ":" + row.Repository + ":" + row.FolderKey;
            if (!keys.Add(key) || !folders.Add(row.FolderKey)) throw new InvalidDataException("Ambiguous duplicate instance or folder key; inspect the loaded library before exporting.");
        }
    }
    static void Text(string? text, int limit, bool required = false)
    {
        if (text == null) { if (required) throw new InvalidDataException("Required metadata is missing."); return; }
        if (text.Length > limit || required && string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl) || text.Contains('\\') ||
            text.Contains("://", StringComparison.Ordinal) || text.Split('/').Any(part => part is "." or "..") || Regex.IsMatch(text, @"(^|\s)(/|[A-Za-z]:|~[/\\])", RegexOptions.CultureInvariant))
            throw new InvalidDataException("A metadata field contains an unsupported length, control character, URL or local-path form. Nothing was silently removed.");
    }
}
