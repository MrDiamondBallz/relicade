using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class FreedoomDataDownloadTests
{
    [Fact]
    public async Task Verified_archive_data_and_original_notices_are_retained_and_reused_without_another_request()
    {
        using var f = new Fixture(); var first = await f.Download();
        Assert.False(first.Reused); Assert.Equal(f.Data, File.ReadAllBytes(first.Source));
        Assert.Equal(f.Zip, File.ReadAllBytes(first.Archive)); Assert.Equal(1, f.Calls);
        using (var zip = ZipFile.OpenRead(first.Archive)) Assert.NotNull(zip.GetEntry("license.txt"));
        var second = await f.Download(); Assert.True(second.Reused); Assert.Equal(first.Source, second.Source); Assert.Equal(1, f.Calls);
        Assert.Equal(2, Directory.GetFiles(f.Cache).Length);
    }

    [Theory]
    [InlineData("archive-hash")][InlineData("size")][InlineData("data-hash")][InlineData("notice-hash")]
    public async Task Wrong_pinned_content_never_produces_a_data_source(string mismatch)
    {
        using var f = new Fixture();
        if (mismatch == "archive-hash") f.Definition = f.Definition with { Sha256 = new string('0', 64) };
        if (mismatch == "size") f.Definition = f.Definition with { Length = f.Zip.Length - 1 };
        if (mismatch == "data-hash") f.Definition = f.Definition with { Data = f.Definition.Data with { Sha256 = new string('0', 64) } };
        if (mismatch == "notice-hash") f.Definition = f.Definition with { Notices = [f.Definition.Notices[0] with { Sha256 = new string('0', 64) }] };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Download());
        Assert.False(File.Exists(Path.Combine(f.Cache, "freedoom2.wad")));
        Assert.Empty(Directory.GetFiles(f.Cache, "*.part"));
    }

    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("case")]
    public async Task Archive_member_identity_is_exact_and_unambiguous(string change)
    {
        using var f = new Fixture(change);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Download());
        Assert.False(File.Exists(Path.Combine(f.Cache, "freedoom2.wad")));
    }

    [Theory]
    [InlineData("https://evil.example/data")][InlineData("http://github.com/data")]
    [InlineData("https://user:password@github.com/data")][InlineData("https://github.com:8443/data")]
    [InlineData("https://127.0.0.1/data")]
    public async Task Redirect_outside_official_https_boundary_is_rejected_before_destination_request(string url)
    {
        using var f = new Fixture(); f.Response = _ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(url) } };
        await Assert.ThrowsAsync<IOException>(() => f.Download()); Assert.Equal(1, f.Calls);
        Assert.Empty(Directory.GetFiles(f.Cache));
    }

    [Fact]
    public async Task Official_asset_redirect_is_followed_without_authentication_or_cookies()
    {
        using var f = new Fixture();
        f.Response = request =>
        {
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            return request.RequestUri!.Host == "github.com"
                ? new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://release-assets.githubusercontent.com/fixture") } }
                : new(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Zip) };
        };
        var result = await f.Download(); Assert.Equal(2, f.Calls); Assert.Equal(f.Data, File.ReadAllBytes(result.Source));
    }

    [Fact]
    public async Task Redirect_loop_is_bounded_and_never_creates_an_archive()
    {
        using var f = new Fixture(); f.Response = _ => new(HttpStatusCode.Redirect) { Headers = { Location = f.Definition.Url } };
        await Assert.ThrowsAsync<IOException>(() => f.Download()); Assert.Equal(4, f.Calls); Assert.Empty(Directory.GetFiles(f.Cache));
    }

    [Theory]
    [InlineData("archive")][InlineData("data")][InlineData("unknown")]
    public async Task Existing_conflicting_or_unknown_cache_bytes_are_preserved(string conflict)
    {
        using var f = new Fixture(); await f.Download();
        var path = Path.Combine(f.Cache, conflict switch { "archive" => "freedoom-0.13.0.zip", "data" => "freedoom2.wad", _ => "retained.part" });
        byte[] retained = [6, 7, 8]; File.WriteAllBytes(path, retained);
        if (conflict == "unknown") await Assert.ThrowsAsync<IOException>(() => f.Download());
        else await Assert.ThrowsAsync<InvalidDataException>(() => f.Download());
        Assert.Equal(retained, File.ReadAllBytes(path)); Assert.Equal(1, f.Calls);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Managed_cache_root_or_descendant_refuses_before_download_or_child_creation(bool descendant)
    {
        using var own = new OwnedSetupFixture(); await own.Initialize(); using var f = new Fixture();
        f.Cache = descendant ? Path.Combine(own.Live, "download-cache") : own.Live;
        var manifest = File.ReadAllBytes(Path.Combine(own.Live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName));
        await Assert.ThrowsAnyAsync<IOException>(() => f.Download()); Assert.Equal(0, f.Calls);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(own.Live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName)));
        if (descendant) Assert.False(Directory.Exists(f.Cache));
    }

    [Fact]
    public async Task Cancellation_before_start_has_no_network_or_cache_mutation()
    {
        using var f = new Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Download(cancel.Token));
        Assert.Equal(0, f.Calls); Assert.False(Directory.Exists(f.Cache));
    }

    internal sealed class Fixture : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "relicade-free-data-" + Guid.NewGuid().ToString("N"));
        internal string Cache; internal readonly byte[] Data; internal readonly byte[] Zip;
        internal FreeDataArchive Definition; internal int Calls;
        internal Func<HttpRequestMessage, HttpResponseMessage>? Response;
        readonly HttpClient client;
        internal Fixture(string? alteration = null, byte[]? data = null)
        {
            Data = data ?? [1, 2, 3, 4, 5];
            Cache = Path.Combine(root, "cache"); Directory.CreateDirectory(root);
            byte[] notice = "Synthetic license fixture"u8.ToArray();
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                void Add(string name, byte[] data) { using var output = zip.CreateEntry(name).Open(); output.Write(data); }
                Add("license.txt", notice);
                if (alteration != "missing") Add(alteration == "case" ? "DATA.WAD" : "data.wad", Data);
                if (alteration == "duplicate") Add("data.wad", Data);
            }
            Zip = bytes.ToArray();
            Definition = new(new Uri("https://github.com/freedoom/freedoom/releases/download/v0.13.0/freedoom-0.13.0.zip"), Zip.Length, Hash(Zip),
                new("data.wad", Data.Length, Hash(Data)), [new("license.txt", notice.Length, Hash(notice))]);
            client = new HttpClient(new Handler(this));
        }
        internal Task<FreeDataDownloadResult> Download(CancellationToken token = default, IProgress<double>? progress = null) => FreedoomDataDownload.DownloadAsync(client, Definition, Cache, progress, token);
        internal static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
        public void Dispose() { client.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        sealed class Handler(Fixture owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            { cancellationToken.ThrowIfCancellationRequested(); owner.Calls++; return Task.FromResult(owner.Response?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent(owner.Zip) }); }
        }
    }
}
