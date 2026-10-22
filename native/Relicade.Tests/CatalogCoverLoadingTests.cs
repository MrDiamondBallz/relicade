using AsyncImageLoader;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class CatalogCoverLoadingTests
{
    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7S8AAAAASUVORK5CYII=");
    private sealed class Loader(Func<Task<Bitmap?>> result) : IAsyncImageLoader
    {
        public List<string> Requests { get; } = [];
        public Task<Bitmap?> ProvideImageAsync(string url) { Requests.Add(url); return result(); }
        public void Dispose() { }
    }
    private static GameInfo Game() => new() { Name = "Synthetic cover", CatalogEntryId = "entry", GameIconUrl = CatalogLibraryCoverTests.Icon,
        CatalogCover = new("entry", CatalogLibraryCoverTests.Header, CatalogLibraryCoverTests.Icon, "header") };
    private static string Root() { var p = Path.Combine(Path.GetTempPath(), "relicade-cover-loading-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cache_only_uses_valid_local_cover_without_network(bool resized)
    {
        var root = Root(); var previous = ImageLoader.AsyncImageLoader;
        using var fake = new Loader(() => throw new InvalidOperationException("No network allowed")); ImageLoader.AsyncImageLoader = fake;
        try
        {
            var path = LauncherArtworkLoader.CachedPath(root, resized ? LauncherArtworkLoader.CardUrl(CatalogLibraryCoverTests.Header) : CatalogLibraryCoverTests.Header);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, Pixel);
            using var game = Game();
            await game.LoadCatalogCoverAsync(root, false, TestContext.Current.CancellationToken);
            Assert.Equal(path, game.CardImageUrl); Assert.Contains(CatalogLibraryCoverTests.Header, game.CardArtworkSource);
            Assert.Empty(fake.Requests); Assert.Equal(Pixel, File.ReadAllBytes(path));
            var custom = Path.Combine(root, "custom.png"); File.WriteAllBytes(custom, Pixel); game.CustomIconPath = custom;
            Assert.Equal(custom, game.CardImageUrl); Assert.Empty(game.CardArtworkSource);
            game.CustomIconPath = null; game.GameIconUrl = "player-edited";
            Assert.Equal(game.IconUrl, game.CardImageUrl); Assert.Empty(game.CardArtworkSource);
        }
        finally { ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("directory")]
    [InlineData("symlink")]
    public async Task Cache_only_missing_or_unsafe_cover_falls_back_and_preserves_unknown_bytes(string kind)
    {
        var root = Root(); var previous = ImageLoader.AsyncImageLoader;
        using var fake = new Loader(() => throw new InvalidOperationException("No network allowed")); ImageLoader.AsyncImageLoader = fake;
        try
        {
            var path = LauncherArtworkLoader.CachedPath(root, CatalogLibraryCoverTests.Header); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var target = Path.Combine(root, "retained.png"); File.WriteAllBytes(target, Pixel);
            if (kind == "corrupt") File.WriteAllText(path, "retained invalid cache");
            if (kind == "directory") Directory.CreateDirectory(path);
            if (kind == "symlink") File.CreateSymbolicLink(path, target);
            using var game = Game();
            // An invalid cache is retained even on an explicit online enrichment request.
            await game.LoadCatalogCoverAsync(root, kind != "missing", TestContext.Current.CancellationToken);
            Assert.Equal(game.IconUrl, game.CardImageUrl); Assert.Empty(game.CardArtworkSource); Assert.Empty(fake.Requests);
            Assert.Equal(Pixel, File.ReadAllBytes(target));
            if (kind == "corrupt") Assert.Equal("retained invalid cache", File.ReadAllText(path));
            if (kind == "directory") Assert.True(Directory.Exists(path));
            if (kind == "symlink") Assert.NotNull(new FileInfo(path).LinkTarget);
        }
        finally { ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("offline")]
    public async Task Explicit_enrichment_uses_shared_loader_and_keeps_icon_on_failure(string outcome)
    {
        var root = Root(); var previous = ImageLoader.AsyncImageLoader;
        using var bitmap = new Bitmap(new MemoryStream(Pixel));
        using var fake = new Loader(() => outcome == "offline" ? Task.FromException<Bitmap?>(new HttpRequestException("synthetic offline")) : Task.FromResult(outcome == "success" ? bitmap : null));
        ImageLoader.AsyncImageLoader = fake;
        try
        {
            using var game = Game(); await game.LoadCatalogCoverAsync(root, true, TestContext.Current.CancellationToken);
            Assert.Equal(new[] { CatalogLibraryCoverTests.Header }, fake.Requests);
            Assert.Equal(outcome == "success" ? CatalogLibraryCoverTests.Header : game.IconUrl, game.CardImageUrl);
            Assert.Equal(outcome == "success", !string.IsNullOrEmpty(game.CardArtworkSource));
        }
        finally { ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("identity")]
    [InlineData("icon")]
    [InlineData("custom")]
    [InlineData("cover")]
    public async Task Late_shared_result_cannot_publish_into_cancelled_or_changed_entry(string change)
    {
        var root = Root(); var previous = ImageLoader.AsyncImageLoader;
        var pending = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bitmap = new Bitmap(new MemoryStream(Pixel)); using var fake = new Loader(() => pending.Task);
        using var cancellation = new CancellationTokenSource(); ImageLoader.AsyncImageLoader = fake;
        try
        {
            using var game = Game(); var loading = game.LoadCatalogCoverAsync(root, true, cancellation.Token);
            Assert.Single(fake.Requests);
            if (change == "cancel") cancellation.Cancel();
            if (change == "identity") game.CatalogEntryId = "other-entry";
            if (change == "icon") game.GameIconUrl = "player-icon";
            if (change == "cover") game.CatalogCover = null;
            if (change == "custom") { var p = Path.Combine(root, "custom.png"); File.WriteAllBytes(p, Pixel); game.CustomIconPath = p; }
            pending.SetResult(bitmap);
            if (change == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
            else await loading;
            Assert.Equal(game.IconUrl, game.CardImageUrl); Assert.Empty(game.CardArtworkSource);
        }
        finally { pending.TrySetResult(null); ImageLoader.AsyncImageLoader = previous; Directory.Delete(root, true); }
    }
}
