using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class CatalogOwnedSetupMappingTests
{
    [AvaloniaTheory]
    [InlineData("github", "troyedwardsjr/z2rs", "z2rs")]
    [InlineData("github", "perfect-dark-pc-port/perfect_dark", "perfect-dark")]
    [InlineData("github", "fgsfdsfgs/perfect_dark", "perfect-dark")]
    [InlineData("gitlab", "troyedwardsjr/z2rs", null)]
    [InlineData("gitlab", "perfect-dark-pc-port/perfect_dark", null)]
    [InlineData("manual", "troyedwardsjr/z2rs", null)]
    [InlineData("github", "another/z2rs", null)]
    public async Task Catalog_identity_survives_actual_add_and_reload_without_claiming_game_data_ready(
        string provider, string repository, string? adapter)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-catalog-setup-" + Guid.NewGuid().ToString("N"));
        var previousRoot = RelicadePaths.OverrideUserDataRoot;
        Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
        try
        {
            var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps");
            using var network = new OfflineMetadata(); using var http = new HttpClient(network);
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root))
            { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
            await using var session = new LauncherSession();
            var entry = new QuiverCatalogApp
            {
                Id = "synthetic-supported-project", Slug = "synthetic-project", Name = "Synthetic project metadata",
                ProjectName = "Synthetic catalog mapping", Launcher = new() { FolderName = "Project" }
            };
            var external = QuiverCatalogMapping.ToGameInfo(manager.CatalogService, entry,
                new() { Provider = provider, Repository = repository });
            var service = new LibraryAddService(manager, new SettingsViewModel(store), session);
            var added = await service.AddAsync(external).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(LibraryAddOutcome.Added, added.Outcome);
            Assert.NotSame(external, added.App);
            var saved = Assert.Single(await new AppCatalogService(dataDirectory: root).LoadLocalAppsAsync());
            Assert.Equal(added.App!.InstanceKey, saved.InstanceKey);
            Assert.Equal(provider == "manual" ? "" : repository, saved.Repository);
            Assert.Equal(provider == "gitlab" ? "gitlab" : null, saved.RepositorySource);
            Assert.Equal(provider == "manual" ? null : entry.Id, saved.CatalogEntryId);
            Assert.False(saved.IsInstalled); Assert.False(saved.OwnedDataReady);
            Assert.False(saved.AutoUpdate);
            Assert.False(File.Exists(Path.Combine(root, "Apps", "Project", "LastPlayed.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "owned-game-data")));

            var requirement = OwnedGameSetupService.Requirement(saved);
            if (adapter == null) Assert.Null(requirement);
            else
            {
                var expected = adapter == "z2rs" ? OwnedGameSetupService.Z2rs : OwnedGameSetupService.PerfectDark;
                Assert.Equal(expected, requirement);
                await OwnedGameSetupService.RefreshAsync(saved, store.Current.AppsPath);
                Assert.False(saved.OwnedDataReady);
                Assert.Contains("Install a supported package", saved.OwnedDataSetupMessage);
                var target = OwnedGameSetupService.PinnedInstallTarget(saved, expected.ReleaseIdentity, expected.AssetIdentity);
                Assert.Equal(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64, target != null);
                if (target != null)
                {
                    Assert.Equal(adapter == "z2rs" ? "z2rs" : "pd.x86_64", target.RelativePath);
                    Assert.Equal(expected.ExecutableSha256, target.Sha256);
                }
                Assert.Null(OwnedGameSetupService.PinnedInstallTarget(saved, "unreviewed-release", expected.AssetIdentity));
                Assert.Null(OwnedGameSetupService.PinnedInstallTarget(saved, expected.ReleaseIdentity, "unreviewed-asset"));
            }
            await session.DisposeAsync();
            Assert.All(network.Requests, uri => Assert.True(uri.Host is "api.quiverlauncher.com" or "api.github.com" or "gitlab.com",
                $"Unexpected metadata request: {uri.Host}"));
        }
        finally
        {
            RelicadePaths.OverrideUserDataRoot = previousRoot;
            Directory.Delete(root, true);
        }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AutoUpdateNewlyAddedApps = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class OfflineMetadata : HttpMessageHandler
    {
        internal ConcurrentQueue<Uri> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Enqueue(request.RequestUri!); token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("Controlled offline metadata; no game payload") });
        }
    }
}
