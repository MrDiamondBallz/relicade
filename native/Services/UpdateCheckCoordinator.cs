using System.Diagnostics;
using Relicade.Core.Services;

namespace Relicade.Services;

public sealed record UpdateCheckResult(ManualLauncherCheckResult Launcher, LibraryCheckResult Apps, bool Interrupted = false)
{
    public bool Complete => !Interrupted && Launcher.CheckSucceeded && Apps.Complete;
    public string? Note => Complete ? null : Interrupted ? "Update check incomplete · Some checks did not finish"
        : Apps.RateLimited > 0 ? "Update check incomplete · Release checks are rate limited"
        : !Apps.Complete ? $"Update check incomplete · {Apps.Failed + Apps.Cancelled} apps could not be checked"
        : "Could not check Relicade";

    public string Details => string.Join("\n\n", Apps.Apps
        .Where(app => app.Outcome != AppCheckOutcome.Successful)
        .Select(app => $"{app.AppName ?? app.IdentityKey}: {app.Reason ?? (app.Outcome == AppCheckOutcome.Cancelled ? "Check did not finish." : app.Outcome == AppCheckOutcome.RateLimited ? "Release service rate limit reached." : "Could not read release information.")}")
        .Concat(Launcher.CheckSucceeded ? [] : new[] { "Relicade: Its update check did not complete successfully. Try again." }));
}

public interface IUpdateCheckWorkflow
{
    bool CanPresentResults { get; }
    Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token);
    Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token,
        IReadOnlySet<string>? appKeys = null);
    void ApplyCheckResult(UpdateCheckResult result, DateTime checkedAt);
    void QueuePostCheckWork(UpdateCheckResult result, bool retry = false);
    Task PresentCheckResultAsync(ManualLauncherCheckResult result, int autoUpdated, bool manual);
    Task ReportCheckFailureAsync(Exception error, bool present);
}

/// <summary>One foreground pass; downloads and secondary refreshes never extend its busy state.</summary>
public sealed class UpdateCheckCoordinator(IUpdateCheckWorkflow workflow, TimeSpan? timeout = null)
{
    private readonly object _gate = new();
    private Task? _active;
    private CancellationTokenSource? _cancel;
    private UpdateCheckResult? _lastResult;
    private long _generation;
    public bool IsChecking { get; private set; }
    public AppCheckProgress Progress { get; private set; } = new(0, 0);
    public event Action? CheckingChanged;
    public event Action? ProgressChanged;
    public void Cancel() { lock (_gate) _cancel?.Cancel(); }

    public Task RetryAsync(bool promptForReview, bool manual, CancellationToken cancellationToken) =>
        CheckAsync(promptForReview, manual, cancellationToken, retry: true);

    public Task CheckAsync(bool promptForReview, bool manual, CancellationToken cancellationToken, bool retry = false)
    {
        lock (_gate)
        {
            if (_active is { IsCompleted: false }) return _active;
            if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;
            return _active = RunAsync(promptForReview, manual, cancellationToken, retry ? _lastResult : null, ++_generation);
        }
    }

