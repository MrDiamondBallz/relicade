namespace Relicade.Services.Mods;

/// <summary>Loads packages across sources without conflating provider failure with exhaustion.</summary>
public sealed class ModCatalogLoader
{
    public const int DefaultPageSize = 30;
    readonly ModProviderRegistry _registry;
    public ModCatalogLoader(ModProviderRegistry registry) => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public IReadOnlyList<(GameModSource Source, ModSourceRef Parsed, IModProvider Provider)> ResolveSources(IEnumerable<GameModSource>? sources)
    {
        var result = new List<(GameModSource, ModSourceRef, IModProvider)>();
        foreach (var source in GameModsConfig.NormalizeSources(sources))
        {
            if (!_registry.TryGet(source.Provider, out var provider))
                provider = new UnavailableModProvider(source.Provider, "The configured mod provider is unavailable.");
            if (!provider.TryParseSource(source.SourceUrl, out var parsed))
            {
                provider = new UnavailableModProvider(source.Provider, "The configured mod source is invalid; repair its URL, then Refresh.");
                provider.TryParseSource(source.SourceUrl, out parsed);
            }
            result.Add((source, parsed, provider));
        }
        return result;
    }
    public bool HasPagedSources(IEnumerable<GameModSource>? sources) => ResolveSources(sources).Any(s => s.Provider.SupportsPagedListing);
    public bool HasRemoteSearchSources(IEnumerable<GameModSource>? sources) => ResolveSources(sources).Any(s => s.Provider.SupportsRemoteSearch);

    public async Task<IReadOnlyList<ModPackage>> LoadAllPackagesAsync(IEnumerable<GameModSource>? sources,
        bool forceRefresh = false, ModListOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packages = new List<ModPackage>();
        foreach (var (_, source, provider) in ResolveSources(sources))
        {
            if (forceRefresh) provider.ForceRefreshOnNextList();
            // This legacy shape cannot represent partial/error state, so errors must propagate.
            var rows = await provider.ListPackagesAsync(source, options ?? ModListOptions.Default, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ModCatalogListBuilder.AppendUniquePackages(packages, rows);
        }
        return packages;
    }

    public Task<ModBrowseSession> LoadBrowseSessionAsync(IEnumerable<GameModSource>? sources, string? sourceFilterKey,
        bool forceRefresh = false, int pageSize = DefaultPageSize, ModListOptions? options = null, CancellationToken cancellationToken = default) =>
        StartAsync(sources, sourceFilterKey, null, forceRefresh, pageSize, options, cancellationToken);

    public Task<ModBrowseSession> LoadSearchSessionAsync(IEnumerable<GameModSource>? sources, string? sourceFilterKey,
        string query, int pageSize = DefaultPageSize, ModListOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return StartAsync(sources, sourceFilterKey, query.Trim(), false, pageSize, options, cancellationToken);
    }
    async Task<ModBrowseSession> StartAsync(IEnumerable<GameModSource>? sources, string? filter, string? query,
        bool forceRefresh, int pageSize, ModListOptions? options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        options ??= ModListOptions.Default;
        var states = ResolveSources(sources)
            .Where(r => string.IsNullOrEmpty(filter) || string.Equals($"{r.Parsed.ProviderId}|{r.Parsed.SourceKey}", filter, StringComparison.OrdinalIgnoreCase))
            .Select(r => new ModSourcePageState(r.Parsed, r.Provider, null, false)).ToList();
        if (forceRefresh) foreach (var state in states) state.Provider.ForceRefreshOnNextList();
        return await LoadMoreBrowseSessionAsync(new ModBrowseSession
        {
            SourceStates = states, IsSearch = query != null, SearchQuery = query,
            SourceFilterKey = filter, Options = options,
        }, pageSize, options, token).ConfigureAwait(false);
    }

    public async Task<ModBrowseSession> LoadMoreBrowseSessionAsync(ModBrowseSession session,
        int pageSize = DefaultPageSize, ModListOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.CanLoadMore) return session;
        // The session is the immutable query/filter generation. Retries never adopt changed UI options.
        options = session.Options;
        var packages = session.Packages.ToList();
        var states = new List<ModSourcePageState>();

        foreach (var state in session.SourceStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.IsDone || state.Error?.Retryable == false) { states.Add(state); continue; }
            try
            {
                ModPackagePage page;
                if (session.IsSearch && state.Provider.SupportsRemoteSearch)
                    page = await state.Provider.SearchPackagesPageAsync(state.Source, session.SearchQuery!, state.NextPageToken, pageSize, options, cancellationToken).ConfigureAwait(false);
                else if (state.Provider.SupportsPagedListing && !session.IsSearch)
                    page = await state.Provider.ListPackagesPageAsync(state.Source, state.NextPageToken, pageSize, options, cancellationToken).ConfigureAwait(false);
                else
                {
                    var listed = await state.Provider.ListPackagesAsync(state.Source, options, cancellationToken).ConfigureAwait(false);
                    var filtered = session.IsSearch ? listed.Where(p => PackageMatchesQuery(p, session.SearchQuery!)).ToList() : listed;
                    page = new() { Packages = filtered, TotalCount = filtered.Count };
                }
                cancellationToken.ThrowIfCancellationRequested();
                ModCatalogListBuilder.AppendUniquePackages(packages, page.Packages);
                states.Add(state with { NextPageToken = page.NextPageToken, IsDone = page.NextPageToken == null, Error = null, TotalCountHint = page.TotalCount });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                states.Add(state with { Error = new(state.Source.ProviderId, state.Source.SourceKey, state.NextPageToken,
                    $"{ex.GetType().Name}: {ex.Message}", Retryable: ex is not NotSupportedException) });
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sum = states.Sum(s => (long)(s.TotalCountHint ?? 0));
        int? total = states.All(s => s.TotalCountHint is >= 0) && sum <= int.MaxValue ? (int)sum : session.TotalCountHint;
        return new()
        {
            Packages = packages, SourceStates = states, TotalCountHint = total, IsSearch = session.IsSearch,
            SearchQuery = session.SearchQuery, SourceFilterKey = session.SourceFilterKey, Options = session.Options,
        };
    }
    public Task<ModBrowseSession> LoadMoreSearchSessionAsync(ModBrowseSession session, int pageSize = DefaultPageSize,
        ModListOptions? options = null, CancellationToken cancellationToken = default) =>
        LoadMoreBrowseSessionAsync(session, pageSize, options, cancellationToken);
    static bool PackageMatchesQuery(ModPackage p, string term) => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        p.Owner.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(term, StringComparison.OrdinalIgnoreCase) || p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase);
}

public sealed class ModBrowseSession
{
    public IReadOnlyList<ModPackage> Packages { get; init; } = [];
    public IReadOnlyList<ModSourcePageState> SourceStates { get; init; } = [];
    public int? TotalCountHint { get; init; }
    public bool IsSearch { get; init; }
    public string? SearchQuery { get; init; }
    public string? SourceFilterKey { get; init; }
    public ModListOptions Options { get; init; } = ModListOptions.Default;
    public bool HasErrors => SourceStates.Any(s => s.Error != null);
    public bool CanLoadMore => SourceStates.Any(s => !s.IsDone && (s.Error == null || s.Error.Retryable));
}
public sealed record ModSourceError(string Provider, string SourceKey, string? AttemptedCursor, string Reason, bool Retryable);
public sealed record ModSourcePageState(ModSourceRef Source, IModProvider Provider, string? NextPageToken, bool IsDone)
{
    public ModSourceError? Error { get; init; }
    public int? TotalCountHint { get; init; }
}
