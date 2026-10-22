using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text;
using Relicade.Core.Services;
using System.Text.Json.Nodes;

namespace Relicade.Services;

public sealed record QuiverCatalogPage<T>(List<T> Items, string? NextCursor, bool IsDone);

public sealed class QuiverCatalogGame
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>An original game on quiverlauncher.com: what it is, and every app that plays it.</summary>
public sealed class QuiverCatalogGameDetail
{
    public QuiverCatalogGameInfo Game { get; set; } = new();
    public List<QuiverCatalogApp> Entries { get; set; } = [];
}

public sealed class QuiverCatalogGameInfo
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Artwork { get; set; }
    public QuiverCatalogArt? LibraryArt { get; set; }
    public List<string> OriginalSystems { get; set; } = [];
}

public sealed class QuiverCatalogArt
{
    public string? Capsule { get; set; }
    public string? Header { get; set; }
    public string? Hero { get; set; }
    public string? Logo { get; set; }
}

public sealed class QuiverCatalogModSource
{
    public string? Provider { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed class QuiverCatalogMods
{
    public string? Path { get; set; }
    public string? Layout { get; set; }
    public List<QuiverCatalogModSource> Sources { get; set; } = [];
}

public sealed class QuiverCatalogLauncher
{
    public string FolderName { get; set; } = "";
    public List<string> FilesToAdd { get; set; } = [];
    public string? ReleaseAssetFilter { get; set; }
    public QuiverCatalogMods? Mods { get; set; }
}

public sealed class QuiverCatalogVerified
{
    public string Version { get; set; } = "";
    /// <summary>Rebuilt under the same tag (a nightly): a new build doesn't change <see cref="Version"/>.</summary>
    public bool Rolling { get; set; }
}

/// <summary>An app in the quiverlauncher.com catalog (the REST API's App object).</summary>
public sealed class QuiverCatalogApp
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public List<QuiverCatalogGame> Games { get; set; } = [];
    public List<string> Consoles { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string? Artwork { get; set; }
    public bool ArtworkFromGame { get; set; }
    public QuiverCatalogArt? LibraryArt { get; set; }
    public QuiverCatalogLauncher Launcher { get; set; } = new();
    public string ProjectType { get; set; } = "";
    public List<string> SupportedOS { get; set; } = [];
    /// <summary>"none", "assisted" or "generated".</summary>
    public string? AiLevel { get; set; }
    public QuiverCatalogDeveloper? Developer { get; set; }
    public int Recommended { get; set; }
    public int ReportIssues { get; set; }
    public int ReportBroken { get; set; }
    // Times are JavaScript milliseconds, which can have a fraction.
    public double AddedAt { get; set; }
    public double? LastReleaseAt { get; set; }
    public string? LastReleaseVersion { get; set; }
    public QuiverCatalogVerified? Verified { get; set; }
}

public sealed class QuiverCatalogDeveloper
{
    public string Name { get; set; } = "";
}

public sealed class QuiverCatalogProject
{
    public string Provider { get; set; } = "";
    public string? Repository { get; set; }
    public string? Author { get; set; }
}

public sealed class QuiverCatalogDetail
{
    public QuiverCatalogApp Entry { get; set; } = new();
    public QuiverCatalogProject Project { get; set; } = new();
}

public sealed class QuiverCatalogReview
{
    public string Author { get; set; } = "";
    public string Result { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Platform { get; set; }
    public string? Version { get; set; }
    public double CreatedAt { get; set; }
}

public sealed class QuiverCatalogConsole
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
}

public sealed class QuiverCatalogFacets
{
    public int Total { get; set; }
    public List<QuiverCatalogConsole> Consoles { get; set; } = [];
}

public sealed class QuiverCatalogReadme
{
    public string Markdown { get; set; } = "";
    public string? RawBase { get; set; }
}

/// <summary>An app in the release status feed: enough to match a library app to the catalog.</summary>
public sealed class QuiverCatalogStatus
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? Repository { get; set; }
    public QuiverCatalogVerified? Verified { get; set; }
    /// <summary>The newest release the developer published, verified or not.</summary>
    public QuiverCatalogVerified? LatestUpstream { get; set; }
}

public sealed class QuiverCatalogAsset
{
    public string Filename { get; set; } = "";
    /// <summary>Where the file downloads from: the release's own file link, not a GitHub API address.</summary>
    public string? Url { get; set; }
    /// <summary>"sha256:" and the hex digest of the file the source catalog saw when the release came out.</summary>
    public string? Checksum { get; set; }
    /// <summary>VirusTotal's verdict on this file, once the site has one.</summary>
    public QuiverCatalogScan? Scan { get; set; }
}

public sealed class QuiverCatalogScan
{
    public string Verdict { get; set; } = "";
    public string? Engines { get; set; }
}

/// <summary>A release as the site judges it: verified, unverified or blocked, with the files it pinned.</summary>
public sealed class QuiverCatalogRelease
{
    public string? ReleaseId { get; set; }
    public string Version { get; set; } = "";
    public string State { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Prerelease { get; set; }
    public double? ReleasedAt { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<QuiverCatalogAsset> Assets { get; set; } = [];
    public QuiverCatalogScan? Scan { get; set; }
    public double? CheckEndsAt { get; set; }
    /// <summary>
    /// The repository rebuilds this release under the same tag (a nightly): its checksums are of the newest build
    /// the source catalog saw, which can be older than the one upstream.
    /// </summary>
    public bool Rolling { get; set; }
}

public sealed record QuiverCatalogQuery(string? Search = null, string? Os = null, string? Console = null,
    string? ProjectType = null, string Sort = "added", string? Ai = null);

/// <summary>Reads the public quiverlauncher.com catalog API. Every call is a plain GET.</summary>
public sealed class QuiverCatalogClient(HttpClient http, string? baseUrl = null)
{
    public const string DefaultBaseUrl = "https://api.quiverlauncher.com/api/v1";
    /// <summary>
    /// The same API on the Convex deployment's own host. api.quiverlauncher.com is a custom domain in front of it, and
    /// when that domain's TLS handshake fails for a player the deployment's host still answers.
    /// </summary>
    public const string FallbackBaseUrl = "https://famous-wildebeest-660.convex.site/api/v1";
    public const string WebsiteUrl = "https://quiverlauncher.com";
    public const int PageSize = 48;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _base = (baseUrl ?? Environment.GetEnvironmentVariable("RELICADE_CATALOG_API") ?? DefaultBaseUrl).TrimEnd('/');
    private bool _usingFallback;

    public static string AppPageUrl(string slug) => $"{WebsiteUrl}/apps/{Uri.EscapeDataString(slug)}";
    public static string ReviewPageUrl(string slug) => AppPageUrl(slug) + "?tab=how-it-runs";

    /// <summary>The most apps the API returns in one page.</summary>
    public const int MaxPageSize = 100;

    public Task<QuiverCatalogPage<QuiverCatalogApp>> GetAppsAsync(QuiverCatalogQuery query, string? cursor, CancellationToken token, int limit = PageSize)
    {
        var parameters = new List<string> { $"limit={limit}" };
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
        Add("search", query.Search);
        Add("os", query.Os);
        // "maker:Nintendo" selects every console a maker made.
        if (query.Console?.StartsWith("maker:", StringComparison.Ordinal) == true) Add("maker", query.Console["maker:".Length..]);
        else Add("console", query.Console);
        Add("projectType", query.ProjectType);
        Add("ai", query.Ai);
        // A search is ordered by relevance; the API ignores sort then.
        if (string.IsNullOrWhiteSpace(query.Search)) Add("sort", query.Sort);
        Add("cursor", cursor);
        return GetPageAsync<QuiverCatalogApp>("/apps?" + string.Join('&', parameters), token);
    }

    public async Task<QuiverCatalogFacets> GetFacetsAsync(CancellationToken token) =>
        await GetAsync<QuiverCatalogFacets>("/facets", token) ?? new();

    /// <summary>A game and the apps that play it; null when the catalog has no such game.</summary>
    public Task<QuiverCatalogGameDetail?> GetGameAsync(string slug, CancellationToken token) =>
        GetAsync<QuiverCatalogGameDetail>($"/games/{Uri.EscapeDataString(slug)}", token);

    public async Task<QuiverCatalogDetail> GetDetailAsync(string slug, CancellationToken token) =>
        await GetAsync<QuiverCatalogDetail>($"/apps/{Uri.EscapeDataString(slug)}", token)
        ?? throw new HttpRequestException("This app is no longer in the catalog.", null, HttpStatusCode.NotFound);

    public Task<QuiverCatalogPage<QuiverCatalogReview>> GetReviewsAsync(string slug, int limit, CancellationToken token) =>
        GetPageAsync<QuiverCatalogReview>($"/apps/{Uri.EscapeDataString(slug)}/reviews?limit={limit}", token);

    public Task<QuiverCatalogReadme?> GetReadmeAsync(string slug, CancellationToken token) =>
        GetAsync<QuiverCatalogReadme>($"/apps/{Uri.EscapeDataString(slug)}/readme", token);

    public Task<QuiverCatalogPage<QuiverCatalogStatus>> GetReleaseStatusAsync(string? cursor, CancellationToken token) =>
        GetPageAsync<QuiverCatalogStatus>("/release-status?limit=100" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), token);

    /// <summary>Every release the site knows for an app, newest first, each verified, unverified or blocked.</summary>
    public async Task<QuiverCatalogPage<QuiverCatalogRelease>> GetReleaseHistoryAsync(string slug, CancellationToken token)
    {
        // A deadline and byte budget refuse oversized/incomplete reads rather than silently cutting off pages.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        var budget = new HistoryReadBudget(64 * 1024 * 1024);
        var releases = new List<QuiverCatalogRelease>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        var receivedPage = false;
        try
        {
            while (true)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                var path = $"/apps/{Uri.EscapeDataString(slug)}/release-history?limit=100" +
                    (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                var page = await GetAsync<JsonObject>(path, lifetime.Token, budget).ConfigureAwait(false);
                receivedPage |= page != null;
                if (page == null)
                    throw new HttpRequestException("Release history is no longer available.", null, HttpStatusCode.NotFound);
                if (page["items"] is not JsonArray rows)
                    throw new InvalidDataException("Release history has no valid items array.");
                Exception? invalidRow = null;
                foreach (var row in rows)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var item = row?.Deserialize<QuiverCatalogRelease>(Json)
                            ?? throw new JsonException("Release history contains a null record.");
                        releases.Add(item);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                    {
                        // Inspect every sibling so one malformed row cannot erase a well-formed block.
                        invalidRow ??= ex;
                    }
                }
                if (invalidRow != null) throw new JsonException("Release history contains an invalid record.", invalidRow);
                if (page["isDone"] is not JsonValue completion || !completion.TryGetValue<bool>(out var done))
                    throw new InvalidDataException("Release history has no valid completion marker.");
                if (done) break;
                cursor = page?["nextCursor"]?.GetValue<string>();
                if (string.IsNullOrEmpty(cursor) || !cursors.Add(cursor))
                    throw new InvalidDataException("Catalog release-history pagination did not advance.");
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return new(await Task.Run(() => MergeHistory(releases, lifetime.Token), lifetime.Token).ConfigureAwait(false), null, true);
        }
        catch (Exception ex)
        {
            // Partial approval is never a completed history. Retain only denial evidence for the safety caller.
            if (token.IsCancellationRequested) throw new CanceledHistoryException(releases, ex, token);
            throw new IncompleteHistoryException(releases, receivedPage, ex);
        }
    }

    internal interface IHistoryReadEvidence
    {
        IReadOnlyList<QuiverCatalogRelease> KnownBlocks { get; }
    }

    private static IReadOnlyList<QuiverCatalogRelease> SnapshotBlocks(IEnumerable<QuiverCatalogRelease> releases) =>
        releases.Where(r => r is { State: "blocked" } && !string.IsNullOrWhiteSpace(r.Version))
            .Select(r => new QuiverCatalogRelease { Version = r.Version, State = "blocked", Reasons = (r.Reasons ?? []).ToList() }).ToArray();

    internal sealed class CanceledHistoryException : OperationCanceledException, IHistoryReadEvidence
    {
        public IReadOnlyList<QuiverCatalogRelease> KnownBlocks { get; }
        public CanceledHistoryException(IEnumerable<QuiverCatalogRelease> releases, Exception inner, CancellationToken token)
            : base("Release-history reading was canceled.", inner, token) => KnownBlocks = SnapshotBlocks(releases);
    }

    internal sealed class IncompleteHistoryException : IOException, IHistoryReadEvidence
    {
        public IReadOnlyList<QuiverCatalogRelease> KnownBlocks { get; }
        public bool NoHistoryPageReceived { get; }
        public IncompleteHistoryException(IEnumerable<QuiverCatalogRelease> releases, bool receivedPage, Exception inner)
            : base("The complete release history could not be checked. Try again later.", inner)
        {
            NoHistoryPageReceived = !receivedPage;
            KnownBlocks = SnapshotBlocks(releases);
        }
    }

    internal static bool SameHistoryRecord(QuiverCatalogRelease left, QuiverCatalogRelease right)
    {
        var a = JsonSerializer.SerializeToNode(left, Json)!.AsObject();
        var b = JsonSerializer.SerializeToNode(right, Json)!.AsObject();
        a.Remove("version"); b.Remove("version");
        return JsonNode.DeepEquals(a, b);
    }

    private static List<QuiverCatalogRelease> MergeHistory(List<QuiverCatalogRelease> releases, CancellationToken token)
    {
        var result = new List<QuiverCatalogRelease>();
        foreach (var release in releases)
        {
            token.ThrowIfCancellationRequested();
            if (release == null || string.IsNullOrWhiteSpace(release.Version)) throw new InvalidDataException("Release history contains an unnamed version.");
            if (release.Assets == null || release.Reasons == null || release.Assets.Any(a => a == null || string.IsNullOrWhiteSpace(a.Filename)))
                throw new InvalidDataException("Release history contains invalid file or review metadata.");
            // Tags are source identities, not semantic-version ordering keys. Build labels and case remain intact.
            // Only an identical repeated record is redundant; retain conflicting revisions and their file metadata.
            if (!result.Any(r => r.Version == release.Version && SameHistoryRecord(r, release))) result.Add(release);
        }
        return result;
    }

    internal sealed class HistoryReadBudget(int remaining)
    {
        public async Task<string> ReadAsync(HttpContent content, CancellationToken token)
        {
            if (content.Headers.ContentLength > remaining) throw new InvalidDataException("Release history exceeds the metadata download limit.");
            using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[32768];
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), token).ConfigureAwait(false);
                if (count == 0) break;
                if (count > remaining) throw new InvalidDataException("Release history exceeds the metadata download limit.");
                remaining -= count; output.Write(buffer, 0, count);
            }
            return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        }
    }

    /// <summary>Reporting is disabled. A catalog read does not authorize sending diagnostics.</summary>
    public Task ReportDownloadProblemAsync(string slug, string version, string fileName, string problem, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    private async Task<QuiverCatalogPage<T>> GetPageAsync<T>(string path, CancellationToken token)
    {
        var page = await GetAsync<JsonObject>(path, token);
        var items = page?["items"]?.Deserialize<List<T>>(Json) ?? [];
        var next = page?["nextCursor"]?.GetValue<string>();
        var done = page?["isDone"]?.GetValue<bool>() ?? true;
        return new(items, done ? null : next, done || next == null);
    }

    /// <summary>
    /// Returns null for a 404; any other failure throws with the site's own message. When api.quiverlauncher.com can't
    /// be reached at all (a failed TLS handshake or connection, or the 499 its proxy sometimes answers), asks the
    /// deployment's own host instead, and keeps asking it from then on.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken token, HistoryReadBudget? budget = null) where T : class
    {
        if (_usingFallback) return await GetAsync<T>(FallbackBaseUrl, path, token, budget).ConfigureAwait(false);
        try
        {
            return await GetAsync<T>(_base, path, token, budget).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (_base == DefaultBaseUrl && Unreachable(ex) && !token.IsCancellationRequested)
        {
            T? answer = null;
            var failed = false;
            try
            {
                answer = await GetAsync<T>(FallbackBaseUrl, path, token, budget).ConfigureAwait(false);
            }
            catch (Exception fallback) when (fallback is HttpRequestException or TimeoutException or JsonException)
            {
                failed = true;
            }
            // The player's real problem is the first one; say that.
            if (failed) ExceptionDispatchInfo.Throw(ex);
            _usingFallback = true;
            Telemetry.Current.Track("catalog_fallback_used", new Dictionary<string, object?>
            {
                ["reason"] = Telemetry.ReasonOf(ex),
                ["cause"] = Telemetry.CauseOf(ex),
            });
            return answer;
        }
    }

    /// <summary>The request never got a real answer: no status at all, or the proxy's 499.</summary>
    private static bool Unreachable(HttpRequestException ex) => ex.StatusCode is null || (int)ex.StatusCode == 499;

    /// <summary>What went wrong, in words: the wrapped cause when the message only says to look at it.</summary>
    public static string Explain(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner?.InnerException != null && inner.Message.Contains("inner exception", StringComparison.OrdinalIgnoreCase))
            inner = inner.InnerException;
        return inner != null && ex.Message.Contains("inner exception", StringComparison.OrdinalIgnoreCase)
            ? $"{ex.Message.Replace(", see inner exception.", ":", StringComparison.OrdinalIgnoreCase)} {inner.Message}"
            : ex.Message;
    }

    private async Task<T?> GetAsync<T>(string baseUrl, string path, CancellationToken token, HistoryReadBudget? budget = null) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.GetAsync(baseUrl + path, budget == null ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            var body = budget == null ? await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)
                : await budget.ReadAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ErrorMessage(body) ?? $"quiverlauncher.com answered {(int)response.StatusCode}.", null, response.StatusCode);
            var parsed = JsonSerializer.Deserialize<T>(body, Json);
            if (budget != null && parsed == null) throw new JsonException("Release history is not a JSON object.");
            return parsed;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("quiverlauncher.com took too long to answer.");
        }
    }

    private static string? ErrorMessage(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
}

