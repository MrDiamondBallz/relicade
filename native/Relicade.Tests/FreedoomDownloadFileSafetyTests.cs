using System.Diagnostics;
using System.Runtime.InteropServices;
using Relicade.Core.Services;
namespace Relicade.Tests;
public sealed class FreedoomDownloadFileSafetyTests
{
    [DllImport("libc", SetLastError = true)] static extern int mkfifo(string path, uint mode);
    [DllImport("libc", SetLastError = true)] static extern int link(string original, string destination);
    [DllImport("libc")] static extern int gettid();

    [Theory]
    [InlineData("archive", "directory")][InlineData("archive", "symlink")][InlineData("archive", "hardlink")]
    [InlineData("data", "directory")][InlineData("data", "symlink")][InlineData("data", "hardlink")]
    public async Task Cached_special_files_are_refused_without_touching_external_bytes(string kind, string fault)
    {
        using var f = new FreedoomDataDownloadTests.Fixture(); var result = await f.Download();
        var path = kind == "archive" ? result.Archive : result.Source;
        var original = File.ReadAllBytes(path); var retained = Path.Combine(Path.GetDirectoryName(f.Cache)!, "external-original");
        File.Move(path, retained);
        if (fault == "directory") Directory.CreateDirectory(path);
        if (fault == "symlink") File.CreateSymbolicLink(path, retained);
        if (fault == "hardlink") Assert.Equal(0, link(retained, path));
        var error = await Record.ExceptionAsync(() => f.Download());
        Assert.True(error is IOException or InvalidDataException); Assert.Equal(original, File.ReadAllBytes(retained)); Assert.Equal(1, f.Calls);
    }

    [Theory]
    [InlineData("archive")][InlineData("data")]
    public async Task Cached_fifo_is_refused_before_a_non_cancellable_open(string kind)
    {
        using var f = new FreedoomDataDownloadTests.Fixture(); var result = await f.Download();
        var path = kind == "archive" ? result.Archive : result.Source; var original = File.ReadAllBytes(path);
        File.Delete(path); Assert.Equal(0, mkfifo(path, 0x180));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Task.Run(async () => { entered.SetResult(gettid()); return await Record.ExceptionAsync(() => f.Download(cancellation.Token)); });
        bool blocked = false; Exception? error;
        try
        {
            var thread = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var deadline = Stopwatch.StartNew();
            while (!operation.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.ReadAllText($"/proc/self/task/{thread}/wchan").Trim() == "wait_for_partner") { blocked = true; break; }
                await Task.Yield();
            }
            cancellation.Cancel();
        }
        finally
        {
            // Drain even a rejected implementation. Read/write opens the FIFO without
            // waiting for a partner; closing it allows an already-open reader to finish.
            using (var release = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            error = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(blocked); Assert.True(error is IOException or InvalidDataException); Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task Held_cache_writer_refuses_without_a_request()
    {
        using var f = new FreedoomDataDownloadTests.Fixture();
        using var writer = new PortableInstallationTransaction(f.Cache).AcquireLease();
        await Assert.ThrowsAnyAsync<IOException>(() => f.Download()); Assert.Equal(0, f.Calls); Assert.False(Directory.Exists(f.Cache));
    }
}
