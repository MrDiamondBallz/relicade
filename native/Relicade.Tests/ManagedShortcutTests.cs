using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// Dispatch fixtures NEVER execute payload bytes or a desktop/Steam process.
public sealed class ManagedShortcutTests : IDisposable
{
    readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "shortcut-fixture-" + Guid.NewGuid().ToString("N"));
    readonly ITestOutputHelper output;
    string Apps => Path.Combine(root, "apps");
    string Launcher => Path.Combine(root, "launcher", "Relicade.Desktop");
    public ManagedShortcutTests(ITestOutputHelper output) { this.output = output; Directory.CreateDirectory(Apps); Directory.CreateDirectory(Path.GetDirectoryName(Launcher)!); File.WriteAllText(Launcher, "OWN NONEXECUTED LAUNCHER FIXTURE"); }
    public void Dispose() => Directory.Delete(root, true);
    static GameInfo Game(string folder = "one", bool manual = false) => new() { Name = "same name", FolderName = folder, Repository = manual ? "" : "fixture/same", AutoUpdate = true };
    async Task Install(GameInfo game, string file = "fixture.AppImage", string version = "v1")
    {
        var source = Path.Combine(root, file); File.WriteAllText(source, "NONEXECUTABLE SYNTHETIC GAME " + version);
        await GameInstallationService.InstallOrUpdateGameAsync(source, game.GetInstallPath(Apps), file, version);
    }
    async Task WriteLibrary(params GameInfo[] games)
    {
        // Write raw rows: the ordinary save/parser may deduplicate them for display.
        Directory.CreateDirectory(Path.Combine(root, "library"));
        await File.WriteAllTextAsync(Path.Combine(root, "library", "apps.json"), JsonSerializer.Serialize(new { apps = games.Select(g => new { name = g.Name, repository = g.Repository, folderName = g.FolderName, autoUpdate = true, gameIconUrl = "https://example.invalid/art.png" }) }));
    }
    GameManager Manager(out Store store, out Reject network)
    {
        store = new Store(Apps); network = new Reject();
        return new GameManager(store, new HttpClient(network), new AppCatalogService(dataDirectory: Path.Combine(root, "library")));
    }
    async Task<int> Dispatch(string[] args, Func<GameInfo, string, Task<(bool, Task?)>> launch)
    {
        using var manager = Manager(out var store, out var network);
        var library = Path.Combine(root, "library", "apps.json"); var before = File.ReadAllBytes(library);
        var settings = JsonSerializer.Serialize(store.Current);
        var result = await new CLIHandler(manager, launch).Execute(args);
        Assert.Equal(0, network.Attempts); Assert.Equal(0, store.Saves);
        Assert.Equal(settings, JsonSerializer.Serialize(store.Current)); Assert.Equal(before, File.ReadAllBytes(library));
        output.WriteLine($"application_network_attempts={network.Attempts}; settings_saves={store.Saves}; library_bytes_unchanged=true; exit={result}");
        return result;
    }
    static Task<(bool, Task?)> Never(GameInfo g, string operation) => throw new Xunit.Sdk.XunitException("Unexpected dispatch of synthetic game");

    [Theory]
    [InlineData("one", false)] [InlineData("two", false)] [InlineData("manual tile", true)]
    [InlineData("quote ' space % Ω $ ` tile", false)]
    public async Task Exact_case_insensitive_instance_dispatch_without_network_or_persistence(string folder, bool manual)
    {
        var game = Game(folder, manual); var other = Game("other"); await Install(game); await Install(other); await WriteLibrary(other, game);
        var selection = Path.Combine(game.GetInstallPath(Apps), "selected_executable.txt"); Assert.False(File.Exists(selection));
        var calls = 0;
        var result = await Dispatch([ManagedShortcutLaunch.Command, game.InstanceKey.ToUpperInvariant()], (selected, operation) =>
        {
            calls++; Assert.Equal(game.InstanceKey, selected.InstanceKey); Assert.True(selected.AutoUpdate || manual);
            Assert.Equal(new PortableInstallationTransaction(game.GetInstallPath(Apps)).ReadCommitted()!.OperationId, operation);
            return Task.FromResult<(bool, Task?)>((true, Task.CompletedTask));
        });
        Assert.Equal(0, result); Assert.Equal(1, calls); Assert.False(File.Exists(selection));
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("case-duplicate")] [InlineData("missing")] [InlineData("changed-folder")] [InlineData("changed-repository")] [InlineData("name")] [InlineData("repository")] [InlineData("path")] [InlineData("corrupt")]
    public async Task Missing_changed_ambiguous_or_non_instance_identity_is_not_dispatched(string kind)
    {
        var game = Game(); await Install(game); var old = game.InstanceKey;
        if (kind == "changed-folder") game.FolderName = "renamed";
        if (kind == "changed-repository") game.Repository = "fixture/changed";
        if (kind is "duplicate" or "case-duplicate") await WriteLibrary(game, Game(kind == "duplicate" ? "one" : "ONE"));
        else await WriteLibrary(game);
        if (kind == "missing") await WriteLibrary();
        if (kind == "corrupt") File.WriteAllText(Path.Combine(root, "library", "apps.json"), "{bad");
        var key = kind switch { "name" => game.Name!, "repository" => game.IdentityKey, "path" => game.GetInstallPath(Apps), _ => old };
        Assert.NotEqual(0, await Dispatch([ManagedShortcutLaunch.Command, key], Never));
    }

    public static IEnumerable<object[]> BadArguments()
    {
        yield return [new[] { "--launch-instance=manual:one" }];
        yield return [new[] { "--launch-instance" }]; yield return [new[] { "--launch-instance", "" }];
        yield return [new[] { "--launch-instance", "manual:one", "extra" }];
        yield return [new[] { "--launch-instance", "manual:one", "--run", "name" }];
        yield return [new[] { "--run", "--launch-instance", "manual:one" }];
        yield return [new[] { "--launch-instance", "manual:one", "--launch-instance", "manual:one" }];
        yield return [new[] { "--launch-instance", "--help" }]; yield return [new[] { "--LAUNCH-INSTANCE", "manual:one" }];
        yield return [new[] { "--launch-instance", "manual:one\n" }]; yield return [new[] { "--kiosk", "--launch-instance", "manual:one" }];
    }
    [Theory, MemberData(nameof(BadArguments))]
    public async Task Malformed_exact_command_is_rejected_before_any_load_or_header(string[] args)
    { await WriteLibrary(Game()); Assert.NotEqual(0, await Dispatch(args, Never)); }

    [Fact]
    public void Kiosk_stripped_raw_args_cannot_turn_invalid_invocation_into_valid_route()
    {
        string[] valid = ["--launch-instance", "manual:one"];
        Assert.True(ManagedShortcutLaunch.ValidOriginalArguments(["Relicade", .. valid], valid));
        Assert.False(ManagedShortcutLaunch.ValidOriginalArguments(["Relicade", "--kiosk", .. valid], valid));
        Assert.False(ManagedShortcutLaunch.ValidOriginalArguments(["Relicade", .. valid, "--kiosk"], valid));
    }

    [Theory] [InlineData(false, true)] [InlineData(true, false)]
    public async Task False_launch_or_missing_process_lifetime_is_nonzero(bool launched, bool lifetime)
    { var game = Game(); await Install(game); await WriteLibrary(game); Assert.NotEqual(0, await Dispatch([ManagedShortcutLaunch.Command, game.InstanceKey], (_, _) => Task.FromResult<(bool, Task?)>((launched, lifetime ? Task.CompletedTask : null)))); }

    [Fact]
    public async Task CLI_keeps_own_session_alive_until_observed_lifetime_finishes()
    {
        var game = Game(); await Install(game); await WriteLibrary(game);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Dispatch([ManagedShortcutLaunch.Command, game.InstanceKey], (_, _) => { started.SetResult(); return Task.FromResult<(bool, Task?)>((true, exited.Task)); });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(run.IsCompleted); exited.SetResult(); Assert.Equal(0, await run);
    }
    [Fact]
    public async Task Unknown_lifetime_fault_is_nonzero()
    { var game = Game(); await Install(game); await WriteLibrary(game); Assert.NotEqual(0, await Dispatch([ManagedShortcutLaunch.Command, game.InstanceKey], (_, _) => Task.FromResult<(bool, Task?)>((true, Task.FromException(new IOException("unknown process lifetime")))))); }

    [Theory] [InlineData("uncertain")] [InlineData("running")] [InlineData("changed-bytes")] [InlineData("nonlaunchable")] [InlineData("unmanaged")]
    public async Task Recovery_running_or_unlaunchable_installation_never_dispatches(string kind)
    {
        var game = Game(); if (kind != "unmanaged") await Install(game, kind == "nonlaunchable" ? "fixture.exe" : "fixture.AppImage"); await WriteLibrary(game);
        IDisposable? lease = null;
        try
        {
            if (kind == "running") lease = PortableInstallationLifetime.Begin(game.GetInstallPath(Apps));
            if (kind == "uncertain") { var lifetime = PortableInstallationLifetime.Begin(game.GetInstallPath(Apps)); lifetime.PreserveUncertain(); }
            if (kind == "changed-bytes") File.WriteAllText(Path.Combine(game.GetInstallPath(Apps), "fixture.AppImage"), "CHANGED");
            Assert.NotEqual(0, await Dispatch([ManagedShortcutLaunch.Command, game.InstanceKey], Never));
            if (kind == "uncertain") Assert.True(File.Exists(Path.Combine(new PortableInstallationTransaction(game.GetInstallPath(Apps)).OperationsPath, "launch.json")));
        }
        finally { lease?.Dispose(); }
    }

    [Fact]
    public async Task Changed_generation_is_rejected_by_production_launch_before_any_spawn()
    {
        var game = Game(); await Install(game); var old = new PortableInstallationTransaction(game.GetInstallPath(Apps)).ReadCommitted()!.OperationId;
        await Install(game, version: "v2"); var called = false; game.GameProcessStarted += _ => called = true;
        Assert.False(await GameLaunchService.LaunchAsync(game, Apps, old)); Assert.False(called);
        Assert.Equal("v2", new PortableInstallationTransaction(game.GetInstallPath(Apps)).ReadCommitted()!.SelectedVersion);
    }
    [Theory] [InlineData("dotnet")] [InlineData("payload.AppImage")] [InlineData("missing")] [InlineData("mount")] [InlineData("temp")] [InlineData("library")] [InlineData("symlink")]
    public async Task Unstable_runtime_mount_payload_and_missing_launcher_refuse_without_fallback(string kind)
    {
        var game = Game(); await Install(game);
        string? candidate = Launcher;
        if (kind == "missing") candidate = null;
        if (kind is "dotnet" or "payload.AppImage") { candidate = Path.Combine(root, kind); File.WriteAllText(candidate, "NONEXECUTED"); }
        if (kind == "mount") { candidate = Path.Combine(Directory.CreateDirectory(Path.Combine(root, ".mount_fixture")).FullName, "Relicade"); File.WriteAllText(candidate, "NONEXECUTED"); }
        if (kind == "temp") { candidate = Path.Combine(Path.GetTempPath(), "Relicade-" + Guid.NewGuid() + ".AppImage"); File.WriteAllText(candidate, "NONEXECUTED"); }
        if (kind == "library") { candidate = Path.Combine(Apps, "Relicade"); File.WriteAllText(candidate, "NONEXECUTED"); }
        if (kind == "symlink") { candidate = Path.Combine(root, "Relicade"); File.CreateSymbolicLink(candidate, Launcher); }
        try { await Assert.ThrowsAsync<NotSupportedException>(() => GameShortcutLaunch.PrepareAsync(game, Apps, new(), null, default, () => candidate)); }
        finally { if (kind == "temp") File.Delete(candidate!); }
        Assert.False(File.Exists(Path.Combine(game.GetInstallPath(Apps), "selected_executable.txt")));
    }
    [Fact]
    public void Relocated_payload_staging_and_retained_generations_are_not_launcher_destinations()
    {
        var relocated = Directory.CreateDirectory(Path.Combine(root, "relocated")).FullName;
        var payload = Path.Combine(relocated, "Relicade.Desktop"); File.WriteAllText(payload, "NONEXECUTED");
        Assert.Throws<NotSupportedException>(() => ManagedShortcutLaunch.ValidateLauncher(payload, Apps, relocated));
        foreach (var generation in new[] { "staging", "previous" })
        {
            var path = Path.Combine(Directory.CreateDirectory(Path.Combine(root, PortableInstallationTransaction.DirectoryName, "fixture-key", generation)).FullName, "Relicade.Desktop");
            File.WriteAllText(path, "NONEXECUTED");
            Assert.Throws<NotSupportedException>(() => ManagedShortcutLaunch.ValidateLauncher(path, Apps, relocated));
        }
    }
    [Fact]
    public async Task Stable_launcher_and_all_generated_fixture_representations_retain_exact_identity()
    {
        var game = Game("Ω % quote space ' $ `"); game.Repository = "fixture/quote \" Ω % ' $ `"; await Install(game);
        var target = await GameShortcutLaunch.PrepareAsync(game, Apps, new(), null, default, () => Launcher);
        Assert.NotNull(target); Assert.Equal(Launcher, target.FileName); Assert.Equal(Path.GetDirectoryName(Launcher), target.WorkingDirectory);
        Assert.Equal(new[] { ManagedShortcutLaunch.Command, game.InstanceKey }, target.Arguments);
        var linux = ShortcutHelper.BuildLinuxDesktopFile(game.Name!, target, null); Assert.Contains("--launch-instance", linux); Assert.Contains("%%", linux); Assert.Contains("Ω", linux);
        var mac = ShortcutHelper.BuildMacLaunchScript(target); Assert.Contains("--launch-instance", mac); Assert.Contains("Ω %", mac);
        var windows = string.Join(" ", target.Arguments.Select(a => ShortcutHelper.QuoteCommandArgument(a, true)));
        Assert.StartsWith("\"--launch-instance\" ", windows); Assert.Contains("\\\"", windows); Assert.Contains("Ω %", windows);
        var desktop = Directory.CreateDirectory(Path.Combine(root, "synthetic-desktop")).FullName;
        await ShortcutHelper.CreateGameShortcutAsync(game, target, null, desktop);
        Assert.Equal(linux, File.ReadAllText(Directory.GetFiles(desktop).Single()));
        output.WriteLine("Linux fixture:\n" + linux + "\nWindows representation:\n" + windows + "\nmacOS representation:\n" + mac);
    }

    [Fact]
    public async Task Same_name_Steam_and_desktop_instances_remain_distinct_and_preserve_existing_state()
    {
        var one = Game("one"); var two = Game("two"); await Install(one); await Install(two);
        var t1 = (await GameShortcutLaunch.PrepareAsync(one, Apps, new(), null, default, () => Launcher))!;
        var t2 = (await GameShortcutLaunch.PrepareAsync(two, Apps, new(), null, default, () => Launcher))!;
        var path = Path.Combine(root, "synthetic-shortcuts.vdf");
        ShortcutHelper.WriteGameToSteamFile(path, one, t1, "fixture-art", out var updated); Assert.False(updated);
        var initial = File.ReadAllBytes(path);
        var firstId = Entries(path)["0"]["appid"];
        ShortcutHelper.WriteGameToSteamFile(path, two, t2, null, out updated); Assert.False(updated);
        var both = Encoding.UTF8.GetString(File.ReadAllBytes(path)); Assert.Contains(one.InstanceKey, both); Assert.Contains(two.InstanceKey, both);
        Assert.Equal(2, Entries(path).Count); Assert.NotEqual(firstId, Entries(path)["1"]["appid"]);
        ShortcutHelper.WriteGameToSteamFile(path, one, t1, null, out updated); Assert.True(updated);
        Assert.Contains("fixture-art", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Equal(firstId, Entries(path)["0"]["appid"]);
        Assert.Equal(string.Join(" ", t1.Arguments.Select(ShortcutHelper.QuoteCommandArgument)), Entries(path)["0"]["LaunchOptions"]);
        Assert.Contains("through Relicade", ShortcutHelper.SteamShortcutMessage(t1, true)); Assert.DoesNotContain("directly", ShortcutHelper.SteamShortcutMessage(t1, true));
        var worker = ShortcutHelper.CreateSteamShortcutWorkerStartInfo(two, Launcher); Assert.Contains("--app-instance", worker.ArgumentList); Assert.Contains(two.InstanceKey, worker.ArgumentList); Assert.DoesNotContain(two.IdentityKey, worker.ArgumentList);
        var desktop = Directory.CreateDirectory(Path.Combine(root, "synthetic-desktop")).FullName;
        await ShortcutHelper.CreateGameShortcutAsync(one, t1, null, desktop); await ShortcutHelper.CreateGameShortcutAsync(two, t2, null, desktop); Assert.Equal(2, Directory.GetFiles(desktop).Length);
        output.WriteLine("Steam fixture bytes=" + Convert.ToHexString(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Legacy_Steam_conversion_retains_ID_art_and_controller_fields()
    {
        var game = Game(); await Install(game); var target = (await GameShortcutLaunch.PrepareAsync(game, Apps, new(), null, default, () => Launcher))!;
        var path = Path.Combine(root, "synthetic-legacy.vdf");
        ShortcutHelper.WriteGameToSteamFile(path, game, new("/old/Relicade", ["--run", game.Name!], "/old"), "legacy-art", out _);
        // These existing Steam preferences are neither recreated nor discarded.
        var data = Entries(path); var oldId = data["0"]["appid"];
        data["0"]["AllowDesktopConfig"] = 0; data["0"]["DevkitOverrideAppID"] = 123; data["0"]["custom-controller"] = "fixture-layout";
        WriteVdf(path, new() { ["shortcuts"] = data.ToDictionary(p => p.Key, p => (object)p.Value) });
        ShortcutHelper.WriteGameToSteamFile(path, game, target, null, out var updated); Assert.True(updated);
        var entry = Entries(path)["0"];
        Assert.Equal(oldId, entry["appid"]); Assert.Equal("legacy-art", entry["icon"]); Assert.Equal(0, entry["AllowDesktopConfig"]);
        Assert.Equal(123, entry["DevkitOverrideAppID"]); Assert.Equal("fixture-layout", entry["custom-controller"]);
    }

    [Fact]
    public async Task Library_changed_after_load_and_readonly_settings_fail_closed()
    {
        var game = Game(); await Install(game); await WriteLibrary(game);
        using var manager = Manager(out _, out var network); await manager.LoadGamesForManagedShortcutAsync();
        await WriteLibrary(Game("changed")); Assert.Throws<IOException>(manager.VerifyManagedShortcutLibrary); Assert.Equal(0, network.Attempts);
        using var blocked = new GameManager(new ReadOnlyStore(Apps), new HttpClient(new Reject()), new AppCatalogService(dataDirectory: Path.Combine(root, "library")));
        Assert.NotEqual(0, await new CLIHandler(blocked, Never).Execute([ManagedShortcutLaunch.Command, game.InstanceKey]));
    }

    static Dictionary<string, Dictionary<string, object>> Entries(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        string Text() { var bytes = new List<byte>(); byte b; while ((b = reader.ReadByte()) != 0) bytes.Add(b); return Encoding.UTF8.GetString(bytes.ToArray()); }
        Dictionary<string, object> Read()
        {
            var d = new Dictionary<string, object>(); byte type;
            while ((type = reader.ReadByte()) != 8) { var key = Text(); d.Add(key, type switch { 0 => Read(), 1 => Text(), 2 => reader.ReadInt32(), _ => throw new InvalidDataException() }); }
            return d;
        }
        return ((Dictionary<string, object>)Read()["shortcuts"]).ToDictionary(p => p.Key, p => (Dictionary<string, object>)p.Value);
    }
    static void WriteVdf(string path, Dictionary<string, object> value)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        void Text(string text) { writer.Write(Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
        void Write(Dictionary<string, object> d)
        {
            foreach (var p in d) { writer.Write((byte)(p.Value is string ? 1 : p.Value is int ? 2 : 0)); Text(p.Key); if (p.Value is string s) Text(s); else if (p.Value is int i) writer.Write(i); else Write((Dictionary<string, object>)p.Value); }
            writer.Write((byte)8);
        }
        Write(value);
    }
    sealed class ReadOnlyStore(string apps) : ISettingsStore, ISettingsRecoveryState
    {
        public AppSettings Current { get; } = new() { AppsPath = apps }; public bool IsReadOnly => true; public Exception? ReadError => new IOException("fixture read-only recovery");
        public AppSettings Load() => Current; public void Save(AppSettings settings) => throw new IOException("forbidden");
    }
    sealed class Reject : HttpMessageHandler
    {
        internal int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Interlocked.Increment(ref Attempts); throw new InvalidOperationException("Application network forbidden"); }
    }
    sealed class Store(string apps) : ISettingsStore
    {
        public int Saves; public AppSettings Current { get; } = new() { AppsPath = apps, ListScope = AppListScope.InstalledOnly };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { Saves++; throw new IOException("Persistence forbidden"); }
    }
}
