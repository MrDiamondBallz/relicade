using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;

namespace Relicade.Tests;

public sealed class OwnedGameSetupEvidenceKindTests
{
    [DllImport("libc", SetLastError = true)] static extern int mkfifo(string path, uint mode);
    [DllImport("libc", SetLastError = true)] static extern int link(string original, string destination);
    [DllImport("libc")] static extern int gettid();

    [AvaloniaTheory]
    [InlineData("library")][InlineData("settings")]
    public async Task Persisted_evidence_fifo_is_rejected_before_a_blocking_open(string kind)
    {
        using var fixture = new OwnedSetupFixture(); await fixture.Initialize();
        var setup = fixture.Setup(); var plan = setup.Preview(fixture.Source, CancellationToken.None);
        var path = kind == "library" ? fixture.Manager.CatalogService.AppsConfigPath : Path.Combine(fixture.Root, "settings.json");
        File.Move(path, path + ".original"); Assert.Equal(0, mkfifo(path, 0x180));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Task.Run(() => { entered.SetResult(gettid()); return Record.Exception(() => setup.Apply(plan, cancellation.Token)); });
        bool blocked = false; Exception? error;
        try
        {
            var thread = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = Stopwatch.StartNew();
            while (!operation.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.ReadAllText($"/proc/self/task/{thread}/wchan").Trim() == "wait_for_partner") { blocked = true; break; }
                await Task.Yield();
            }
            cancellation.Cancel();
        }
        finally
        {
            // Release a rejected implementation's blocked synthetic reader and
            // drain it even when the regression assertions below fail.
            using var release = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            error = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(blocked, "Regular-file validation must precede any potentially blocking open.");
        Assert.True(error is IOException or InvalidDataException);
        Assert.False(File.Exists(plan.Destination)); Assert.Equal(fixture.Data, File.ReadAllBytes(fixture.Source));
        Assert.Equal(0, fixture.NetworkCalls);
    }

    [AvaloniaTheory]
    [InlineData("library", "directory")][InlineData("library", "symlink")][InlineData("library", "hardlink")]
    [InlineData("settings", "directory")][InlineData("settings", "symlink")][InlineData("settings", "hardlink")]
    public async Task Nonregular_or_linked_persisted_evidence_is_refused_without_copy(string kind, string fault)
    {
        using var fixture = new OwnedSetupFixture(); await fixture.Initialize();
        var setup = fixture.Setup(); var plan = setup.Preview(fixture.Source, CancellationToken.None);
        var path = kind == "library" ? fixture.Manager.CatalogService.AppsConfigPath : Path.Combine(fixture.Root, "settings.json");
        var original = File.ReadAllBytes(path); File.Move(path, path + ".original");
        if (fault == "directory") Directory.CreateDirectory(path);
        if (fault == "symlink") File.CreateSymbolicLink(path, path + ".original");
        if (fault == "hardlink") Assert.Equal(0, link(path + ".original", path));
        var error = Record.Exception(() => setup.Apply(plan, CancellationToken.None));
        Assert.True(error is IOException or InvalidDataException);
        Assert.False(File.Exists(plan.Destination)); Assert.Equal(original, File.ReadAllBytes(path + ".original"));
        Assert.Equal(fixture.Data, File.ReadAllBytes(fixture.Source)); Assert.Equal(0, fixture.NetworkCalls);
    }
}
