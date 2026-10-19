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
