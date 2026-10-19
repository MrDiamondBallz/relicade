using Relicade.Services.Mods;
using Relicade.Services.Mods.Providers.GameBanana;
using Relicade.Services.Mods.Providers.Thunderstore;
using Relicade.Services;
using Relicade.Models;
using Relicade.ViewModels;
using System.Net;
using static Relicade.Tests.ModsPreservationTests;

namespace Relicade.Tests;

public sealed class ModsPreservationDependencyTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "mods-dependencies-" + Guid.NewGuid().ToString("N"));
    public ModsPreservationDependencyTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    static ModPackage Mod(string name, string version = "1.0.0", params string[] deps) => new()
    {
        ProviderId = "fixture", SourceKey = "source", Id = name, Name = name, Owner = "Author", FullName = "Author-" + name,
        LatestVersion = new() { Version = version, DownloadUrl = "https://example.invalid/" + name, Dependencies = deps }
    };
    [Theory]
    [InlineData("cycle")]
    [InlineData("missing")]
    [InlineData("version")]
    public async Task Unresolvable_dependency_graph_is_rejected_before_any_mutation(string fault)
    {
        var a = Mod("A", "1.0.0", "Author-B-1.0.0");
        var b = fault == "cycle" ? Mod("B", "1.0.0", "Author-A-1.0.0") : Mod("B", fault == "version" ? "2.0.0" : "1.0.0");
        var provider = new Provider(); var service = new ModInstallService(new([provider]));
        await Assert.ThrowsAnyAsync<Exception>(() => service.InstallWithDependenciesAsync(root, "mods", a, fault == "missing" ? [a] : [a,b], provider));
        Assert.False(File.Exists(InstalledModsStore.GetSidecarPath(root)));
        Assert.False(Directory.Exists(Path.Combine(root, "mods")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_root_preserves_existing_dependencies_siblings_and_reports_new_partial_work(bool dependencyAlreadyInstalled)
    {
        var a = Mod("A", "2.0.0", "Author-B-1.0.0"); var old = Mod("A"); var b = Mod("B"); var sibling = Mod("Sibling");
        var provider = new Provider();
        provider.Archives[old.LatestVersion!.DownloadUrl] = Zip("root.txt", "root-old");
        provider.Archives[b.LatestVersion!.DownloadUrl] = Zip("dependency.txt", "dependency-old");
        provider.Archives[sibling.LatestVersion!.DownloadUrl] = Zip("sibling.txt", "sibling-old");
        var service = new ModInstallService(new([provider]));
        await service.InstallAsync(root, "mods", old, provider);
        await service.InstallAsync(root, "mods", sibling, provider);
        if (dependencyAlreadyInstalled) await service.InstallAsync(root, "mods", b, provider);
        provider.FailUrl = a.LatestVersion!.DownloadUrl;
        var ex = await Assert.ThrowsAsync<IOException>(() => service.InstallWithDependenciesAsync(root, "mods", a, [a,b], provider));
        if (!dependencyAlreadyInstalled) Assert.Contains("already committed", ex.Message);
        var records = new InstalledModsStore().Load(root).Mods;
        Assert.Equal(3, records.Count); Assert.Equal("1.0.0", records.Single(r => r.Id == "A").Version);
        Assert.Equal("root-old", File.ReadAllText(Path.Combine(root,"mods","root.txt")));
        Assert.Equal("dependency-old", File.ReadAllText(Path.Combine(root,"mods","dependency.txt")));
        Assert.Equal("sibling-old", File.ReadAllText(Path.Combine(root,"mods","sibling.txt")));
    }
    [Fact]
    public async Task Sidecar_error_and_provider_error_are_visible_in_workspace_and_retry_after_valid_repair()
    {
        using var manager = new GameManager(new Settings(root));
        await using var session = new LauncherSession();
        var provider = new Provider { Failure = "list" };
        var game = new GameInfo { Name = "Fixture", Repository = "fixture/game", FolderName = "Fixture", ModsPath = "mods", ModsSources = Sources().ToList() };
        var install = game.GetInstallPath(manager.GamesFolder); Directory.CreateDirectory(install);
        var model = new ModsViewModel { Game = game };
        var workspace = new ModsCatalogWorkspace(manager, session, model, a => { a(); return Task.CompletedTask; }, new(new([provider])));
        await workspace.RefreshAsync(false);
        Assert.True(model.HasLoadError); Assert.Contains("Retry", model.Status); Assert.Empty(model.Rows);
        provider.Failure = null;
        File.WriteAllText(InstalledModsStore.GetSidecarPath(install), "{broken");
        await workspace.RefreshAsync(false);
        Assert.True(model.HasLoadError); Assert.Contains("repair", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{broken", File.ReadAllText(InstalledModsStore.GetSidecarPath(install)));
        File.WriteAllText(InstalledModsStore.GetSidecarPath(install), "{\"mods\":[]}");
        await workspace.RefreshAsync(false);
        Assert.False(model.HasLoadError); Assert.Single(model.Rows);
    }
    sealed class Settings(string root) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = root };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    [Theory]
    [InlineData("gamebanana", "null")]
    [InlineData("gamebanana", "{}")]
    [InlineData("thunderstore", "null")]
    [InlineData("thunderstore", "{}")]
    public async Task Invalid_provider_response_is_not_successful_exhaustion(string id, string json)
    {
        using var http = new HttpClient(new Handler(json));
        IModProvider provider = id == "gamebanana" ? new GameBananaModProvider(http, root) : new ThunderstoreModProvider(http, root);
        var loader = new ModCatalogLoader(new([provider]));
        var result = await loader.LoadBrowseSessionAsync([new() { Provider = id, SourceUrl = id == "gamebanana" ? "123" : "fixture" }], null);
        Assert.True(result.HasErrors); Assert.False(Assert.Single(result.SourceStates).IsDone); Assert.True(result.CanLoadMore);
    }
    sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith("/filters/") ? "{\"sections\":[]}" : json;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
}