    private async Task RunAsync(bool prompt, bool manual, CancellationToken lifetime, UpdateCheckResult? previous, long generation)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _cancel = cancel;
        if (timeout is { } deadline) cancel.CancelAfter(deadline);
        var token = cancel.Token;
        using var priority = ReleaseRequestCoordinator.PrioritizeInteractiveChecks();
        var watch = Stopwatch.StartNew();
        IsChecking = true;
        Progress = new(0, 0);
        CheckingChanged?.Invoke();
        ProgressChanged?.Invoke();
        var launcher = previous?.Launcher ?? new ManualLauncherCheckResult { CheckSucceeded = false };
        var apps = LibraryCheckResult.Empty;
        var interrupted = false;
        var accepting = true;
        var malformed = false;
        var completed = new Dictionary<string, AppCheckResult>(StringComparer.OrdinalIgnoreCase);
        var expected = (previous?.Apps.Apps.Where(a => a.Outcome != AppCheckOutcome.Successful) ?? [])
            .DistinctBy(a => a.IdentityKey, StringComparer.OrdinalIgnoreCase).ToDictionary(a => a.IdentityKey, StringComparer.OrdinalIgnoreCase);
        var targetsKnown = previous?.Apps.ExpectedIdentityKeys != null || expected.Count > 0;
        // If cancellation preceded discovery, retry discovery; never invent unchecked-N identities.
        var appKeys = previous == null || previous.Interrupted && !targetsKnown ? null : expected.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            var launcherTask = previous?.Launcher.CheckSucceeded == true
                ? Task.FromResult(previous.Launcher) : workflow.CheckLauncherAsync(manual, token);
            var appsTask = appKeys is { Count: 0 } ? Task.FromResult(LibraryCheckResult.Empty) : workflow.CheckAppsAsync(manual, new InlineProgress(value =>
            {
                lock (_gate)
                {
                    if (!accepting || generation != _generation || token.IsCancellationRequested) return;
                    if (value.Targets != null)
                    {
                        targetsKnown = true;
                        expected.Clear();
                        foreach (var target in value.Targets)
                            if (!expected.TryAdd(target.IdentityKey, target)) malformed = true;
                    }
                    if (value.Result is { } row)
                    {
                        if ((targetsKnown && !expected.ContainsKey(row.IdentityKey)) || !completed.TryAdd(row.IdentityKey, row)) malformed = true;
                    }
                    Progress = new(completed.Count, targetsKnown ? expected.Count : value.Total, value.Result, value.Targets);
                }
                ProgressChanged?.Invoke();
            }), token, appKeys);
            try { await Task.WhenAll(launcherTask, appsTask).WaitAsync(token); }
            finally
            {
                // Fence callbacks before taking a synchronized snapshot, including transports ignoring cancellation.
                lock (_gate)
                {
                    accepting = false;
                    if (launcherTask.IsCompletedSuccessfully) launcher = launcherTask.Result;
                    if (appsTask.IsCompletedSuccessfully)
                    {
                        apps = appsTask.Result;
                        if (apps.ExpectedIdentityKeys is { } keys)
                        {
                            targetsKnown = true;
                            expected = keys.ToDictionary(key => key, key => new AppCheckResult(key, AppCheckOutcome.Cancelled), StringComparer.OrdinalIgnoreCase);
                        }
                        if (!targetsKnown)
                        {
                            targetsKnown = true;
                            foreach (var row in apps.Apps) expected.TryAdd(row.IdentityKey, row);
                        }
                        var finalRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var row in apps.Apps)
                        {
                            if (!expected.ContainsKey(row.IdentityKey) || !finalRows.Add(row.IdentityKey)) malformed = true;
                            else completed[row.IdentityKey] = row;
                        }
                    }
                }
                _ = launcherTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _ = appsTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { interrupted = true; }
        catch (Exception error)
        {
            interrupted = true;
            cancel.Cancel();
            Trace.WriteLine($"Update check failed: {error.GetType().Name}");
        }
        finally
        {
            lock (_gate)
            {
                accepting = false;
                _cancel = null;
                IsChecking = false;
            }
            CheckingChanged?.Invoke();
            Trace.WriteLine($"Foreground update check: elapsedMs={watch.ElapsedMilliseconds}, completed={Progress.Completed}, total={Progress.Total}");
        }
        if (lifetime.IsCancellationRequested) return;
        // Expected minus completed by identity, never by completion order or positional Skip.
        var rows = expected.Values.Select(target => completed.GetValueOrDefault(target.IdentityKey) ??
            target with { Outcome = AppCheckOutcome.Cancelled, Reason = target.Reason ?? "The update check did not finish for this app." })
            .ToDictionary(row => row.IdentityKey, StringComparer.OrdinalIgnoreCase);
        if (previous != null)
            foreach (var row in previous.Apps.Apps.Where(a => a.Outcome == AppCheckOutcome.Successful)) rows.TryAdd(row.IdentityKey, row);
        apps = new(rows.Values.ToArray(), (previous?.Apps.InvalidTokenProviders ?? []).Concat(apps.InvalidTokenProviders ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            ExpectedIdentityKeys = targetsKnown ? rows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) : null
        };
        var result = new UpdateCheckResult(launcher, apps, interrupted || malformed || !targetsKnown);
        _lastResult = result;
        workflow.ApplyCheckResult(result, DateTime.Now);
        priority.Dispose();
        if (!result.Interrupted) workflow.QueuePostCheckWork(result, retry: previous != null);
        if (prompt && !result.Interrupted && workflow.CanPresentResults)
            await workflow.PresentCheckResultAsync(launcher, 0, manual);
    }

    private sealed class InlineProgress(Action<AppCheckProgress> report) : IProgress<AppCheckProgress>
    {
        public void Report(AppCheckProgress value) => report(value);
    }
}
