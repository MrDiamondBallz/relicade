using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Views;
using Relicade.ViewModels;
using Relicade.Services;
using Fixture = Relicade.Tests.FreedoomEngineHandoffTests.Fixture;

namespace Relicade.Tests;

public sealed class Z2rsFirstInstallTests
{
    [AvaloniaFact]
    public async Task Uninstalled_z2rs_entry_can_prepare_explicit_engine_setup_without_requests()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        f.Game.Repository = "troyedwardsjr/z2rs";
        await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]);
        var progress = OwnedGameSetupFlow.CreateEngineProgress(f.Game, f.Manager, f.Store.Current, session);
        try
        {
            Assert.True(progress.CanStart);
            Assert.False(progress.Succeeded);
            Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
            Assert.False(Directory.Exists(f.Live));
        }
        finally { progress.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Explicit_engine_choice_preserves_separate_rom_confirmation(bool accept)
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var starts = 0; f.Game.GameProcessStarted += _ => starts++;
        var result = await session.RunAsync(() => f.Install(session.Token, (message, title) =>
        { Assert.Equal("Set up z2rs", title); Assert.Contains("No ROM is downloaded", message); return Task.FromResult(accept); }));
        Assert.Equal(accept, result); Assert.Equal(accept ? 1 : 0, f.DownloadCalls); Assert.Equal(0, starts);
        Assert.Equal(accept, File.Exists(Path.Combine(f.Live, "z2rs")));
        if (accept)
        {
            Assert.Equal(f.Engine, File.ReadAllBytes(Path.Combine(f.Live, "z2rs")));
            Assert.Equal(f.Definition.AssetIdentity, new PortableInstallationTransaction(f.Live).ReadCommitted()!.AssetIdentity);
        }
        Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt")));
    }

    [AvaloniaTheory]
    [InlineData("unchanged")][InlineData("kiosk")][InlineData("removed")][InlineData("path")]
    public async Task Current_context_is_checked_after_catalog_consent_before_engine_transfer(string change)
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        f.Game.CatalogEntryId = "synthetic-z2rs"; f.Game.GameManager = f.Manager;
        await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]);
        f.Dialogs.BeforeConsent = () =>
        {
            if (change == "kiosk") { f.Store.Current.KioskMode = true; f.Store.Save(f.Store.Current); }
            if (change == "removed") f.Manager.Games.Remove(f.Game);
            if (change == "path") f.Game.FolderName = "Another";
            return Task.CompletedTask;
        };
        var installed = false;
        var failure = await Record.ExceptionAsync(async () => installed = await session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        if (change == "unchanged") Assert.Null(failure); else Assert.IsAssignableFrom<IOException>(failure);
        Assert.Equal(change == "unchanged", installed); Assert.Equal(change == "unchanged" ? 1 : 0, f.DownloadCalls);
        Assert.Equal(1, f.Dialogs.UnverifiedPrompts);
    }

    [AvaloniaTheory]
    [InlineData("version")][InlineData("files")][InlineData("history")]
    public async Task Existing_installations_and_pending_work_are_not_replaced(string kind)
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var file = kind == "history" ? Path.Combine(new PortableInstallationTransaction(f.Live).OperationsPath, "pending", "journal.json") : Path.Combine(f.Live, "save.dat");
        if (kind == "version") f.Game.InstalledVersion = "future";
        else { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "retain me"); }
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.Equal(0, f.DownloadCalls + f.MetadataCalls);
        if (kind != "version") Assert.Equal("retain me", File.ReadAllText(file));
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Native_progress_confirm_and_cancel_reuse_owned_operation_lifetime(bool cancel)
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnDownload = async token => { entered.TrySetResult(); await release.Task; token.ThrowIfCancellationRequested(); };
        var model = OwnedGameSetupFlow.CreateEngineProgress(f.Game, f.Manager, f.Store.Current, session, f.Dialogs, f.Definition);
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal("Set up z2rs", window.Title); Assert.True(window.CancelButton.IsFocused);
            Assert.Contains("No ROM is downloaded", model.Confirmation);
            window.InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (cancel) { window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(model.Work.IsCompleted); Assert.True(window.IsVisible); }
            release.TrySetResult(); await model.Work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(!cancel, model.Succeeded); Assert.False(session.Token.IsCancellationRequested);
            Assert.Equal(!cancel, File.Exists(Path.Combine(f.Live, "z2rs")));
            Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        }
        finally { release.TrySetResult(); window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Primary_card_and_controller_actions_open_setup_before_any_request(bool controllerInput)
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var anchor = new Button(); var opened = 0;
        var controller = new LibraryLaunchController(f.Manager, new SettingsViewModel(f.Store), session, null!, (_, _) => anchor,
            (_, _) => { }, (_, _) => Task.CompletedTask, _ => { }, () => { }, _ => { }, () => 800);
        controller.ConfigureOwnedGameSetup((game, _) => { Assert.Same(f.Game, game); opened++; return Task.CompletedTask; });
        if (controllerInput) await controller.PerformSelectedGameActionAsync(f.Game);
        else await controller.PerformGamePrimaryActionAsync(f.Game, anchor);
        Assert.Equal(1, opened); Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
        Assert.Equal("Set up game", f.Game.ButtonText); Assert.False(f.Game.IsInstalled);
    }

    [AvaloniaFact]
    public async Task Cancel_after_real_z2rs_commit_retains_engine_and_suppresses_data_handoff()
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new FreedoomEngineProgressViewModel(session, f.Game, async token =>
        {
            var installed = await f.Install(token, (_, _) => Task.FromResult(true));
            committed.TrySetResult(); await release.Task; return installed;
        }, "Set up z2rs", GuidedEngineInstallService.ProjectFor(f.Game, f.Definition).Confirmation);
        try
        {
            var work = model.StartAsync(); await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(f.Game.IsInstalled);
            var receipt = File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
            model.Cancel(); Assert.False(work.IsCompleted); release.TrySetResult(); await work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(model.Succeeded); Assert.Contains("engine remains installed", model.Message);
            Assert.Equal(receipt, File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
            Assert.Equal(f.Engine, File.ReadAllBytes(Path.Combine(f.Live, "z2rs")));
            Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        }
        finally { release.TrySetResult(); await model.Work; model.DisposeAfterDrain(); }
    }

    [Fact]
    public void Archive_opt_in_is_explicit_and_does_not_admit_other_package_types()
    {
        var hash = new string('a', 64);
        var regular = new FirstInstallPackageConstraint("v1", "engine.tar.gz", hash);
        Assert.Throws<IOException>(() => regular.ValidateSelection("v1", "engine.tar.gz"));
        var compiled = regular with { AllowTarGzip = true };
        compiled.ValidateSelection("v1", "engine.tar.gz");
        foreach (var name in new[] { "engine.zip", "engine.msi", "../engine.tar.gz", "engine.tar.gz.exe" })
        {
            var bad = compiled with { FileName = name };
            Assert.Throws<IOException>(() => bad.ValidateSelection("v1", name));
        }
        Assert.Throws<IOException>(() => compiled.ValidateSelection("v2", "engine.tar.gz"));
    }

    [AvaloniaTheory]
    [InlineData("release")][InlineData("asset")][InlineData("url")][InlineData("bytes")][InlineData("bytes-provider-altered")]
    public async Task Changed_package_or_metadata_is_not_replaced_with_an_alternative(string change)
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        if (change == "release") f.Release.tag_name = "v0.7.0";
        if (change == "asset") f.Release.assets[0].name = "another.tar.gz";
        if (change == "url") f.Release.assets[0].browser_download_url = "https://example.invalid/another.tar.gz";
        var corrupt = change.StartsWith("bytes", StringComparison.Ordinal);
        if (corrupt) f.CorruptArchive = true;
        if (change == "bytes-provider-altered") f.AlterPublishedDigest();
        if (corrupt)
        {
            Assert.False(await session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
            Assert.Contains(change == "bytes" ? "Download Not Verified" : "differs from the reviewed", f.Dialogs.Error);
        }
        else await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.Equal(corrupt ? 1 : 0, f.DownloadCalls);
        Assert.False(File.Exists(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
    }

    [AvaloniaFact]
    public async Task Ordinary_download_menu_remains_separate_from_guided_primary_action()
    {
        using var f = new ZFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var anchor = new Button(); var opened = 0;
        var controller = new LibraryLaunchController(f.Manager, new SettingsViewModel(f.Store), session, null!, (_, _) => anchor,
            (_, _) => { }, (_, _) => Task.CompletedTask, _ => { }, () => { }, _ => { }, () => 800);
        controller.ConfigureOwnedGameSetup((_, _) => { opened++; return Task.CompletedTask; });
        await controller.LaunchFromMenuAsync(f.Game, anchor);
        Assert.Equal(0, opened); Assert.True(f.MetadataCalls > 0);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
    }

    sealed class ZFixture : IDisposable
    {
        const string Asset = "z2rs-0.6.0-x86_64-unknown-linux-gnu.tar.gz";
        readonly string? oldRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore oldStore = SettingsStoreProvider.Default;
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "z2rs-first-" + Guid.NewGuid().ToString("N"));
        internal readonly byte[] Engine = PortableInstallationFixtures.NativePayload("synthetic z2rs, never executed");
        byte[] archive = [];
        internal readonly GameInfo Game = new() { Name = "Synthetic z2rs", Repository = "troyedwardsjr/z2rs", RepositorySource = "github", FolderName = "Engine", IsCustom = true };
        internal GameManager Manager = null!; internal FileSettingsStore Store = null!;
        internal OwnedDataRequirement Definition = null!; internal GitHubRelease Release = null!;
        internal bool CorruptArchive;
        internal FirstInstallPackageTests.Dialogs Dialogs = new();
        internal int MetadataCalls, DownloadCalls;
        internal Func<CancellationToken,Task>? OnDownload;
        internal string Live => Path.Combine(Root, "Apps", "Engine");
        internal async Task Initialize()
        {
            Directory.CreateDirectory(Root); RelicadePaths.OverrideUserDataRoot = Root;
            Store = new FileSettingsStore(Path.Combine(Root, "settings.json"));
            Store.Current.AppsPath = Path.Combine(Root, "Apps"); Store.Current.Platform = TargetOS.LinuxX64;
            Store.Save(Store.Current); SettingsStoreProvider.Default = Store;
            GitHubApiCache.Initialize(Path.Combine(Root, "cache"));
            using (var bytes = new MemoryStream())
            {
                using (var zip = new GZipStream(bytes, CompressionLevel.Fastest, true))
                using (var tar = new TarWriter(zip, TarEntryFormat.Gnu))
                    tar.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, "z2rs") { DataStream = new MemoryStream(Engine), Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute });
                archive = bytes.ToArray();
            }
            var hash = Convert.ToHexStringLower(SHA256.HashData(archive));
            Definition = OwnedGameSetupService.Z2rs with { AssetIdentity = Asset + "|sha256:" + hash, ExecutableSha256 = Convert.ToHexStringLower(SHA256.HashData(Engine)) };
            Release = new() { tag_name = Definition.ReleaseIdentity, assets = [new() { name = Asset, browser_download_url = $"https://github.com/troyedwardsjr/z2rs/releases/download/{Definition.ReleaseIdentity}/{Asset}", digest = "sha256:" + hash }] };
            Manager = new GameManager(Store, new HttpClient(new Handler(this)), new AppCatalogService(dataDirectory: Root));
            Manager.Games = new ObservableCollection<GameInfo> { Game }; await Manager.CatalogService.SaveLocalAppsAsync([Game]);
        }
        internal void AlterPublishedDigest() => Release.assets[0].digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(archive.Concat(new byte[] { 1 }).ToArray()));
        internal Task<bool> Install(CancellationToken token, Func<string,string,Task<bool>> confirm) => GuidedEngineInstallService.InstallAsync(Game, Manager, Store.Current, token, confirm, Dialogs, Definition);
        public void Dispose() { Manager?.Dispose(); SettingsStoreProvider.Default = oldStore; RelicadePaths.OverrideUserDataRoot = oldRoot; if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        sealed class Handler(ZFixture f) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (request.RequestUri!.Host == "api.quiverlauncher.com") throw new HttpRequestException("Synthetic offline catalog");
                if (request.RequestUri.Host == "api.github.com")
                {
                    Assert.StartsWith("/repos/troyedwardsjr/z2rs/releases", request.RequestUri.AbsolutePath); f.MetadataCalls++;
                    return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(f.Release) : JsonSerializer.Serialize(new[] { f.Release })) };
                }
                Assert.Equal("github.com", request.RequestUri.Host); Assert.EndsWith("/" + Asset, request.RequestUri.AbsolutePath);
                f.DownloadCalls++; if (f.OnDownload != null) await f.OnDownload(token); token.ThrowIfCancellationRequested();
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(f.CorruptArchive ? f.archive.Concat(new byte[] { 1 }).ToArray() : f.archive) };
            }
        }
    }
}
