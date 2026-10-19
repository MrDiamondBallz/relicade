using System.Security.Cryptography;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

// These tests intentionally compile against the pre-MR-01 API. Never invoke native trash.
public sealed class ManagedRemovalBaselineTests : IDisposable
{
    readonly CredentialVaultTestScope credentials = new();
    readonly string root = Path.Combine(Path.GetTempPath(), "managed-removal-red-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "Apps", "live");
    public ManagedRemovalBaselineTests() => Directory.CreateDirectory(root);
    public void Dispose() { credentials.Dispose(); /* Retain synthetic fixture for independent review. */ }
    async Task Install()
    {
        var source = Path.Combine(root, "fixture.exe"); File.WriteAllText(source, "NONEXECUTABLE synthetic payload");
        await GameInstallationService.InstallOrUpdateGameAsync(source, Live, "fixture.exe", "old",
            new() { PreservedDataPaths = ["saves"], InstalledBuildText = "fixture.exe\n" + new string('a', 64) + "\n" });
        Directory.CreateDirectory(Path.Combine(Live, "saves")); File.WriteAllText(Path.Combine(Live, "saves", "slot"), "current save");
        File.WriteAllText(Path.Combine(root, "sibling"), "outside keep");
    }
    int snapshot;
    Dictionary<string, string> Snapshot()
    {
        var values = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
        var receipts = Path.Combine(Path.GetTempPath(), "removal-snapshots", Path.GetFileName(root)); Directory.CreateDirectory(receipts);
        File.WriteAllText(Path.Combine(receipts, snapshot++ + ".json"), System.Text.Json.JsonSerializer.Serialize(values));
        return values;
    }
    [Fact]
    public async Task Direct_located_removal_must_preserve_managed_payload_metadata_and_history()
    {
        await Install(); var before = Snapshot(); var callbacks = new List<string>();
        // Record only: even the baseline must never dispatch to the real OS trash.
        var error = Record.Exception(() => LocatedInstallRemoval.Remove(Live, false, callbacks.Add));
        Assert.Equal(before.OrderBy(p => p.Key), Snapshot().OrderBy(p => p.Key));
        Assert.Empty(callbacks); Assert.IsAssignableFrom<IOException>(error);
    }
    sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Synthetic network denied without transport");
    }
    sealed class Store(string root) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps") };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    [Fact]
    public async Task Library_located_route_must_preserve_managed_metadata_and_saved_location()
    {
        await Install();
        // InstalledBuild points at a nonexistent archive. The old route deletes only
        // metadata, never calls its hardcoded native trash callback in this fixture.
        File.WriteAllText(Path.Combine(Live, InstalledBuild.FileNameOnDisk), "absent.zip\n" + new string('a', 64) + "\n");
        var store = new Store(root); var catalog = new AppCatalogService(dataDirectory: Path.Combine(root, "catalog"));
        var game = new GameInfo { Name = "Synthetic", FolderName = "live", Repository = "fixture/removal", InstallPath = Live, IsInLocalAppsJson = true, Status = GameStatus.Installed };
        await catalog.SaveLocalAppsAsync([game]);
        using var manager = new GameManager(store, new HttpClient(new NoNetwork()), catalogService: catalog);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        await using var session = new LauncherSession(); var errors = new List<string>();
        var actions = new LibraryActions(manager, new LibraryPersistenceService(manager), library.Settings, session, library,
            () => throw new Exception("No picker"), (message, _, question, _) => { if (!question) errors.Add(message); return Task.FromResult(true); },
            _ => throw new Exception("No OS dispatch"), () => Task.CompletedTask);
        var before = Snapshot(); await actions.UninstallAsync(game);
        Assert.Equal(before.OrderBy(p => p.Key), Snapshot().OrderBy(p => p.Key));
        Assert.Equal(Live, game.InstallPath); Assert.NotEmpty(errors);
        Assert.Equal(Live, Assert.Single(await catalog.LoadLocalAppsAsync()).InstallPath);
    }
}
