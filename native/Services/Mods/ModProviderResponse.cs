using System.Text.Json;

namespace Relicade.Services.Mods;

internal static class ModProviderResponse
{
    internal static T ParsePage<T>(string json, string rowsProperty, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty(rowsProperty, out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new JsonException("Mod provider response has no valid package array.");
        if (rows.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.Object))
            throw new JsonException("Mod provider response contains an invalid package row.");
        return JsonSerializer.Deserialize<T>(json, options) ?? throw new JsonException("Mod provider response is null.");
    }
}
