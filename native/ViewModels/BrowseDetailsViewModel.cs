using Relicade.Models;
using Relicade.Services;

namespace Relicade.ViewModels;

/// <summary>One player's feedback, laid out like the website's: who, when and where, how it ran, and what they said.</summary>
public sealed record BrowseReviewLine(string Author, string Meta, string Result, string Tone, string Body)
{
    public string Initial => Author.Length > 0 ? Author[..1].ToUpperInvariant() : "?";
    public bool HasBody => Body.Length > 0;
    public bool Positive => Tone == "positive";
    public bool Caution => Tone == "caution";
    public bool Negative => Tone == "negative";
}

/// <summary>
/// An app's details on Browse: what the site knows about it, recent player reviews (read-only;
/// reviews are written on quiverlauncher.com) and its README.
/// </summary>
public sealed class BrowseDetailsViewModel(QuiverCatalogClient client, Func<QuiverCatalogApp, QuiverCatalogProject, GameInfo> toLibraryEntry,
    Func<GameInfo, CancellationToken, Task<DocumentContent>> repositoryReadme,
    Func<GameInfo, CancellationToken, Task<IReadOnlyList<QuiverCatalogRelease>>>? repositoryReleases = null) : ObservableViewModel, IDisposable
{
    public const int ReviewCount = 10;
    private int _generation;
    private BrowseItem? _item;
    private GameInfo? _entry;
    private QuiverCatalogProject? _project;
    private IReadOnlyList<QuiverCatalogRelease>? _releases;
    private bool _releasesMore;
    private string _releasesStatus = "";
    private bool _isLoadingReleases, _canRetryReleases;
    private IReadOnlyList<BrowseReviewLine> _reviews = [];
    private string _reviewsStatus = "";
    private string _error = "";

    public BrowseItem? Item { get => _item; private set => Set(ref _item, value); }
    /// <summary>The app as a library entry; null until the catalog says where it comes from.</summary>
    public GameInfo? Entry { get => _entry; private set => Set(ref _entry, value); }
    /// <summary>Who made the app and where it comes from, once its page has loaded.</summary>
    public QuiverCatalogProject? Project { get => _project; private set => Set(ref _project, value); }
    public IReadOnlyList<BrowseReviewLine> Reviews { get => _reviews; private set => Set(ref _reviews, value); }
    /// <summary>The app's releases, newest first, once the Releases tab has asked for them.</summary>
    public IReadOnlyList<QuiverCatalogRelease>? Releases { get => _releases; private set => Set(ref _releases, value); }
    /// <summary>The site has older releases than the ones shown.</summary>
    public bool ReleasesMore { get => _releasesMore; private set => Set(ref _releasesMore, value); }
    public bool IsLoadingReleases { get => _isLoadingReleases; private set => Set(ref _isLoadingReleases, value); }
    public bool CanRetryReleases { get => _canRetryReleases; private set => Set(ref _canRetryReleases, value); }
    public string ReleasesStatus { get => _releasesStatus; private set => Set(ref _releasesStatus, value); }
    public string ReviewsStatus { get => _reviewsStatus; private set => Set(ref _reviewsStatus, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public DocumentViewModel Readme { get; } = new();
    public string? ReviewUrl => Item?.App is { } app ? QuiverCatalogClient.ReviewPageUrl(app.Slug) : null;

    public async Task OpenAsync(BrowseItem item, CancellationToken token)
    {
        var generation = ++_generation;
        Item = item;
        Entry = item.ListApp;
        Project = null;
        Releases = null;
        ReleasesMore = false;
        ReleasesStatus = "";
        IsLoadingReleases = CanRetryReleases = false;
        Error = "";
        Reviews = [];
        ReviewsStatus = item.App == null ? "" : "Loading reviews…";
        var readme = Readme.OpenAsync(item.Title, "Loading README…", ct => LoadReadmeAsync(item, ct), token);
        if (item.App is { } app)
            await Task.WhenAll(LoadDetailAsync(app, generation, token), LoadReviewsAsync(app, generation, token));
        await readme;
    }

    public void Close()
    {
        ++_generation;
        Readme.Cancel();
        Item = null;
        Entry = null;
        Project = null;
        Releases = null;
        ReleasesStatus = "";
        IsLoadingReleases = CanRetryReleases = false;
        Reviews = [];
        ReviewsStatus = "";
        Error = "";
    }

    public void Dispose() => Close();

    /// <summary>Whether the app has releases to show: the catalog's history, or for an app not in the catalog, its repository's.</summary>
    public bool HasReleases => Item?.App != null || (repositoryReleases != null && !string.IsNullOrWhiteSpace(Item?.ListApp?.Repository));

    /// <summary>Reads the app's release history the first time its Releases tab opens.</summary>
    public async Task LoadReleasesAsync(CancellationToken token)
    {
        if (!HasReleases || Releases != null || IsLoadingReleases || (ReleasesStatus.Length > 0 && !CanRetryReleases)) return;
        var generation = _generation;
        var item = Item!;
        IsLoadingReleases = true; CanRetryReleases = false;
        ReleasesStatus = "Loading releases…";
        try
        {
            if (item.App is { } app)
            {
                var page = await client.GetReleaseHistoryAsync(app.Slug, token);
                token.ThrowIfCancellationRequested();
                if (generation != _generation) return;
                Releases = page.Items;
                ReleasesMore = !page.IsDone;
                ReleasesStatus = page.Items.Count == 0 ? "Releases are being cataloged. Check the project's repository for now." : "";
            }
            else
            {
                var releases = await repositoryReleases!(item.ListApp!, token);
                token.ThrowIfCancellationRequested();
                if (generation != _generation) return;
                Releases = releases;
                ReleasesMore = false;
                ReleasesStatus = releases.Count == 0 ? "This app's repository has no releases." : "";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (generation == _generation)
            {
                ReleasesStatus = "Release loading cancelled.";
                CanRetryReleases = true;
            }
            throw;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            if (generation == _generation)
            {
                ReleasesStatus = item.App != null ? $"Couldn't load releases. {ex.Message}"
                    : $"Couldn't load releases from the app's repository. {ex.Message}";
                // A transient read may be explicitly retried. Missing/removed sources
                // and successfully empty histories do not become automatic retry loops.
                CanRetryReleases = ex is not HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone };
            }
        }
        finally { if (generation == _generation) IsLoadingReleases = false; }
    }

    private async Task LoadDetailAsync(QuiverCatalogApp app, int generation, CancellationToken token)
    {
        try
        {
            var detail = await client.GetDetailAsync(app.Slug, token);
            if (generation != _generation) return;
            Project = detail.Project;
            Entry = toLibraryEntry(detail.Entry, detail.Project);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            if (generation == _generation) Error = $"Couldn't load this app from quiverlauncher.com, so it can't be added right now. {ex.Message}";
        }
    }

    private async Task LoadReviewsAsync(QuiverCatalogApp app, int generation, CancellationToken token)
    {
        try
        {
            var page = await client.GetReviewsAsync(app.Slug, ReviewCount, token);
            if (generation != _generation) return;
            Reviews = page.Items.Select(ToLine).ToList();
            ReviewsStatus = "";
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            if (generation == _generation) ReviewsStatus = $"Couldn't load reviews. {ex.Message}";
        }
    }

    private async Task<DocumentContent> LoadReadmeAsync(BrowseItem item, CancellationToken token)
    {
        try
        {
            if (item.App == null)
                return item.ListApp == null ? new("No README.", IsMarkdown: false) : await repositoryReadme(item.ListApp, token);
            var readme = await client.GetReadmeAsync(item.App.Slug, token);
            return string.IsNullOrWhiteSpace(readme?.Markdown)
                ? new(string.IsNullOrWhiteSpace(item.App.Description) ? "This app has no README." : item.App.Description, IsMarkdown: false)
                : new(readme.Markdown, readme.RawBase);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            return new($"Couldn't load the README. {ex.Message}", IsMarkdown: false);
        }
    }

    internal static BrowseReviewLine ToLine(QuiverCatalogReview review)
    {
        var meta = new List<string>();
        if (review.CreatedAt > 0) meta.Add(DateTimeOffset.FromUnixTimeMilliseconds((long)review.CreatedAt).LocalDateTime.ToString("d MMM yyyy"));
        if (review.Platform is { Length: > 0 } platform) meta.Add(BrowseText.PlatformNames([platform]));
        if (review.Version is { Length: > 0 } version) meta.Add($"tested on {version}");
        var tone = review.Result switch { "runs" => "positive", "issues" => "caution", "broken" => "negative", _ => "" };
        return new(string.IsNullOrWhiteSpace(review.Author) ? "A player" : review.Author.Trim(),
            string.Join(" · ", meta.Where(p => p.Length > 0)), BrowseText.ReviewResult(review.Result), tone, review.Body.Trim());
    }
}
