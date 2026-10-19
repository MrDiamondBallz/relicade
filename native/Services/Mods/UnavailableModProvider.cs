namespace Relicade.Services.Mods;

/// <summary>Keeps invalid configured sources visible as errors alongside usable providers.</summary>
internal sealed class UnavailableModProvider(string id, string reason) : IModProvider
{
    public string Id => id;
    public string DisplayName => id;
    public bool SupportsPagedListing => false;
    public bool SupportsRemoteSearch => false;
    public bool TryParseSource(string url, out ModSourceRef source)
    { source = new() { ProviderId = id, SourceKey = url, SourceUrl = url, DisplayLabel = id }; return true; }
    public void ForceRefreshOnNextList() { }
    public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef source, ModListOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException(reason);
    public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef source, string? pageToken, int pageSize, ModListOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException(reason);
    public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef source, string query, string? pageToken, int pageSize, ModListOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException(reason);
    public Task<Stream> DownloadAsync(ModPackageVersion version, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException(reason);
    public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();
    public Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default) => throw new NotSupportedException(reason);
    public Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default) => throw new NotSupportedException(reason);
}
