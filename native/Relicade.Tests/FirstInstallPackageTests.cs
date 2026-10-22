using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class FirstInstallPackageTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "first-engine-" + Guid.NewGuid().ToString("N"));
    readonly byte[] payload = PortableInstallationFixtures.NativePayload("synthetic engine, never executed");
    string Live => Path.Combine(root, "Engine");
    string Hash => Convert.ToHexStringLower(SHA256.HashData(payload));
    const string Asset = "engine-linux-x64.AppImage";
    public FirstInstallPackageTests() { Directory.CreateDirectory(root); GitHubApiCache.Initialize(Path.Combine(root, "cache")); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    Task Stage(string stage) { File.WriteAllBytes(Path.Combine(stage, Asset), payload); return Task.CompletedTask; }

    [Fact]
    public async Task First_install_activates_once_and_never_replaces_a_prior_generation()
    {
        var tx = new PortableInstallationTransaction(Live, new() { FirstInstallationOnly = true });
        await tx.InstallAsync(Stage, "first");
        var manifest = File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName));
        await Assert.ThrowsAsync<IOException>(() => tx.InstallAsync(Stage, "replacement"));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal("first", tx.ReadCommitted()!.SelectedVersion);
    }
    [Theory]
    [InlineData("personal-file")][InlineData("manifest")][InlineData("pending-journal")][InlineData("launch-receipt")]
    public async Task Existing_files_and_pending_work_are_preserved_without_recovery(string kind)
    {
        var tx = new PortableInstallationTransaction(Live, new() { FirstInstallationOnly = true });
        var path = kind switch
        {
            "personal-file" => Path.Combine(Live, "save.dat"),
            "manifest" => Path.Combine(Live, PortableInstallationTransaction.ManifestFileName),
            "launch-receipt" => Path.Combine(tx.OperationsPath, "launch.json"),
            _ => Path.Combine(tx.OperationsPath, Guid.NewGuid().ToString("N"), "journal.json")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); byte[] original = [7, 8, 9]; File.WriteAllBytes(path, original);
        var staged = false;
        await Assert.ThrowsAsync<IOException>(() => tx.InstallAsync(_ => { staged = true; return Task.CompletedTask; }, "first"));
        Assert.False(staged); Assert.Equal(original, File.ReadAllBytes(path));
    }
    [Fact]
    public async Task Interrupted_prejournal_first_attempt_can_retry_without_old_generation_recovery()
    {
        var tx = new PortableInstallationTransaction(Live, new() { FirstInstallationOnly = true });
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tx.InstallAsync(async stage =>
        { await Stage(stage); cancel.Cancel(); }, "cancelled", cancel.Token));
        Assert.False(File.Exists(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        await tx.InstallAsync(Stage, "retry");
        Assert.Equal("retry", tx.ReadCommitted()!.SelectedVersion);
    }
    [Fact]
    public async Task Normal_explicit_update_remains_available_without_first_install_constraint()
    {
        var tx = new PortableInstallationTransaction(Live);
        await tx.InstallAsync(Stage, "first"); await tx.InstallAsync(Stage, "second");
        Assert.Equal("second", tx.ReadCommitted()!.SelectedVersion);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_download_caller_enforces_pinned_bytes_in_addition_to_mutable_metadata(bool wrongBytes)
    {
        var bytes = wrongBytes ? PortableInstallationFixtures.NativePayload("different synthetic bytes") : payload;
        using var client = new HttpClient(new Handler(bytes)); var dialogs = new Dialogs();
        var game = new GameInfo { Name = "Synthetic engine", Repository = "fixture/engine", FolderName = "Engine" };
        var release = Release(bytes);
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, release,
            new() { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs,
            releaseMode: ReleaseInstallMode.ExplicitRelease, firstInstall: new("first", Asset, Hash));
        Assert.Equal(!wrongBytes, game.IsInstalled);
        Assert.Equal(!wrongBytes, File.Exists(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        if (wrongBytes) Assert.Contains("differs from the reviewed", dialogs.Error);
        else { Assert.Null(dialogs.Error); Assert.Equal(Hash, game.InstalledBuild!.Sha256); }
        Assert.Equal(0, dialogs.Fallbacks);
    }
    [Fact]
    public async Task Concurrent_install_during_download_cannot_turn_first_setup_into_update()
    {
        using var client = new HttpClient(new Handler(payload, async () =>
            await new PortableInstallationTransaction(Live).InstallAsync(Stage, "other-install")));
        var game = new GameInfo { Repository = "fixture/engine", FolderName = "Engine" }; var dialogs = new Dialogs();
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, Release(payload),
            new() { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs,
            releaseMode: ReleaseInstallMode.ExplicitRelease, firstInstall: new("first", Asset, Hash));
        Assert.NotNull(dialogs.Error);
        Assert.Equal("other-install", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(Live, Asset)));
    }
    [Fact]
    public async Task Context_change_after_download_refuses_before_activation()
    {
        var valid = true;
        using var client = new HttpClient(new Handler(payload, () => { valid = false; return Task.CompletedTask; }));
        var game = new GameInfo { Repository = "fixture/engine", FolderName = "Engine" }; var dialogs = new Dialogs();
        var constraint = new FirstInstallPackageConstraint("first", Asset, Hash)
        { ValidateContext = () => { if (!valid) throw new IOException("Synthetic settings changed."); } };
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, Release(payload),
            new() { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs,
            releaseMode: ReleaseInstallMode.ExplicitRelease, firstInstall: constraint);
        Assert.Contains("settings changed", dialogs.Error); Assert.False(File.Exists(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
    }
    [DllImport("libc", SetLastError = true)] static extern int mkfifo(string path, uint mode);
    [DllImport("libc")] static extern int gettid();
    [Theory]
    [InlineData("manifest", false)][InlineData("journal", false)][InlineData("writer", false)]
    [InlineData("manifest", true)][InlineData("journal", true)][InlineData("writer", true)]
    public async Task First_setup_refuses_fifo_evidence_without_blocking_or_recovery(string kind, bool downloadCaller)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Observes owned Linux FIFO readers.");
        var tx = new PortableInstallationTransaction(Live, new() { FirstInstallationOnly = true });
        var path = kind == "manifest" ? Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)
            : kind == "writer" ? Path.Combine(tx.OperationsPath, "writer.lock")
            : Path.Combine(tx.OperationsPath, Guid.NewGuid().ToString("N"), "journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); Assert.Equal(0, mkfifo(path, 0x180));
        using var client = new HttpClient(new Handler(payload)); var dialogs = new Dialogs();
        var game = new GameInfo { Repository = "fixture/engine", FolderName = "Engine" };
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Task.Run(async () =>
        {
            entered.SetResult(gettid());
            return await Record.ExceptionAsync(() => downloadCaller
                ? GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, Release(payload),
                    new() { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs,
                    releaseMode: ReleaseInstallMode.ExplicitRelease, firstInstall: new("first", Asset, Hash))
                : tx.InstallAsync(Stage, "first"));
        });
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
        }
        finally
        {
            using var release = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            error = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(blocked, "First-time setup must inspect presence/kind before any metadata open.");
        if (downloadCaller) Assert.NotNull(dialogs.Error); else Assert.IsAssignableFrom<IOException>(error);
        Assert.False(File.Exists(Path.Combine(Live, Asset)));
    }

    [Theory]
    [InlineData("managed")][InlineData("held-writer")][InlineData("idle-writer")]
    public async Task Enclosing_installation_authority_is_refused_before_child_history_creation(string state)
    {
        var parent = new PortableInstallationTransaction(Live);
        IDisposable? lease = null;
        if (state == "managed") await parent.InstallAsync(Stage, "parent");
        else { lease = parent.AcquireLease(); if (state == "idle-writer") { lease.Dispose(); lease = null; } }
        var child = new PortableInstallationTransaction(Path.Combine(Live, "saves", "Child"), new() { FirstInstallationOnly = true });
        try
        {
            await Assert.ThrowsAsync<IOException>(() => child.InstallAsync(Stage, "child"));
            Assert.False(Directory.Exists(child.OperationsPath));
            Assert.False(Directory.Exists(Path.Combine(Live, "saves")));
        }
        finally { lease?.Dispose(); }
    }

    [Theory]
    [InlineData("target-file")][InlineData("target-link")][InlineData("history-link")][InlineData("directory-writer")]
    public async Task Conflicting_target_paths_are_not_reinterpreted_as_empty(string kind)
    {
        var tx = new PortableInstallationTransaction(Live, new() { FirstInstallationOnly = true });
        var retained = Path.Combine(root, "retained"); Directory.CreateDirectory(retained);
        File.WriteAllText(Path.Combine(retained, "save.dat"), "keep personal bytes");
        if (kind == "target-file") File.WriteAllText(Live, "keep target");
        if (kind == "target-link") Directory.CreateSymbolicLink(Live, retained);
        if (kind == "history-link") { Directory.CreateDirectory(Path.GetDirectoryName(tx.OperationsPath)!); Directory.CreateSymbolicLink(tx.OperationsPath, retained); }
        if (kind == "directory-writer") Directory.CreateDirectory(Path.Combine(tx.OperationsPath, "writer.lock"));
        if (kind.EndsWith("-link", StringComparison.Ordinal))
            await Assert.ThrowsAsync<InvalidDataException>(() => tx.InstallAsync(Stage, "first"));
        else await Assert.ThrowsAsync<IOException>(() => tx.InstallAsync(Stage, "first"));
        Assert.Equal("keep personal bytes", File.ReadAllText(Path.Combine(retained, "save.dat")));
        Assert.False(File.Exists(Path.Combine(retained, Asset)));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Pinned_size_limit_applies_to_headers_and_unknown_length_streams(bool unknownLength)
    {
        using var client = new HttpClient(new Handler(payload, unknownLength: unknownLength)); var dialogs = new Dialogs();
        var game = new GameInfo { Repository = "fixture/engine", FolderName = "Engine" };
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, Release(payload),
            new() { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs,
            releaseMode: ReleaseInstallMode.ExplicitRelease, firstInstall: new("first", Asset, Hash) { MaximumBytes = payload.Length - 1 });
        Assert.Contains("size limit", dialogs.Error); Assert.False(game.IsInstalled);
        Assert.False(File.Exists(Path.Combine(Live, Asset)));
    }

    GitHubRelease Release(byte[] bytes) => new() { tag_name = "first", assets = [new()
    { name = Asset, browser_download_url = "https://example.invalid/engine.AppImage", digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)) }] };
    sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
    sealed class Handler(byte[] bytes, Func<Task>? beforeResponse = null, bool unknownLength = false) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Assert.Equal("example.invalid", request.RequestUri!.Host); if (beforeResponse != null) await beforeResponse(); token.ThrowIfCancellationRequested(); return new(HttpStatusCode.OK) { Content = unknownLength ? new StreamContent(new NonSeekable(bytes)) : new ByteArrayContent(bytes) }; }
    }
    internal sealed class Dialogs : IGameDownloadDialogs
    {
        internal string? Error; internal int Fallbacks; internal int UnverifiedPrompts; internal bool AcceptUnverified = true;
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(false);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string p, LinuxWindowsRunnerConfig? c = null, bool isInstall = true) => throw new InvalidOperationException("Unexpected runner request");
        public Task ShowRateLimitExceededAsync() => throw new InvalidOperationException("Unexpected rate limit");
        public Task ShowGitLabRateLimitExceededAsync() => throw new InvalidOperationException("Unexpected provider");
        public Task ShowErrorAsync(string message, string title) { Error = title + ": " + message; return Task.CompletedTask; }
        public Task<bool> ConfirmUnverifiedReleaseAsync(string a, string v, ReleaseCheck c) { UnverifiedPrompts++; return Task.FromResult(AcceptUnverified); }
        public Task<bool> OfferOtherReleaseAsync(string a, string p, string v) { Fallbacks++; return Task.FromResult(true); }
    }
}
