using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relicade.Services.Mods;

public sealed class InstalledModRecord
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("sourceKey")]
    public string SourceKey { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Selected GameBanana (or similar) file id when the package had multiple downloads.</summary>
    [JsonPropertyName("downloadFileId")]
    public string? DownloadFileId { get; set; }

    [JsonPropertyName("downloadFileName")]
    public string? DownloadFileName { get; set; }

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];
}

public sealed class InstalledModsDocument
{
    /// <summary>Distinguishes commits even when selected metadata is unchanged but payload bytes differ.</summary>
    [JsonPropertyName("generation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Generation { get; set; }

    [JsonPropertyName("mods")]
    public List<InstalledModRecord> Mods { get; set; } = [];
}

public sealed class InstalledModsStore
{
    public const string SidecarFileName = ".relicade-mods.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string GetSidecarPath(string installRoot) =>
        Path.Combine(installRoot, SidecarFileName);

    public InstalledModsDocument Load(string installRoot)
    {
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        return LoadCore(installRoot);
    }

    public void Save(string installRoot, InstalledModsDocument document)
    {
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        SaveCore(installRoot, document);
    }

    internal static InstalledModsDocument LoadCore(string installRoot)
    {
        var bytes = ModInstallTransaction.ReadOptional(GetSidecarPath(installRoot));
        return bytes == null ? new() : Parse(bytes);
    }

    internal static InstalledModsDocument Parse(byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
                throw new JsonException("The mod sidecar must contain a mods array.");
            foreach (var mod in mods.EnumerateArray())
            {
                if (mod.ValueKind != JsonValueKind.Object ||
                    !mod.TryGetProperty("provider", out var provider) || provider.ValueKind != JsonValueKind.String ||
                    !mod.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                    !mod.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                    throw new JsonException("The mod sidecar contains an invalid installed record.");
            }
            var document = JsonSerializer.Deserialize<InstalledModsDocument>(bytes, JsonOptions)
                ?? throw new JsonException("The mod sidecar cannot be null.");
            if (document.Mods.Any(m => m == null || m.Provider == null || m.SourceKey == null || m.Id == null ||
                m.Name == null || m.FullName == null || m.Owner == null || m.Version == null || m.Files == null || m.Files.Any(f => f == null)))
                throw new JsonException("Invalid mod record or payload list.");
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The installed mod sidecar needs repair; original data was retained.", ex);
        }
    }

    internal static byte[] Serialize(InstalledModsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        Parse(bytes);
        return bytes;
    }

    internal static void SaveCore(string installRoot, InstalledModsDocument document)
    {
        LoadCore(installRoot); // Revalidate disk while holding the exclusive writer lock.
        ModInstallTransaction.AtomicWrite(GetSidecarPath(installRoot), Serialize(document));
    }

    public InstalledModRecord? Find(
        InstalledModsDocument document,
        string provider,
        string id)
    {
        return document.Mods.FirstOrDefault(m =>
            string.Equals(m.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public InstalledModRecord? FindByFullName(
        InstalledModsDocument document,
        string provider,
        string sourceKey,
        string fullName)
    {
        return document.Mods.FirstOrDefault(m =>
            string.Equals(m.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.FullName, fullName, StringComparison.OrdinalIgnoreCase));
    }
}
