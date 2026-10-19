using System.Net;
using System.Security.Cryptography;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class ArtworkAcceptanceTests
{
    private readonly ITestOutputHelper _output;
    public ArtworkAcceptanceTests(ITestOutputHelper output) => _output = output;
    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7S8AAAAASUVORK5CYII=");
    private sealed class Fixture(bool unavailable) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (unavailable) throw new HttpRequestException("Offline synthetic artwork fixture");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pixel) });
        }
    }
    private sealed class MissingImage : IAsyncImageLoader
    {
        public Task<Bitmap?> ProvideImageAsync(string url) => Task.FromResult<Bitmap?>(null);
        public void Dispose() { }
    }

    [AvaloniaFact]
    public async Task Missing_or_unreachable_art_shows_explicit_unavailable_label()
    {
        var image = new AdvancedImage((Uri?)null) { Source = "https://artwork.example.test/missing.png", Loader = new MissingImage(), Width = 240, Height = 180 };
        var window = new Window { Content = image };
        try
        {
            window.Show();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            image.IsLoading.Should().BeFalse();
            image.CurrentImage.Should().BeNull();
            image.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.IsVisible && t.Text == "Artwork unavailable");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Remote_art_is_cached_for_restart_without_network_and_uncached_offline_art_is_not_fabricated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "relicade-art-acceptance-" + Guid.NewGuid().ToString("N"));
        const string url = "https://artwork.example.test/real-source-metadata.png";
        try
        {
            using var online = new Fixture(false);
            using (var loader = new LauncherArtworkLoader(Path.Combine(directory, "Images"), new HttpClient(online)))
                (await loader.ProvideImageAsync(url)).Should().NotBeNull();
            online.Requests.Should().Be(1);
            File.ReadAllBytes(LauncherArtworkLoader.CachedPath(directory, url)).Should().Equal(Pixel);
            using var offline = new Fixture(true);
            using (var loader = new LauncherArtworkLoader(Path.Combine(directory, "Images"), new HttpClient(offline)))
            {
                (await loader.ProvideImageAsync(url)).Should().NotBeNull("restart must use the genuine cached bytes");
                offline.Requests.Should().Be(0);
                (await loader.ProvideImageAsync(url + "/not-cached")).Should().BeNull("an absent picture is not replaced with invented game art");
            }
            File.Exists(LauncherArtworkLoader.CachedPath(directory, url + "/not-cached")).Should().BeFalse();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    [Trait("Category", "LiveArtwork")]
    public async Task Explicit_live_artwork_source_loads_caches_and_reopens_offline()
    {
        var url = Environment.GetEnvironmentVariable("RELICADE_ARTWORK_SMOKE_URL");
        if (string.IsNullOrWhiteSpace(url)) Assert.Skip("Set RELICADE_ARTWORK_SMOKE_URL explicitly for a real external artwork acceptance smoke.");
        Uri.TryCreate(url, UriKind.Absolute, out var source).Should().BeTrue();
        source!.Scheme.Should().Be("https");
        var directory = Environment.GetEnvironmentVariable("RELICADE_ARTWORK_SMOKE_CACHE") ??
            Path.Combine(Path.GetTempPath(), "relicade-live-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var loader = new LauncherArtworkLoader(Path.Combine(directory, "Images")))
        {
            (await loader.ProvideImageAsync(url!)).Should().NotBeNull("normal game cards retain real source artwork");
            var image = await loader.ProvideImageAsync(LauncherArtworkLoader.FullSize(url)!);
            image.Should().NotBeNull("the explicitly selected real artwork must decode, not a generic tile");
            // Avalonia.Headless uses a 1x1 dummy drawing implementation. Do not mistake its
            // PixelSize for actual image dimensions; real-renderer decoding is a separate smoke.
        }
        var cache = LauncherArtworkLoader.CachedPath(directory, LauncherArtworkLoader.BannerUrl(url!));
        if (!File.Exists(cache)) cache = LauncherArtworkLoader.CachedPath(directory, url!);
        File.Exists(cache).Should().BeTrue();
        var bytes = File.ReadAllBytes(cache);
        bytes.Length.Should().BeGreaterThan(1024, "the live response must be artwork bytes, not the synthetic one-pixel fixture");
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        _output.WriteLine($"Runtime-only artwork from {url}: {bytes.Length} bytes; SHA256 {digest}");
        using var offline = new Fixture(true);
        using (var loader = new LauncherArtworkLoader(Path.Combine(directory, "Images"), new HttpClient(offline)))
        {
            (await loader.ProvideImageAsync(url!)).Should().NotBeNull();
            (await loader.ProvideImageAsync(LauncherArtworkLoader.FullSize(url)!)).Should().NotBeNull();
        }
        offline.Requests.Should().Be(0);
    }
}
