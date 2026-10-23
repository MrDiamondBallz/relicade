using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.ViewModels;
using Relicade.Views;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PerfectDarkFirstInstallTests
{
    [Theory]
    [InlineData("perfect-dark-pc-port/perfect_dark")]
    [InlineData("fgsfdsfgs/perfect_dark")]
    public void Required_first_install_selects_the_reviewed_Perfect_Dark_package(string repository)
    {
        using var game = new GameInfo
        {
            Name = "Synthetic Perfect Dark entry", RepositorySource = "github",
            Repository = repository, FolderName = "SyntheticPD", Status = GameStatus.NotInstalled
        };
        Assert.True(GuidedEngineInstallService.OffersRequiredFirstInstall(game));
        var project = GuidedEngineInstallService.ProjectFor(game);
        Assert.Equal("perfect-dark-pc-port/perfect_dark", project.Repository);
        Assert.Equal("Set up Perfect Dark", project.Title);
        Assert.Same(OwnedGameSetupService.PerfectDark, project.Requirement);
        Assert.Equal(22_213_594, project.MaximumBytes);
        Assert.Contains("Perfect Dark US 1.1", project.Confirmation);
        Assert.Contains("No ROM is downloaded", project.Confirmation);
        Assert.Contains("separate", project.Confirmation);
        Assert.False(game.IsInstalled); Assert.False(game.OwnedDataReady);
    }

    [Theory]
    [InlineData("perfect-dark-pc-port/perfect_dark")]
    [InlineData("troyedwardsjr/z2rs")]
    public void Linked_entries_keep_their_ordinary_download_action(string repository)
    {
        using var game = new GameInfo { Repository = repository, RepositorySource = "github",
            InstallPath = "synthetic-linked-folder", Status = GameStatus.NotInstalled };
        Assert.False(GuidedEngineInstallService.OffersRequiredFirstInstall(game));
        Assert.Equal("Download", game.ButtonText);
    }

    [AvaloniaTheory]
    [InlineData("perfect-dark-pc-port/perfect_dark")]
    [InlineData("fgsfdsfgs/perfect_dark")]
    public async Task Actual_setup_flow_opens_engine_confirmation_before_any_request(string repository)
    {
        using var f = new Fixture(repository); await f.Initialize(); await using var session = new LauncherSession();
        using var dialogs = new LauncherDialogLifetime(session.Token, action => Dispatcher.UIThread.Post(action));
        var flow = new OwnedGameSetupFlow(f.Manager, new SettingsViewModel(f.Store), session, dialogs);
        var work = flow.OpenAsync(f.Game, null);
        var windows = (HashSet<Window>)typeof(LauncherDialogLifetime).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialogs)!;
        try
        {
            var progress = Assert.IsType<FreedoomEngineProgressDialog>(Assert.Single(windows));
            Assert.Equal("Set up Perfect Dark", progress.Title);
            Assert.Contains("Perfect Dark US 1.1", progress.Model.Confirmation);
            Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
            progress.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(f.Game.IsInstalled); Assert.False(f.Game.OwnedDataReady);
            Assert.False(Directory.Exists(f.Live));
        }
        finally { foreach (var window in windows.ToArray()) window.Close(); await work.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Primary_actions_offer_setup_instead_of_an_ordinary_download(bool controllerInput)
    {
        using var f = new Fixture("perfect-dark-pc-port/perfect_dark"); await f.Initialize(); await using var session = new LauncherSession();
        var anchor = new Button(); var opened = 0;
        var controller = new LibraryLaunchController(f.Manager, new SettingsViewModel(f.Store), session, null!, (_, _) => anchor,
            (_, _) => { }, (_, _) => Task.CompletedTask, _ => { }, () => { }, _ => { }, () => 800);
        controller.ConfigureOwnedGameSetup((game, _) => { Assert.Same(f.Game, game); opened++; return Task.CompletedTask; });
        if (controllerInput) await controller.PerformSelectedGameActionAsync(f.Game);
        else await controller.PerformGamePrimaryActionAsync(f.Game, anchor);
        Assert.Equal(1, opened); Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
        Assert.Equal("Set up game", f.Game.ButtonText); Assert.False(f.Game.IsInstalled);
    }

    [AvaloniaTheory]
    [InlineData("perfect-dark-pc-port/perfect_dark", false, false)]
    [InlineData("perfect-dark-pc-port/perfect_dark", true, false)]
    [InlineData("perfect-dark-pc-port/perfect_dark", true, true)]
    [InlineData("fgsfdsfgs/perfect_dark", true, true)]
    public async Task Engine_commit_and_owned_file_copy_require_separate_explicit_choices(string repository, bool acceptEngine, bool acceptCopy)
    {
        using var f = new Fixture(repository); await f.Initialize(); await using var session = new LauncherSession();
        var original = Path.Combine(f.Root, "synthetic-owned-file.z64"); File.WriteAllBytes(original, f.OwnedBytes);
        var starts = 0; f.Game.GameProcessStarted += _ => starts++;
        var installed = await session.RunAsync(() => f.Install(session.Token, (message, title) =>
        { Assert.Equal("Set up Perfect Dark", title); Assert.Contains("No ROM is downloaded", message); return Task.FromResult(acceptEngine); }));
        Assert.Equal(acceptEngine, installed); Assert.Equal(acceptEngine ? 1 : 0, f.DownloadCalls);
        Assert.False(f.Game.OwnedDataReady); Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        if (acceptEngine)
        {
            Assert.Equal(f.Engine, File.ReadAllBytes(Path.Combine(f.Live, "pd.x86_64")));
            Assert.Equal(f.Definition.AssetIdentity, new PortableInstallationTransaction(f.Live).ReadCommitted()!.AssetIdentity);
            var setup = new OwnedGameSetupSession(f.Manager, f.Game.InstanceKey, f.Definition);
            var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
            var dialog = new OwnedGameSetupDialog(model);
            try
            {
                model.PickFile = () => Task.FromResult<string?>(original); dialog.Show(); Dispatcher.UIThread.RunJobs();
                Assert.False(model.CanConfirm); Assert.False(dialog.DownloadButton.IsVisible);
                dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(OwnedGameSetupUiState.Confirm, model.State); Assert.NotNull(model.Plan);
                Assert.False(File.Exists(model.Plan.Destination)); Assert.Equal(f.OwnedBytes, File.ReadAllBytes(original));
                if (acceptCopy)
                {
                    dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.Equal(OwnedGameSetupUiState.Completed, model.State); setup.ValidateReady();
                    Assert.Equal(f.OwnedBytes, File.ReadAllBytes(model.Plan.Destination));
                }
                else { dialog.Close(); await dialog.Work; Assert.False(File.Exists(model.Plan.Destination)); }
            }
            finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
        }
        Assert.Equal(f.OwnedBytes, File.ReadAllBytes(original)); Assert.Equal(0, starts);
        Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt"))); Assert.False(f.Game.IsLoading);
    }

    [AvaloniaTheory]
    [InlineData("release")] [InlineData("asset")] [InlineData("url")] [InlineData("rolling-bytes")]
    public async Task Changed_rolling_release_cannot_replace_the_reviewed_package(string change)
    {
        using var f = new Fixture("perfect-dark-pc-port/perfect_dark"); await f.Initialize(); await using var session = new LauncherSession();
        if (change == "release") f.Release.tag_name = "unreviewed";
        if (change == "asset") f.Release.assets[0].name = "different.tar.gz";
        if (change == "url") f.Release.assets[0].browser_download_url = "https://example.invalid/other.tar.gz";
        if (change == "rolling-bytes")
        {
            f.ChangePublishedBytes();
            Assert.False(await session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
            Assert.Contains("differs from the reviewed", f.Dialogs.Error);
        }
        else await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.Equal(change == "rolling-bytes" ? 1 : 0, f.DownloadCalls); Assert.Equal(0, f.Dialogs.Fallbacks);
        Assert.False(f.Game.IsInstalled); Assert.False(f.Game.OwnedDataReady);
        Assert.False(File.Exists(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
    }

    [AvaloniaTheory]
    [InlineData("kiosk")] [InlineData("removed")] [InlineData("path")]
    public async Task Changed_context_after_catalog_consent_refuses_engine_transfer(string change)
    {
        using var f = new Fixture("perfect-dark-pc-port/perfect_dark"); await f.Initialize(); await using var session = new LauncherSession();
        f.Game.CatalogEntryId = "synthetic-pd"; f.Game.GameManager = f.Manager;
        await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]);
        f.Dialogs.BeforeConsent = () =>
        {
            if (change == "kiosk") { f.Store.Current.KioskMode = true; f.Store.Save(f.Store.Current); }
            if (change == "removed") f.Manager.Games.Remove(f.Game);
            if (change == "path") f.Game.FolderName = "Other";
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.Equal(1, f.Dialogs.UnverifiedPrompts); Assert.Equal(0, f.DownloadCalls);
        Assert.False(Directory.Exists(f.Live)); Assert.False(f.Game.OwnedDataReady);
    }

    [AvaloniaFact]
    public async Task Cancellation_during_transfer_drains_before_closing_without_data_handoff()
    {
        using var f = new Fixture("perfect-dark-pc-port/perfect_dark"); await f.Initialize(); await using var session = new LauncherSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = false;
        f.OnDownload = async token => { entered.TrySetResult(); try { await release.Task; token.ThrowIfCancellationRequested(); } finally { ended = true; } };
        var model = OwnedGameSetupFlow.CreateEngineProgress(f.Game, f.Manager, f.Store.Current, session, f.Dialogs, f.Definition);
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            window.Show(); window.InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(model.Work.IsCompleted); Assert.True(window.IsVisible); Assert.False(ended);
            release.TrySetResult(); await model.Work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(ended); Assert.False(model.Succeeded); Assert.False(session.Token.IsCancellationRequested);
            Assert.False(f.Game.IsInstalled); Assert.False(f.Game.OwnedDataReady);
            Assert.False(File.Exists(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
            Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        }
        finally { release.TrySetResult(); window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("personal-file")] [InlineData("pending-history")]
    public async Task Existing_user_files_or_pending_work_are_preserved_before_any_request(string kind)
    {
        using var f = new Fixture("perfect-dark-pc-port/perfect_dark"); await f.Initialize(); await using var session = new LauncherSession();
        var path = kind == "personal-file" ? Path.Combine(f.Live, "save.dat") : Path.Combine(new PortableInstallationTransaction(f.Live).OperationsPath, "pending", "journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "synthetic retained user bytes");
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => f.Install(session.Token, (_, _) => Task.FromResult(true))));
        Assert.Equal(0, f.MetadataCalls + f.DownloadCalls); Assert.Equal("synthetic retained user bytes", File.ReadAllText(path));
    }

    sealed class Fixture(string repository) : IDisposable
    {
        const string Asset = "pd-x86_64-linux.tar.gz";
        readonly string? oldRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore oldStore = SettingsStoreProvider.Default;
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "pd-first-install-" + Guid.NewGuid().ToString("N"));
        internal readonly byte[] Engine = PortableInstallationFixtures.NativePayload("synthetic Perfect Dark engine, never executed");
        internal readonly byte[] OwnedBytes = "synthetic owned data, not a ROM"u8.ToArray();
        byte[] archive = [];
        HttpClient http = null!;
        internal readonly GameInfo Game = new() { Name = "Synthetic Perfect Dark", Repository = repository, RepositorySource = "github", FolderName = "Engine", IsCustom = true };
        internal GameManager Manager = null!; internal FileSettingsStore Store = null!;
        internal OwnedDataRequirement Definition = null!; internal GitHubRelease Release = null!;
        internal FirstInstallPackageTests.Dialogs Dialogs = new();
        internal int MetadataCalls, DownloadCalls;
        internal Func<CancellationToken, Task>? OnDownload;
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
                using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, true))
                using (var tar = new TarWriter(gzip, TarEntryFormat.Gnu))
                    tar.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, "pd.x86_64") { DataStream = new MemoryStream(Engine), Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute });
                archive = bytes.ToArray();
            }
            var hash = Convert.ToHexStringLower(SHA256.HashData(archive));
            Definition = OwnedGameSetupService.PerfectDark with { AssetIdentity = Asset + "|sha256:" + hash,
                ExecutableSha256 = Convert.ToHexStringLower(SHA256.HashData(Engine)), Length = OwnedBytes.Length, Md5 = Convert.ToHexStringLower(MD5.HashData(OwnedBytes)) };
            Release = new() { tag_name = Definition.ReleaseIdentity, assets = [new() { name = Asset, browser_download_url = $"https://github.com/perfect-dark-pc-port/perfect_dark/releases/download/{Definition.ReleaseIdentity}/{Asset}", digest = "sha256:" + hash }] };
            http = new HttpClient(new Handler(this));
            Manager = new GameManager(Store, http, new AppCatalogService(dataDirectory: Root));
            Manager.Games = new ObservableCollection<GameInfo> { Game }; await Manager.CatalogService.SaveLocalAppsAsync([Game]);
        }
        internal void ChangePublishedBytes() { archive = archive.Concat(new byte[] { 1 }).ToArray(); Release.assets[0].digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(archive)); }
        internal Task<bool> Install(CancellationToken token, Func<string, string, Task<bool>> confirm) => GuidedEngineInstallService.InstallAsync(Game, Manager, Store.Current, token, confirm, Dialogs, Definition);
        public void Dispose() { Manager?.Dispose(); http?.Dispose(); Game.Dispose(); SettingsStoreProvider.Default = oldStore; RelicadePaths.OverrideUserDataRoot = oldRoot; if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        sealed class Handler(Fixture f) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (request.RequestUri!.Host == "api.quiverlauncher.com") throw new HttpRequestException("Synthetic offline catalog");
                if (request.RequestUri.Host == "api.github.com")
                {
                    Assert.StartsWith("/repos/perfect-dark-pc-port/perfect_dark/releases", request.RequestUri.AbsolutePath); f.MetadataCalls++;
                    return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/latest") ? JsonSerializer.Serialize(f.Release) : JsonSerializer.Serialize(new[] { f.Release })) };
                }
                Assert.Equal($"https://github.com/perfect-dark-pc-port/perfect_dark/releases/download/ci-dev-build/{Asset}", request.RequestUri.AbsoluteUri);
                f.DownloadCalls++; if (f.OnDownload != null) await f.OnDownload(token); token.ThrowIfCancellationRequested();
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(f.archive) };
            }
        }
    }
}
