using System.Text.Json.Serialization;
using Relicade.Services;
using Relicade.Core.Services;
using Avalonia.Media.Imaging;

namespace Relicade.Models;

public partial class GameInfo
{
    private CatalogCover? _catalogCover;
    private string? _resolvedCatalogCover;

    public CatalogCover? CatalogCover
    {
        get => _catalogCover;
        set
        {
            if (_catalogCover == value) return;
            _catalogCover = value;
            _resolvedCatalogCover = null;
            NotifyCardArtwork();
        }
    }

    [JsonIgnore]
    public string CardImageUrl => !HasCustomIcon && _catalogCover?.Matches(this) == true &&
        _resolvedCatalogCover != null && (_resolvedCatalogCover == _catalogCover.Url || File.Exists(_resolvedCatalogCover))
        ? _resolvedCatalogCover : IconUrl;

    [JsonIgnore]
    public string CardArtworkSource => !HasCustomIcon && _catalogCover?.Matches(this) == true &&
        _resolvedCatalogCover != null && (_resolvedCatalogCover == _catalogCover.Url || File.Exists(_resolvedCatalogCover))
        ? $"Game cover from SteamGridDB ({_catalogCover.Variant}). Source: {_catalogCover.Url}" : "";

    private void NotifyCardArtwork()
    {
        DispatchPropertyChanged(nameof(CardImageUrl));
        DispatchPropertyChanged(nameof(CardArtworkSource));
    }

    internal async Task LoadCatalogCoverAsync(string cacheDirectory, bool allowDownload, CancellationToken cancellationToken)
    {
        var cover = _catalogCover;
        if (cover?.Matches(this) != true || HasCustomIcon) return;
        cancellationToken.ThrowIfCancellationRequested();
        var sharedPaths = new[] { LauncherArtworkLoader.CardUrl(cover.Url), cover.Url }
            .Distinct().Select(url => LauncherArtworkLoader.CachedPath(cacheDirectory, url));
        var retainedInvalidCache = false;
        foreach (var path in sharedPaths)
        {
            try
            {
                PortableInstallationTransaction.CheckLinks(path);
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                PortableInstallationTransaction.RequireRegularEvidenceFile(path);
                using var stream = File.OpenRead(path);
                if (stream.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("Unsupported cached cover size.");
                var header = new byte[12];
                var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
                if (!(read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) &&
                    !(read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255) &&
                    !(read == 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8)))
                    throw new InvalidDataException("Unsupported cached cover header.");
                stream.Position = 0;
                // Validate locally without asking the shared loader to refetch a bad cache.
                using var decoded = new Bitmap(stream);
                if (decoded.PixelSize.Width < 1 || decoded.PixelSize.Height < 1) throw new InvalidDataException("Invalid cached cover.");
                cancellationToken.ThrowIfCancellationRequested();
                if (_catalogCover != cover || !cover.Matches(this)) return;
                _resolvedCatalogCover = path;
                NotifyCardArtwork();
                return;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                retainedInvalidCache = true;
            }
        }
        // Do not delete or overwrite an unknown/corrupt cache entry to make a cover appear.
        if (retainedInvalidCache) return;
        if (!allowDownload) return;
        try
        {
            // The shared loader owns the bitmap/cache, not this library entry.
            var bitmap = await AsyncImageLoader.ImageLoader.AsyncImageLoader.ProvideImageAsync(cover.Url)
                .WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_catalogCover != cover || !cover.Matches(this) || HasCustomIcon) return;
            _resolvedCatalogCover = bitmap != null ? cover.Url : null;
            NotifyCardArtwork();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException or InvalidOperationException or NotSupportedException or TaskCanceledException)
        {
            // An unavailable cover must not hide the existing icon or block Add.
            if (_catalogCover != cover) return;
            _resolvedCatalogCover = null;
            NotifyCardArtwork();
        }
    }
}
