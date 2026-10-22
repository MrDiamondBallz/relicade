using System.Reflection;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Relicade.Tests;

public class HeadlessSessionLifetimeTests
{
    [Fact]
    public async Task Published_dispatch_task_is_nonnull_and_completes_after_session_disposal()
    {
        // Read only: specifically guards the upstream StartNew publication race.
        // A fixed number of independent lifetimes, not retries after a failure.
        var dispatchTaskField = typeof(HeadlessUnitTestSession).GetField(
            "_dispatchTask", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dispatchTaskField);
        for (var i = 0; i < 16; i++)
        {
            var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
            Task? dispatchTask = null;
            try
            {
                dispatchTask = Assert.IsAssignableFrom<Task>(dispatchTaskField.GetValue(session));
                Assert.False(dispatchTask.IsCompleted);
                await session.Dispatch(() => Assert.True(Dispatcher.UIThread.CheckAccess()),
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.True(dispatchTask!.IsCompletedSuccessfully);
        }
    }
}
