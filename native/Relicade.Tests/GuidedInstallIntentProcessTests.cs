using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class GuidedInstallIntentProcessTests(ITestOutputHelper output)
{
    const string WorkerMethod = "Relicade.Tests.GuidedInstallIntentProcessTests.Own_source_worker_observes_or_retries_only_its_explicit_fixture";
    const string Repository = "perfect-dark-pc-port/perfect_dark", Asset = "pd-x86_64-linux.tar.gz";
    const string Prefix = "RELICADE_INTENT_READY:";
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(45);

    [AvaloniaTheory]
    [InlineData("Resolving")][InlineData("Downloading")]
    public async Task Killed_owned_worker_reopens_read_only_in_a_new_process_before_explicit_retry(string stage)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "guided-intent-process-" + nonce);
        Assert.False(Directory.Exists(root)); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "fixture-owner"), nonce);
        output.WriteLine("Retained synthetic process evidence: " + root);
        await Seed(root);
        var sentinels = SentinelHashes(root);
        var first = Start(root, nonce, stage);
        var firstPid = first.Process.Id;
        string recordPath; byte[] original;
        try
        {
            var ready = await first.Ready.Task.WaitAsync(Deadline);
            Assert.Equal(firstPid, ready.Pid); Assert.Equal(stage, ready.Stage); Assert.Equal(nonce, ready.Nonce);
            recordPath = Assert.Single(Directory.GetFiles(Path.Combine(root, "data", "DownloadIntents"), "*.json"));
            original = File.ReadAllBytes(recordPath);
            var record = JsonSerializer.Deserialize<GuidedInstallIntent.Record>(original)!;
            Assert.Equal(ready.Attempt, record.AttemptId); Assert.Equal(stage, record.Stage);
            Assert.False(File.Exists(Path.Combine(root, "data", "Apps", "Engine", PortableInstallationTransaction.ManifestFileName)));
            Assert.False(first.Process.HasExited);
            first.KillOwned(); // Only the exact worker this fixture started.
            await first.Process.WaitForExitAsync().WaitAsync(Deadline);
            Assert.NotEqual(0, first.Process.ExitCode);
        }
        finally { await first.StopAndDrain(); }
        File.WriteAllText(Path.Combine(root, "coord", "killed-receipt.json"), JsonSerializer.Serialize(new { pid = firstPid, exit = first.ExitCode, stage }));
        Assert.Equal(original, File.ReadAllBytes(recordPath)); Assert.Equal(sentinels, SentinelHashes(root));
        var beforeInspect = Snapshot(Path.Combine(root, "data"));
        var inspector = Start(root, nonce, "inspect");
        try { await inspector.Process.WaitForExitAsync().WaitAsync(Deadline); Assert.Equal(0, inspector.Process.ExitCode); }
        finally { await inspector.StopAndDrain(); }
        Assert.NotEqual(firstPid, inspector.Pid);
        Assert.Equal(beforeInspect, Snapshot(Path.Combine(root, "data")));
        var inspected = ReadResult(root, "inspect");
        Assert.Equal(stage, inspected.GetProperty("stage").GetString());
        Assert.Equal(0, inspected.GetProperty("requests").GetInt32());
        Assert.False(inspected.GetProperty("installed").GetBoolean()); Assert.False(inspected.GetProperty("ready").GetBoolean());
        var retry = Start(root, nonce, "retry");
        try { await retry.Process.WaitForExitAsync().WaitAsync(Deadline); Assert.Equal(0, retry.Process.ExitCode); }
        finally { await retry.StopAndDrain(); }
        Assert.NotEqual(firstPid, retry.Pid); Assert.NotEqual(inspector.Pid, retry.Pid);
        var completed = ReadResult(root, "retry");
        Assert.Equal("Completed", completed.GetProperty("stage").GetString());
        Assert.True(completed.GetProperty("installed").GetBoolean()); Assert.False(completed.GetProperty("ready").GetBoolean());
        Assert.Equal(1, completed.GetProperty("payloadRequests").GetInt32());
        Assert.Equal(original, File.ReadAllBytes(recordPath)); Assert.Equal(sentinels, SentinelHashes(root));
        var records = Directory.GetFiles(Path.Combine(root, "data", "DownloadIntents"), "*.json"); Assert.Equal(2, records.Length);
        var next = JsonSerializer.Deserialize<GuidedInstallIntent.Record>(File.ReadAllBytes(Assert.Single(records, p => p != recordPath)))!;
        Assert.Equal(JsonSerializer.Deserialize<GuidedInstallIntent.Record>(original)!.AttemptId, next.PreviousAttemptId);
        Assert.Equal("Completed", next.Stage);
        var live = Path.Combine(root, "data", "Apps", "Engine");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(live, PortableInstallationTransaction.ManifestFileName)));
        var operation = manifest.RootElement.GetProperty("operationId").GetString()!;
        Assert.Equal(completed.GetProperty("operation").GetString(), operation);
        using var journal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(new PortableInstallationTransaction(live).OperationsPath, operation, "journal.json")));
        Assert.Equal("Committed", journal.RootElement.GetProperty("phase").GetString());
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "inputs", "engine.bin")), File.ReadAllBytes(Path.Combine(live, "pd.x86_64")));
        output.WriteLine($"Owned worker {firstPid} killed at durable {stage}; distinct inspector {inspector.Pid} made zero requests/data writes; explicit retry {retry.Pid} committed synthetic bytes. No acquired runtime executed.");
        // Retain receipts and authored inputs, including the terminated attempt, for inspection.
    }

    [AvaloniaFact]
    public async Task Own_source_worker_observes_or_retries_only_its_explicit_fixture()
    {
        var root = Environment.GetEnvironmentVariable("RELICADE_INTENT_ROOT");
        if (root == null)
        {
            // A normal suite invocation is a real local fixture control, not a silent skip.
            var own = Path.Combine(Path.GetTempPath(), "guided-intent-control-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(own); await Seed(own);
            using var context = new Context(own, "inspect");
            var prepared = GuidedEngineInstallService.PrepareFirstInstall(context.Game, context.Manager, CancellationToken.None, context.Definition);
            var identity = GuidedInstallIntent.For(context.Game, context.Manager, prepared);
            using var attempt = GuidedInstallIntent.Begin(identity); Assert.Equal("Resolving", GuidedInstallIntent.Inspect(identity)!.Stage);
            Assert.Equal(0, context.Handler.Requests); return;
        }
        var nonce = Environment.GetEnvironmentVariable("RELICADE_INTENT_NONCE")!;
        Assert.True(Guid.TryParseExact(nonce, "N", out _));
        Assert.Equal("guided-intent-process-" + nonce, Path.GetFileName(root));
        PortableInstallationTransaction.CheckLinks(root);
        Assert.Equal(nonce, File.ReadAllText(Path.Combine(root, "fixture-owner")));
        var mode = Environment.GetEnvironmentVariable("RELICADE_INTENT_MODE")!;
        Assert.Contains(mode, new[] { "Resolving", "Downloading", "inspect", "retry" });
        using var f = new Context(root, mode);
        await using var session = new LauncherSession();
        f.Handler.AtBoundary = async stage =>
        {
            var path = Assert.Single(Directory.GetFiles(Path.Combine(root, "data", "DownloadIntents"), "*.json"));
            var record = JsonSerializer.Deserialize<GuidedInstallIntent.Record>(File.ReadAllBytes(path))!;
            Assert.Equal(stage, record.Stage);
            Console.WriteLine(Prefix + JsonSerializer.Serialize(new Ready(Environment.ProcessId, nonce, stage, record.AttemptId)));
            await Console.Out.FlushAsync();
            await Console.In.ReadLineAsync(); // Parent owns termination; no polling, timer or external process.
            throw new IOException("The parent released a crash barrier instead of terminating; no success is inferred.");
        };
        if (mode is "Resolving" or "Downloading")
        {
            await session.RunAsync(() => GuidedEngineInstallService.InstallAsync(f.Game, f.Manager, f.Store.Current,
                session.Token, (_, _) => Task.FromResult(true), f.Dialogs, f.Definition));
            throw new InvalidOperationException("The requested durable boundary was not reached.");
        }
        var before = Snapshot(Path.Combine(root, "data"));
        var preparedRetry = GuidedEngineInstallService.PrepareFirstInstall(f.Game, f.Manager, session.Token, f.Definition);
        var identityRetry = GuidedInstallIntent.For(f.Game, f.Manager, preparedRetry);
        var model = OwnedGameSetupFlow.CreateEngineProgress(f.Game, f.Manager, f.Store.Current, session, f.Dialogs, f.Definition);
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            window.Show(); Assert.True(model.HasPreviousAttempt); Assert.Equal("Retry engine install", window.InstallButton.Content);
            Assert.True(model.CanStart); Assert.True(window.CancelButton.IsDefault);
            Assert.Equal(0, f.Handler.Requests); Assert.True(model.Work.IsCompleted);
            if (mode == "retry")
            { window.InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await model.Work.WaitAsync(Deadline); Assert.True(model.Succeeded); }
            else Assert.Equal(before, Snapshot(Path.Combine(root, "data")));
            var record = GuidedInstallIntent.Inspect(identityRetry)!;
            string? operation = null;
            if (mode == "retry")
            {
                using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
                operation = manifest.RootElement.GetProperty("operationId").GetString();
            }
            File.WriteAllText(Path.Combine(root, "coord", mode + "-result.json"), JsonSerializer.Serialize(new
            { pid = Environment.ProcessId, stage = record.Stage, requests = f.Handler.Requests, payloadRequests = f.Handler.PayloadRequests,
                installed = f.Game.IsInstalled, ready = f.Game.OwnedDataReady, operation }));
        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    sealed record Ready(int Pid, string Nonce, string Stage, string Attempt);
    sealed class Worker(Process process, string logPath, bool expectsHandshake)
    {
        internal Process Process { get; } = process;
        internal int Pid { get; } = process.Id;
        internal int ExitCode { get; private set; }
        bool killAttempted;
        internal TaskCompletionSource<Ready> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly List<string> lines = [];
        Task output = Task.CompletedTask, errors = Task.CompletedTask;
        string errorText = "";
        internal void ReadPipes()
        {
            output = ReadOutput(); errors = ReadErrors();
        }
        async Task ReadOutput()
        {
            try
            {
                while (await Process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (lines.Count >= 1024 || line.Length > 16384) throw new IOException("Unexpected worker output budget.");
                    lines.Add(line);
                    if (line.StartsWith(Prefix, StringComparison.Ordinal)) Ready.TrySetResult(JsonSerializer.Deserialize<Ready>(line[Prefix.Length..])!);
                }
                if (expectsHandshake) Ready.TrySetException(new IOException("Worker exited before its durable-stage handshake."));
            }
            catch (Exception ex) { if (expectsHandshake) Ready.TrySetException(ex); throw; }
        }
        async Task ReadErrors() { errorText = await Process.StandardError.ReadToEndAsync(); }
        internal void KillOwned()
        {
            if (killAttempted) throw new InvalidOperationException("A worker termination was already attempted; no blind retry.");
            killAttempted = true; Process.Kill(entireProcessTree: false);
        }
        internal async Task StopAndDrain()
        {
            // A timeout or failed assertion still drains the exact owned process before returning.
            // Never kill unrelated processes or delete a root while a worker could still write.
            try
            {
                if (!Process.HasExited && !killAttempted) KillOwned();
                await Process.WaitForExitAsync().WaitAsync(Deadline); ExitCode = Process.ExitCode;
                await Task.WhenAll(output, errors).WaitAsync(Deadline);
                File.WriteAllText(logPath, string.Join('\n', lines) + "\nSTDERR\n" + errorText);
            }
            finally { Process.Dispose(); }
        }
    }
    static Worker Start(string root, string nonce, string mode)
    {
        var coord = Path.Combine(root, "coord"); Directory.CreateDirectory(coord);
        var home = Path.Combine(coord, mode + "-home"); Directory.CreateDirectory(home);
        var temp = Path.Combine(home, "tmp"); Directory.CreateDirectory(temp);
        var exe = Environment.ProcessPath ?? throw new IOException("Missing current own-source test host.");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(GuidedInstallIntentProcessTests).Assembly.Location);
        else Assert.Equal(OperatingSystem.IsWindows() ? "Relicade.Tests.exe" : "Relicade.Tests", Path.GetFileName(exe));
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", WorkerMethod, "-xml", Path.Combine(coord, mode + "-worker.xml") }) psi.ArgumentList.Add(arg);
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? throw new IOException("An explicit tested SDK root is required.");
        psi.Environment.Clear();
        foreach (var pair in new Dictionary<string, string>
        {
            ["DOTNET_ROOT"] = dotnetRoot, ["PATH"] = dotnetRoot, ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["AVALONIA_TELEMETRY_OPTOUT"] = "1", ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["HOME"] = home, ["DOTNET_CLI_HOME"] = home, ["XDG_CONFIG_HOME"] = home, ["XDG_DATA_HOME"] = home, ["XDG_CACHE_HOME"] = home,
            ["TMPDIR"] = temp, ["TMP"] = temp, ["TEMP"] = temp, ["LANG"] = "C.UTF-8",
            ["RELICADE_INTENT_ROOT"] = root, ["RELICADE_INTENT_NONCE"] = nonce, ["RELICADE_INTENT_MODE"] = mode
        }) psi.Environment[pair.Key] = pair.Value;
        var worker = new Worker(Process.Start(psi) ?? throw new IOException("Own-source worker did not start."), Path.Combine(coord, mode + "-process.log"), mode is "Resolving" or "Downloading");
        worker.ReadPipes(); return worker;
    }

    static async Task Seed(string root)
    {
        var inputs = Path.Combine(root, "inputs"); Directory.CreateDirectory(inputs); Directory.CreateDirectory(Path.Combine(root, "coord"));
        var engine = PortableInstallationFixtures.NativePayload("synthetic restart engine; never executed");
        File.WriteAllBytes(Path.Combine(inputs, "engine.bin"), engine);
        using (var file = new FileStream(Path.Combine(inputs, "package.tar.gz"), FileMode.CreateNew))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Gnu))
            tar.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, "pd.x86_64") { DataStream = new MemoryStream(engine), Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute });
        using var f = new Context(root, "seed");
        f.Store.Current.AppsPath = Path.Combine(root, "data", "Apps"); f.Store.Current.Platform = TargetOS.LinuxX64;
        f.Store.Save(f.Store.Current);
        await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]);
        File.WriteAllText(Path.Combine(root, "data", "personal-save.sentinel"), "authored retained save, not real user data");
        File.WriteAllText(Path.Combine(inputs, "original.sentinel"), "authored original, not a ROM");
    }
    sealed class Context : IDisposable
    {
        readonly string? previousRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore previousStore = SettingsStoreProvider.Default;
        readonly HttpClient http;
        internal readonly string Root;
        internal readonly FileSettingsStore Store;
        internal readonly Handler Handler;
        internal readonly GameManager Manager;
        internal readonly GameInfo Game = new() { Name = "Synthetic process restart", RepositorySource = "github", Repository = Repository, FolderName = "Engine", IsCustom = true };
        internal readonly OwnedDataRequirement Definition;
        internal readonly FirstInstallPackageTests.Dialogs Dialogs = new();
        internal string Live => Path.Combine(Root, "data", "Apps", "Engine");
        internal Context(string root, string mode)
        {
            Root = root;
            HttpClient? createdHttp = null;
            GameManager? createdManager = null;
            try
            {
                RelicadePaths.OverrideUserDataRoot = Path.Combine(root, "data"); Directory.CreateDirectory(RelicadePaths.UserDataRoot);
                Store = new FileSettingsStore(Path.Combine(RelicadePaths.UserDataRoot, "settings.json"));
                if (mode == "seed") { Store.Current.AppsPath = Path.Combine(root, "data", "Apps"); Store.Current.Platform = TargetOS.LinuxX64; Store.Save(Store.Current); }
                SettingsStoreProvider.Default = Store;
                var archive = File.ReadAllBytes(Path.Combine(root, "inputs", "package.tar.gz"));
                Definition = OwnedGameSetupService.PerfectDark with { AssetIdentity = Asset + "|sha256:" + Hash(archive), ExecutableSha256 = Hash(File.ReadAllBytes(Path.Combine(root, "inputs", "engine.bin"))) };
                Handler = new Handler(archive, mode); http = createdHttp = new HttpClient(Handler);
                Manager = createdManager = new GameManager(Store, http, new AppCatalogService(dataDirectory: RelicadePaths.UserDataRoot));
                Manager.Games = new ObservableCollection<GameInfo> { Game };
            }
            catch
            {
                try { createdManager?.Dispose(); }
                finally
                {
                    createdHttp?.Dispose(); Game.Dispose();
                    SettingsStoreProvider.Default = previousStore;
                    RelicadePaths.OverrideUserDataRoot = previousRoot;
                }
                throw;
            }
        }
        public void Dispose()
        {
            try { Manager.Dispose(); }
            finally
            {
                http.Dispose(); Game.Dispose();
                SettingsStoreProvider.Default = previousStore;
                RelicadePaths.OverrideUserDataRoot = previousRoot;
            }
        }
    }
    sealed class Handler(byte[] archive, string mode) : HttpMessageHandler
    {
        internal int Requests, PayloadRequests;
        internal Func<string, Task>? AtBoundary;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            if (mode == "inspect") throw new InvalidOperationException("The separate inspector must make no request.");
            var uri = request.RequestUri!;
            if (uri.Host == "api.quiverlauncher.com") throw new HttpRequestException("Synthetic offline catalog");
            if (uri.Host == "api.github.com")
            {
                Assert.StartsWith("/repos/" + Repository + "/releases", uri.AbsolutePath);
                if (mode == "Resolving") await AtBoundary!("Resolving");
                var release = new GitHubRelease { tag_name = "ci-dev-build", assets = [new() { name = Asset, browser_download_url = "https://github.com/" + Repository + "/releases/download/ci-dev-build/" + Asset, digest = "sha256:" + Hash(archive) }] };
                return new(HttpStatusCode.OK) { Content = new StringContent(uri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(release) : JsonSerializer.Serialize(new[] { release })) };
            }
            Assert.Equal("https://github.com/" + Repository + "/releases/download/ci-dev-build/" + Asset, uri.AbsoluteUri);
            PayloadRequests++; if (mode == "Downloading") await AtBoundary!("Downloading");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
        }
    }
    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string[] Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => Path.GetRelativePath(root, p) + "|" + File.GetLastWriteTimeUtc(p).Ticks + "|" + (Directory.Exists(p) ? "directory" : Hash(File.ReadAllBytes(p)))).ToArray();
    static string[] SentinelHashes(string root) => new[] { "data/personal-save.sentinel", "inputs/original.sentinel" }.Select(p => Hash(File.ReadAllBytes(Path.Combine(root, p)))).ToArray();
    static JsonElement ReadResult(string root, string mode) { using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "coord", mode + "-result.json"))); return document.RootElement.Clone(); }
}
