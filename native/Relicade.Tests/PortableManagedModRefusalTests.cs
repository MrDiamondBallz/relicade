using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Relicade.Core.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class PortableManagedModRefusalTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-managed-mod-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "live");
    public PortableManagedModRefusalTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    static async Task Install(string root, string version)
    {
        var archive = Path.Combine(root, "fixture.zip");
        using (var file = File.Create(archive))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("game.exe").Open()))
            writer.Write("NONEXECUTABLE synthetic payload " + version);
        await GameInstallationService.InstallOrUpdateGameAsync(archive, Path.Combine(root, "live"), "fixture.zip", version,
            new() { PreservedDataPaths = ["saves"] });
    }
    async Task Initial(string state)
    {
        await Install(root, "old");
        Directory.CreateDirectory(Path.Combine(Live, "saves"));
        File.WriteAllText(Path.Combine(Live, "saves", "slot.dat"), "current save");
        File.WriteAllText(Path.Combine(root, "sibling.txt"), "unchanged sibling");
        if (state != "fresh") await Install(root, "new");
        if (state == "rolled-back") new PortableInstallationTransaction(Live).Rollback();
    }
    static string[] Snapshot(string target) => Directory.EnumerateFileSystemEntries(target, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetFileName(p) is not ("writer.lock" or ".relicade-mod-writer.lock"))
        .Select(p => Path.GetRelativePath(target, p) + (Directory.Exists(p) ? "/" : ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))))
        .OrderBy(p => p, StringComparer.Ordinal).ToArray();

    static async Task Refuse(string target, string operation, string failure = "none")
    {
        var live = Path.Combine(target, "live");
        var tx = new PortableInstallationTransaction(live);
        var committed = tx.ReadCommitted()!;
        var before = Snapshot(target);
        var provider = new RecordingProvider(failure);
        var package = ModsPreservationTests.Package();
        var service = new ModInstallService(new([provider]));
        int boundaries = 0;
        service.TransactionBoundary = _ => boundaries++;
        var error = await Record.ExceptionAsync(async () =>
        {
            switch (operation)
            {
                case "save": new InstalledModsStore().Save(live, new()); break;
                case "load": new InstalledModsStore().Load(live); break;
                case "install": await service.InstallAsync(live, "mods", package, provider, package.DownloadFiles[0]); break;
                case "dependencies": await service.InstallWithDependenciesAsync(live, "mods", package, [package], provider); break;
                case "files": await service.InstallSelectedFilesAsync(live, "mods", package, [package], provider, package.DownloadFiles); break;
                case "uninstall": service.Uninstall(live, "mods", package.ProviderId, package.Id); break;
                case "matching": service.UninstallMatching(live, "mods", package); break;
                case "file": service.UninstallMatchingFile(live, "mods", package, package.DownloadFiles[0].Id); break;
                case "record": service.UninstallMatchingRecord(live, "mods", package, new()); break;
                default: throw new InvalidOperationException(operation);
            }
        });
        var refusal = Assert.IsType<IOException>(error);
        Assert.Contains("not supported", refusal.Message);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, boundaries);
        Assert.False(File.Exists(InstalledModsStore.GetSidecarPath(live)));
        Assert.False(Directory.Exists(Path.Combine(live, ".relicade-mod-staging")));
        Assert.False(Directory.Exists(Path.Combine(live, ".relicade-mod-transactions")));
        Assert.False(Directory.Exists(Path.Combine(live, "mods")));
        Assert.Equal(before, Snapshot(target));
        Assert.Equal(committed.OperationId, new PortableInstallationTransaction(live).ReadCommitted()!.OperationId);
        Assert.Equal("current save", File.ReadAllText(Path.Combine(live, "saves", "slot.dat")));
        Assert.Equal("unchanged sibling", File.ReadAllText(Path.Combine(target, "sibling.txt")));
        Console.WriteLine("Refused " + operation + " before provider/staging/commit; exact managed payload, data and recovery evidence unchanged.");
    }

    [Theory]
    [InlineData("fresh")][InlineData("updated")][InlineData("rolled-back")]
    public async Task Unsupported_mod_entrypoints_refuse_healthy_committed_generations_without_any_mutation(string state)
    {
        await Initial(state);
        foreach (var operation in new[] { "save", "load", "install", "dependencies", "files", "uninstall", "matching", "file", "record" })
            await Refuse(root, operation);
        // A refusal must neither poison the generation nor leave either writer lock held.
        await Install(root, "next");
        Assert.Equal("next", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        Assert.Equal("current save", File.ReadAllText(Path.Combine(Live, "saves", "slot.dat")));
    }

    [Theory]
    [InlineData("download")][InlineData("parse")][InlineData("cancel")]
    public async Task Unsupported_managed_install_refuses_even_before_a_provider_failure_or_staging(string failure)
    {
        await Initial("fresh");
        await Refuse(root, "install", failure);
    }

    [Fact]
    public async Task Independent_mod_process_refuses_healthy_game_then_independent_reopen_and_update_work()
    {
        await Initial("updated");
        var before = Snapshot(root);
        await Child("refuse");
        Assert.Equal(before, Snapshot(root));
        await Child("reopen");
        await Install(root, "next");
        Assert.Equal("next", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
    }

    async Task Child(string mode)
    {
        var exe = Environment.ProcessPath!;
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(PortableManagedModRefusalTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.PortableManagedModRefusalTests.Process_control_entry" }) start.ArgumentList.Add(arg);
        start.Environment["RELICADE_MANAGED_MOD_ROOT"] = root;
        start.Environment["RELICADE_MANAGED_MOD_MODE"] = mode;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await output + await error);
            Console.WriteLine(await output);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task Process_control_entry()
    {
        var target = Environment.GetEnvironmentVariable("RELICADE_MANAGED_MOD_ROOT");
        if (target == null) return;
        if (Environment.GetEnvironmentVariable("RELICADE_MANAGED_MOD_MODE") == "refuse")
            foreach (var operation in new[] { "save", "load", "install", "dependencies", "files", "uninstall", "matching", "file", "record" })
                await Refuse(target, operation);
        else
        {
            var live = Path.Combine(target, "live");
            Assert.Equal("new", new PortableInstallationTransaction(live).ReadCommitted()!.SelectedVersion);
            Assert.Equal("NONEXECUTABLE synthetic payload new", File.ReadAllText(Path.Combine(live, "game.exe")));
            Assert.Equal("current save", File.ReadAllText(Path.Combine(live, "saves", "slot.dat")));
            Assert.Equal("unchanged sibling", File.ReadAllText(Path.Combine(target, "sibling.txt")));
            Console.WriteLine("Separate reopen validates unchanged complete managed generation and sentinels.");
        }
    }

    sealed class RecordingProvider(string failure) : IModProvider
    {
        readonly ModsPreservationTests.Provider inner = new() { Failure = failure };
        public int Calls;
        public string Id => inner.Id;
        public string DisplayName => inner.DisplayName;
        public bool SupportsPagedListing => inner.SupportsPagedListing;
        public bool SupportsRemoteSearch => inner.SupportsRemoteSearch;
        public bool TryParseSource(string url, out ModSourceRef source) => inner.TryParseSource(url, out source);
        public void ForceRefreshOnNextList() { Calls++; inner.ForceRefreshOnNextList(); }
        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef s, ModListOptions? o = null, CancellationToken cancellationToken = default)
        { Calls++; return inner.ListPackagesAsync(s, o, cancellationToken); }
        public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef s, string? page, int size, ModListOptions? o = null, CancellationToken cancellationToken = default)
        { Calls++; return inner.ListPackagesPageAsync(s, page, size, o, cancellationToken); }
        public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef s, string query, string? page, int size, ModListOptions? o = null, CancellationToken cancellationToken = default)
        { Calls++; return inner.SearchPackagesPageAsync(s, query, page, size, o, cancellationToken); }
        public Task<Stream> DownloadAsync(ModPackageVersion v, IProgress<double>? p = null, CancellationToken cancellationToken = default)
        { Calls++; return inner.DownloadAsync(v, p, cancellationToken); }
        public IReadOnlySet<string> GetArchiveMetadataFileNames() { Calls++; return inner.GetArchiveMetadataFileNames(); }
        public Task<string?> GetReadmeAsync(ModPackage p, CancellationToken c = default) { Calls++; return inner.GetReadmeAsync(p, c); }
        public Task<string?> GetChangelogAsync(ModPackage p, CancellationToken c = default) { Calls++; return inner.GetChangelogAsync(p, c); }
    }
}
