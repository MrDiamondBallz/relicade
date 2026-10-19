using System.Diagnostics;
using System.Runtime.InteropServices;
using Relicade.Core.Services;
using Relicade.Services;
namespace Relicade.Tests;

// These are real OWN-SOURCE benign test workers, never installed game payloads.
// Native game, runner, external-service and OS-shell acceptance remain OPEN.
public sealed class ManagedShortcutChildTreeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "sc-tree-" + Guid.NewGuid().ToString("N"));
    const string Mode = "RELICADE_SHORTCUT_TREE_MODE", Location = "RELICADE_SHORTCUT_TREE_ROOT";
    public ManagedShortcutChildTreeTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    static ProcessStartInfo Worker(string mode, string root, bool setsid = false)
    {
        var executable = Environment.ProcessPath!;
        var info = new ProcessStartInfo(setsid ? "/usr/bin/setsid" : executable) { UseShellExecute = false };
        if (setsid) { info.ArgumentList.Add("--wait"); info.ArgumentList.Add(executable); }
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet") info.ArgumentList.Add(typeof(ManagedShortcutChildTreeTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ManagedShortcutChildTreeTests.Benign_child_tree_worker" }) info.ArgumentList.Add(arg);
        info.Environment[Mode] = mode; info.Environment[Location] = root;
        return info;
    }
    static async Task Until(string path)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException(Path.GetFileName(path));
            await Task.Delay(20);
        }
    }
    static async Task CheckedExit(Process process)
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        Assert.Equal(0, process.ExitCode);
    }
    async Task Run(string mode)
    {
        var info = Worker(mode, root); info.RedirectStandardOutput = info.RedirectStandardError = true;
        if (mode == "clone-preexisting")
        {
            var wrapper = await CompileBenignCloneWrapper();
            info.ArgumentList.Insert(0, info.FileName); info.FileName = wrapper;
        }
        using var worker = Process.Start(info)!;
        var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            if (mode == "controller")
            {
                await Until(Path.Combine(root, "request-writer"));
                await AssertWriter(root, true); // Different parent, NOT an unrelated observer child.
                File.WriteAllText(Path.Combine(root, "writer-checked"), "done");
            }
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally { if (!worker.HasExited) { worker.Kill(true); await worker.WaitForExitAsync(); } }
        Assert.True(worker.ExitCode == 0, await stdout + await stderr);
        Console.WriteLine(await stdout);
    }

    [Fact] public Task Owned_tree_waits_for_orphan_setsid_descendant_and_then_releases_real_writer() => Run("controller");
    [Fact] public Task Existing_unrelated_child_refuses_observer_before_spawn() => Run("preexisting");
    [Fact] public Task Abandoned_unspawned_observer_releases_exclusive_kernel_ownership() => Run("unspawned");
    [Fact] public Task Spawn_exception_preserves_uncertain_receipt_for_reopen() => Run("spawn-fault");
    [Fact] public Task Production_completion_returns_false_for_observed_nonzero_exit() => Run("nonzero");
    [Fact] public Task Interrupted_observer_fault_preserves_uncertainty_in_production_completion() => Run("observer-fault");
    [Fact] public Task Unsupported_sandbox_refuses_before_any_native_spawn() => Run("sandbox");
    [Fact] public Task Existing_non_SIGCHLD_clone_child_refuses_observer_before_spawn() => Run("clone-preexisting");

    [DllImport("libc", SetLastError = true)]
    static extern int waitpid(int pid, out int status, int options);

    async Task<string> CompileBenignCloneWrapper()
    {
        // A tiny OWN-SOURCE lifecycle worker, not game bytes. Existing isolated
        // gcc is used only for this Linux test, never installed/downloaded.
        var source = Path.Combine(root, "own-clone-worker.c"); var binary = Path.Combine(root, "own-clone-worker");
        File.WriteAllText(source, """
            #define _GNU_SOURCE
            #include <sched.h>
            #include <unistd.h>
            #include <stdlib.h>
            #include <stdio.h>
            static int child(void *arg) {
                char ready[4096], release[4096];
                snprintf(ready, sizeof(ready), "%s/clone-ready", (char *)arg);
                snprintf(release, sizeof(release), "%s/clone-release", (char *)arg);
                FILE *f = fopen(ready, "w"); if (!f) _exit(7); fputs("ready", f); fclose(f);
                for (int i=0; i<3000 && access(release, F_OK); ++i) usleep(10000);
                _exit(0);
            }
            int main(int argc, char **argv) {
                char *root = getenv("RELICADE_SHORTCUT_TREE_ROOT");
                if (!root || argc<2) return 2;
                void *stack = malloc(1024*1024); if (!stack) return 3;
                if (clone(child, (char *)stack+1024*1024, 0, root)<0) return 4;
                execv(argv[1], argv+1); return 5;
            }
            """);
        var info = new ProcessStartInfo("gcc") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-Wall", "-Wextra", "-Werror", source, "-o", binary }) info.ArgumentList.Add(arg);
        using var compiler = Process.Start(info)!;
        var stdout = compiler.StandardOutput.ReadToEndAsync(); var stderr = compiler.StandardError.ReadToEndAsync();
        await compiler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(compiler.ExitCode == 0, await stdout + await stderr);
        return binary;
    }

    [Fact]
    public async Task Killed_observer_keeps_receipt_blocking_even_after_orphan_exits()
    {
        var info = Worker("crash-controller", root); info.RedirectStandardOutput = info.RedirectStandardError = true;
        using var worker = Process.Start(info)!;
        var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            await Until(Path.Combine(root, "leaf-ready")); await Until(Path.Combine(root, "controller-observing"));
            worker.Kill(); await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await AssertWriter(root, true);
            File.WriteAllText(Path.Combine(root, "release-leaf"), "release");
            await Until(Path.Combine(root, "leaf-finished"));
            await AssertWriter(root, true); // Orphan exit is NOT authority to clear an interrupted receipt.
            Assert.Contains("Launching", File.ReadAllText(Path.Combine(new PortableInstallationTransaction(Path.Combine(root, "tile")).OperationsPath, "launch.json")));
            Console.WriteLine("actual observer kill/reopen: writer refused while orphan held AND after orphan exit; uncertainty retained");
        }
        finally
        {
            File.WriteAllText(Path.Combine(root, "release-leaf"), "release");
            if (!worker.HasExited) { worker.Kill(); await worker.WaitForExitAsync(); }
        }
        _ = await stdout; _ = await stderr;
    }

    static async Task AssertWriter(string root, bool blocked)
    {
        using var writer = Process.Start(Worker(blocked ? "writer-blocked" : "writer-allowed", root))!;
        await CheckedExit(writer);
    }

    [Fact]
    public async Task Benign_child_tree_worker()
    {
        var mode = Environment.GetEnvironmentVariable(Mode); var location = Environment.GetEnvironmentVariable(Location);
        if (mode == null || location == null) return; // Worker-entry no-op is explicitly NOT scenario coverage.
        var install = Path.Combine(location, "tile");
        var tx = new PortableInstallationTransaction(install);
        if (mode == "leaf")
        {
            File.WriteAllText(Path.Combine(location, "leaf-ready"), Environment.ProcessId.ToString());
            await Until(Path.Combine(location, "release-leaf"));
            File.WriteAllText(Path.Combine(location, "leaf-finished"), "done"); return;
        }
        if (mode == "middle")
        {
            using var leaf = Process.Start(Worker("leaf", location, setsid: true))!;
            await Until(Path.Combine(location, "leaf-ready"));
            File.WriteAllText(Path.Combine(location, "middle-exiting"), "done"); return;
        }
        if (mode == "root")
        {
            using var middle = Process.Start(Worker("middle", location))!;
            await Until(Path.Combine(location, "middle-exiting")); return;
        }
        if (mode.StartsWith("writer-"))
        {
            var before = File.ReadAllBytes(Path.Combine(install, PortableInstallationTransaction.ManifestFileName));
            if (mode == "writer-blocked")
            {
                Assert.Throws<IOException>(() => PortableInstallationLifetime.Begin(install));
                Assert.Throws<IOException>(() => tx.Rollback());
                Assert.Throws<IOException>(() => tx.SelectExecutable("fixture.AppImage"));
                Assert.Throws<IOException>(() => tx.AcquireModWriterLease());
                Console.WriteLine("separate writer begin/rollback/selection/mod refused");
            }
            else
            {
                using var lease = PortableInstallationLifetime.Begin(install);
                Console.WriteLine("separate writer acquired new launch lease after complete observed tree");
            }
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(install, PortableInstallationTransaction.ManifestFileName))); return;
        }
        if (mode == "preexisting")
        {
            using var existing = Process.Start(new ProcessStartInfo("sleep", "0.3") { UseShellExecute = false })!;
            Assert.Throws<IOException>(() => ManagedShortcutChildLifetime.Begin());
            await existing.WaitForExitAsync();
            using var available = ManagedShortcutChildLifetime.Begin(); return;
        }
        if (mode == "clone-preexisting")
        {
            await Until(Path.Combine(location, "clone-ready"));
            try { Assert.Throws<IOException>(() => ManagedShortcutChildLifetime.Begin()); }
            finally
            {
                File.WriteAllText(Path.Combine(location, "clone-release"), "release");
                Assert.True(waitpid(-1, out var status, 0x40000000) > 0); Assert.Equal(0, status);
            }
            using var available = ManagedShortcutChildLifetime.Begin();
            Console.WriteLine("actual non-SIGCHLD clone preflight refused before managed spawn; clone released and reaped with __WALL"); return;
        }
        if (mode == "unspawned")
        {
            using (var first = ManagedShortcutChildLifetime.Begin())
                Assert.Throws<IOException>(() => ManagedShortcutChildLifetime.Begin());
            using var second = ManagedShortcutChildLifetime.Begin(); return;
        }
        if (mode == "sandbox")
        {
            Environment.SetEnvironmentVariable("FLATPAK_ID", "org.relicade.own-test");
            try { Assert.Throws<NotSupportedException>(() => ManagedShortcutChildLifetime.Begin()); }
            finally { Environment.SetEnvironmentVariable("FLATPAK_ID", null); }
            using var available = ManagedShortcutChildLifetime.Begin(); return;
        }
        var source = Path.Combine(location, "fixture.AppImage");
        File.WriteAllBytes(source, ManagedShortcutLifetimeTests.NativeShape()); // NEVER executed
        await GameInstallationService.InstallOrUpdateGameAsync(source, install, "fixture.AppImage", "v1");
        if (mode == "spawn-fault")
        {
            using var observer = ManagedShortcutChildLifetime.Begin();
            using (var lifetime = PortableInstallationLifetime.Begin(install, tx.ReadCommitted()!.OperationId))
            {
                Assert.ThrowsAny<Exception>(() => observer.Start(new ProcessStartInfo("/definitely-absent-no-payload") { UseShellExecute = false }));
                lifetime.PreserveUncertain();
            }
            await AssertWriter(location, true); return;
        }
        if (mode is "nonzero" or "observer-fault")
        {
            using var observer = ManagedShortcutChildLifetime.Begin();
            using (var lifetime = PortableInstallationLifetime.Begin(install, tx.ReadCommitted()!.OperationId))
            using (var process = observer.Start(new ProcessStartInfo("sleep", mode == "nonzero" ? "not-a-duration" : "0.1") { UseShellExecute = false })!)
            {
                if (mode == "observer-fault")
                {
                    observer.Dispose(); // Interrupted ownership is NOT completion.
                    await Assert.ThrowsAsync<IOException>(() => GameLaunchService.CompleteManagedShortcutAsync(lifetime, process, observer));
                    await process.WaitForExitAsync();
                }
                else Assert.False(await GameLaunchService.CompleteManagedShortcutAsync(lifetime, process, observer));
            }
            await AssertWriter(location, mode == "observer-fault"); return;
        }
        Assert.True(mode is "controller" or "crash-controller");
        using (var observer = ManagedShortcutChildLifetime.Begin())
        using (var lifetime = PortableInstallationLifetime.Begin(install, tx.ReadCommitted()!.OperationId))
        using (var process = observer.Start(Worker("root", location))!)
        {
            var complete = GameLaunchService.CompleteManagedShortcutAsync(lifetime, process, observer);
            await Until(Path.Combine(location, "leaf-ready"));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            File.WriteAllText(Path.Combine(location, "controller-observing"), "direct-root-exited");
            if (mode == "crash-controller") { await Task.Delay(TimeSpan.FromSeconds(50)); return; }
            Assert.False(complete.IsCompleted); // Root exited; orphan in a DIFFERENT session still held.
            Assert.Contains("Launching", File.ReadAllText(Path.Combine(tx.OperationsPath, "launch.json")));
            File.WriteAllText(Path.Combine(location, "request-writer"), "ready");
            await Until(Path.Combine(location, "writer-checked"));
            Assert.False(complete.IsCompleted);
            File.WriteAllText(Path.Combine(location, "release-leaf"), "release");
            Assert.True(await complete.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.True(File.Exists(Path.Combine(location, "leaf-finished")));
            Console.WriteLine("kernel ECHILD observed only AFTER setsid/orphan own worker completion; no direct-PID clear");
        }
        Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        await AssertWriter(location, false);
        using var reopened = ManagedShortcutChildLifetime.Begin();
    }
}
