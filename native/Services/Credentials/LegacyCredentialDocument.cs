using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Relicade.Services.Credentials;

internal static class LegacyCredentialDocument
{
    private static readonly string[] Names = ["GitHubApiToken", "GitLabApiToken", "CodebergApiToken"];
    internal static bool HasCredentials(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(property =>
        Names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.ValueKind != JsonValueKind.Null &&
        (property.Value.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(property.Value.GetString())));

    internal const int MaximumBytes = 4 * 1024 * 1024;

    internal static JsonDocument ParseStrict(byte[] bytes)
    {
        try
        {
            if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new JsonException();
            // Only unambiguous UTF-8 JSON, no replacement decoding or BOM/UTF-16 guessing.
            _ = new UTF8Encoding(false, true).GetString(bytes);
            var doc = JsonDocument.Parse(bytes);
            try
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                ValidateObject(doc.RootElement);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (!Names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) throw new JsonException();
                    var token = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    if (!string.IsNullOrEmpty(token)) CredentialIdentity.ValidateToken(token);
                }
                // Validate known nonsecret settings as well; unknown JSON fields remain in the document.
                _ = JsonSerializer.Deserialize<AppSettings>(bytes) ?? throw new JsonException();
                return doc;
            }
            catch { doc.Dispose(); throw; }
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException or CredentialVaultUnavailableException or NotSupportedException)
        { throw new JsonException("The settings document is unsupported or invalid. Repair or restore it privately."); }
    }

    private static void ValidateObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                ValidateObject(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) ValidateObject(value);
        else if (element.ValueKind == JsonValueKind.String)
            _ = element.GetString(); // Reject unpaired escaped UTF-16 even in unknown nonsecret values.
    }

    internal static bool IsCredentialName(string name) => Names.Contains(name, StringComparer.OrdinalIgnoreCase);
    internal static Dictionary<CredentialProvider, string> Tokens(JsonElement root)
    {
        var values = new Dictionary<CredentialProvider, string>();
        foreach (var property in root.EnumerateObject())
        {
            var index = Array.FindIndex(Names, name => name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || property.Value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(property.Value.GetString())) continue;
            values.Add((CredentialProvider)index, property.Value.GetString()!);
        }
        return values;
    }

    /// <summary>Refuse new raw snapshots of sensitive legacy settings; never rewrite the original or scrub corrupt bytes.</summary>
    internal static void RequireSafeSnapshot(Stream source)
    {
        var position = source.Position;
        try
        {
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = reader.ReadToEnd();
            bool sensitive;
            try
            {
                using var document = JsonDocument.Parse(text);
                sensitive = HasCredentials(document.RootElement);
            }
            catch (JsonException)
            {
                // Conservative detection for damaged/UTF-16-without-BOM legacy input. Detection only: never repair these bytes.
                var searchable = Regex.Replace(text.Replace("\0", ""), @"\\u([0-9a-fA-F]{4})",
                    match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
                sensitive = Names.Any(name => searchable.Contains(name, StringComparison.OrdinalIgnoreCase));
            }
            if (sensitive) throw new LegacyCredentialCleanupRequiredException();
        }
        finally { source.Position = position; }
    }
}
