using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// Synthetic siblings, outside lock-only parent history, never native trash/game execution.
public sealed class ManagedRemovalCompositionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in new[] { "library", "cli", "cli-injected" })
        foreach (var outcome in new[] { "allowed", "callback-error", "persistence-error", "active-writer", "active-mod", "located-managed", "default-managed", "located-history" })
            yield return [route, outcome];
    }
    static string Apps(ManagedRemovalFixture f) => Path.Combine(f.Root, "Apps");
    static string Linked(ManagedRemovalFixture f) => Path.Combine(Apps(f), "linked");
    static string Mod(ManagedRemovalFixture f) => Path.Combine(Apps(f), ".relicade-mod-writer.lock");
    static void Prepare(ManagedRemovalFixture f)
    {
        Directory.CreateDirectory(Linked(f));
        File.WriteAllText(Path.Combine(Linked(f), "fixture.exe"), "NONEXECUTABLE SYNTHETIC");
        File.WriteAllText(Path.Combine(Linked(f), InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        File.WriteAllText(Path.Combine(Linked(f), "version.txt"), "old");
        File.WriteAllText(Path.Combine(Linked(f), "LastPlayed.txt"), "synthetic saved data");
        File.WriteAllText(Path.Combine(Linked(f), "keep"), "unrelated user data");
        // Identities are outside both disjoint targets. Repeated slashes/dot aliases
        // canonicalize to the same paths; no symlink/hardlink permission expansion.
        foreach (var p in new[] { Apps(f), f.Live, Linked(f) }) { using var held = new PortableInstallationTransaction(p).AcquireLease(); }
        File.WriteAllText(Mod(f), "");
    }
    static Process Start(ManagedRemovalFixture f, string mode)
    {
        var exe = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe) == "dotnet") psi.ArgumentList.Add(typeof(ManagedRemovalCompositionTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ManagedRemovalCompositionTests.Worker" }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_COMPOSITION_ROOT"] = f.Root; psi.Environment["RELICADE_COMPOSITION_MODE"] = mode;
        return Process.Start(psi)!;
    }
    void Success(Process worker, string label)
    {
        var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            Assert.True(worker.WaitForExit(40000), "Timed out waiting for own-source authority probe");
            var text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            var dir = Path.Combine(Path.GetTempPath(), "composition-process-receipts"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, worker.Id + ".json"), JsonSerializer.Serialize(new { label, pid = worker.Id, exit = worker.ExitCode, output = text }));
            output.WriteLine($"{label}; pid={worker.Id}; exit={worker.ExitCode}; {text}"); Assert.True(worker.ExitCode == 0, text);
        }
        finally { if (!worker.HasExited) { worker.Kill(true); worker.WaitForExit(); } }
    }
    [Fact]
    public void Worker()
    {
        var root = Environment.GetEnvironmentVariable("RELICADE_COMPOSITION_ROOT");
        if (root == null) return; // explicitly unaccepted standalone worker entry
        using var f = new ManagedRemovalFixture(root, initialize: false);
        var mode = Environment.GetEnvironmentVariable("RELICADE_COMPOSITION_MODE");
        if (mode == "probe-held")
        {
            foreach (var p in new[] { Apps(f), f.Live, Linked(f) })
                Assert.ThrowsAny<IOException>(() => { using var held = new PortableInstallationTransaction(p).AcquireLease(); });
            Assert.ThrowsAny<IOException>(() => { using var held = new FileStream(Mod(f), FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            Console.WriteLine("Actual independent writer/mod probe refused all THREE stable authorities and common mod identity during callback");
        }
        else if (mode == "probe-released")
        {
            foreach (var p in new[] { Apps(f), f.Live, Linked(f) }) { using var held = new PortableInstallationTransaction(p).AcquireLease(); }
            using var mod = new FileStream(Mod(f), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Console.WriteLine("Actual independent writer/mod probe acquired all THREE stable authorities and common mod identity after operation");
        }
        else if (mode is "active-writer" or "active-mod")
        {
            using var held = mode == "active-writer" ? new PortableInstallationTransaction(Apps(f)).AcquireLease() : new FileStream(Mod(f), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            File.WriteAllText(Path.Combine(root, "writer-barrier"), mode);
            using var wait = new ManualResetEventSlim(); wait.Wait();
        }
        else throw new InvalidOperationException(mode);
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task Sibling_removal_composes_authority_validates_every_target_and_releases_after_errors(string route, string outcome)
    {
        using var f = new ManagedRemovalFixture(); Prepare(f);
        if (outcome == "located-managed") File.WriteAllText(Path.Combine(Linked(f), PortableInstallationTransaction.ManifestFileName), "unknown marker must refuse");
        if (outcome == "default-managed") { Directory.CreateDirectory(f.Live); File.WriteAllText(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName), "unknown default marker must refuse"); }
        if (outcome == "located-history") File.WriteAllText(Path.Combine(new PortableInstallationTransaction(Linked(f)).OperationsPath, "unknown"), "retained evidence must refuse");
        using var writer = outcome.StartsWith("active-") ? Start(f, outcome) : null;
        if (writer != null)
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(f.Root, "writer-barrier")) && !writer.HasExited && timer.Elapsed < TimeSpan.FromSeconds(35)) await Task.Delay(20);
            Assert.False(writer.HasExited); Assert.True(File.Exists(Path.Combine(f.Root, "writer-barrier")));
        }
        var callbacks = new List<string>(); string? libraryFile = null;
        void Mutation(string path)
        {
            callbacks.Add(path);
            using (var probe = Start(f, "probe-held")) Success(probe, route + " " + outcome + " callback-held");
            if (outcome == "callback-error") throw new IOException("synthetic composition callback failure");
            File.Move(path, Path.Combine(f.Root, "benign-trash"));
            // A callback-side synthetic corruption makes real persistence fail
            // AFTER metadata mutation; no claimed transactional rollback.
            if (outcome == "persistence-error") File.WriteAllText(libraryFile!, "{synthetic corrupt library");
        }
        var reachesCallback = outcome is "allowed" or "callback-error" or "persistence-error";
        string[] before, after;
        try
        {
            if (route == "library")
            {
                await using var h = new ManagedRemovalLibraryTests.Harness(f, true);
                h.Game.InstallPath = Linked(f); h.Mutation = Mutation; await h.Save();
                libraryFile = Path.Combine(f.Root, "catalog", "apps.json");
                before = f.Snapshot(); await h.Remove(); after = f.Snapshot();
                Assert.Equal(0, h.Network.Calls); Assert.Equal(outcome == "allowed" ? 0 : 1, h.Errors.Count);
                Assert.Equal(reachesCallback ? 1 : 0, h.Questions);
                if (!reachesCallback || outcome == "callback-error") { Assert.Equal(Linked(f), h.Game.InstallPath); Assert.Equal(before, after); }
                if (outcome == "allowed") Assert.Null(h.Game.InstallPath);
                if (outcome == "persistence-error") Assert.Contains("Failed to delete", h.Errors[0]);
                output.WriteLine($"Library outcome={outcome}; errors={string.Join(';', h.Errors)}; callbacks={callbacks.Count}; HTTP={h.Network.Calls}");
            }
            else
            {
                using var vault = new CredentialVaultTestScope();
                var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
                var catalog = Path.Combine(f.Root, "cli-catalog"); Directory.CreateDirectory(Path.Combine(catalog, "Apps")); Directory.CreateDirectory(Path.Combine(catalog, "Cache"));
                RelicadePaths.OverrideUserDataRoot = catalog;
                var store = new FileSettingsStore(Path.Combine(catalog, "settings.json"));
                File.WriteAllText(Path.Combine(catalog, "settings.json"), JsonSerializer.Serialize(new AppSettings { AppsPath = Apps(f) })); SettingsStoreProvider.Default = store;
                var game = new GameInfo { Name = "Synthetic composition", FolderName = "live", Repository = "fixture/composition", InstallPath = Linked(f) };
                libraryFile = Path.Combine(catalog, "apps.json"); File.WriteAllText(libraryFile, JsonSerializer.Serialize(new { apps = new[] { AppCatalogService.SerializeApp(game) } }));
                var network = new ManagedRemovalLibraryTests.Handler();
                using var manager = route == "cli-injected" ? new GameManager(store, new HttpClient(network), new AppCatalogService(dataDirectory: catalog)) : null;
                var cli = new CLIHandler(manager, moveToTrash: Mutation) { RemovalHttpClient = new HttpClient(network) };
                var old = Console.Out; using var text = new StringWriter(); int code;
                before = f.Snapshot();
                try { Console.SetOut(text); code = await cli.Execute([route == "cli" ? "--uninstall" : "-x", game.Name]); }
                finally { Console.SetOut(old); RelicadePaths.OverrideUserDataRoot = priorRoot; SettingsStoreProvider.Default = priorStore; }
                after = f.Snapshot(); Assert.Equal(outcome == "allowed" ? 0 : 1, code); Assert.Equal(0, network.Calls);
                if (!reachesCallback || outcome == "callback-error") Assert.Equal(before, after);
                if (outcome != "allowed") { Assert.DoesNotContain("other files were kept", text.ToString()); Assert.Contains("ERROR:", text.ToString()); }
                output.WriteLine($"route={route}; outcome={outcome}; code={code}; callbacks={callbacks.Count}; HTTP={network.Calls}; stdout={text}");
            }
            Assert.Equal(reachesCallback ? new[] { Path.Combine(Linked(f), "fixture.exe") } : Array.Empty<string>(), callbacks);
            Assert.Equal("unrelated user data", File.ReadAllText(Path.Combine(Linked(f), "keep")));
            if (outcome == "allowed") { Assert.False(File.Exists(Path.Combine(Linked(f), "version.txt"))); Assert.False(File.Exists(Path.Combine(Linked(f), "LastPlayed.txt"))); }
        }
        finally { if (writer != null && !writer.HasExited) { writer.Kill(true); await writer.WaitForExitAsync(); output.WriteLine($"Killed own held-authority worker pid={writer.Id}; exit={writer.ExitCode}"); } }
        using (var probe = Start(f, "probe-released")) Success(probe, route + " " + outcome + " released");
    }
    [Fact]
    public async Task Common_authority_is_released_during_consent_and_revalidated_after_parent_writer_enters()
    {
        using var f = new ManagedRemovalFixture(); Prepare(f);
        await using var h = new ManagedRemovalLibraryTests.Harness(f, true); h.Game.InstallPath = Linked(f); await h.Save();
        IDisposable? writer = null; string[]? before = null;
        h.Confirm = () =>
        {
            using (var probe = Start(f, "probe-released")) Success(probe, "consent exclusion released");
            writer = new PortableInstallationTransaction(Apps(f)).AcquireLease(); before = f.Snapshot(); return Task.FromResult(true);
        };
        try { await h.Remove(); Assert.Single(h.Errors); Assert.Empty(h.Callbacks); Assert.Equal(1, h.Questions); Assert.Equal(before, f.Snapshot()); Assert.Equal(Linked(f), h.Game.InstallPath); Assert.Equal(0, h.Network.Calls); }
        finally { writer?.Dispose(); }
        using var released = Start(f, "probe-released"); Success(released, "consent failure released");
    }
}
