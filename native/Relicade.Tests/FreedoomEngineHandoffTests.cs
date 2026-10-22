using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class FreedoomEngineHandoffTests
{
    [Fact]
    public async Task Confirmed_first_setup_installs_exact_synthetic_engine_without_launching_or_importing_data()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var started = 0; f.Game.GameProcessStarted += _ => started++;
        var result = await session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true)));
        Assert.True(result, f.Dialogs.Error); Assert.Null(f.Dialogs.Error);
        Assert.Equal(2, f.MetadataCalls); Assert.Equal(1, f.DownloadCalls); Assert.Equal(0, started);
        Assert.Equal(f.Payload, File.ReadAllBytes(Path.Combine(f.Live, Fixture.Asset)));
        var manifest = new PortableInstallationTransaction(f.Live).ReadCommitted()!;
        Assert.Equal(f.Definition.AssetIdentity, manifest.AssetIdentity);
        Assert.Equal(f.Definition.ReleaseIdentity, manifest.ReleaseIdentity);
        Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt")));
        Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        Assert.Null(f.Game.PreferredVersion); Assert.False(f.Game.IsLoading);
    }
    [Fact]
    public async Task Declining_the_explicit_engine_prompt_makes_no_request_or_install_folder()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        Assert.False(await session.RunAsync(() => f.Install(session.Token, (message, title) =>
        { Assert.Contains("Linux x64", message); Assert.Contains("Nothing is played automatically", message); return Task.FromResult(false); })));
        Assert.Equal(0, f.MetadataCalls + f.DownloadCalls); Assert.False(Directory.Exists(f.Live)); Assert.False(f.Game.IsLoading);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Closing_after_confirmation_or_metadata_await_does_not_download(bool duringMetadata)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transportEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (duringMetadata) f.OnMetadata = async token =>
        {
            entered.TrySetResult();
            try { await proceed.Task; token.ThrowIfCancellationRequested(); }
            finally { transportEnded.TrySetResult(); }
        };
        var work = session.RunAsync(() => f.Install(session.Token, async (_, _) =>
        { if (!duringMetadata) { entered.TrySetResult(); await proceed.Task; } return true; }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = session.DisposeAsync().AsTask();
        try
        {
            // Confirmation belongs to this session. Shared metadata transport has
            // separate ownership: canceling its last waiter signals its token,
            // but does not require a hostile transport to finish before returning.
            if (!duringMetadata) Assert.False(close.IsCompleted);
        }
        finally { proceed.TrySetResult(); }
        Assert.False(await work); await close;
        if (duringMetadata) await transportEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, f.DownloadCalls); Assert.False(Directory.Exists(f.Live)); Assert.False(f.Game.IsLoading);
    }
    [Fact]
    public async Task Closing_during_engine_transfer_drains_owned_request_without_activation()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnDownload = async token => { entered.SetResult(); await proceed.Task; token.ThrowIfCancellationRequested(); };
        var work = session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = session.DisposeAsync().AsTask();
        try { Assert.False(close.IsCompleted); }
        finally { proceed.TrySetResult(); }
        Assert.False(await work); await close;
        Assert.False(File.Exists(Path.Combine(f.Live, Fixture.Asset)));
        Assert.False(File.Exists(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.False(f.Game.IsLoading);
    }
    [Fact]
    public async Task Offline_metadata_can_be_retried_without_changing_version_preferences()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        f.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.False(Directory.Exists(f.Live)); Assert.False(f.Game.IsLoading);
        f.Offline = false;
        Assert.True(await session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))), f.Dialogs.Error);
        Assert.Equal(1, f.DownloadCalls); Assert.Null(f.Game.PreferredVersion);
    }
    [Theory]
    [InlineData("release")][InlineData("asset")][InlineData("url")]
    public async Task Missing_or_changed_reviewed_metadata_does_not_choose_a_substitute(string changed)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        if (changed == "release") f.Release.tag_name = "another-version";
        if (changed == "asset") f.Release.assets[0].name = "another-linux-x64.AppImage";
        if (changed == "url") f.Release.assets[0].browser_download_url = "https://example.invalid/unreviewed.AppImage";
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.Equal(0, f.DownloadCalls); Assert.False(Directory.Exists(f.Live)); Assert.False(f.Game.IsLoading);
    }
    [Fact]
    public async Task Changed_saved_permissions_during_confirmation_refuse_before_any_network()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) =>
        { f.Store.Current.KioskMode = true; f.Store.Save(f.Store.Current); return Task.FromResult(true); })));
        Assert.Equal(0, f.MetadataCalls + f.DownloadCalls); Assert.False(Directory.Exists(f.Live));
    }
    [Fact]
    public async Task Existing_engine_version_is_not_downgraded_even_if_its_card_has_no_live_folder()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        f.Game.InstalledVersion = "future-version"; var confirmed = false;
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) =>
        { confirmed = true; return Task.FromResult(true); })));
        Assert.False(confirmed); Assert.Equal("future-version", f.Game.InstalledVersion);
        Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Linked_catalog_entry_preserves_explicit_unknown_status_consent(bool accept)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        f.Game.GameManager = f.Manager; f.Game.CatalogEntryId = "doom-uzdoom";
        await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]);
        f.Dialogs.AcceptUnverified = accept;
        var installed = await session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true)));
        Assert.Equal(accept, installed); Assert.True(f.CatalogCalls > 0); Assert.Equal(1, f.Dialogs.UnverifiedPrompts);
        Assert.Equal(accept ? 1 : 0, f.DownloadCalls); Assert.Equal(accept, File.Exists(Path.Combine(f.Live, Fixture.Asset)));
    }

    sealed class Fixture : IDisposable
    {
        internal const string Asset = "Linux-UZDoom-Release-x86_64.AppImage";
        readonly string? previousRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore previousStore = SettingsStoreProvider.Default;
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "freedoom-first-" + Guid.NewGuid().ToString("N"));
        internal readonly byte[] Payload = PortableInstallationFixtures.NativePayload("synthetic guided engine, never executed");
        internal readonly GameInfo Game = new() { Name = "Synthetic UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", FolderName = "Engine", IsCustom = true };
        internal GameManager Manager = null!; internal FileSettingsStore Store = null!;
        internal OwnedDataRequirement Definition = null!; internal GitHubRelease Release = null!;
        internal FirstInstallPackageTests.Dialogs Dialogs = new();
        internal int MetadataCalls, DownloadCalls, CatalogCalls; internal bool Offline;
        internal Func<CancellationToken, Task>? OnMetadata;
        internal Func<CancellationToken, Task>? OnDownload;
        internal string Live => Path.Combine(Root, "Apps", "Engine");
        internal async Task Initialize()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Guided engine installation currently targets Linux x64.");
            Directory.CreateDirectory(Root); RelicadePaths.OverrideUserDataRoot = Root;
            Store = new FileSettingsStore(Path.Combine(Root, "settings.json"));
            Store.Current.AppsPath = Path.Combine(Root, "Apps"); Store.Current.Platform = TargetOS.LinuxX64;
            Store.Save(Store.Current); SettingsStoreProvider.Default = Store;
            GitHubApiCache.Initialize(Path.Combine(Root, "cache"));
            Manager = new GameManager(Store, new HttpClient(new Handler(this)), new AppCatalogService(dataDirectory: Root));
            Manager.Games = new ObservableCollection<GameInfo> { Game };
            await Manager.CatalogService.SaveLocalAppsAsync([Game]);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Payload));
            Definition = OwnedGameSetupService.FreedoomPhase2 with { AssetIdentity = Asset + "|sha256:" + hash, ExecutableSha256 = hash };
            Release = new() { tag_name = Definition.ReleaseIdentity, assets = [new() { name = Asset,
                browser_download_url = $"https://github.com/UZDoom/UZDoom/releases/download/{Definition.ReleaseIdentity}/{Asset}", digest = "sha256:" + hash }] };
        }
        internal Task<bool> Install(CancellationToken token, Func<string,string,Task<bool>> confirm)
            => FreedoomEngineInstallService.InstallAsync(Game, Manager, Store.Current, token, confirm, Dialogs, Definition);
        public void Dispose()
        { Manager?.Dispose(); SettingsStoreProvider.Default = previousStore; RelicadePaths.OverrideUserDataRoot = previousRoot; if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        sealed class Handler(Fixture f) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (request.RequestUri!.Host == "api.quiverlauncher.com")
                { f.CatalogCalls++; throw new HttpRequestException("Synthetic offline catalog status."); }
                if (request.RequestUri!.Host == "api.github.com")
                {
                    Assert.StartsWith("/repos/UZDoom/UZDoom/releases", request.RequestUri.AbsolutePath); f.MetadataCalls++;
                    if (f.Offline) throw new HttpRequestException("Synthetic offline metadata.");
                    if (f.OnMetadata != null) await f.OnMetadata(token);
                    token.ThrowIfCancellationRequested();
                    var body = request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(f.Release) : JsonSerializer.Serialize(new[] { f.Release });
                    return new(HttpStatusCode.OK) { Content = new StringContent(body) };
                }
                Assert.Equal("github.com", request.RequestUri.Host); Assert.EndsWith("/" + Asset, request.RequestUri.AbsolutePath);
                f.DownloadCalls++; if (f.OnDownload != null) await f.OnDownload(token); token.ThrowIfCancellationRequested();
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Payload) };
            }
        }
    }
}