/// <summary>Turns a catalog app into a library entry, through the same parser as app list files.</summary>
public static class QuiverCatalogMapping
{
    /// <summary>The app as an entry of an app list file (the format of apps.json).</summary>
    public static JsonObject ToListEntry(QuiverCatalogApp app, QuiverCatalogProject project)
    {
        var folder = FolderFor(app);
        var entry = new JsonObject
        {
            ["catalogEntryId"] = string.IsNullOrWhiteSpace(app.Id) ? null : app.Id,
            ["catalogCover"] = CatalogCover.FromEntry(app)?.ToJson(),
            ["name"] = app.Name,
            ["project"] = app.ProjectName,
            ["folderName"] = folder,
            // An icon borrowed from the game would show another app's art in the library.
            ["appIconUrl"] = app.ArtworkFromGame ? null : app.Artwork,
            ["tags"] = new JsonArray(app.Tags.Select(t => (JsonNode?)t).ToArray()),
            ["filesToAdd"] = new JsonArray(app.Launcher.FilesToAdd.Select(f => (JsonNode?)f).ToArray()),
        };
        // A "manual" project has no repository, which the parser treats as a manually managed app.
        if (project.Provider is "github" or "gitlab" && !string.IsNullOrWhiteSpace(project.Repository))
        {
            entry["repository"] = project.Repository.Trim();
            if (project.Provider == "gitlab") entry["repositorySource"] = "gitlab";
            if (!string.IsNullOrWhiteSpace(app.Launcher.ReleaseAssetFilter)) entry["releaseAssetFilter"] = app.Launcher.ReleaseAssetFilter;
        }
        if (app.Launcher.Mods is { Path: { Length: > 0 } path } mods)
        {
            entry["mods"] = new JsonObject
            {
                ["path"] = path,
                ["layout"] = mods.Layout,
                ["sources"] = new JsonArray(mods.Sources.Select(s => (JsonNode?)new JsonObject
                {
                    ["provider"] = s.Provider,
                    ["sourceUrl"] = s.SourceUrl,
                }).ToArray()),
            };
        }
        return entry;
    }

    /// <summary>The app's folder: the catalog's, or its slug when the catalog gives none.</summary>
    public static string FolderFor(QuiverCatalogApp app) => string.IsNullOrWhiteSpace(app.Launcher.FolderName)
        ? Mods.GameModsConfig.SanitizeFolderName(app.Slug)
        : app.Launcher.FolderName.Trim();

    public static Models.GameInfo ToGameInfo(AppCatalogService parser, QuiverCatalogApp app, QuiverCatalogProject project) =>
        parser.ParseAppsFromJson(new JsonObject { ["apps"] = new JsonArray(ToListEntry(app, project)) }.ToJsonString()).Single();
}
