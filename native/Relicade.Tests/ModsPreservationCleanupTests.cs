using System.Net;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.Services.Mods;
using Relicade.ViewModels;
using static Relicade.Tests.ModsPreservationTests;

namespace Relicade.Tests;

public sealed class ModsPreservationCleanupTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "mods-cleanup-" + Guid.NewGuid().ToString("N"));
    readonly List<string> denied = [];
    public ModsPreservationCleanupTests() => Directory.CreateDirectory(root);
    void RestorePermissions()
    {
        foreach (var path in denied)
            if (Directory.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public void Dispose() { RestorePermissions(); Directory.Delete(root, true); }
    static void At(ModInstallService service, Action<string> action) =>
        typeof(ModInstallService).GetProperty("TransactionBoundary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(service, action);
    string DenyStaging(string install)
    {
        var path = Assert.Single(Directory.GetDirectories(Path.Combine(install, ".relicade-mod-staging")));
        denied.Add(path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        // This assertion fails under privileged DAC bypass: the test must actually deny writes.
        Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(path, "denied-probe"), "not allowed"));
        return path;
    }
    static void AssertCleanupDiagnostic(Exception error, string operation)
    {
        // Reflection allows the same behavioural tests to compile against the exact old baseline.
        var method = typeof(ModInstallService).Assembly.GetType("Relicade.Services.Mods.ModInstallOutcome")?.GetMethod("GetCleanupFailures");
        Assert.NotNull(method);
        var failures = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method.Invoke(null, [error]));
        var failure = Assert.Single(failures.Cast<object>());
        Assert.Equal(operation, failure.GetType().GetProperty("RetainedPath")!.GetValue(failure));
        Assert.True(failure.GetType().GetProperty("Cause")!.GetValue(failure) is IOException or UnauthorizedAccessException);
    }
    static ModPackage Multiple() => new()
    {
        ProviderId = "fixture", SourceKey = "source", Id = "package", FullName = "Author-package", Name = "package", Owner = "Author",
        LatestVersion = new() { Version = "2", DownloadUrl = "https://example.invalid/one" },
        DownloadFiles = new[] { "one", "two" }.Select(id => new ModDownloadFile { Id = id, FileName = id + ".zip", Version = "2", DownloadUrl = "https://example.invalid/" + id }).ToList()
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Precommit_cleanup_denial_keeps_original_error_exact_generation_and_retry(bool cancel, bool existing)
    {
        if (!OperatingSystem.IsLinux()) return; // Linux permission control, not cross-platform acceptance.
        var package = Package(); var provider = new Provider { Bytes = Zip(content: "new") };
        if (existing)
        {
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            File.WriteAllText(Path.Combine(root, "mods", "payload.txt"), "old");
            var record = Record(package); record.DownloadFileId = package.DownloadFiles[0].Id; record.Files = ["payload.txt"];
            var sibling = Record(Package("sibling")); sibling.Files = ["sibling.txt"];
            File.WriteAllText(Path.Combine(root, "mods", "sibling.txt"), "sibling");
            new InstalledModsStore().Save(root, new() { Mods = [record, sibling] });
        }
        var sidecar = InstalledModsStore.GetSidecarPath(root);
        var before = File.Exists(sidecar) ? File.ReadAllBytes(sidecar) : null;
        var service = new ModInstallService(new([provider]));
        using var cancellation = new CancellationTokenSource();
        var fault = new IOException("Controlled staging failure"); string? operation = null;
        At(service, point =>
        {
            if (point != "Staged") return;
            operation = DenyStaging(root);
            if (cancel) cancellation.Cancel(); else throw fault;
        });
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles, cancellationToken: cancellation.Token));
        if (cancel) Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        else Assert.Same(fault, error);
        Assert.NotNull(operation); AssertCleanupDiagnostic(error, operation);
        Assert.True(Directory.Exists(operation)); Assert.False(File.Exists(Path.Combine(operation, "journal.json")));
        Assert.Equal(before, File.Exists(sidecar) ? File.ReadAllBytes(sidecar) : null);
        var reopened = new InstalledModsStore().Load(root);
        Assert.Equal(existing ? 2 : 0, reopened.Mods.Count);
        if (existing)
        {
            Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
            Assert.Equal("sibling", File.ReadAllText(Path.Combine(root, "mods", "sibling.txt")));
        }
        else Assert.False(File.Exists(Path.Combine(root, "mods", "payload.txt")));
        RestorePermissions();
        await new ModInstallService(new([provider])).InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles);
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
        Assert.Equal(existing ? 2 : 1, new InstalledModsStore().Load(root).Mods.Count);
        // Retained inactive evidence is not silently scavenged by unrelated retries.
        Assert.True(Directory.Exists(operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_denial_after_prior_file_commit_keeps_token_commit_ledger_and_old_second_file(bool cancel)
    {
        if (!OperatingSystem.IsLinux()) return;
        var package = Multiple(); var provider = new FilesProvider();
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "mods", "two.txt"), "old-two");
        var old = Record(package); old.DownloadFileId = "two"; old.DownloadFileName = "two.zip"; old.Files = ["two.txt"];
        new InstalledModsStore().Save(root, new() { Mods = [old] });
        var service = new ModInstallService(new([provider]));
        using var cancellation = new CancellationTokenSource();
        var fault = new IOException("Controlled second staging failure");
        int staged = 0; string? operation = null; byte[]? before = null;
        At(service, point =>
        {
            if (point != "Staged" || ++staged != 2) return;
            before = File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root)); operation = DenyStaging(root);
            if (cancel) cancellation.Cancel(); else throw fault;
        });
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles, cancellationToken: cancellation.Token));
        if (cancel) Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        else Assert.Same(fault, error.InnerException);
        Assert.Equal("one", Assert.Single(Assert.IsType<string[]>(error.Data["CommittedMods"])).Split("file: ")[1].TrimEnd(')'));
        AssertCleanupDiagnostic(error, operation!);
        Assert.Equal(before, File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root)));
        Assert.Equal("old-two", File.ReadAllText(Path.Combine(root, "mods", "two.txt")));
        Assert.Equal("one", File.ReadAllText(Path.Combine(root, "mods", "one.txt")));
        Assert.Equal(2, new InstalledModsStore().Load(root).Mods.Count);
        RestorePermissions();
        await new ModInstallService(new([provider])).InstallSelectedFilesAsync(root, "mods", package, [package], provider, package.DownloadFiles);
        Assert.Equal("two", File.ReadAllText(Path.Combine(root, "mods", "two.txt")));
        Assert.Equal(1, provider.Downloads.Count(id => id == "one"));
        Assert.Equal(2, new InstalledModsStore().Load(root).Mods.Count);
        Assert.True(Directory.Exists(operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_reports_cleanup_diagnostic_and_prior_commits_without_claiming_success(bool cancel)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var http = new HttpClient(new ActionHandler());
        using var manager = new GameManager(new Settings(root), http);
        await using var session = new LauncherSession();
        var game = new GameInfo { Name = "Fixture", Repository = "fixture/game", FolderName = "Fixture", ModsPath = "mods", Status = GameStatus.Installed };
        var install = game.GetInstallPath(manager.GamesFolder); Directory.CreateDirectory(install);
        var package = new ModPackage { ProviderId = "gamebanana", SourceKey = "source", Id = "123", FullName = "Fixture-package", Name = "Fixture", Owner = "Author" };
        var item = new ModListItem { Package = package };
        var model = new ModsViewModel { Game = game, AllItems = [item], Catalog = [package] };
        var workspace = new ModsCatalogWorkspace(manager, session, model, a => { a(); return Task.CompletedTask; });
        var messages = new List<string>();
        int staged = 0; string? operation = null;
        At(workspace.ModInstaller, point =>
        {
            if (point != "Staged" || ++staged != 2) return;
            operation = DenyStaging(install);
            if (cancel) throw new OperationCanceledException("Controlled UI staging cancellation", session.Token);
            throw new IOException("Controlled UI staging failure");
        });
        var actions = new ModsActions(manager, session, model, workspace, (t,p,l,files,ids) => Task.FromResult(files), (m,t) => { messages.Add(m); return Task.CompletedTask; }, (m,t) => Task.FromResult(MessagePromptResult.Yes));
        await actions.InstallModAsync(item);
        Assert.Contains("committed", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("101", model.Status);
        Assert.Contains("Cleanup failed; retained operation evidence", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retained", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(operation!, model.Status);
        Assert.Contains(cancel ? "cancelled" : "failed", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(item.IsBusy); Assert.True(item.CanUninstall);
        Assert.Equal("101", Assert.Single(new InstalledModsStore().Load(install).Mods).DownloadFileId);
        Assert.False(File.Exists(Path.Combine(install, "mods", "two.txt")));
        if (!cancel) Assert.Contains(messages, m => m.Contains("cleanup", StringComparison.OrdinalIgnoreCase));
        RestorePermissions();
        await actions.InstallModAsync(item);
        Assert.Equal(2, new InstalledModsStore().Load(install).Mods.Count);
        Assert.Equal("two", File.ReadAllText(Path.Combine(install, "mods", "two.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_download_cleanup_denial_retains_original_token_cause_and_diagnostic(bool cancel)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var cancellation = new CancellationTokenSource();
        var directory = Path.Combine(Path.GetTempPath(), "RelicadeMods");
        Directory.CreateDirectory(directory);
        var before = Directory.GetFiles(directory).ToHashSet();
        var fault = new IOException("Controlled partial download failure");
        using var content = new StreamContent(new FailingStream(() =>
        {
            denied.Add(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(directory, "denied-probe"), "not allowed"));
            if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw fault;
        }));
        string? partial = null;
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => ModArchiveDownload.CopyContentToTempFileAsync(content, cancellationToken: cancellation.Token));
            if (cancel) Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
            else Assert.Same(fault, error);
            partial = Assert.Single(Directory.GetFiles(directory).Where(p => !before.Contains(p)));
            AssertCleanupDiagnostic(error, partial);
            Assert.True(File.Exists(partial));
        }
        finally
        {
            RestorePermissions();
            // Only the fixture's newly created partial file is explicitly removed.
            foreach (var path in Directory.GetFiles(directory).Where(p => !before.Contains(p))) File.Delete(path);
        }
    }
    sealed class FailingStream(Action fail) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { fail(); return ValueTask.FromResult(0); }
    }

    sealed class Settings(string path) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = path };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    sealed class ActionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(".zip"))
            {
                var id = path.EndsWith("one.zip") ? "one" : "two";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Zip(id + ".txt", id)) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"_sName":"Fixture","_sVersion":"2","_aFiles":[
                  {"_idRow":101,"_sFile":"one.zip","_sDownloadUrl":"https://example.invalid/one.zip","_sVersion":"2"},
                  {"_idRow":102,"_sFile":"two.zip","_sDownloadUrl":"https://example.invalid/two.zip","_sVersion":"2"}]}
                """) });
        }
    }
    sealed class FilesProvider : IModProvider
    {
        public List<string> Downloads = [];
        public string Id => "fixture"; public string DisplayName => Id; public bool SupportsPagedListing => false; public bool SupportsRemoteSearch => false;
        public bool TryParseSource(string u, out ModSourceRef s) { s = new() { ProviderId = Id, SourceKey = u, SourceUrl = u, DisplayLabel = u }; return true; }
        public void ForceRefreshOnNextList() { }
        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef s, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef s, string? t, int n, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef s, string q, string? t, int n, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(ModPackageVersion v, IProgress<double>? p = null, CancellationToken c = default)
        {
            var id = v.DownloadUrl.Split('/').Last(); Downloads.Add(id);
            return Task.FromResult<Stream>(new MemoryStream(Zip(id + ".txt", id)));
        }
        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();
        public Task<string?> GetReadmeAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
        public Task<string?> GetChangelogAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
    }
}
