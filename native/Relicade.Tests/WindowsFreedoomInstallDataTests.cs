using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

// Every archive, executable-shaped file and data file here is synthetic. None is executed.
public sealed class WindowsFreedoomInstallDataTests
{
    [AvaloniaFact]
    public async Task ZIP_extraction_data_completion_and_close_never_invoke_the_separate_Play_action()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var processEvents = 0; f.Game.GameProcessStarted += _ => processEvents++;
        Assert.True(await f.Install(session.Token), f.Dialogs.Error);
        Assert.Null(f.Dialogs.Error); Assert.Equal(0, f.Dialogs.ChooserCalls);
        Assert.Equal(f.Engine, File.ReadAllBytes(Path.Combine(f.Live, "uzdoom.exe")));
        Assert.True(File.Exists(Path.Combine(f.Live, "updater.exe")));
        var manifest = new PortableInstallationTransaction(f.Live).ReadCommitted()!;
        Assert.Equal("uzdoom.exe", manifest.SelectedExecutable);
        Assert.Equal(f.Definition.AssetIdentity, manifest.AssetIdentity);
        Assert.Equal(f.Definition.ExecutableSha256, manifest.Files.Single(row => row.Path == "uzdoom.exe").Sha256);
        var setup = f.Setup(); var playCalls = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        OwnedGameSetupFlow.ConfigureFreedoomPlay(model, false, _ => Task.CompletedTask,
            _ => { playCalls++; throw new InvalidOperationException("This test never selects Play."); }, null,
            GameLaunchService.FreedoomPlayUnavailableReason);
        var dialog = new OwnedGameSetupDialog(model);
        // The dialog installs its native picker; inject after composition so the
        // synthetic selection reaches the real preview/copy path on Windows.
        model.PickFile = () => Task.FromResult<string?>(f.Source);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Confirm, model.State);
            Assert.False(File.Exists(model.Plan!.Destination)); Assert.Equal(0, playCalls);
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Completed, model.State);
            Assert.Equal(f.Data, File.ReadAllBytes(model.Plan.Destination));
            Assert.True(dialog.PlayButton.IsVisible); Assert.False(dialog.PlayButton.IsEnabled);
            Assert.Equal("Play unavailable", dialog.PlayButton.Content);
            Assert.Contains("Windows guided Play is being prepared", model.Message);
            Assert.False(dialog.PlayButton.IsDefault);
            setup.ValidateReady();
            Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
            Assert.Equal(0, playCalls); Assert.Equal(0, processEvents);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
        Assert.Equal(0, playCalls); Assert.Equal(0, processEvents);
        Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt")));
        var refusal = await Assert.ThrowsAsync<NotSupportedException>(() => GameLaunchService.LaunchFreedoomAsync(f.Game, f.Manager.GamesFolder));
        Assert.Contains("Windows guided Play is being prepared", refusal.Message);
        Assert.Equal(0, processEvents); // Exercise the production entrypoint too, never a real process.
    }

    [Fact]
    public async Task Cancellation_during_the_synthetic_ZIP_download_never_activates_or_launches()
    {
        using var f = new Fixture(); await f.Initialize(); using var cancel = new CancellationTokenSource();
        var launched = 0; f.Game.GameProcessStarted += _ => launched++;
        f.OnDownload = _ => { cancel.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Install(cancel.Token));
        Assert.False(File.Exists(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(0, launched); Assert.Equal(0, f.Dialogs.ChooserCalls);
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
    }

    [Fact]
    public async Task A_changed_staged_executable_is_refused_before_activation_or_helper_selection()
    {
        using var f = new Fixture(); await f.Initialize();
        var expected = f.Definition;
        f.Engine[100] ^= 1; f.Archive = Fixture.Zip(f.Engine);
        var archiveHash = Fixture.Hash(f.Archive);
        f.Definition = expected with { AssetIdentity = Fixture.Asset + "|sha256:" + archiveHash };
        f.SetRelease(archiveHash); // Archive is valid; its selected executable is deliberately wrong.
        Assert.False(await f.Install(CancellationToken.None));
        Assert.Contains("staged setup executable", f.Dialogs.Error);
        Assert.False(File.Exists(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(0, f.Dialogs.ChooserCalls); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
    }

    [Fact]
    public async Task Changing_the_committed_executable_refuses_data_setup_without_copying_input()
    {
        using var f = new Fixture(); await f.Initialize(); Assert.True(await f.Install(CancellationToken.None), f.Dialogs.Error);
        File.AppendAllText(Path.Combine(f.Live, "uzdoom.exe"), "changed synthetic bytes");
        var setup = f.Setup();
        Assert.ThrowsAny<IOException>(() => setup.Preview(f.Source, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
    }

    [Fact]
    public async Task The_Linux_adapter_cannot_import_into_a_Windows_generation()
    {
        using var f = new Fixture(); await f.Initialize(); Assert.True(await f.Install(CancellationToken.None), f.Dialogs.Error);
        var setup = new OwnedGameSetupSession(f.Manager, f.Game.InstanceKey,
            f.Definition with { Platform = OwnedDataPlatform.LinuxX64 });
        var error = Assert.ThrowsAny<IOException>(() => setup.Preview(f.Source, CancellationToken.None));
        Assert.Contains("matching x64 host", error.Message);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
    }

    [Fact]
    public async Task Case_equivalent_saved_root_remains_bound_but_another_root_invalidates_preview()
    {
        using var f = new Fixture(); await f.Initialize(); Assert.True(await f.Install(CancellationToken.None), f.Dialogs.Error);
        var setup = f.Setup();
        f.Store.Current.AppsPath = f.Store.Current.AppsPath.ToUpperInvariant(); f.Store.Save(f.Store.Current);
        var plan = setup.Preview(f.Source, CancellationToken.None);
        Assert.False(File.Exists(plan.Destination));
        f.Store.Current.AppsPath = Path.Combine(f.Root, "DifferentApps"); f.Store.Save(f.Store.Current);
        Assert.ThrowsAny<IOException>(() => setup.Preview(f.Source, CancellationToken.None));
        Assert.False(File.Exists(plan.Destination)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
    }

    internal sealed class Fixture : IDisposable
    {
        internal const string Asset = "Windows-UZDoom-Release-x86_64.zip";
        readonly CredentialVaultTestScope vault = new();
        readonly string? oldRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore oldStore = SettingsStoreProvider.Default;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "freedoom-windows-synthetic-" + Guid.NewGuid().ToString("N"));
        internal string Live => Path.Combine(Root, "Apps", "Engine");
        internal string Source => Path.Combine(Root, "synthetic-source.wad");
        internal byte[] Engine { get; } = PortableInstallationFixtures.WindowsPayload(new string('X', 4032));
        internal byte[] Data { get; } = "SYNTHETIC DATA, NOT A GAME"u8.ToArray();
        internal byte[] Archive = [];
        internal OwnedDataRequirement Definition = null!;
        internal GitHubRelease Release = null!;
        internal readonly GameInfo Game = new() { Name = "Synthetic Windows UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", FolderName = "Engine", IsCustom = true };
        internal readonly Dialogs Dialogs = new();
        internal GameManager Manager = null!;
        internal FileSettingsStore Store = null!;
        HttpClient? client;
        internal int DownloadCalls, MetadataCalls;
        internal Func<CancellationToken, Task>? OnDownload;
        internal async Task Initialize()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
                "Exercises Windows ZIP selection, data-file persistence and native lease semantics; skipped hosts are not acceptance.");
            Directory.CreateDirectory(Root); RelicadePaths.OverrideUserDataRoot = Root;
            File.WriteAllBytes(Source, Data);
            Store = new FileSettingsStore(Path.Combine(Root, "settings.json"));
            Store.Current.AppsPath = Path.Combine(Root, "Apps"); Store.Current.Platform = TargetOS.Windows;
            Store.Current.AutoUpdateNewlyAddedApps = false; Store.Current.BackgroundUpdateCheckEnabled = false;
            Store.Save(Store.Current); SettingsStoreProvider.Default = Store;
            GitHubApiCache.Initialize(Path.Combine(Root, "cache"));
            client = new HttpClient(new Handler(this));
            Manager = new GameManager(Store, client, new AppCatalogService(dataDirectory: Root));
            Manager.Games = new ObservableCollection<GameInfo> { Game };
            await Manager.CatalogService.SaveLocalAppsAsync([Game]);
            Archive = Zip(Engine); var archiveHash = Hash(Archive);
            Definition = OwnedGameSetupService.FreedoomPhase2Windows with
            {
                AssetIdentity = Asset + "|sha256:" + archiveHash, ExecutableSha256 = Hash(Engine), ExecutableLength = Engine.Length,
                Length = Data.Length, Md5 = Convert.ToHexStringLower(MD5.HashData(Data)), ExpectedSha256 = Hash(Data)
            };
            SetRelease(archiveHash);
        }
        internal static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
        internal static byte[] Zip(byte[] engine)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                // Helper deliberately comes first; no generic executable chooser may substitute it.
                using (var helper = zip.CreateEntry("updater.exe").Open()) helper.Write(PortableInstallationFixtures.WindowsPayload("SYNTHETIC HELPER, NEVER EXECUTED"));
                using var selected = zip.CreateEntry("uzdoom.exe").Open(); selected.Write(engine);
            }
            return output.ToArray();
        }
        internal void SetRelease(string hash) => Release = new() { tag_name = "5.0.3", assets = [new()
        { name = Asset, browser_download_url = "https://github.com/UZDoom/UZDoom/releases/download/5.0.3/" + Asset, digest = "sha256:" + hash }] };
        internal OwnedGameSetupSession Setup() => new(Manager, Game.InstanceKey, Definition);
        internal Task<bool> Install(CancellationToken token) => GuidedEngineInstallService.InstallAsync(Game, Manager, Store.Current, token,
            (message, _) => { Assert.Contains("Windows x64", message); Assert.Contains("Nothing is played automatically", message); return Task.FromResult(true); }, Dialogs, Definition);
        public void Dispose()
        {
            Manager?.Dispose(); client?.Dispose(); SettingsStoreProvider.Default = oldStore;
            RelicadePaths.OverrideUserDataRoot = oldRoot; vault.Dispose();
            // Keep the clearly synthetic state/receipts for review in the isolated test root.
        }
        sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); var uri = request.RequestUri!;
                if (uri.Host == "api.quiverlauncher.com") throw new HttpRequestException("Synthetic offline catalog metadata.");
                if (uri.Host == "api.github.com")
                {
                    Assert.StartsWith("/repos/UZDoom/UZDoom/releases", uri.AbsolutePath); fixture.MetadataCalls++;
                    var body = uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal) ? JsonSerializer.Serialize(fixture.Release) : JsonSerializer.Serialize(new[] { fixture.Release });
                    return new(HttpStatusCode.OK) { Content = new StringContent(body) };
                }
                Assert.Equal("github.com", uri.Host); Assert.EndsWith("/" + Asset, uri.AbsolutePath);
                fixture.DownloadCalls++; if (fixture.OnDownload != null) await fixture.OnDownload(token);
                token.ThrowIfCancellationRequested(); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Archive) };
            }
        }
    }

    internal sealed class Dialogs : IGameDownloadDialogs
    {
        internal int ChooserCalls;
        internal string? Error;
        public Task<string?> ChoosePortableExecutableAsync(string name, IReadOnlyList<string> candidates, CancellationToken token)
        { ChooserCalls++; throw new InvalidOperationException("Pinned setup must never ask a chooser to select a helper."); }
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => throw new InvalidOperationException("Unexpected runner prompt.");
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string path, LinuxWindowsRunnerConfig? old = null, bool isInstall = true) => throw new InvalidOperationException("Unexpected runner configuration.");
        public Task ShowRateLimitExceededAsync() => throw new InvalidOperationException("Unexpected rate limit.");
        public Task ShowGitLabRateLimitExceededAsync() => throw new InvalidOperationException("Unexpected provider.");
        public Task ShowErrorAsync(string message, string title) { Error = title + ": " + message; return Task.CompletedTask; }
        public Task<bool> ConfirmUnverifiedReleaseAsync(string name, string version, ReleaseCheck check) => Task.FromResult(true);
    }
}
