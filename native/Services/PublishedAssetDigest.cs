namespace Relicade.Services;

/// <summary>Syntax validation only. A published digest is not a software safety assessment.</summary>
internal static class PublishedAssetDigest
{
    internal static string? Normalize(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;

    internal static string? FromPublisher(string? value) =>
        value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? Normalize(value["sha256:".Length..]) : null;
}
