using System.IO.Compression;
using System.Text;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class ModsPreservationTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "mods-preservation-" + Guid.NewGuid().ToString("N"));
    public ModsPreservationTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"mods\":null}")]
    [InlineData("[]")]
    [InlineData("{\"mods\":[null]}")]
    public void Existing_invalid_sidecar_is_not_empty_success(string text)
    {
        File.WriteAllText(InstalledModsStore.GetSidecarPath(root), text);
        Assert.ThrowsAny<Exception>(() => new InstalledModsStore().Load(root));
        Assert.Equal(text, File.ReadAllText(InstalledModsStore.GetSidecarPath(root)));
    }

    [Fact]
    public void Save_revalidates_corruption_introduced_after_load()
    {
        var store = new InstalledModsStore();
        var doc = store.Load(root);
        File.WriteAllText(InstalledModsStore.GetSidecarPath(root), "{broken");
        Assert.ThrowsAny<Exception>(() => store.Save(root, doc));
        Assert.Equal("{broken", File.ReadAllText(InstalledModsStore.GetSidecarPath(root)));
    }

    [Fact]
    public void Valid_external_repair_can_be_reloaded()
    {
        var store = new InstalledModsStore();
        File.WriteAllText(InstalledModsStore.GetSidecarPath(root), "null");
        Assert.ThrowsAny<Exception>(() => store.Load(root));
        File.WriteAllText(InstalledModsStore.GetSidecarPath(root), "{\"mods\":[]}");
        Assert.Empty(store.Load(root).Mods);
        store.Save(root, new());
        Assert.Empty(new InstalledModsStore().Load(root).Mods);
    }

    [Fact]
    public void Unreadable_existing_sidecar_retains_access_error()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = InstalledModsStore.GetSidecarPath(root);
        File.WriteAllText(path, "{\"mods\":[]}");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try { Assert.Throws<UnauthorizedAccessException>(() => new InstalledModsStore().Load(root)); }
        finally { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Theory]
    [InlineData("original", "same.zip")]
    [InlineData(null, "missing.zip")]
    public void Explicit_identity_never_uses_another_only_file(string? id, string name)
    {
        var package = Package();
        var record = Record(package); record.DownloadFileId = id; record.DownloadFileName = name;
        Assert.Null(ModDownloadFileSelection.FindCatalogFile(package, record));
        Assert.Empty(ModDownloadFileSelection.ResolveFilesToUpdate(package, [record]));
        Assert.False(ModDownloadFileSelection.IsRecordUpdateAvailable(record, package));
        var picker = Assert.Single(ModDownloadFileSelection.ResolveInstalledFilesForPicker(package, [record]));
        Assert.Equal(id ?? "", picker.Id); Assert.Equal(name, picker.FileName);
        Assert.False(ModDownloadFileSelection.IsFileInstalled(package.DownloadFiles[0], [record]));
    }

    [Fact]
    public void Source_identity_is_part_of_package_ownership()
    {
        var p = Package(); var record = Record(p); record.SourceKey = "other";
        Assert.False(ModCatalogListBuilder.RecordMatchesPackage(record, p));
    }

    [Theory]
    [InlineData("download")]
    [InlineData("parse")]
    [InlineData("cancel")]
    public async Task Failed_replacement_preserves_prior_bytes_and_exact_manifest(string failure)
    {
        var p = Package(); var provider = new Provider(); var installer = new ModInstallService(new([provider]));
        await installer.InstallAsync(root, "mods", p, provider, p.DownloadFiles[0]);
        var before = File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root));
        provider.Failure = failure;
        await Assert.ThrowsAnyAsync<Exception>(() => installer.InstallAsync(root, "mods", p, provider, p.DownloadFiles[0]));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
        Assert.Equal(before, File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root)));
        Assert.Single(new ModInstallService(new([provider])).LoadInstalled(root).Mods);
    }

    [Fact]
    public async Task Sibling_collision_is_rejected_without_changes()
    {
        var p = Package(); var provider = new Provider(); var installer = new ModInstallService(new([provider]));
        await installer.InstallAsync(root, "mods", p, provider, p.DownloadFiles[0]);
        var before = File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root));
        var other = Package("other");
        await Assert.ThrowsAnyAsync<Exception>(() => installer.InstallAsync(root, "mods", other, provider, other.DownloadFiles[0]));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
        Assert.Equal(before, File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root)));
    }

    [Fact]
    public async Task Initial_provider_failure_is_retryable_at_first_page()
    {
        var provider = new Provider { Failure = "list" }; var loader = new ModCatalogLoader(new([provider]));
        var session = await loader.LoadBrowseSessionAsync(Sources(), null);
        Assert.False(Assert.Single(session.SourceStates).IsDone); Assert.True(session.CanLoadMore);
        Assert.NotNull(session.SourceStates[0].GetType().GetProperty("Error")?.GetValue(session.SourceStates[0]));
        provider.Failure = null;
        var retry = await loader.LoadMoreBrowseSessionAsync(session);
        Assert.Single(retry.Packages); Assert.Null(provider.Cursors.Last());
    }

    [Fact]
    public async Task Failed_page_preserves_cursor_rows_and_retry_dedupes()
    {
        var provider = new Provider(); var loader = new ModCatalogLoader(new([provider]));
        var session = await loader.LoadSearchSessionAsync(Sources(), null, "  needle  ");
        provider.Failure = "list";
        var failed = await loader.LoadMoreSearchSessionAsync(session);
        Assert.Equal("page-2", Assert.Single(failed.SourceStates).NextPageToken);
        Assert.False(failed.SourceStates[0].IsDone); Assert.Single(failed.Packages);
        Assert.Equal("needle", failed.SearchQuery);
        provider.Failure = null;
        var retry = await loader.LoadMoreSearchSessionAsync(failed);
        Assert.Single(retry.Packages); Assert.Equal("page-2", provider.Cursors.Last());
    }

    [Fact]
    public async Task Legacy_list_api_does_not_hide_provider_failure()
    {
        var provider = new Provider { Failure = "list" }; var loader = new ModCatalogLoader(new([provider]));
        await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadAllPackagesAsync(Sources()));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_empty_success()
    {
        var provider = new Provider { Failure = "cancel-list" }; var loader = new ModCatalogLoader(new([provider]));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.LoadSearchSessionAsync(Sources(), null, "needle", cancellationToken: cts.Token));
    }

    internal static GameModSource[] Sources() => [new() { Provider = "fixture", SourceUrl = "source" }];
    internal static ModPackage Package(string id = "package") => new()
    {
        ProviderId = "fixture", SourceKey = "source", Id = id, FullName = "Author-" + id, Owner = "Author", Name = id,
        LatestVersion = new() { Version = "2", DownloadUrl = "https://example.invalid/archive" },
        DownloadFiles = [new() { Id = "different", FileName = "same.zip", Version = "2", DownloadUrl = "https://example.invalid/archive" }]
    };
    internal static InstalledModRecord Record(ModPackage p) => new() { Provider = p.ProviderId, SourceKey = p.SourceKey, Id = p.Id, FullName = p.FullName, Version = "1" };
    internal static byte[] Zip(string name = "payload.txt", string content = "old")
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(content);
        return ms.ToArray();
    }
    internal sealed class Provider : IModProvider
    {
        public string? Failure; public byte[] Bytes = Zip(); public List<string?> Cursors = [];
        public Dictionary<string, byte[]> Archives = [];
        public string? FailUrl;
        public ModListOptions? LastOptions;
        public string Id => "fixture"; public string DisplayName => "Fixture";
        public bool SupportsPagedListing => true; public bool SupportsRemoteSearch => true;
        public bool TryParseSource(string url, out ModSourceRef source) { source = new() { ProviderId = Id, SourceKey = url, SourceUrl = url, DisplayLabel = "Fixture" }; return true; }
        public void ForceRefreshOnNextList() { }
        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef s, ModListOptions? o = null, CancellationToken cancellationToken = default)
        { if (Failure == "list") throw new IOException("Controlled listing failure"); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ModPackage>>([Package()]); }
        public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef s, string? cursor, int size, ModListOptions? o = null, CancellationToken cancellationToken = default)
        { LastOptions = o; Cursors.Add(cursor); if (Failure == "list") throw new IOException("Controlled listing failure"); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new ModPackagePage { Packages = [Package()], NextPageToken = cursor == null ? "page-2" : null }); }
        public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef s, string q, string? c, int z, ModListOptions? o = null, CancellationToken cancellationToken = default) => ListPackagesPageAsync(s, c, z, o, cancellationToken);
        public Task<Stream> DownloadAsync(ModPackageVersion v, IProgress<double>? p = null, CancellationToken cancellationToken = default)
        { if (Failure == "download" || FailUrl == v.DownloadUrl) throw new IOException("Controlled download failure"); if (Failure == "cancel") throw new OperationCanceledException(); return Task.FromResult<Stream>(new MemoryStream(Failure == "parse" ? Encoding.UTF8.GetBytes("not an archive") : Archives.GetValueOrDefault(v.DownloadUrl, Bytes))); }
        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();
        public Task<string?> GetReadmeAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
        public Task<string?> GetChangelogAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
    }
}
