using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Relicade.Services.Mods;
using Relicade.Services;
using Relicade.Models;
using Relicade.ViewModels;
using static Relicade.Tests.ModsPreservationTests;

namespace Relicade.Tests;

public sealed class ModsPreservationRecoveryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "mods-recovery-" + Guid.NewGuid().ToString("N"));
    public ModsPreservationRecoveryTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    string Sidecar => InstalledModsStore.GetSidecarPath(root);
    string Payload => Path.Combine(root, "mods", "payload.txt");
    string JournalPath => Assert.Single(Directory.GetFiles(Path.Combine(root, ModInstallTransaction.DirectoryName), "journal.json", SearchOption.AllDirectories));
    async Task<(Provider Provider, ModInstallService Installer)> Initial()
    {
        var provider = new Provider(); var service = new ModInstallService(new([provider]));
        await service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]);
        provider.Bytes = Zip(content: "new");
        return (provider, service);
    }
    [Theory]
    [InlineData("Prepared")]
    [InlineData("Applying")]
    [InlineData("BeforePayloadActivate:mods/payload.txt")]
    [InlineData("Payload:mods/payload.txt")]
    [InlineData("BeforeManifest")]
    public async Task Activation_fault_restores_old_bytes_and_exact_manifest(string point)
    {
        var (provider, service) = await Initial(); var before = File.ReadAllBytes(Sidecar);
        service.TransactionBoundary = p => { if (p == point) throw new IOException("Controlled activation failure"); };
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        Assert.Equal("old", File.ReadAllText(Payload)); Assert.Equal(before, File.ReadAllBytes(Sidecar));
        Assert.Single(new InstalledModsStore().Load(root).Mods);
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, ModInstallTransaction.DirectoryName)));
    }
    [Theory]
    [InlineData("Manifest")]
    [InlineData("Committed")]
    [InlineData("Retired")]
    public async Task Postcommit_fault_reports_committed_state_not_rollback(string point)
    {
        var (provider, service) = await Initial();
        service.TransactionBoundary = p => { if (p == point) throw new IOException("Controlled postcommit failure"); };
        var ex = await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        Assert.Contains("committed", ex.Message); Assert.Equal("new", File.ReadAllText(Payload));
        Assert.Single(new InstalledModsStore().Load(root).Mods);
    }
    [Fact]
    public async Task Failed_rollback_is_explicit_and_reopen_retries_recovery()
    {
        var (provider, service) = await Initial(); var before = File.ReadAllBytes(Sidecar);
        service.TransactionBoundary = p => { if (p is "BeforeManifest" or "Recovering") throw new IOException("Controlled rollback failure"); };
        var ex = await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        Assert.Contains("recovery is incomplete", ex.Message); Assert.True(File.Exists(JournalPath));
        Assert.Equal("new", File.ReadAllText(Payload));
        Assert.Single(new InstalledModsStore().Load(root).Mods);
        Assert.Equal("old", File.ReadAllText(Payload)); Assert.Equal(before, File.ReadAllBytes(Sidecar));
    }
    [Fact]
    public async Task Actual_denied_manifest_save_preserves_old_generation()
    {
        if (OperatingSystem.IsWindows()) return;
        var (provider, service) = await Initial(); var before = File.ReadAllBytes(Sidecar);
        var mode = File.GetUnixFileMode(root);
        service.TransactionBoundary = p => { if (p == "BeforeManifest") File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute); };
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
            Assert.Equal(before, File.ReadAllBytes(Sidecar));
        }
        finally { File.SetUnixFileMode(root, mode); }
        Assert.Single(new InstalledModsStore().Load(root).Mods);
        Assert.Equal("old", File.ReadAllText(Payload)); Assert.Equal(before, File.ReadAllBytes(Sidecar));
    }
    [Theory]
    [InlineData("external")]
    [InlineData("corrupt")]
    [InlineData("escape")]
    [InlineData("bad-backup")]
    [InlineData("missing-payloads")]
    public async Task Unknown_or_invalid_recovery_evidence_fails_closed_and_is_retained(string fault)
    {
        var (provider, service) = await Initial();
        service.TransactionBoundary = p => { if (p is "BeforeManifest" or "Recovering") throw new IOException("Retain prepared recovery"); };
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        var path = JournalPath; var json = JsonNode.Parse(File.ReadAllText(path))!;
        switch (fault)
        {
            case "external": File.WriteAllText(Payload, "external-edits"); break;
            case "corrupt": File.WriteAllText(path, "{broken"); break;
            case "escape": json["payloads"]![0]!["path"] = "../outside"; File.WriteAllText(path, json.ToJsonString()); break;
            case "bad-backup": File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "backup", "0"), "damaged"); break;
            case "missing-payloads": json["payloads"] = new JsonArray(); File.WriteAllText(path, json.ToJsonString()); break;
        }
        var manifest = File.ReadAllBytes(Sidecar); var bytes = File.ReadAllBytes(Payload); var journal = File.ReadAllBytes(path);
        var readError = Xunit.Record.Exception(() => new InstalledModsStore().Load(root));
        var saveError = Xunit.Record.Exception(() => new InstalledModsStore().Save(root, new()));
        if (fault == "external") { Assert.IsType<IOException>(readError); Assert.IsType<IOException>(saveError); }
        else { Assert.IsType<InvalidDataException>(readError); Assert.IsType<InvalidDataException>(saveError); }
        Assert.Equal(manifest, File.ReadAllBytes(Sidecar)); Assert.Equal(bytes, File.ReadAllBytes(Payload)); Assert.Equal(journal, File.ReadAllBytes(path));
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/drive")]
    [InlineData("nested/../../escape")]
    [InlineData("CON.txt")]
    [InlineData("nested/file:stream")]
    public async Task Invalid_archive_paths_never_change_live_payload(string path)
    {
        var (provider, service) = await Initial(); var before = File.ReadAllBytes(Sidecar);
        provider.Bytes = Zip(path, "hostile");
        await Assert.ThrowsAnyAsync<Exception>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        Assert.Equal("old", File.ReadAllText(Payload)); Assert.Equal(before, File.ReadAllBytes(Sidecar));
    }
    [Fact]
    public async Task Preexisting_link_is_rejected_before_activation()
    {
        if (OperatingSystem.IsWindows()) return;
        var (provider, service) = await Initial(); var before = File.ReadAllBytes(Sidecar);
        var outside = Path.Combine(root, "unrelated"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "safe.txt"), "safe");
        Directory.CreateSymbolicLink(Path.Combine(root, "mods", "linked"), outside);
        provider.Bytes = Zip("linked/safe.txt", "bad");
        await Assert.ThrowsAnyAsync<Exception>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        Assert.Equal("safe", File.ReadAllText(Path.Combine(outside, "safe.txt"))); Assert.Equal(before, File.ReadAllBytes(Sidecar));
    }
    [Theory]
    [InlineData("Staged", "old")]
    [InlineData("Applying", "new")]
    public async Task Cancellation_before_commit_preserves_old_and_during_commit_finishes_safely(string point, string expected)
    {
        var (provider, service) = await Initial(); using var cts = new CancellationTokenSource();
        service.TransactionBoundary = p => { if (p == point) cts.Cancel(); };
        var task = service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0], cancellationToken: cts.Token);
        if (point == "Staged") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); else await task;
        Assert.Equal(expected, File.ReadAllText(Payload)); Assert.Single(new InstalledModsStore().Load(root).Mods);
    }
    [Fact]
    public async Task Concurrent_writers_reread_manifest_and_preserve_siblings()
    {
        var provider = new Provider(); var first = Package(); var second = Package("sibling");
        provider.Bytes = Zip("first.txt");
        await new ModInstallService(new([provider])).InstallAsync(root, "mods", first, provider, first.DownloadFiles[0]);
        var sibling = new Provider { Bytes = Zip("second.txt") };
        await Task.WhenAll(Task.Run(() => new ModInstallService(new([provider])).InstallAsync(root, "mods", first, provider, first.DownloadFiles[0])),
            Task.Run(() => new ModInstallService(new([sibling])).InstallAsync(root, "mods", second, sibling, second.DownloadFiles[0])));
        Assert.Equal(2, new InstalledModsStore().Load(root).Mods.Count);
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "first.txt"))); Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "second.txt")));
    }
    static Process Child(string target, string point)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(typeof(ModsPreservationRecoveryTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ModsPreservationRecoveryTests.Transaction_child_control" }) psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_MOD_TEST_ROOT"] = target; psi.Environment["RELICADE_MOD_TEST_POINT"] = point;
        return Process.Start(psi)!;
    }
    [Fact]
    public async Task Transaction_child_control()
    {
        // This method is a subprocess control entry, not an independent platform acceptance test.
        var target = Environment.GetEnvironmentVariable("RELICADE_MOD_TEST_ROOT");
        if (target == null) return;
        var point = Environment.GetEnvironmentVariable("RELICADE_MOD_TEST_POINT");
        var archive = Path.Combine(target, "candidate.zip");
        var provider = new Provider { Bytes = File.Exists(archive) ? File.ReadAllBytes(archive) : Zip(content: "new") }; var service = new ModInstallService(new([provider]));
        service.TransactionBoundary = p =>
        {
            if (p != point) return;
            File.WriteAllText(Path.Combine(target, "reached.txt"), p);
            using var wait = new ManualResetEventSlim(); wait.Wait();
        };
        await service.InstallAsync(target, "mods", Package(), provider, Package().DownloadFiles[0]);
    }
    [Theory]
    [InlineData("Staged", "old")]
    [InlineData("Prepared", "old")]
    [InlineData("Applying", "old")]
    [InlineData("BeforePayloadActivate:mods/payload.txt", "old")]
    [InlineData("Payload:mods/payload.txt", "old")]
    [InlineData("BeforeManifest", "old")]
    [InlineData("Manifest", "new")]
    [InlineData("Committed", "new")]
    [InlineData("Retired", "new")]
    public async Task Killed_writer_recovers_coherent_generation_on_independent_reopen(string point, string expected)
    {
        await Initial(); var before = File.ReadAllBytes(Sidecar);
        using var child = Child(root, point);
        try
        {
            await Reached(child);
            Assert.Throws<IOException>(() => new InstalledModsStore().Load(root)); // second process writer lock
            var provider = new Provider();
            await Assert.ThrowsAsync<IOException>(() => new ModInstallService(new([provider])).InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync();
            Assert.Single(new InstalledModsStore().Load(root).Mods);
            Assert.Equal(expected, File.ReadAllText(Payload));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "mods"), "*", SearchOption.AllDirectories));
            if (expected == "old") Assert.Equal(before, File.ReadAllBytes(Sidecar));
            Console.WriteLine($"Killed at {point}; exit={child.ExitCode}; reopen={expected}; prior manifest retained={expected == "old"}");
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    async Task Reached(Process child)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "reached.txt")) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(25);
        if (child.HasExited) throw new Exception("Child exited early: " + await child.StandardOutput.ReadToEndAsync() + await child.StandardError.ReadToEndAsync());
        Assert.True(File.Exists(Path.Combine(root, "reached.txt")), "Child did not reach controlled transaction boundary");
    }
    static byte[] MultiZip(params (string Name, string Contents)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var file in files)
                using (var writer = new StreamWriter(archive.CreateEntry(file.Name).Open())) writer.Write(file.Contents);
        return buffer.ToArray();
    }
    [Theory]
    [InlineData("Payload:mods/payload.txt", false)]
    [InlineData("Payload:mods/obsolete.txt", false)]
    [InlineData("Payload:mods/new/added.txt", false)]
    [InlineData("Manifest", true)]
    public async Task Killed_multi_payload_replacement_never_exposes_a_mixed_manifest(string point, bool committed)
    {
        var provider = new Provider { Bytes = MultiZip(("payload.txt", "old"), ("obsolete.txt", "obsolete")) };
        var service = new ModInstallService(new([provider]));
        await service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]);
        var before = File.ReadAllBytes(Sidecar);
        File.WriteAllText(Path.Combine(root, "untouched.txt"), "unrelated");
        File.WriteAllBytes(Path.Combine(root, "candidate.zip"), MultiZip(("payload.txt", "new"), ("new/added.txt", "added")));
        using var child = Child(root, point);
        try
        {
            await Reached(child); child.Kill(true); await child.WaitForExitAsync();
            var doc = new InstalledModsStore().Load(root);
            Assert.Equal(committed ? "new" : "old", File.ReadAllText(Payload));
            Assert.Equal(!committed, File.Exists(Path.Combine(root, "mods", "obsolete.txt")));
            Assert.Equal(committed, File.Exists(Path.Combine(root, "mods", "new", "added.txt")));
            Assert.Equal(committed ? new[] { "payload.txt", "new/added.txt" } : new[] { "payload.txt", "obsolete.txt" }, Assert.Single(doc.Mods).Files);
            if (!committed) Assert.Equal(before, File.ReadAllBytes(Sidecar));
            Assert.Equal("unrelated", File.ReadAllText(Path.Combine(root, "untouched.txt")));
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task Empty_or_linked_archives_are_rejected_without_replacing_old_mod()
    {
        var (provider, service) = await Initial(); var before = File.ReadAllBytes(Sidecar);
        provider.Bytes = MultiZip();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("linked.txt"); entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open()); writer.Write("../unrelated");
        }
        provider.Bytes = buffer.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(root, "mods", Package(), provider, Package().DownloadFiles[0]));
        Assert.Equal("old", File.ReadAllText(Payload)); Assert.Equal(before, File.ReadAllBytes(Sidecar));
    }
    [Fact]
    public void Legacy_filename_uninstall_uses_the_selected_stored_record_not_first_null_id()
    {
        var package = Package(); Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root,"mods","one.txt"),"one"); File.WriteAllText(Path.Combine(root,"mods","two.txt"),"two");
        var one = Record(package); one.DownloadFileName = "one.zip"; one.Files = ["one.txt"];
        var two = Record(package); two.DownloadFileName = "two.zip"; two.Files = ["two.txt"];
        new InstalledModsStore().Save(root, new() { Mods = [one,two] });
        var service = new ModInstallService(new([]));
        Assert.True(service.UninstallMatchingRecord(root, "mods", package, two));
        Assert.Equal("one.zip", Assert.Single(service.LoadInstalled(root).Mods).DownloadFileName);
        Assert.True(File.Exists(Path.Combine(root,"mods","one.txt"))); Assert.False(File.Exists(Path.Combine(root,"mods","two.txt")));
    }
    [Fact]
    public async Task Mixed_sources_keep_usable_rows_and_per_source_retry_error()
    {
        var provider = new MultiSourceProvider(); var loader = new ModCatalogLoader(new([provider]));
        var session = await loader.LoadBrowseSessionAsync([new() { Provider = "mixed", SourceUrl = "good" }, new() { Provider = "mixed", SourceUrl = "bad" }], null);
        Assert.Single(session.Packages); Assert.True(session.HasErrors); Assert.True(session.CanLoadMore);
        Assert.Equal("bad", Assert.Single(session.SourceStates.Where(s => s.Error != null)).Error!.SourceKey);
        provider.Repaired = true;
        var retry = await loader.LoadMoreBrowseSessionAsync(session);
        Assert.Equal(2, retry.Packages.Count); Assert.False(retry.HasErrors); Assert.False(retry.CanLoadMore);
    }
    sealed class MultiSourceProvider : IModProvider
    {
        public bool Repaired; public string Id => "mixed"; public string DisplayName => Id; public bool SupportsPagedListing => true; public bool SupportsRemoteSearch => false;
        public bool TryParseSource(string u, out ModSourceRef s) { s = new() { ProviderId = Id, SourceKey = u, SourceUrl = u, DisplayLabel = u }; return true; }
        public void ForceRefreshOnNextList() { }
        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef s, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef s, string? t, int z, ModListOptions? o = null, CancellationToken c = default)
        {
            if (s.SourceKey == "bad" && !Repaired) throw new IOException("Controlled source outage");
            return Task.FromResult(new ModPackagePage { Packages = [new() { ProviderId = Id, SourceKey = s.SourceKey, Id = "same-id", FullName = "same-name", Owner = "same", Name = "same" }] });
        }
        public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef s, string q, string? t, int z, ModListOptions? o = null, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(ModPackageVersion v, IProgress<double>? p = null, CancellationToken c = default) => throw new NotSupportedException();
        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();
        public Task<string?> GetReadmeAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
        public Task<string?> GetChangelogAsync(ModPackage p, CancellationToken c = default) => Task.FromResult<string?>(null);
    }
}
