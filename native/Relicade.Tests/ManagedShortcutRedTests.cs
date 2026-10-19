using System.Reflection;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

// These exact behavior tests compile and run against the pre-route product.
public sealed class ManagedShortcutRedTests : IDisposable
{
    readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "managed-shortcut-red-" + Guid.NewGuid().ToString("N"));
    public ManagedShortcutRedTests() => Directory.CreateDirectory(root);
    public void Dispose() { Directory.Delete(root, true); if (Directory.Exists(root + "-launcher")) Directory.Delete(root + "-launcher", true); }
    [Fact]
    public async Task Managed_generation_prepares_launcher_route_not_direct_payload()
    {
        var source = Path.Combine(root, "fixture.AppImage"); File.WriteAllBytes(source, ManagedShortcutLifetimeTests.NativeShape());
        await GameInstallationService.InstallOrUpdateGameAsync(source, Path.Combine(root, "tile"), "fixture.AppImage", "v1");
        var launcher = Path.Combine(Directory.CreateDirectory(root + "-launcher").FullName, "Relicade.Desktop"); File.WriteAllText(launcher, "OWN fixture launcher; never executed");
        var game = new GameInfo { Name = "same name", FolderName = "tile", Repository = "fixture/same" };
        var seam = typeof(GameShortcutLaunch).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).SingleOrDefault(m => m.Name == "PrepareAsync" && m.GetParameters().Length == 6);
        var result = seam == null ? await GameShortcutLaunch.PrepareAsync(game, root, new()) :
            await (Task<GameShortcutTarget?>)seam.Invoke(null, [game, root, new AppSettings(), null, CancellationToken.None, (Func<string?>)(() => launcher)])!;
        Assert.NotNull(result); Assert.Equal(launcher, result.FileName);
        Assert.Equal(new[] { "--launch-instance", game.InstanceKey }, result.Arguments);
        Assert.False(File.Exists(Path.Combine(root, "tile", "selected_executable.txt")));
    }
    [Fact]
    public async Task Exact_instance_CLI_dispatches_offline_and_propagates_observed_lifetime()
    {
        var game = new GameInfo { Name = "same name", FolderName = "tile", Repository = "fixture/same" };
        var source = Path.Combine(root, "fixture.AppImage"); File.WriteAllBytes(source, ManagedShortcutLifetimeTests.NativeShape());
        await GameInstallationService.InstallOrUpdateGameAsync(source, Path.Combine(root, "tile"), "fixture.AppImage", "v1");
        var catalog = new AppCatalogService(dataDirectory: root);
        await catalog.SaveLocalAppsAsync([game]);
        var store = new Store(root);
        using var manager = new GameManager(store, new HttpClient(new Reject()), catalog);
        var dispatched = false;
        Func<GameInfo, string, Task<(bool Launched, Task? Lifetime)>> launch = (selected, operation) =>
        { dispatched = true; Assert.Equal(game.InstanceKey, selected.InstanceKey); Assert.False(string.IsNullOrEmpty(operation)); return Task.FromResult<(bool, Task?)>((true, Task.CompletedTask)); };
        var ctor = typeof(CLIHandler).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(GameManager), typeof(Func<GameInfo, string, Task<(bool, Task?)>>)], null);
        var cli = ctor == null ? new CLIHandler(manager) : (CLIHandler)ctor.Invoke([manager, launch]);
        Assert.Equal(0, await cli.Execute(["--launch-instance", game.InstanceKey])); Assert.True(dispatched);
        Assert.Equal(0, store.Saves);
    }
    sealed class Reject : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("No application request is permitted");
    }
    sealed class Store(string root) : ISettingsStore
    {
        public int Saves; public AppSettings Current { get; } = new() { AppsPath = root };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { Saves++; throw new Exception("No save permitted"); }
    }
}
