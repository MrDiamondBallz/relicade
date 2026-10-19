using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class ManagedRemovalAncestorOverlapTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("library", false)][InlineData("library", true)]
    [InlineData("cli", false)][InlineData("cli", true)]
    [InlineData("cli-injected", false)][InlineData("cli-injected", true)]
    public async Task Overlapping_unmanaged_default_and_located_authorities_share_exclusion_without_reacquisition(string route, bool throwing)
    {
        using var f = new ManagedRemovalFixture();
        var child = Path.Combine(f.Live, "ordinary-child"); Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "fixture.exe"), "NONEXECUTABLE synthetic");
        File.WriteAllText(Path.Combine(child, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        File.WriteAllText(Path.Combine(child, "version.txt"), "old");
        File.WriteAllText(Path.Combine(child, "LastPlayed.txt"), "synthetic play data");
        File.WriteAllText(Path.Combine(f.Live, "unrelated"), "parent sibling keep");
        using (new PortableInstallationTransaction(child).AcquireLease()) { }
        var calls = new List<string>();
        void Mutate(string path)
        {
            calls.Add(path);
            Assert.ThrowsAny<IOException>(() => { using var outer = f.Tx.AcquireLease(); });
            Assert.ThrowsAny<IOException>(() => { using var inner = new PortableInstallationTransaction(child).AcquireLease(); });
            if (throwing) throw new IOException("synthetic overlapping callback failure");
            File.Move(path, Path.Combine(f.Root, "benign-trash"));
        }
        if (route == "library")
        {
            await using var h = new ManagedRemovalLibraryTests.Harness(f, true);
            h.Game.InstallPath = child; h.Mutation = Mutate; await h.Save();
            var before = f.Snapshot(); await h.Remove();
            Assert.Equal(new[] { Path.Combine(child, "fixture.exe") }, calls);
            if (throwing) { Assert.Single(h.Errors); Assert.Equal(child, h.Game.InstallPath); Assert.Equal(before, f.Snapshot()); }
            else { Assert.Empty(h.Errors); Assert.Null(h.Game.InstallPath); }
            Assert.Equal(0, h.Network.Calls);
        }
        else
        {
            using var vault = new CredentialVaultTestScope();
            var priorRoot = RelicadePaths.OverrideUserDataRoot; var priorStore = SettingsStoreProvider.Default;
            var catalog = Path.Combine(f.Root, "catalog");
            Directory.CreateDirectory(Path.Combine(catalog, "Apps")); Directory.CreateDirectory(Path.Combine(catalog, "Cache"));
            RelicadePaths.OverrideUserDataRoot = catalog;
            var store = new FileSettingsStore(Path.Combine(catalog, "settings.json"));
            File.WriteAllText(Path.Combine(catalog, "settings.json"), JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(f.Root, "Apps") }));
            SettingsStoreProvider.Default = store;
            var game = new GameInfo { Name = "Synthetic overlap", Repository = "fixture/overlap", FolderName = "live", InstallPath = child };
            File.WriteAllText(Path.Combine(catalog, "apps.json"), JsonSerializer.Serialize(new { apps = new[] { AppCatalogService.SerializeApp(game) } }));
            var network = new ManagedRemovalLibraryTests.Handler();
            using var manager = route == "cli-injected" ? new GameManager(store, new HttpClient(network), new AppCatalogService(dataDirectory: catalog)) : null;
            var cli = new CLIHandler(manager, moveToTrash: Mutate) { RemovalHttpClient = new HttpClient(network) };
            var before = f.Snapshot(); var old = Console.Out; using var text = new StringWriter(); int code;
            try { Console.SetOut(text); code = await cli.Execute(["--uninstall", game.Name]); }
            finally { Console.SetOut(old); SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot; }
            output.WriteLine($"route={route}; callback_throw={throwing}; code={code}; HTTP={network.Calls}; stdout={text}");
            Assert.Equal(new[] { Path.Combine(child, "fixture.exe") }, calls);
            Assert.Equal(throwing ? 1 : 0, code); Assert.Equal(0, network.Calls);
            if (throwing) Assert.Equal(before, f.Snapshot());
        }
        Assert.Equal("parent sibling keep", File.ReadAllText(Path.Combine(f.Live, "unrelated")));
        using var releasedParent = f.Tx.AcquireLease();
        using var releasedChild = new PortableInstallationTransaction(child).AcquireLease();
    }
}
