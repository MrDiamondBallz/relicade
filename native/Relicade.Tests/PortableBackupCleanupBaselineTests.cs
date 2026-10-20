using System.Text.Json;
using System.Text.RegularExpressions;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// Baseline-compatible functional test: ec40b8fe builds successfully with this
// identical fixture. Its retained duplicate assertion fails because cleanup is
// unsupported, NOT because the baseline has a new data-loss defect.
public sealed class PortableBackupCleanupBaselineTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Public_command_reclaims_only_older_duplicate_payload(bool injected)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        await s.Install("one"); await s.Install("two"); await s.Install("three");
        var oldBackups = Directory.GetDirectories(s.Tx.OperationsPath)
            .Where(d => Path.GetFileName(d) != s.Tx.ReadCommitted()!.OperationId)
            .Select(d => Path.Combine(d, "backup", "fixture.AppImage")).Where(File.Exists).ToArray();
        Assert.Single(oldBackups);
        var before = s.Snapshot();
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        var match = Regex.Match(preview.Text, @"Plan SHA256: ([a-f0-9]{64})");
        var apply = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", match.Success ? match.Groups[1].Value : new string('0', 64)]);
        output.WriteLine($"preview={preview.Code}; apply={apply.Code}; actual_HTTP={s.Network.Calls}; {preview.Text}; {apply.Text}");
        // Substantive RED: real prior COMMITTED production backup still occupies
        // disk on baseline, regardless of unknown-command diagnostics/API presence.
        Assert.All(oldBackups, p => Assert.False(File.Exists(p), "Expected explicit cleanup to reclaim this verified older duplicate payload: " + p));
        Assert.Equal(0, preview.Code); Assert.Equal(0, apply.Code); Assert.Equal(0, s.Network.Calls);
        var after = s.Snapshot();
        foreach (var row in before)
            if (!oldBackups.Contains(Path.Combine(s.Root, row.Key))) Assert.Equal(row.Value, after[row.Key]);
        Assert.Equal("three", s.Tx.ReadCommitted()!.SelectedVersion);
    }
}

internal sealed class PortableBackupCleanupBaselineScope : IDisposable
{
    readonly CredentialVaultTestScope vault = new();
    readonly ISettingsStore previous = SettingsStoreProvider.Default;
    readonly string? oldRoot = RelicadePaths.OverrideUserDataRoot;
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "backup-cleanup-" + Guid.NewGuid().ToString("N"));
    public string Live => Path.Combine(Root, "Apps", "live");
    public string Catalog => Path.Combine(Root, "catalog");
    public string Library => Path.Combine(Catalog, "apps.json");
    public string Settings => Path.Combine(Catalog, "settings.json");
    public PortableInstallationTransaction Tx => new(Live);
    public FileSettingsStore Store { get; }
    public ManagedRemovalLibraryTests.Handler Network { get; } = new();
    public GameInfo Game { get; } = new() { Name = "Synthetic backup cleanup", Repository = "fixture/cleanup", FolderName = "live", AutoUpdate = true };
    public PortableBackupCleanupBaselineScope()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Apps"));
        foreach (var d in new[] { Catalog, Path.Combine(Catalog, "Apps"), Path.Combine(Catalog, "Cache") }) Directory.CreateDirectory(d);
        RelicadePaths.OverrideUserDataRoot = Catalog;
        File.WriteAllText(Settings, JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(Root, "Apps") }));
        Store = new FileSettingsStore(Settings); SettingsStoreProvider.Default = Store;
        SaveLibrary();
    }
    public void SaveLibrary(params GameInfo[] other) => File.WriteAllText(Library, JsonSerializer.Serialize(new { apps = new[] { Game }.Concat(other).Select(AppCatalogService.SerializeApp) }));
    public async Task Install(string version)
    {
        await Tx.InstallAsync(p => { File.WriteAllBytes(Path.Combine(p, "fixture.AppImage"), PortableInstallationFixtures.NativePayload(version)); return Task.CompletedTask; }, version);
    }
    public async Task<(int Code, string Text)> Execute(bool injected, string[] args)
    {
        var manager = injected ? new GameManager(Store, new HttpClient(Network), new AppCatalogService(dataDirectory: Catalog)) : null;
        var cli = new CLIHandler(manager) { RemovalHttpClient = new HttpClient(Network) };
        var old = Console.Out; using var text = new StringWriter();
        try { Console.SetOut(text); return (await cli.Execute(args), text.ToString()); }
        finally { Console.SetOut(old); manager?.Dispose(); }
    }
    public SortedDictionary<string, string> Snapshot() => new(Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(Root, p), p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))), StringComparer.Ordinal);
    public void Dispose()
    {
        SettingsStoreProvider.Default = previous; RelicadePaths.OverrideUserDataRoot = oldRoot; vault.Dispose();
        // Keep synthetic receipts/snapshots in the owned isolated test state for review.
    }
}
