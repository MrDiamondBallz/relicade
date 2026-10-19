using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// Compiles against the original API. NotInstalled is deliberately set so the
// unsafe baseline never reaches its hardcoded real trash or OS-package helpers.
public sealed class ManagedRemovalCliBaselineTests
{
    [Theory]
    [InlineData(false, "none")][InlineData(true, "none")]
    [InlineData(false, "MSI")][InlineData(true, "MSI")]
    [InlineData(false, "Flatpak")][InlineData(true, "Flatpak")]
    public async Task Stale_NotInstalled_must_refuse_retained_managed_evidence_not_announce_success(bool located, string receipt)
    {
        using var credentials = new CredentialVaultTestScope();
        var root = Path.Combine(Path.GetTempPath(), "managed-removal-cli-red-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "Apps", "live"); Directory.CreateDirectory(root);
        await new PortableInstallationTransaction(live).InstallAsync(candidate =>
        {
            File.WriteAllText(Path.Combine(candidate, "fixture.exe"), "NONEXECUTABLE synthetic CLI fixture");
            return Task.CompletedTask;
        }, "old");
        if (receipt == "MSI") WindowsInstallerService.WriteReceipt(live, new("Linked", "old", Path.Combine(live, "fixture.exe")));
        if (receipt == "Flatpak") File.WriteAllText(Path.Combine(live, FlatpakService.ReceiptFileName), "synthetic conflicting receipt");
        File.WriteAllText(Path.Combine(root, "sibling"), "keep sibling");
        var store = new Store(root); var catalog = new AppCatalogService(dataDirectory: Path.Combine(root, "catalog"));
        var network = new NoNetwork();
        using var manager = new GameManager(store, new HttpClient(network), catalogService: catalog);
        var game = new GameInfo { Name = "Synthetic CLI", FolderName = "live", Repository = "fixture/removal", InstallPath = located ? live : null,
            Status = GameStatus.NotInstalled, SelectedExecutable = "fixture.exe", InstalledVersion = "old", IsManagedPortable = false };
        manager.Games.Add(game); await catalog.SaveLocalAppsAsync([game]);
        string[] Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(root, p) + " " + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        var before = Snapshot(); var cli = new CLIHandler(manager); var old = Console.Out; using var output = new StringWriter();
        int result;
        try
        {
            Console.SetOut(output);
            result = await (Task<int>)typeof(CLIHandler).GetMethod("UninstallGame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cli, [game.Name])!;
        }
        finally { Console.SetOut(old); }
        var after = Snapshot(); var text = output.ToString();
        var receipts = Path.Combine(Path.GetTempPath(), "removal-cli-red-receipts"); Directory.CreateDirectory(receipts);
        File.WriteAllText(Path.Combine(receipts, Path.GetFileName(root) + ".json"), JsonSerializer.Serialize(new { before, after, result, output = text, requests = network.Calls }));
        Assert.Equal(before, after); Assert.Equal(0, network.Calls); Assert.Equal(GameStatus.NotInstalled, game.Status);
        Assert.Equal(located ? live : null, game.InstallPath); Assert.Equal("fixture.exe", game.SelectedExecutable);
        Assert.NotEqual(0, result); Assert.Contains("Remove from Library", text); Assert.DoesNotContain("is not installed", text);
        Assert.DoesNotContain("uninstalled successfully", text);
    }
    sealed class Store(string root) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps") };
        public AppSettings Load() => Current;
        public void Save(AppSettings value) { }
    }
    sealed class NoNetwork : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new HttpRequestException("Synthetic transport denies all requests"); }
    }
}
