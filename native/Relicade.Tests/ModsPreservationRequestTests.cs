using System.Net;
using Relicade.Core.Models;
using System.Text.Json.Nodes;
using Relicade.Models;
using Relicade.Services;
using Relicade.Services.Mods;
using Relicade.ViewModels;
using static Relicade.Tests.ModsPreservationTests;

namespace Relicade.Tests;

public sealed class ModsPreservationRequestTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "mods-request-" + Guid.NewGuid().ToString("N"));
    public ModsPreservationRequestTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    string Sidecar => InstalledModsStore.GetSidecarPath(root);
    void SaveLegacy(ModPackage package, params InstalledModRecord[] extra)
    {
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "mods", "payload.txt"), "old");
        var old = Record(package); old.DownloadFileName = "same.zip"; old.Files = ["payload.txt"];
        new InstalledModsStore().Save(root, new() { Mods = [old, ..extra] });
    }
    static void At(ModInstallService service, Action<string> action) =>
        typeof(ModInstallService).GetProperty("TransactionBoundary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(service, action);

    [Fact]
    public async Task Filename_only_update_replaces_exact_record_and_preserves_sibling_on_reopen()
    {
        var package = Package(); var sibling = Record(Package("sibling")); sibling.Files = ["sibling.txt"];
        SaveLegacy(package, sibling);
        File.WriteAllText(Path.Combine(root, "mods", "sibling.txt"), "sibling");
        var provider = new Provider { Bytes = Zip(content: "new") };
        var file = Assert.Single(ModDownloadFileSelection.ResolveFilesToUpdate(package, new InstalledModsStore().Load(root).Mods));
        await new ModInstallService(new([provider])).InstallSelectedFilesAsync(root, "mods", package, [package], provider, [file]);
        var reopened = new InstalledModsStore().Load(root).Mods;
        Assert.Equal(2, reopened.Count);
        Assert.Equal(file.Id, reopened.Single(r => r.Id == package.Id).DownloadFileId);
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
        Assert.Equal("sibling", File.ReadAllText(Path.Combine(root, "mods", "sibling.txt")));
        Assert.Equal("sibling.txt", Assert.Single(reopened.Single(r => r.Id == "sibling").Files));
    }
    [Theory]
    [InlineData("Prepared")]
    [InlineData("BeforeManifest")]
    [InlineData("Manifest")]
    public async Task Filename_only_transition_journal_recovers_exact_old_or_new_identity(string point)
    {
        var package = Package(); SaveLegacy(package); var before = File.ReadAllBytes(Sidecar);
        var provider = new Provider { Bytes = Zip(content: "new") }; var service = new ModInstallService(new([provider]));
        At(service, p => { if (p == point || p == "Recovering") throw new IOException("Controlled transition interruption"); });
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(root, "mods", package, provider, package.DownloadFiles[0]));
        var journal = JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(root, ".relicade-mod-transactions"), "journal.json", SearchOption.AllDirectories))))!;
        Assert.Equal(package.DownloadFiles[0].Id, journal["fileId"]!.GetValue<string>());
        Assert.Single(journal["oldRecordIndexes"]!.AsArray());
        var reopened = Assert.Single(new InstalledModsStore().Load(root).Mods);
        Assert.Equal(point == "Manifest" ? package.DownloadFiles[0].Id : null, reopened.DownloadFileId);
        Assert.Equal("same.zip", reopened.DownloadFileName);
        Assert.Equal(point == "Manifest" ? "new" : "old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
        if (point != "Manifest") Assert.Equal(before, File.ReadAllBytes(Sidecar));
    }
    [Theory]
    [InlineData("download")]
    [InlineData("parse")]
    [InlineData("cancel")]
    public async Task Failed_filename_only_update_preserves_exact_legacy_generation(string failure)
    {
        var package = Package(); SaveLegacy(package); var before = File.ReadAllBytes(Sidecar);
        var provider = new Provider { Failure = failure };
        await Assert.ThrowsAnyAsync<Exception>(() => new ModInstallService(new([provider])).InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles));
        Assert.Null(Assert.Single(new InstalledModsStore().Load(root).Mods).DownloadFileId);
        Assert.Equal(before, File.ReadAllBytes(Sidecar));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
    }
    [Fact]
    public async Task Duplicate_legacy_records_fail_closed_even_without_overlapping_payloads()
    {
        var package = Package(); var duplicate = Record(package); duplicate.DownloadFileName = "same.zip"; duplicate.Files = ["duplicate.txt"];
        SaveLegacy(package, duplicate); File.WriteAllText(Path.Combine(root, "mods", "duplicate.txt"), "duplicate");
        var before = File.ReadAllBytes(Sidecar); var provider = new Provider { Bytes = Zip("new.txt", "new") };
        await Assert.ThrowsAnyAsync<Exception>(() => new ModInstallService(new([provider])).InstallAsync(root, "mods", package, provider, package.DownloadFiles[0]));
        Assert.Equal(before, File.ReadAllBytes(Sidecar)); Assert.False(File.Exists(Path.Combine(root, "mods", "new.txt")));
        Assert.Equal(2, new InstalledModsStore().Load(root).Mods.Count);
    }
    [Fact]
    public void Ambiguous_catalog_filename_cannot_offer_automatic_legacy_update()
    {
        var package = Multiple(sameNames: true); var record = Record(package); record.DownloadFileName = "same.zip";
        Assert.Null(ModDownloadFileSelection.FindCatalogFile(package, record));
        Assert.Empty(ModDownloadFileSelection.ResolveFilesToUpdate(package, [record]));
    }
    [Fact]
    public async Task Explicit_missing_id_cannot_claim_same_filename_payload()
    {
        var package = Package(); SaveLegacy(package);
        var doc = new InstalledModsStore().Load(root); doc.Mods[0].DownloadFileId = "missing"; new InstalledModsStore().Save(root, doc);
        var before = File.ReadAllBytes(Sidecar); var provider = new Provider { Bytes = Zip(content: "new") };
        Assert.Empty(ModDownloadFileSelection.ResolveFilesToUpdate(package, doc.Mods));
        await Assert.ThrowsAnyAsync<Exception>(() => new ModInstallService(new([provider])).InstallAsync(root, "mods", package, provider, package.DownloadFiles[0]));
        Assert.Equal(before, File.ReadAllBytes(Sidecar)); Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
    }

    static ModPackage Multiple(bool sameNames = false) => new()
    {
        ProviderId = "fixture", SourceKey = "source", Id = "package", FullName = "Author-package", Name = "package", Owner = "Author",
        LatestVersion = new() { Version = "2", DownloadUrl = "https://example.invalid/one" },
        DownloadFiles = new[] { "one", "two", "three" }.Select(id => new ModDownloadFile { Id = id, FileName = sameNames ? "same.zip" : id + ".zip", Version = "2", DownloadUrl = "https://example.invalid/" + id }).ToList()
    };
    static void AssertCommit(Exception error, params string[] ids)
    {
        var actual = error.Data["CommittedMods"] as string[] ?? [];
        Assert.Equal(ids.Length, actual.Length);
        foreach (var id in ids) Assert.Contains(actual, s => s.Contains("fixture", StringComparison.Ordinal) && s.Contains("source", StringComparison.Ordinal) && s.Contains("package", StringComparison.Ordinal) && s.Contains(id, StringComparison.Ordinal));
        var typed = Assert.IsAssignableFrom<Array>(error.Data["CommittedModIdentities"]);
        Assert.Equal(ids.Length, typed.Length);
        foreach (var id in ids) Assert.Contains(typed.Cast<object>(), value =>
            (string?)value.GetType().GetProperty("Provider")!.GetValue(value) == "fixture" &&
            (string?)value.GetType().GetProperty("SourceKey")!.GetValue(value) == "source" &&
            (string?)value.GetType().GetProperty("PackageId")!.GetValue(value) == "package" &&
            (string?)value.GetType().GetProperty("FileId")!.GetValue(value) == id);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multi_file_failure_reports_all_prior_distinct_commits_and_preserves_sibling(bool cancel)
    {
        var package = Multiple(); var provider = new RequestProvider { FailAt = "three", Cancel = cancel };
        var service = new ModInstallService(new([provider]));
        var sibling = Package("sibling"); var siblingProvider = new Provider { Bytes = Zip("sibling.txt", "sibling") };
        await service.InstallAsync(root, "mods", sibling, siblingProvider, sibling.DownloadFiles[0]);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles));
        var reopened = new InstalledModsStore().Load(root).Mods;
        Assert.Equal(3, reopened.Count); Assert.Equal(new[] { "one", "two" }, reopened.Where(r => r.Id == "package").Select(r => r.DownloadFileId));
        Assert.Equal("sibling", File.ReadAllText(Path.Combine(root, "mods", "sibling.txt")));
        Assert.Equal("one", File.ReadAllText(Path.Combine(root, "mods", "one.txt")));
        Assert.False(File.Exists(Path.Combine(root, "mods", "three.txt")));
        if (cancel) Assert.Same(provider.Fault, Assert.IsAssignableFrom<OperationCanceledException>(error));
        else { Assert.Contains("remain installed", error.Message); Assert.Same(provider.Fault, error.InnerException); }
        AssertCommit(error, "one", "two");
        provider.FailAt = null;
        await new ModInstallService(new([provider])).InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles);
        Assert.Equal(4, new InstalledModsStore().Load(root).Mods.Count);
        Assert.Equal(1, provider.Downloads.Count(id => id == "one"));
        Assert.Equal(1, provider.Downloads.Count(id => id == "two"));
        Assert.Equal("three", File.ReadAllText(Path.Combine(root, "mods", "three.txt")));
    }
    [Theory]
    [InlineData("Manifest", false)]
    [InlineData("Committed", false)]
    [InlineData("Retired", false)]
    [InlineData("Manifest", true)]
    public async Task Followup_failure_includes_current_and_prior_commits(string point, bool cancel)
    {
        var package = Multiple(); var provider = new RequestProvider(); var service = new ModInstallService(new([provider]));
        var fault = cancel ? (Exception)new OperationCanceledException("Controlled committed cancellation") : new IOException("Controlled followup failure");
        int commits = 0;
        At(service, p => { if (p == point && ++commits == 2) throw fault; });
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles));
        Assert.Equal(new[] { "one", "two" }, new InstalledModsStore().Load(root).Mods.Select(r => r.DownloadFileId));
        AssertCommit(error, "one", "two");
        if (cancel) Assert.Same(fault, Assert.IsAssignableFrom<OperationCanceledException>(error));
        else Assert.Contains("follow-up", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dependency_partial_failure_reports_full_committed_identity(bool cancel)
    {
        var leaf = new ModPackage { ProviderId = "fixture", SourceKey = "source", Id = "leaf", FullName = "Author-leaf", Name = "leaf", Owner = "Author", LatestVersion = new() { Version = "1.0.0", DownloadUrl = "https://example.invalid/leaf" } };
        var dep = new ModPackage { ProviderId = "fixture", SourceKey = "source", Id = "dependency", FullName = "Author-dependency", Name = "dependency", Owner = "Author", LatestVersion = new() { Version = "1.0.0", DownloadUrl = "https://example.invalid/dependency", Dependencies = ["Author-leaf-1.0.0"] } };
        var package = new ModPackage { ProviderId = "fixture", SourceKey = "source", Id = "package", FullName = "Author-package", Name = "package", Owner = "Author", LatestVersion = new() { Version = "2", DownloadUrl = "https://example.invalid/root", Dependencies = ["Author-dependency-1.0.0"] } };
        var provider = new RequestProvider { FailAt = "root", Cancel = cancel };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new ModInstallService(new([provider])).InstallWithDependenciesAsync(root, "mods", package, [package,dep,leaf], provider));
        Assert.Equal(new[] { "leaf", "dependency" }, new InstalledModsStore().Load(root).Mods.Select(r => r.Id));
        var reports = error.Data["CommittedMods"] as string[] ?? [];
        Assert.Equal(2, reports.Length);
        foreach (var id in new[] { "leaf", "dependency" }) Assert.Contains(reports, report => report.Contains("fixture/source/" + id, StringComparison.Ordinal));
        if (cancel) Assert.Same(provider.Fault, error); else Assert.Same(provider.Fault, error.InnerException);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Second_file_activation_fault_keeps_first_commit_and_exact_second_preimage(bool cancel)
    {
        var package = Multiple(); var provider = new RequestProvider(); var service = new ModInstallService(new([provider]));
        Directory.CreateDirectory(Path.Combine(root, "mods")); File.WriteAllText(Path.Combine(root, "mods", "two.txt"), "old-two");
        var old = Record(package); old.DownloadFileId = "two"; old.DownloadFileName = "two.zip"; old.Files = ["two.txt"];
        new InstalledModsStore().Save(root, new() { Mods = [old] });
        Exception fault = cancel ? new OperationCanceledException("Controlled activation cancellation") : new IOException("Controlled activation failure");
        int staged = 0; byte[]? preimage = null;
        At(service, p => { if (p == "Staged" && ++staged == 2) preimage = File.ReadAllBytes(Sidecar); if (p == "BeforeManifest" && staged == 2) throw fault; });
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles));
        AssertCommit(error, "one");
        Assert.Equal(preimage, File.ReadAllBytes(Sidecar));
        Assert.Equal("old-two", File.ReadAllText(Path.Combine(root, "mods", "two.txt")));
        Assert.Equal("one", File.ReadAllText(Path.Combine(root, "mods", "one.txt")));
        Assert.Equal("1", new InstalledModsStore().Load(root).Mods.Single(r => r.DownloadFileId == "two").Version);
        if (cancel) Assert.Same(fault, error); else Assert.Same(fault, error.InnerException);
    }
    sealed class RequestProvider : IModProvider
    {
        public string? FailAt; public bool Cancel; public Exception? Fault;
        public List<string> Downloads = [];
        public string Id => "fixture"; public string DisplayName => Id; public bool SupportsPagedListing => false; public bool SupportsRemoteSearch => false;
        public bool TryParseSource(string u, out ModSourceRef s) { s = new() { ProviderId = Id, SourceKey = u, SourceUrl = u, DisplayLabel = u }; return true; }
        public void ForceRefreshOnNextList() { }
        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef s, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef s, string? t, int n, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef s, string q, string? t, int n, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(ModPackageVersion v, IProgress<double>? p = null, CancellationToken c = default)
        {
            var id = v.DownloadUrl.Split('/').Last();
            Downloads.Add(id);
            if (id == FailAt) { Fault = Cancel ? new OperationCanceledException("Controlled request cancellation", c) : new IOException("Controlled request failure"); throw Fault; }
            return Task.FromResult<Stream>(new MemoryStream(Zip(id + ".txt", id)));
        }
        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();
        public Task<string?> GetReadmeAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
        public Task<string?> GetChangelogAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Install_action_refreshes_rows_and_reports_partial_work_after_io_or_cancel(bool cancel, bool unreadableState)
    {
        string? install = null;
        using var http = new HttpClient(new ActionHandler(cancel, () => { if (unreadableState) File.WriteAllText(InstalledModsStore.GetSidecarPath(install!), "{broken"); }));
        using var manager = new GameManager(new Settings(root), http);
        await using var session = new LauncherSession();
        var game = new GameInfo { Name = "Fixture", Repository = "fixture/game", FolderName = "Fixture", ModsPath = "mods", Status = GameStatus.Installed };
        install = game.GetInstallPath(manager.GamesFolder); Directory.CreateDirectory(install);
        var package = new ModPackage { ProviderId = "gamebanana", SourceKey = "source", Id = "123", FullName = "Fixture-package", Name = "Fixture", Owner = "Author" };
        var item = new ModListItem { Package = package };
        var model = new ModsViewModel { Game = game, AllItems = [item], Catalog = [package] };
        var workspace = new ModsCatalogWorkspace(manager, session, model, a => { a(); return Task.CompletedTask; });
        var messages = new List<string>();
        var actions = new ModsActions(manager, session, model, workspace, (t,p,l,files,ids) => Task.FromResult(files), (m,t) => { messages.Add(m); return Task.CompletedTask; }, (m,t) => Task.FromResult(MessagePromptResult.Yes));
        await actions.InstallModAsync(item);
        Console.WriteLine("Action status: " + model.Status + "; messages: " + string.Join("; ", messages));
        if (unreadableState)
        {
            Assert.Equal("{broken", File.ReadAllText(InstalledModsStore.GetSidecarPath(install)));
            Assert.Equal(true, model.GetType().GetProperty("HasLoadError")?.GetValue(model)); Assert.Contains("reload failed", model.Status);
        }
        else
        {
            var reopened = Assert.Single(new InstalledModsStore().Load(install).Mods);
            Assert.Equal("101", reopened.DownloadFileId); Assert.True(item.CanUninstall);
        }
        Assert.Equal("one", File.ReadAllText(Path.Combine(install, "mods", "one.txt")));
        Assert.Contains("101", model.Status); Assert.Contains("committed", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("before replacement", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(item.IsBusy);
        if (!cancel) Assert.NotEmpty(messages);
    }
    sealed class Settings(string root) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = root };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    sealed class ActionHandler(bool cancel, Action beforeFailure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/two.zip"))
            {
                beforeFailure();
                throw cancel ? new OperationCanceledException("Controlled second file cancellation") : new IOException("Controlled second file failure");
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/one.zip")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Zip("one.txt", "one")) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"_sName":"Fixture","_sVersion":"2","_aFiles":[
                  {"_idRow":101,"_sFile":"one.zip","_sDownloadUrl":"https://example.invalid/one.zip","_sVersion":"2"},
                  {"_idRow":102,"_sFile":"two.zip","_sDownloadUrl":"https://example.invalid/two.zip","_sVersion":"2"}]}
                """) });
        }
    }
}
