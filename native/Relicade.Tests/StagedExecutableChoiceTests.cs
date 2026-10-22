using System.Diagnostics;
using System.Runtime.InteropServices;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class StagedExecutableChoiceTests : IDisposable
{
    [DllImport("libc", SetLastError = true)] static extern int mkfifo(string path, uint mode);
    [DllImport("libc", SetLastError = true)] static extern int link(string original, string destination);
    [DllImport("libc")] static extern int gettid();

    [Theory]
    [InlineData("fifo")][InlineData("hardlink")]
    public async Task Nonregular_or_shared_file_introduced_during_choice_is_refused_before_open(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Exercises Linux FIFO and link-count evidence.");
        string staged = "";
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var transaction = new PortableInstallationTransaction(Live, new() { ChooseExecutableAsync = (_, _) =>
        {
            var path = Path.Combine(staged, "editor.exe"); File.Delete(path);
            Assert.Equal(0, kind == "fifo" ? mkfifo(path, 0x180) : link(Path.Combine(staged, "game.exe"), path));
            entered.TrySetResult(gettid()); return Task.FromResult<string?>("game.exe");
        } });
        var operation = Task.Run(async () => await Record.ExceptionAsync(() => transaction.InstallAsync(path =>
        { staged = path; return Stage(path); }, "v1", cancel.Token)));
        var blocked = false;
        Exception? error = null;
        try
        {
            var tid = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var deadline = Stopwatch.StartNew();
            while (!operation.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.ReadAllText($"/proc/self/task/{tid}/wchan").Trim() == "wait_for_partner") { blocked = true; break; }
                await Task.Yield();
            }
            cancel.Cancel();
        }
        finally
        {
            // A rejected regression must also release the controlled FIFO reader.
            if (kind == "fifo" && !string.IsNullOrEmpty(staged))
                using (var release = new FileStream(Path.Combine(staged, "editor.exe"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            error = await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.False(blocked); Assert.True(error is IOException or InvalidDataException);
        AssertNoActivation();
        using var released = new PortableInstallationTransaction(Live).AcquireLease();
    }

    [Theory]
    [InlineData("initial", "file")]
    [InlineData("initial", "directory")]
    [InlineData("initial", "symlink")]
    [InlineData("initial", "fifo")]
    [InlineData("initial", "hardlink")]
    [InlineData("choice", "file")]
    [InlineData("choice", "directory")]
    [InlineData("choice", "symlink")]
    [InlineData("choice", "fifo")]
    [InlineData("choice", "hardlink")]
    [InlineData("staged", "file")]
    [InlineData("staged", "directory")]
    [InlineData("staged", "symlink")]
    [InlineData("staged", "fifo")]
    [InlineData("staged", "hardlink")]
    public async Task Package_cannot_supply_the_reserved_transaction_lock(string phase, string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Exercises Linux file kinds and kernel wait evidence.");
        var prior = new PortableInstallationTransaction(Live);
        await prior.InstallAsync(path => { File.WriteAllText(Path.Combine(path, "old.exe"), "original payload"); return Task.CompletedTask; }, "old");
        var manifest = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        var external = Path.Combine(root, "unrelated.txt"); File.WriteAllText(external, "preserved unrelated bytes");
        string staged = "";
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Introduce()
        {
            var path = Path.Combine(staged, ".relicade-mod-writer.lock");
            switch (kind)
            {
                case "fifo": Assert.Equal(0, mkfifo(path, 0x180)); break;
                case "hardlink": Assert.Equal(0, link(external, path)); break;
                case "symlink": File.CreateSymbolicLink(path, external); break;
                case "directory": Directory.CreateDirectory(path); break;
                default: File.WriteAllText(path, "untrusted reserved payload"); break;
            }
            entered.TrySetResult(gettid());
        }
        var transaction = new PortableInstallationTransaction(Live, new()
        {
            ChooseExecutableAsync = (_, _) => { if (phase == "choice") Introduce(); return Task.FromResult<string?>("game.exe"); },
            TransactionBoundary = boundary => { if (phase == "staged" && boundary == "Staged") Introduce(); }
        });
        var operation = Task.Run(async () => await Record.ExceptionAsync(() => transaction.InstallAsync(async path =>
        { staged = path; await Stage(path); if (phase == "initial") Introduce(); }, "new")));
        var blocked = false;
        Exception? error = null;
        try
        {
            var tid = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (kind == "fifo")
            {
                var deadline = Stopwatch.StartNew();
                while (!operation.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    if (File.ReadAllText($"/proc/self/task/{tid}/wchan").Trim() == "wait_for_partner") { blocked = true; break; }
                    await Task.Yield();
                }
            }
        }
        finally
        {
            // Drain only our controlled FIFO if the rejected implementation opens it.
            if (blocked)
                using (var release = new FileStream(Path.Combine(staged, ".relicade-mod-writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            error = await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.False(blocked);
        Assert.True(error is IOException or InvalidDataException);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal("original payload", File.ReadAllText(Path.Combine(Live, "old.exe")));
        Assert.Equal("preserved unrelated bytes", File.ReadAllText(external));
        Assert.False(File.Exists(Path.Combine(Live, "game.exe")));
        Assert.Equal("old", prior.ReadCommitted()!.SelectedVersion);
        Assert.Equal(0, new FileInfo(Path.Combine(Live, ".relicade-mod-writer.lock")).Length);
        using var released = prior.AcquireLease();
    }

    readonly string root = Path.Combine(Path.GetTempPath(), "staged-choice-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "Game");
    static Task Stage(string path)
    {
        File.WriteAllText(Path.Combine(path, "game.exe"), "owned non-runnable game fixture");
        File.WriteAllText(Path.Combine(path, "editor.exe"), "owned non-runnable editor fixture");
        return Task.CompletedTask;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    void AssertNoActivation()
    {
        Assert.False(File.Exists(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(new[] { ".relicade-mod-writer.lock" }, Directory.GetFileSystemEntries(Live).Select(Path.GetFileName));
        Assert.Equal(0, new FileInfo(Path.Combine(Live, ".relicade-mod-writer.lock")).Length);
    }

    [Fact]
    public async Task Callback_receives_immutable_relative_choices_and_no_live_payload()
    {
        var calls = 0;
        var tx = new PortableInstallationTransaction(Live, new() { ChooseExecutableAsync = async (choices, token) =>
        {
            calls++; AssertNoActivation(); Assert.Equal(new[] { "editor.exe", "game.exe" }, choices);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)choices)[0] = "other.exe");
            Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(Live).AcquireLease());
            await Task.Yield(); return "game.exe";
        } });
        await tx.InstallAsync(Stage, "v1");
        Assert.Equal(1, calls); Assert.Equal("game.exe", tx.ReadCommitted()!.SelectedExecutable);
    }

    [Theory]
    [InlineData(null)][InlineData("../outside.exe")][InlineData("missing.exe")][InlineData("GAME.exe")]
    public async Task Declined_or_invalid_choice_never_activates(string? choice)
    {
        var tx = new PortableInstallationTransaction(Live, new() { ChooseExecutableAsync = (_, _) => Task.FromResult(choice) });
        var error = await Record.ExceptionAsync(() => tx.InstallAsync(Stage, "v1"));
        if (choice == null) Assert.IsType<PortableExecutableSelectionCancelledException>(error);
        else Assert.IsType<InvalidDataException>(error);
        AssertNoActivation();
    }

    [Fact]
    public async Task Cancellation_keeps_authority_until_the_actual_callback_drains()
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tx = new PortableInstallationTransaction(Live, new() { ChooseExecutableAsync = (_, _) => { entered.SetResult(); return reply.Task; } });
        var operation = tx.InstallAsync(Stage, "v1", cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cancel.Cancel(); Assert.False(operation.IsCompleted);
            Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(Live).AcquireLease());
            AssertNoActivation();
        }
        finally
        {
            reply.TrySetResult("game.exe");
            await Assert.ThrowsAsync<PortableExecutableSelectionCancelledException>(() => operation);
        }
        using var released = new PortableInstallationTransaction(Live).AcquireLease();
        AssertNoActivation();
    }

    [Theory]
    [InlineData("edit")][InlineData("add")][InlineData("remove")][InlineData("link")]
    public async Task Changed_staging_during_the_choice_preserves_the_old_generation(string change)
    {
        var initial = new PortableInstallationTransaction(Live);
        await initial.InstallAsync(path => { File.WriteAllText(Path.Combine(path, "old.exe"), "old payload"); return Task.CompletedTask; }, "v1");
        var originalManifest = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        string staged = "";
        var tx = new PortableInstallationTransaction(Live, new() { ChooseExecutableAsync = (_, _) =>
        {
            var file = Path.Combine(staged, "editor.exe");
            if (change == "edit") File.WriteAllText(file, "changed payload");
            if (change == "add") File.WriteAllText(Path.Combine(staged, "unknown.exe"), "extra payload");
            if (change is "remove" or "link") File.Delete(file);
            if (change == "link") File.CreateSymbolicLink(file, Path.Combine(staged, "game.exe"));
            return Task.FromResult<string?>("game.exe");
        } });
        var error = await Record.ExceptionAsync(() => tx.InstallAsync(path => { staged = path; return Stage(path); }, "v2"));
        Assert.NotNull(error); Assert.True(error is IOException or InvalidDataException, error.ToString());
        Assert.Equal(originalManifest, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal("old payload", File.ReadAllText(Path.Combine(Live, "old.exe")));
    }

    [Theory]
    [InlineData("single")][InlineData("explicit")][InlineData("initial")][InlineData("retained")]
    public async Task Unambiguous_or_authorized_selection_does_not_prompt(string mode)
    {
        if (mode == "retained") await new PortableInstallationTransaction(Live, new() { SelectedExecutableRelativePath = "editor.exe" }).InstallAsync(Stage, "v1");
        var tx = new PortableInstallationTransaction(Live, new()
        {
            SelectedExecutableRelativePath = mode == "explicit" ? "editor.exe" : null,
            InitialExecutableRelativePath = mode == "initial" ? "editor.exe" : null,
            ChooseExecutableAsync = (_, _) => throw new Exception("Resolved selection must not prompt")
        });
        await tx.InstallAsync(path =>
        {
            if (mode == "single") { File.WriteAllText(Path.Combine(path, "editor.exe"), "fixture"); return Task.CompletedTask; }
            return Stage(path);
        }, "v2");
        Assert.Equal("editor.exe", tx.ReadCommitted()!.SelectedExecutable);
    }

    [Fact]
    public async Task Interactive_choice_does_not_silently_prefer_a_root_helper_over_a_nested_game()
    {
        var calls = 0;
        var tx = new PortableInstallationTransaction(Live, new() { ChooseExecutableAsync = (choices, _) =>
        { calls++; Assert.Equal(new[] { "bin/game.exe", "helper.exe" }, choices); return Task.FromResult<string?>("bin/game.exe"); } });
        await tx.InstallAsync(path =>
        {
            Directory.CreateDirectory(Path.Combine(path, "bin"));
            File.WriteAllText(Path.Combine(path, "helper.exe"), "helper");
            File.WriteAllText(Path.Combine(path, "bin", "game.exe"), "game"); return Task.CompletedTask;
        }, "v1");
        Assert.Equal(1, calls); Assert.Equal("bin/game.exe", tx.ReadCommitted()!.SelectedExecutable);
    }
}
