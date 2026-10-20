using Relicade.Services;

namespace Relicade.Tests;

// Exercises cleanup console lifecycle decisions with injected OS calls. This is
// not native Windows console/handle acceptance; the Desktop route is also audited.
public sealed class PortableBackupCleanupConsoleTests
{
    [Theory]
    [InlineData(true, 0)][InlineData(true, 1)][InlineData(true, 130)]
    [InlineData(false, 0)][InlineData(false, 1)][InlineData(false, 130)]
    public void Windows_attach_reset_execute_and_detach_preserve_result_and_original_arguments(bool attached, int result)
    {
        var calls = new List<string>();
        string[] original = ["--kiosk", "--preview-portable-backups", "exact-key"];
        var captured = original;
        var code = PortableBackupCleanupConsole.Run(() =>
        {
            calls.Add("execute"); Assert.Same(original, captured);
            Assert.Equal(["--kiosk", "--preview-portable-backups", "exact-key"], captured);
            return result;
        }, true, () => { calls.Add("attach"); return attached; },
        () => calls.Add("reset"), () => calls.Add("detach"));
        Assert.Equal(result, code);
        Assert.Equal(attached ? ["attach", "reset", "execute", "detach"] : new[] { "attach", "execute", "detach" }, calls);
    }

    [Fact]
    public void Nonwindows_executes_without_any_console_API_or_stream_reset()
    {
        var calls = new List<string>();
        var code = PortableBackupCleanupConsole.Run(() => { calls.Add("execute"); return 7; },
            false, () => throw new Exception("No Windows attach"),
            () => throw new Exception("No stream reset"), () => throw new Exception("No Windows detach"));
        Assert.Equal(7, code); Assert.Equal(["execute"], calls);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Windows_detaches_when_execution_throws_without_hiding_failure(bool attached)
    {
        var calls = new List<string>(); var failure = new IOException("cleanup failed after partial progress");
        var actual = Assert.Throws<IOException>(() => PortableBackupCleanupConsole.Run(
            () => { calls.Add("execute"); throw failure; }, true,
            () => { calls.Add("attach"); return attached; }, () => calls.Add("reset"), () => calls.Add("detach")));
        Assert.Same(failure, actual);
        Assert.Equal(attached ? ["attach", "reset", "execute", "detach"] : new[] { "attach", "execute", "detach" }, calls);
    }

    [Fact]
    public void Windows_stream_reset_failure_still_detaches_and_never_runs_cleanup()
    {
        var calls = new List<string>(); var failure = new IOException("standard handle unavailable");
        var actual = Assert.Throws<IOException>(() => PortableBackupCleanupConsole.Run(
            () => throw new Exception("Must not execute after reset failed"), true,
            () => { calls.Add("attach"); return true; }, () => { calls.Add("reset"); throw failure; }, () => calls.Add("detach")));
        Assert.Same(failure, actual); Assert.Equal(["attach", "reset", "detach"], calls);
    }
}
