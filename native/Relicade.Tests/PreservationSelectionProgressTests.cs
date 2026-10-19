namespace Relicade.Tests;

public class PreservationSelectionProgressTests
{
    // Hold only Progress<T> posts; let normal await continuations run. No sleeps or lucky scheduling.
    private sealed class HeldProgressContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (callback.Target is Progress<double>) return;
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    [Fact]
    public async Task Solid_archive_progress_is_observed_before_completion_even_when_context_posts_are_held()
    {
        var previous = SynchronizationContext.Current;
        Task exercise;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new HeldProgressContext());
            exercise = new GameInstallationServiceSevenZipTests().InstallOrUpdateGameAsync_extracts_generated_solid_7z_and_reports_progress();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await exercise.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
