using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.Views;
using Relicade.Models;
using Relicade.ViewModels;
using Fixture = Relicade.Tests.FreedoomEngineHandoffTests.Fixture;

namespace Relicade.Tests;

public sealed class GuidedInstallTruthfulProgressTests
{
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task Actual_guided_stream_shows_only_measured_transfer_progress(bool knownLength)
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await using var session = new LauncherSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DownloadContent = () =>
        {
            var content = new StreamContent(new HeldBody(fixture.Payload, entered, release));
            if (knownLength) content.Headers.ContentLength = fixture.Payload.Length;
            return content;
        };
        var model = OwnedGameSetupFlow.CreateEngineProgress(fixture.Game, fixture.Manager,
            fixture.Store.Current, session, fixture.Dialogs, fixture.Definition);
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var work = model.StartAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(model.IsBusy); Assert.False(work.IsCompleted); Assert.Equal(1, fixture.DownloadCalls);
            Assert.Equal(!knownLength, window.TransferProgress.IsIndeterminate);
            if (knownLength) Assert.Equal(32d / fixture.Payload.Length * 100, model.Progress, precision: 6);
            else Assert.DoesNotContain("%", model.Message);
            Assert.False(fixture.Game.IsInstalled); Assert.False(model.Succeeded);
        }
        finally
        {
            // The held body deliberately does not detach on cancellation. Release
            // then drain the actual owned transfer before fixture dependency disposal.
            try { model.Cancel(); }
            finally
            {
                release.TrySetResult();
                try { await model.Work; }
                finally { try { window.Close(); } finally { model.DisposeAfterDrain(); } }
            }
        }
    }
    [AvaloniaTheory]
    [InlineData(0, 100, false, 0)]
    [InlineData(32, 0, true, 0)]
    [InlineData(32, -1, true, 0)]
    [InlineData(32, 16, true, 0)]
    [InlineData(100, 100, false, 100)]
    public async Task Transfer_totals_do_not_claim_verification_or_accept_stale_phases(long received, long total, bool indeterminate, double expected)
    {
        await using var session = new LauncherSession();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var game = new GameInfo();
        var model = new FreedoomEngineProgressViewModel(session, game, _ => release.Task);
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            window.Show(); model.StartAsync();
            model.ReportProgress(new(GuidedEnginePhase.Downloading, received, total < 0 ? null : total));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(indeterminate, window.TransferProgress.IsIndeterminate);
            Assert.Equal(expected, model.Progress); Assert.False(model.Succeeded);
            Assert.Contains(indeterminate ? "unknown" : "Verification is next", model.Message);
            model.ReportProgress(new(GuidedEnginePhase.Verifying));
            Assert.True(window.TransferProgress.IsIndeterminate); Assert.Contains("Verifying", model.Message);
            var verifying = model.Message;
            model.ReportProgress(new(GuidedEnginePhase.Downloading, received, total));
            game.DownloadProgress = 99; Dispatcher.UIThread.RunJobs();
            Assert.Equal(verifying, model.Message); Assert.False(model.Succeeded);
            model.ReportProgress(new(GuidedEnginePhase.Staging)); Assert.Contains("Preparing", model.Message);
            model.Cancel(); model.ReportProgress(new(GuidedEnginePhase.Committing));
            Assert.Contains("Finishing safely", model.Message); Assert.Contains("retained", model.Message);
            Assert.True(window.TransferProgress.IsIndeterminate); Assert.False(model.Succeeded);
            release.TrySetResult(false); await model.Work;
            var terminal = model.Message;
            model.ReportProgress(new(GuidedEnginePhase.Committed)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(terminal, model.Message); Assert.False(model.Succeeded);
        }
        finally
        {
            release.TrySetResult(false);
            try { await model.Work; }
            finally { try { window.Close(); } finally { model.DisposeAfterDrain(); } }
        }
    }

    [AvaloniaFact]
    public async Task Actual_guided_commit_reports_distinct_phases_and_measured_bytes_without_play()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await using var session = new LauncherSession();
        var observations = new System.Collections.Concurrent.ConcurrentQueue<GuidedEngineProgress>();
        var installed = await session.RunAsync(() => GuidedEngineInstallService.InstallAsync(fixture.Game,
            fixture.Manager, fixture.Store.Current, session.Token, (_, _) => Task.FromResult(true),
            fixture.Dialogs, fixture.Definition, observations.Enqueue));
        Assert.True(installed); Assert.True(fixture.Game.IsInstalled);
        Assert.Equal(new[] { GuidedEnginePhase.Resolving, GuidedEnginePhase.Downloading, GuidedEnginePhase.Verifying,
            GuidedEnginePhase.Staging, GuidedEnginePhase.Committing, GuidedEnginePhase.Committed },
            observations.Select(value => value.Phase).Distinct());
        var received = observations.Last(value => value.Phase == GuidedEnginePhase.Downloading);
        Assert.Equal(fixture.Payload.Length, received.ReceivedBytes);
        Assert.Equal(fixture.Payload.Length, received.TotalBytes);
        Assert.Equal(1, fixture.DownloadCalls); Assert.False(fixture.Game.OwnedDataReady);
    }

    [AvaloniaFact]
    public async Task Queued_prior_attempt_observations_cannot_regress_or_change_the_new_model()
    {
        await using var session = new LauncherSession(); using var game = new GameInfo();
        var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FreedoomEngineProgressViewModel(session, game, _ => firstRelease.Task);
        var next = new FreedoomEngineProgressViewModel(session, game, _ => nextRelease.Task);
        try
        {
            _ = first.StartAsync();
            Dispatcher.UIThread.Post(() => first.ReportProgress(new(GuidedEnginePhase.Downloading, 32, 103)));
            first.ReportProgress(new(GuidedEnginePhase.Verifying)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(GuidedEnginePhase.Verifying, first.Phase); Assert.Contains("Verifying", first.Message);
            first.Cancel(); firstRelease.TrySetResult(false); await first.Work; first.DisposeAfterDrain();
            var terminal = first.Message;
            _ = next.StartAsync(); var initial = next.Message;
            Dispatcher.UIThread.Post(() => first.ReportProgress(new(GuidedEnginePhase.Committed)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(terminal, first.Message); Assert.Equal(initial, next.Message);
            Assert.Equal(GuidedEnginePhase.Resolving, next.Phase); Assert.False(next.Succeeded);
            next.ReportProgress(new(GuidedEnginePhase.Downloading, 32, 103));
            Assert.Equal(32d / 103 * 100, next.Progress, precision: 6);
        }
        finally
        {
            firstRelease.TrySetResult(false); nextRelease.TrySetResult(false);
            try { await Task.WhenAll(first.Work, next.Work); }
            finally { try { first.DisposeAfterDrain(); } finally { next.DisposeAfterDrain(); } }
        }
    }

    sealed class HeldBody(byte[] payload, TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream(payload)
    {
        int reads;
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (reads++ == 0) return base.Read(buffer.Span[..Math.Min(32, buffer.Length)]);
            entered.TrySetResult(); await release.Task; cancellationToken.ThrowIfCancellationRequested();
            return base.Read(buffer.Span);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
