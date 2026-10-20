using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableLifetimeExitIdentityTests
{
    static Process Child(string root)
    {
        var exe = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(ManagedRemovalProcessTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ManagedRemovalProcessTests.Worker" })
            psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_REMOVAL_ROOT"] = root;
        psi.Environment["RELICADE_REMOVAL_MODE"] = "tracked";
        return Process.Start(psi)!;
    }
    static async Task Barrier(Process child, string root)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "barrier")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(20);
        Assert.False(child.HasExited);
        Assert.True(File.Exists(Path.Combine(root, "barrier")));
    }
    static void Stop(Process child)
    {
        if (!child.HasExited) child.Kill(true);
        child.WaitForExit();
        Assert.True(child.HasExited);
    }
    static string Receipt(ManagedRemovalFixture f) => Path.Combine(f.Tx.OperationsPath, "launch.json");
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
    static extern int OpenLock(string path, int flags);
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    static extern int WaitLock(int fd, int operation);
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
    static extern int CloseLock(int fd);
    static async Task WaitForWriterRelease(ManagedRemovalFixture f)
    {
        // Receipt deletion precedes RetainUntilExit's lease-disposing finally.
        // Block on the actual kernel lock-release event, not a sleep/retry or receipt oracle.
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux flock completion fixture");
        await Task.Run(() =>
        {
            var fd = OpenLock(Path.Combine(f.Tx.OperationsPath, "writer.lock"), 2 /* O_RDWR */);
            Assert.True(fd >= 0);
            try { Assert.Equal(0, WaitLock(fd, 2 /* LOCK_EX, blocking */)); }
            finally { Assert.Equal(0, CloseLock(fd)); }
        }).WaitAsync(TimeSpan.FromSeconds(10));
    }
    static void Released(ManagedRemovalFixture f) { using var lease = f.Tx.AcquireLease(); }
    static void Held(ManagedRemovalFixture f) => Assert.ThrowsAny<IOException>(() => f.Tx.AcquireLease());
    static void Evidence(string name, Process child, string? receipt, Exception? failure)
    {
        var dir = Path.Combine(Path.GetTempPath(), "exit-identity-receipts");
        Directory.CreateDirectory(dir);
        var row = JsonSerializer.Serialize(new { name, pid = child.Id, exited = child.HasExited,
            exit = child.HasExited ? (int?)child.ExitCode : null, receipt, failure = failure?.ToString() });
        File.WriteAllText(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json"), row);
        Console.WriteLine(row);
    }
    [Theory]
    [InlineData("between-check-and-identity")]
    [InlineData("already-exited")]
    [InlineData("still-live")]
    public async Task Real_owned_worker_exit_at_identity_boundary_releases_only_completed_lifetime(string boundary)
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root);
        var before = File.ReadAllText(Receipt(f));
        Assert.Contains("Launching", before); Held(f);
        bool invoked = false;
        try
        {
            if (boundary == "already-exited") Stop(process);
            var failure = Record.Exception(() => lifetime.Attach(process, () =>
            {
                invoked = true;
                Assert.False(process.HasExited);
                Held(f);
                if (boundary == "between-check-and-identity")
                {
                    Stop(process);
                    Assert.False(File.Exists("/proc/" + process.Id + "/stat"));
                    Console.WriteLine("REAL owned worker exited/reaped after actual live-check/tracked-open, before actual Identity read. No injected IO failure.");
                }
            }));
            Evidence(boundary, process, File.Exists(Receipt(f)) ? File.ReadAllText(Receipt(f)) : null, failure);
            if (failure != null) lifetime.PreserveUncertain();
            Assert.Equal(boundary != "already-exited", invoked);
            Assert.Null(failure);
            if (boundary == "still-live")
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Receipt(f)));
                Assert.Equal("Running", doc.RootElement.GetProperty("State").GetString());
                Assert.Equal(process.Id, doc.RootElement.GetProperty("ProcessId").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("StartIdentity").GetString()));
                Held(f);
                Assert.ThrowsAny<IOException>(() => PortableInstallationLifetime.EnsureNotRunning(f.Tx.OperationsPath));
            }
            else { Assert.False(File.Exists(Receipt(f))); Released(f); }
            Assert.Equal("current save old", File.ReadAllText(Path.Combine(f.Live, "saves", "slot")));
            Assert.Equal("synthetic owned original", File.ReadAllText(Path.Combine(f.Live, "owned-data", "original")));
        }
        finally { Stop(process); }
        if (boundary == "still-live")
        {
            var timer = Stopwatch.StartNew();
            while (File.Exists(Receipt(f)) && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Assert.False(File.Exists(Receipt(f)));
            await WaitForWriterRelease(f); Released(f);
        }
    }
    [Fact]
    public async Task Real_preopened_proc_read_proven_exit_releases_completed_lifetime()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root);
        var before = File.ReadAllText(Receipt(f));
        Held(f); Assert.Contains("Launching", before); bool opened = false;
        var failure = Record.Exception(() => lifetime.Attach(process, null, () =>
        {
            opened = true; Assert.False(process.HasExited); Held(f);
            Stop(process);
            Console.WriteLine("REAL proc/stat stream OPEN before ORIGINAL worker exit/reap; next operation is actual procfs READ, no injected exception.");
        }));
        Evidence("real-preopened-proc-read-after-exit", process, File.Exists(Receipt(f)) ? File.ReadAllText(Receipt(f)) : null, failure);
        if (failure != null) lifetime.PreserveUncertain();
        Assert.True(opened); Assert.True(process.HasExited); Assert.Null(failure);
        Assert.False(File.Exists(Receipt(f))); Released(f);
        Assert.Equal("current save old", File.ReadAllText(Path.Combine(f.Live, "saves", "slot")));
        Assert.Equal("synthetic owned original", File.ReadAllText(Path.Combine(f.Live, "owned-data", "original")));
    }
    [Fact]
    public async Task Real_preopened_proc_read_with_unknown_original_exit_retains_uncertainty()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root); var before = File.ReadAllText(Receipt(f));
        var failure = Record.Exception(() => lifetime.Attach(process, null, () => { Stop(process); process.Dispose(); }));
        Assert.IsType<IOException>(failure); Assert.Equal(3, failure.HResult);
        Assert.Throws<InvalidOperationException>(() => process.HasExited);
        Held(f); lifetime.PreserveUncertain(); Released(f);
        Assert.Equal(before, File.ReadAllText(Receipt(f)));
        Assert.ThrowsAny<IOException>(() => PortableInstallationLifetime.EnsureNotRunning(f.Tx.OperationsPath));
        Console.WriteLine("REAL preopened READ ESRCH with UNKNOWN original exit: " + failure);
    }
    sealed class InjectedIo(int result) : IOException("INJECTED CALLBACK FAULT CONTROL, not OS causal proof")
    { public int Number { get; } = result; public void SetNumber() => HResult = Number; }
    [Theory]
    [InlineData(false, 3)][InlineData(true, 3)]
    [InlineData(true, 2)][InlineData(true, 13)][InlineData(true, unchecked((int)0x80070003))]
    public async Task After_open_callback_IO_is_never_OS_exit_authority(bool exit, int number)
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root); var before = File.ReadAllText(Receipt(f));
        var expected = new InjectedIo(number); expected.SetNumber();
        try
        {
            var failure = Record.Exception(() => lifetime.Attach(process, null, () => { if (exit) Stop(process); throw expected; }));
            Assert.Same(expected, failure); Assert.Equal(exit, process.HasExited);
            Held(f); lifetime.PreserveUncertain(); Released(f);
            Assert.Equal(before, File.ReadAllText(Receipt(f)));
            Evidence("injected-after-open-" + number, process, before, failure);
        }
        finally { Stop(process); }
    }
    [Fact]
    public async Task Real_preopened_exit_cleanup_error_propagates()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root); var receipt = Receipt(f);
        var failure = Record.Exception(() => lifetime.Attach(process, null, () =>
        {
            Stop(process); File.Delete(receipt); Directory.CreateDirectory(receipt);
        }));
        Assert.NotNull(failure); Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(receipt)); Released(f); lifetime.PreserveUncertain();
        Evidence("real-preopened-exit-cleanup-refused", process, "directory retained", failure);
        Directory.Delete(receipt);
    }
    [Theory]
    [InlineData("missing-file")][InlineData("missing-directory")]
    [InlineData("permission")][InlineData("unexpected")]
    public async Task Explicit_callback_faults_are_not_exit_authority_and_retain_uncertainty(string kind)
    {
        // These are fault controls ONLY, not the real-exit causal RED/GREEN above.
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root);
        var before = File.ReadAllText(Receipt(f));
        Exception expected = kind switch { "missing-file" => new FileNotFoundException("INJECTED CONTROL"),
            "missing-directory" => new DirectoryNotFoundException("INJECTED CONTROL"),
            "permission" => new UnauthorizedAccessException("INJECTED CONTROL"), _ => new IOException("INJECTED CONTROL") };
        try
        {
            var failure = Record.Exception(() => lifetime.Attach(process, () => throw expected));
            Assert.Same(expected, failure); Assert.False(process.HasExited);
            Held(f); lifetime.PreserveUncertain(); Released(f);
            Assert.Equal(before, File.ReadAllText(Receipt(f)));
            Assert.ThrowsAny<IOException>(() => PortableInstallationLifetime.EnsureNotRunning(f.Tx.OperationsPath));
            Evidence("injected-live-" + kind, process, before, failure);
        }
        finally { Stop(process); }
        Assert.Equal(before, File.ReadAllText(Receipt(f)));
    }
    [Fact]
    public async Task Real_missing_identity_with_unknown_original_exit_fails_closed()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root);
        var before = File.ReadAllText(Receipt(f));
        var failure = Record.Exception(() => lifetime.Attach(process, () => { Stop(process); process.Dispose(); }));
        // The tracked PID genuinely vanished, but the ORIGINAL disposed handle cannot prove exit.
        Assert.IsAssignableFrom<IOException>(failure);
        Assert.Throws<InvalidOperationException>(() => process.HasExited);
        lifetime.PreserveUncertain(); Released(f);
        Assert.Equal(before, File.ReadAllText(Receipt(f)));
        Assert.ThrowsAny<IOException>(() => PortableInstallationLifetime.EnsureNotRunning(f.Tx.OperationsPath));
        Console.WriteLine("REAL missing /proc identity, ORIGINAL exit unknown: " + failure);
    }
    [Fact]
    public async Task Real_receipt_write_directory_failure_is_not_identity_completion()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root);
        var before = File.ReadAllText(Receipt(f));
        var moved = f.Tx.OperationsPath + "-moved";
        try
        {
            var failure = Record.Exception(() => lifetime.Attach(process, () => Directory.Move(f.Tx.OperationsPath, moved)));
            Assert.IsType<DirectoryNotFoundException>(failure);
            Assert.False(process.HasExited);
            Assert.Equal(before, File.ReadAllText(Path.Combine(moved, "launch.json")));
            Directory.Move(moved, f.Tx.OperationsPath);
            Held(f); lifetime.PreserveUncertain(); Released(f);
            Assert.ThrowsAny<IOException>(() => PortableInstallationLifetime.EnsureNotRunning(f.Tx.OperationsPath));
            Evidence("actual-receipt-write-directory-missing", process, before, failure);
        }
        finally { if (Directory.Exists(moved)) Directory.Move(moved, f.Tx.OperationsPath); Stop(process); }
        Assert.Equal(before, File.ReadAllText(Receipt(f)));
    }
    [Fact]
    public async Task Real_exit_cleanup_failure_propagates_instead_of_reporting_success()
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        using var process = Child(f.Root);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        await Barrier(process, f.Root);
        var receipt = Receipt(f);
        var failure = Record.Exception(() => lifetime.Attach(process, () =>
        {
            Stop(process); File.Delete(receipt); Directory.CreateDirectory(receipt);
        }));
        Assert.NotNull(failure);
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(receipt)); Released(f);
        lifetime.PreserveUncertain();
        Evidence("actual-exit-cleanup-refused", process, "directory retained", failure);
        Directory.Delete(receipt);
    }
}
