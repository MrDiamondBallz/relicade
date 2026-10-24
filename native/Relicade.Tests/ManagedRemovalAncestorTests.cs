using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// All data synthetic/nonexecutable; no native trash/platform dispatch.
public sealed class ManagedRemovalAncestorTests(ITestOutputHelper output)
{
    static void Metadata(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "version.txt"), "current synthetic data version");
        File.WriteAllText(Path.Combine(path, "LastPlayed.txt"), "current synthetic data play/save");
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in new[] { "direct", "library", "cli", "cli-injected" })
        foreach (var fault in new[] { "current-saves", "owned-data", "deep", "missing-marker", "corrupt-marker", "marker-only", "retained-previous", "backup", "prePrepared", "missing-live", "dot-alias", "linked-alias", "unknown-history", "missing-ancestor-lock" })
            yield return [route, fault];
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task Every_public_route_preserves_managed_ancestor_and_location_without_creating_child_locks(string route, string fault)
    {
        using var f = new ManagedRemovalFixture(); await f.Install(); await f.Install("new");
        var target = Path.Combine(f.Live, fault == "owned-data" ? "owned-data" : fault == "deep" ? "saves/deep/current" : "saves");
        var operation = Directory.GetDirectories(f.Tx.OperationsPath).First(p => Directory.Exists(Path.Combine(p, "previous")));
        if (fault is "retained-previous" or "backup") target = Path.Combine(operation, fault == "backup" ? "backup/saves" : "previous/saves");
        if (fault == "prePrepared") target = Path.Combine(f.Tx.OperationsPath, Guid.NewGuid().ToString("N"), "candidate", "saves");
        Metadata(target);
        if (fault is "missing-marker" or "unknown-history" or "missing-ancestor-lock") File.Delete(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
        if (fault == "corrupt-marker") File.WriteAllText(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName), "{bad");
        if (fault == "marker-only")
        {
            // Authority cannot depend on valid history or a parseable manifest.
            target = Path.Combine(f.Root, "marker-only", "current"); Metadata(target);
            File.WriteAllText(Path.Combine(f.Root, "marker-only", PortableInstallationTransaction.ManifestFileName), "{unknown");
        }
        if (fault == "unknown-history") File.WriteAllText(Path.Combine(f.Tx.OperationsPath, "unknown"), "retained unknown authority");
        if (fault == "missing-ancestor-lock") File.Delete(Path.Combine(f.Tx.OperationsPath, "writer.lock"));
        if (fault == "missing-live")
        {
            Directory.Move(f.Live, Path.Combine(f.Root, "retained-live")); // target is now absent, durable authority is not
        }
        if (fault == "dot-alias") target = Path.Combine(f.Live, "saves", "..", "saves");
        if (fault == "linked-alias")
        {
            var alias = Path.Combine(f.Root, "linked");
            try { Directory.CreateSymbolicLink(alias, f.Live); }
            catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
            { Assert.Skip("The Windows process lacks directory symbolic-link privilege (ERROR_PRIVILEGE_NOT_HELD); no OS security setting was changed and this link route is not validated."); }
            target = Path.Combine(alias, "saves");
        }
        await Refuse(f, target, route);
    }
    async Task Refuse(ManagedRemovalFixture f, string target, string route)
    {
        if (route == "direct")
        {
            var before = f.Snapshot(); var calls = new List<string>();
            var error = Record.Exception(() => LocatedInstallRemoval.Remove(target, false, calls.Add));
            Assert.NotNull(error); Assert.Empty(calls); Assert.Equal(before, f.Snapshot());
            output.WriteLine($"direct target={target}; refusal={error.GetType().Name}; callbacks=0; exact snapshot preserved");
        }
        else if (route == "library")
        {
            await using var h = new ManagedRemovalLibraryTests.Harness(f, true);
            h.Game.FolderName = "unmanaged-tile"; h.Game.InstallPath = target;
            using (new PortableInstallationTransaction(Path.Combine(f.Root, "Apps", "unmanaged-tile")).AcquireLease()) { }
            await h.Save(); var before = f.Snapshot(); await h.Remove();
            Assert.Equal(before, f.Snapshot()); Assert.Equal(target, h.Game.InstallPath);
            Assert.Single(h.Errors); Assert.Empty(h.Callbacks); Assert.Equal(0, h.Network.Calls);
            Assert.Equal(0, h.Questions); Assert.Equal(0, h.Opened); Assert.Equal(0, h.Settings.Saves);
            output.WriteLine($"LibraryActions refusal={h.Errors[0]}; callbacks/HTTP/questions/OS dispatch=0; location retained");
        }
        else
        {
            using var vault = new CredentialVaultTestScope();
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            var catalog = Path.Combine(f.Root, "cli-catalog");
            Directory.CreateDirectory(Path.Combine(catalog, "Apps")); Directory.CreateDirectory(Path.Combine(catalog, "Cache"));
            using (new PortableInstallationTransaction(Path.Combine(f.Root, "Apps", "unmanaged-tile")).AcquireLease()) { }
            RelicadePaths.OverrideUserDataRoot = catalog;
            var store = new FileSettingsStore(Path.Combine(catalog, "settings.json"));
            File.WriteAllText(Path.Combine(catalog, "settings.json"), JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(f.Root, "Apps") }));
            SettingsStoreProvider.Default = store;
            var game = new GameInfo { Name = "Synthetic ancestor tile", Repository = "fixture/ancestor", FolderName = "unmanaged-tile", InstallPath = target };
            File.WriteAllText(Path.Combine(catalog, "apps.json"), JsonSerializer.Serialize(new { apps = new[] { AppCatalogService.SerializeApp(game) } }));
            var network = new ManagedRemovalLibraryTests.Handler(); var calls = new List<string>();
            using var manager = route == "cli-injected" ? new GameManager(store, new HttpClient(network), new AppCatalogService(dataDirectory: catalog)) : null;
            var cli = new CLIHandler(manager, moveToTrash: calls.Add) { RemovalHttpClient = new HttpClient(network) };
            var before = f.Snapshot(); var old = Console.Out; using var text = new StringWriter(); int code;
            try { Console.SetOut(text); code = await cli.Execute([route == "cli" ? "--uninstall" : "-x", game.Name]); }
            finally { Console.SetOut(old); SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot; }
            Assert.NotEqual(0, code); Assert.Contains("ERROR:", text.ToString()); Assert.DoesNotContain("other files were kept", text.ToString());
            Assert.DoesNotContain("successfully", text.ToString()); Assert.Equal(before, f.Snapshot()); Assert.Empty(calls); Assert.Equal(0, network.Calls);
            output.WriteLine($"public Execute route={route}; exit={code}; HTTP/callbacks=0; stdout={text}");
        }
    }
    [Theory]
    [InlineData("history")][InlineData("marker")][InlineData("ancestor")]
    public async Task Unreadable_ancestor_authority_refuses_with_caps_zero(string denied)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid(),
            "This fixture requires Linux /proc capability evidence and POSIX mode denial; it does not validate Windows ACL refusal.");
        Assert.Contains("CapEff:\t0000000000000000", File.ReadAllText("/proc/self/status"));
        using var f = new ManagedRemovalFixture(); await f.Install();
        var target = Path.Combine(f.Live, "saves"); Metadata(target);
        var path = denied == "marker" ? Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName) : denied == "history" ? f.Tx.OperationsPath : f.Live;
        if (denied == "history") File.Delete(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
        var before = f.Snapshot(); var mode = File.GetUnixFileMode(path); File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.ThrowsAny<Exception>(() => LocatedInstallRemoval.Remove(target, false, _ => throw new Exception("callback forbidden")));
            if (denied == "marker") Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(path));
            else Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(path));
        }
        finally { File.SetUnixFileMode(path, mode); }
        Assert.Equal(before, f.Snapshot());
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Unmanaged_nested_folder_keeps_exact_semantics_and_holds_existing_ancestor_exclusion(bool throwing)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        var child = Path.Combine(f.Live, "ordinary-child"); Metadata(child);
        File.WriteAllText(Path.Combine(child, "fixture.exe"), "NONEXECUTABLE synthetic");
        File.WriteAllText(Path.Combine(child, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        using (new PortableInstallationTransaction(child).AcquireLease()) { }
        var before = f.Snapshot(); var calls = new List<string>();
        var error = Record.Exception(() => LocatedInstallRemoval.Remove(child, false, path =>
        {
            calls.Add(path);
            Assert.ThrowsAny<IOException>(() => { using var ancestor = f.Tx.AcquireLease(); });
            Assert.ThrowsAny<IOException>(() => { using var own = new PortableInstallationTransaction(child).AcquireLease(); });
            if (throwing) throw new IOException("synthetic callback error");
            File.Move(path, Path.Combine(f.Root, "benign-trash"));
        }));
        Assert.Equal(new[] { Path.Combine(child, "fixture.exe") }, calls);
        if (throwing) { Assert.IsType<IOException>(error); Assert.Equal(before, f.Snapshot()); }
        else { Assert.Null(error); Assert.True(File.Exists(Path.Combine(f.Root, "benign-trash"))); Assert.False(File.Exists(Path.Combine(child, "version.txt"))); }
        using var after = f.Tx.AcquireLease(); // release includes ancestor authority on BOTH outcomes
    }
    static Process Child(string root, string mode)
    {
        var exe = Environment.ProcessPath!; var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(ManagedRemovalAncestorTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ManagedRemovalAncestorTests.Worker" }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_ANCESTOR_ROOT"] = root; psi.Environment["RELICADE_ANCESTOR_MODE"] = mode;
        return Process.Start(psi)!;
    }
    async Task Success(Process p, string label)
    {
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)); await p.WaitForExitAsync(timeout.Token);
        var text = await stdout + await stderr;
        var receipts = Path.Combine(Path.GetTempPath(), "ancestor-process-receipts"); Directory.CreateDirectory(receipts);
        File.WriteAllText(Path.Combine(receipts, p.Id + ".json"), JsonSerializer.Serialize(new { label, pid = p.Id, exit = p.ExitCode, output = text }));
        output.WriteLine($"{label}; pid={p.Id}; exit={p.ExitCode}; {text}"); Assert.True(p.ExitCode == 0, text);
    }
    [Fact]
    public async Task Worker()
    {
        var root = Environment.GetEnvironmentVariable("RELICADE_ANCESTOR_ROOT"); if (root == null) return; // not a substantive standalone test
        var mode = Environment.GetEnvironmentVariable("RELICADE_ANCESTOR_MODE")!;
        using var f = new ManagedRemovalFixture(root, initialize: false);
        void Pause() { File.WriteAllText(Path.Combine(root, "barrier"), mode); using var wait = new ManualResetEventSlim(); wait.Wait(); }
        if (mode == "writer") { using var held = f.Tx.AcquireLease(); Pause(); }
        else if (mode.StartsWith("pause:")) await ManagedRemovalFixture.InstallAt(f.Live, "new", p => { if (p == mode[6..]) Pause(); });
        else if (mode == "reopen")
        {
            Assert.Equal("old", f.Tx.ReadCommitted()!.SelectedVersion);
            Assert.Equal("current save old", File.ReadAllText(Path.Combine(f.Live, "saves", "slot")));
            Assert.Equal("synthetic owned original", File.ReadAllText(Path.Combine(f.Live, "owned-data", "original")));
            Console.WriteLine("Actual independent OLD reopen preserved current saves and owned data");
        }
        else if (mode == "refuse")
        {
            var child = Path.Combine(f.Live, "saves");
            foreach (var route in new[] { "direct", "library", "cli", "cli-injected" }) await Refuse(f, child, route);
            Console.WriteLine("Actual separate removal worker refused all four entrypoint variants without child locks/recovery/live recreation");
        }
        else throw new InvalidOperationException(mode);
    }
    [Theory]
    [InlineData("writer")][InlineData("pause:Extracted")][InlineData("pause:PreviousRenamed")][InlineData("pause:CandidateRenamed")]
    public async Task Separate_active_parent_writer_and_killed_rename_gaps_exclude_descendant_removal(string mode)
    {
        using var f = new ManagedRemovalFixture();
        if (mode == "writer") Metadata(Path.Combine(f.Live, "saves")); else await f.Install();
        using var writer = Child(f.Root, mode);
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(f.Root, "barrier")) && !writer.HasExited && timer.Elapsed < TimeSpan.FromSeconds(35)) await Task.Delay(20);
            Assert.False(writer.HasExited); Assert.True(File.Exists(Path.Combine(f.Root, "barrier")));
            using (var remover = Child(f.Root, "refuse")) await Success(remover, "active parent " + mode);
            writer.Kill(true); await writer.WaitForExitAsync(); output.WriteLine($"actual parent kill pid={writer.Id}; exit={writer.ExitCode}; mode={mode}");
            if (mode != "writer")
            {
                using (var remover = Child(f.Root, "refuse")) await Success(remover, "killed parent before recovery " + mode);
                using (var reopen = Child(f.Root, "reopen")) await Success(reopen, "independent reopen " + mode);
            }
        }
        finally { if (!writer.HasExited) { writer.Kill(true); await writer.WaitForExitAsync(); } }
    }
}
