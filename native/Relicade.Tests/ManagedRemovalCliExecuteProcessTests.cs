using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

// Retained method-level and desktop process fixtures are unchanged. These NEW
// workers call public Execute with initially absent manager in their own process.
public sealed class ManagedRemovalCliExecuteProcessTests
{
    static Process Child(string root, string mode)
    {
        var exe = Environment.ProcessPath!; var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(ManagedRemovalCliExecuteProcessTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ManagedRemovalCliExecuteProcessTests.Worker" }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_EXECUTE_REMOVAL_ROOT"] = root; psi.Environment["RELICADE_EXECUTE_REMOVAL_MODE"] = mode;
        return Process.Start(psi)!;
    }
    static async Task Success(Process child, string label)
    {
        var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35)); await child.WaitForExitAsync(timeout.Token);
        var text = await output + await errors; Console.WriteLine(label + " pid=" + child.Id + " exit=" + child.ExitCode + "\n" + text);
        var receipts = Path.Combine(Path.GetTempPath(), "removal-execute-process-receipts"); Directory.CreateDirectory(receipts);
        File.WriteAllText(Path.Combine(receipts, child.Id + ".json"), JsonSerializer.Serialize(new { label, pid = child.Id, exit = child.ExitCode, output = text }));
        Assert.True(child.ExitCode == 0, text);
    }
    static async Task Barrier(Process child, string root)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "barrier")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(20);
        Assert.False(child.HasExited); Assert.True(File.Exists(Path.Combine(root, "barrier")));
    }
    [Fact]
    public async Task Worker()
    {
        var root = Environment.GetEnvironmentVariable("RELICADE_EXECUTE_REMOVAL_ROOT");
        if (root == null) return; // standalone entry NO-OP, explicitly UNACCEPTED
        var mode = Environment.GetEnvironmentVariable("RELICADE_EXECUTE_REMOVAL_MODE")!;
        using var f = new ManagedRemovalFixture(root, initialize: false);
        void Pause() { File.WriteAllText(Path.Combine(root, "barrier"), mode); using var wait = new ManualResetEventSlim(); wait.Wait(); }
        if (mode.StartsWith("pause:")) await ManagedRemovalFixture.InstallAt(f.Live, "new", p => { if (p == mode[6..]) Pause(); });
        else if (mode == "hold-writer") { using var lease = f.Tx.AcquireLease(); Pause(); }
        else if (mode == "hold-G05") { using var lease = ModInstallTransaction.Acquire(f.Live); Pause(); }
        else if (mode == "tracked") Pause();
        else if (mode == "refuse")
        {
            using var vault = new CredentialVaultTestScope(); var previous = SettingsStoreProvider.Default; var oldRoot = RelicadePaths.OverrideUserDataRoot;
            var catalog = Path.Combine(root, "execute-catalog"); Directory.CreateDirectory(catalog);
            Directory.CreateDirectory(Path.Combine(catalog, "Cache")); Directory.CreateDirectory(Path.Combine(catalog, "Apps"));
            RelicadePaths.OverrideUserDataRoot = catalog;
            var settings = Path.Combine(catalog, "settings.json");
            File.WriteAllText(settings, JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(root, "Apps") }));
            File.WriteAllText(Path.Combine(catalog, "apps.json"), JsonSerializer.Serialize(new { apps = new[] { new { name = "Synthetic Execute", repository = "fixture/removal", folderName = "live", installPath = f.Live, autoUpdate = true, appIconUrl = "https://example.invalid/never.png" } } }));
            SettingsStoreProvider.Default = new FileSettingsStore(settings);
            var network = new ManagedRemovalLibraryTests.Handler(); var callbacks = 0; var before = f.Snapshot(); var exists = Directory.Exists(f.Live);
            try
            {
                var result = await new CLIHandler(moveToTrash: _ => { callbacks++; throw new Exception("No real/native helper allowed"); })
                    { RemovalHttpClient = new HttpClient(network) }.Execute(["--uninstall", "Synthetic Execute"]);
                Assert.NotEqual(0, result); Assert.Equal(0, callbacks); Assert.Equal(0, network.Calls);
                Assert.Equal(exists, Directory.Exists(f.Live)); Assert.Equal(before, f.Snapshot());
                Console.WriteLine("ACTUAL public Execute initially absent manager refused; application_HTTP_requests=0; byte snapshot and missing-live identity preserved; liveExists=" + exists);
            }
            finally { SettingsStoreProvider.Default = previous; RelicadePaths.OverrideUserDataRoot = oldRoot; }
        }
        else if (mode == "reopen")
        {
            var manifest = f.Tx.ReadCommitted(); Assert.NotNull(manifest); Assert.Equal("old", manifest.SelectedVersion);
            Assert.Equal("NONEXECUTABLE synthetic old", File.ReadAllText(Path.Combine(f.Live, "fixture.exe")));
            Assert.Equal("current save old", File.ReadAllText(Path.Combine(f.Live, "saves", "slot")));
            Assert.Equal("synthetic owned original", File.ReadAllText(Path.Combine(f.Live, "owned-data", "original")));
            Assert.Equal("MR01 synthetic sibling canary", File.ReadAllText(Path.Combine(root, "sibling-canary")));
        }
        else throw new InvalidOperationException(mode);
    }
    [Theory]
    [InlineData("Extracted")][InlineData("Prepared")][InlineData("Applying")][InlineData("PreviousRenamed")][InlineData("CandidateRenamed")]
    public async Task Public_Execute_refuses_while_paused_after_kill_and_before_independent_reopen(string point)
    {
        using var f = new ManagedRemovalFixture(); await f.Install(); using var writer = Child(f.Root, "pause:" + point);
        try
        {
            await Barrier(writer, f.Root); Assert.Equal(point != "PreviousRenamed", Directory.Exists(f.Live));
            using (var remover = Child(f.Root, "refuse")) await Success(remover, "public Execute while writer " + point);
            writer.Kill(true); await writer.WaitForExitAsync(); Console.WriteLine("Killed own writer " + writer.Id + " exit=" + writer.ExitCode);
            using (var remover = Child(f.Root, "refuse")) await Success(remover, "public Execute after kill " + point);
            using (var reopen = Child(f.Root, "reopen")) await Success(reopen, "independent reopen " + point);
        }
        finally { if (!writer.HasExited) { writer.Kill(true); await writer.WaitForExitAsync(); } }
    }
    [Theory]
    [InlineData("hold-writer")][InlineData("hold-G05")]
    public async Task Public_Execute_respects_independent_unmanaged_writer_domains(string mode)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live); File.WriteAllText(Path.Combine(f.Live, "version.txt"), "unmanaged");
        using var writer = Child(f.Root, mode);
        try { await Barrier(writer, f.Root); using var remover = Child(f.Root, "refuse"); await Success(remover, mode); }
        finally { if (!writer.HasExited) { writer.Kill(true); await writer.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task Public_Execute_respects_actual_owned_process_lifetime()
    {
        using var f = new ManagedRemovalFixture(); await f.Install(); using var lifetime = PortableInstallationLifetime.Begin(f.Live); using var tracked = Child(f.Root, "tracked");
        try { await Barrier(tracked, f.Root); lifetime.Attach(tracked); using var remover = Child(f.Root, "refuse"); await Success(remover, "actual tracked own-source lifetime"); }
        finally { if (!tracked.HasExited) { tracked.Kill(true); await tracked.WaitForExitAsync(); } }
        var timer = Stopwatch.StartNew();
        while (File.Exists(Path.Combine(f.Tx.OperationsPath, "launch.json")) && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Assert.False(File.Exists(Path.Combine(f.Tx.OperationsPath, "launch.json")));
    }
}
