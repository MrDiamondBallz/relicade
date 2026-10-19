using Relicade.Services;

namespace Relicade.Tests;

public class PreservationSelectionRetryTests
{
    private sealed class Workflow : IUpdateCheckWorkflow
    {
        public bool CanPresentResults => false;
        public IProgress<AppCheckProgress>? OldProgress;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseOld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<IReadOnlySet<string>?> Selections = [];
        public UpdateCheckResult? Result;
        public Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token) => Task.FromResult(new ManualLauncherCheckResult { CheckSucceeded = true });
        public async Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token, IReadOnlySet<string>? appKeys = null)
        {
            Selections.Add(appKeys);
            if (Selections.Count == 1)
            {
                OldProgress = progress;
                progress.Report(new(0, 2, Targets: [new("folder:A", AppCheckOutcome.Cancelled, "A"), new("folder:B", AppCheckOutcome.Cancelled, "B")]));
                progress.Report(new(1, 2, new("folder:B", AppCheckOutcome.Successful, "B")));
                Started.SetResult();
                await ReleaseOld.Task;
                // A legacy transport ignores cancellation. Its eventual callback must not change another generation.
                progress.Report(new(2, 2, new("folder:A", AppCheckOutcome.Failed, "Late A")));
                return new([new("folder:B", AppCheckOutcome.Successful), new("folder:A", AppCheckOutcome.Failed)]);
            }
            progress.Report(new(0, 1, Targets: [new("folder:A", AppCheckOutcome.Cancelled, "A")]));
            OldProgress!.Report(new(2, 2, new("folder:B", AppCheckOutcome.Failed, "Stale B")));
            progress.Report(new(1, 1, new("folder:A", AppCheckOutcome.Successful, "A")));
            return new([new("folder:A", AppCheckOutcome.Successful, "A")]);
        }
        public void ApplyCheckResult(UpdateCheckResult result, DateTime time) => Result = result;
        public void QueuePostCheckWork(UpdateCheckResult result, bool retry = false) { }
        public Task PresentCheckResultAsync(ManualLauncherCheckResult result, int updated, bool manual) => Task.CompletedTask;
        public Task ReportCheckFailureAsync(Exception error, bool present) => Task.CompletedTask;
    }

    [Fact]
    public async Task B_first_interruption_retains_A_and_retry_checks_A_only_with_generation_fencing()
    {
        var workflow = new Workflow();
        var coordinator = new UpdateCheckCoordinator(workflow);
        var checking = coordinator.CheckAsync(false, true, TestContext.Current.CancellationToken);
        await workflow.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        coordinator.Cancel();
        await checking.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            Assert.Equal(new[] { "folder:A", "folder:B" }, workflow.Result!.Apps.Apps.Select(a => a.IdentityKey).Order().ToArray());
            Assert.Equal(AppCheckOutcome.Cancelled, workflow.Result.Apps.Apps.Single(a => a.IdentityKey == "folder:A").Outcome);
            Assert.False(workflow.Result.Complete);
            await coordinator.RetryAsync(false, true, TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "folder:A" }, workflow.Selections.Last()!.Order().ToArray());
            Assert.True(workflow.Result.Complete);
            Assert.Equal(2, workflow.Result.Apps.Successful);
            Assert.Equal(2, workflow.Result.Apps.Apps.Select(a => a.IdentityKey).Distinct().Count());
            Assert.Equal(1, coordinator.Progress.Total);
            workflow.ReleaseOld.TrySetResult();
            workflow.OldProgress!.Report(new(99, 99, new("folder:B", AppCheckOutcome.Failed)));
            Assert.True(workflow.Result.Complete);
            Assert.Equal(1, coordinator.Progress.Total);
        }
        finally { workflow.ReleaseOld.TrySetResult(); }
    }

    private sealed class ParallelWorkflow : IUpdateCheckWorkflow
    {
        public bool CanPresentResults => false;
        public UpdateCheckResult? Result;
        public Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token) => Task.FromResult(new ManualLauncherCheckResult { CheckSucceeded = true });
        public async Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token, IReadOnlySet<string>? appKeys = null)
        {
            var expected = Enumerable.Range(0, 128).Select(i => new AppCheckResult("folder:" + i, AppCheckOutcome.Cancelled)).ToArray();
            progress.Report(new(0, expected.Length, Targets: expected));
            await Task.Yield();
            await Task.WhenAll(expected.Reverse().Select(row => Task.Run(() =>
                progress.Report(new(1, expected.Length, row with { Outcome = AppCheckOutcome.Successful })), token)));
            // Deliberately omit rows from the final return: every success must come from the synchronized progress map.
            return LibraryCheckResult.Empty;
        }
        public void ApplyCheckResult(UpdateCheckResult result, DateTime time) => Result = result;
        public void QueuePostCheckWork(UpdateCheckResult result, bool retry = false) { }
        public Task PresentCheckResultAsync(ManualLauncherCheckResult result, int updated, bool manual) => Task.CompletedTask;
        public Task ReportCheckFailureAsync(Exception error, bool present) => Task.CompletedTask;
    }

    [Fact]
    public async Task Concurrent_out_of_order_callbacks_produce_one_success_per_expected_identity()
    {
        var workflow = new ParallelWorkflow();
        var coordinator = new UpdateCheckCoordinator(workflow);
        await coordinator.CheckAsync(false, true, TestContext.Current.CancellationToken);
        Assert.Equal(128, workflow.Result!.Apps.Apps.Count);
        Assert.Equal(128, workflow.Result.Apps.Successful);
        Assert.Equal(128, workflow.Result.Apps.Apps.Select(a => a.IdentityKey).Distinct().Count());
        Assert.True(workflow.Result.Complete);
        Assert.Equal(128, coordinator.Progress.Completed);
    }

    private sealed class CompletedGenerationWorkflow : IUpdateCheckWorkflow
    {
        private IProgress<AppCheckProgress>? priorProgress;
        private int calls;
        public bool CanPresentResults => false;
        public UpdateCheckResult? Result;
        public Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token) => Task.FromResult(new ManualLauncherCheckResult { CheckSucceeded = true });
        public Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token, IReadOnlySet<string>? appKeys = null)
        {
            var row = new AppCheckResult(++calls == 1 ? "folder:A" : "folder:B", AppCheckOutcome.Successful);
            progress.Report(new(0, 1, Targets: [row]));
            priorProgress?.Report(new(99, 99, new("folder:A", AppCheckOutcome.Failed)));
            priorProgress = progress;
            return Task.FromResult(new LibraryCheckResult([row]));
        }
        public void ApplyCheckResult(UpdateCheckResult result, DateTime time) => Result = result;
        public void QueuePostCheckWork(UpdateCheckResult result, bool retry = false) { }
        public Task PresentCheckResultAsync(ManualLauncherCheckResult result, int updated, bool manual) => Task.CompletedTask;
        public Task ReportCheckFailureAsync(Exception error, bool present) => Task.CompletedTask;
    }

    [Fact]
    public async Task Completed_uncancelled_generation_cannot_publish_into_a_new_pass()
    {
        var workflow = new CompletedGenerationWorkflow();
        var coordinator = new UpdateCheckCoordinator(workflow);
        await coordinator.CheckAsync(false, true, TestContext.Current.CancellationToken);
        await coordinator.CheckAsync(false, true, TestContext.Current.CancellationToken);
        Assert.True(workflow.Result!.Complete);
        Assert.Equal("folder:B", Assert.Single(workflow.Result.Apps.Apps).IdentityKey);
        Assert.Equal(1, coordinator.Progress.Total);
    }

    [Fact]
    public void Duplicate_success_rows_cannot_establish_completion()
    {
        Assert.False(new LibraryCheckResult([new("same", AppCheckOutcome.Successful), new("same", AppCheckOutcome.Successful)]).Complete);
    }
}
