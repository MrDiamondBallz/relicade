using Relicade.Core.Services;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Relicade.Models;
namespace Relicade.Services;

/// <summary>Small, explicitly credited artwork shipped for an exact catalog game.</summary>
public static class ReviewedGameArtwork
{
    public const string TaiseiEntryId = "k177mxt12q61yv9fsvf6wvh1pn8f4n2n";
    public const string TaiseiUrl = "avares://Relicade/Assets/GameArt/taisei-elly-afensorm-cover.png";
    public const string TaiseiAttribution = "Taisei Project: Elly portrait by afensorm, CC BY 4.0. Cropped and resized; not a gameplay screenshot. " +
        "https://github.com/taisei-project/taisei-rawmedia/blob/d3dd520f3f1a18ee958cd60d4bf2c88ea93530c6/gfx/dialog/afens/elly.kra · https://creativecommons.org/licenses/by/4.0/";
    public static string? ForEntry(QuiverCatalogApp entry) =>
        entry.Id == TaiseiEntryId && entry.Slug == "taisei-project" ? TaiseiUrl : null;
    public static bool IsReference(string entryId, string url, string variant) =>
        entryId == TaiseiEntryId && url == TaiseiUrl && variant == "portrait";
    public static bool IsGame(GameInfo game) => game.CatalogEntryId == TaiseiEntryId &&
        RepositorySourceHelper.IsGitHub(game.RepositorySource) &&
        string.Equals(game.Repository, "taisei-project/taisei", StringComparison.OrdinalIgnoreCase);

    internal static Bitmap LoadTaisei()
    {
        // The only resource URI is compiled here, never taken from remote paths.
        // Validate the small shipped representation before decoding it.
        using var source = AssetLoader.Open(new Uri(TaiseiUrl));
        var bytes = new byte[236502]; var count = source.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count != 236501 || !Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, count))).Equals(
            "bafc99557dda5099707cd16ce61aef271d19db99e8e457914e70838ef998df35", StringComparison.OrdinalIgnoreCase) ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)) != 960 || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)) != 540)
            throw new InvalidDataException("The reviewed Taisei artwork does not match its shipped representation.");
        using var stream = new MemoryStream(bytes, 0, count, writable: false);
        return new Bitmap(stream);
    }
}
