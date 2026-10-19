using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Relicade.Core.Services;
using Relicade.Services.Mods;

namespace Relicade.Tests;

public sealed class PortableInstallationNegativeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-negative-" + Guid.NewGuid().ToString("N"));
    string Live => Path.Combine(root, "live"); string Source => Path.Combine(root, "fixture.zip");
    public PortableInstallationNegativeTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    void Zip(params string[] names)
    {
        using var f = File.Create(Source); using var z = new ZipArchive(f, ZipArchiveMode.Create);
        foreach (var name in names) { using var w = new StreamWriter(z.CreateEntry(name).Open()); w.Write("synthetic bytes: " + name); }
    }
    async Task Initial()
    {
        Zip("game.exe", "other.exe", "data.txt");
        await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v1", new() { SelectedExecutableRelativePath = "game.exe" });
    }
    static Dictionary<string, string> Snapshot(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetFileName(p) != ".relicade-mod-writer.lock").ToDictionary(p => Path.GetRelativePath(path, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    [Theory]
    [InlineData("../escape.exe")][InlineData("/absolute.exe")][InlineData("C:/drive.exe")][InlineData("dir/file:ads")]
    [InlineData("CON.txt")][InlineData("dir/../../escape")][InlineData(".relicade-portable.json")][InlineData("version.txt")]
    public async Task Hostile_archive_paths_or_reserved_metadata_never_change_live(string path)
    {
        await Initial(); var before = Snapshot(Live); Zip("game.exe", path);
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2"));
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
    }
    [Theory]
    [InlineData("duplicate")][InlineData("case")][InlineData("link")][InlineData("empty")][InlineData("missing-target")]
    public async Task Invalid_candidate_layout_preserves_exact_previous_generation(string fault)
    {
        await Initial(); var before = Snapshot(Live);
        if (fault == "link")
        {
            using var f = File.Create(Source); using var zip = new ZipArchive(f, ZipArchiveMode.Create);
            var e = zip.CreateEntry("game.exe"); e.ExternalAttributes = unchecked((int)0xA1FF0000); using var w = new StreamWriter(e.Open()); w.Write("../../outside");
        }
        else Zip(fault switch { "duplicate" => ["game.exe", "game.exe"], "case" => ["game.exe", "GAME.exe"], "empty" => [], _ => ["data.txt"] });
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2"));
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
    }
    [Theory]
    [InlineData("BeforeVersionMetadata")][InlineData("BeforePrepared")][InlineData("BeforeCommitted")]
    public async Task Actual_metadata_access_denial_keeps_evidence_and_recovery_can_be_retried(string point)
    {
        if (OperatingSystem.IsWindows()) return; await Initial(); var before = Snapshot(Live); Zip("game.exe", "changed.txt");
        string? denied = null; UnixFileMode priorMode = 0;
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2", new()
            {
                TransactionBoundary = p =>
                {
                    if (p != point) return;
                    var tx = new PortableInstallationTransaction(Live);
                    var op = Directory.GetDirectories(tx.OperationsPath).Single(d => !File.Exists(Path.Combine(d, "journal.json")) || JsonNode.Parse(File.ReadAllText(Path.Combine(d, "journal.json")))!["phase"]!.GetValue<string>() != "Committed");
                    denied = point == "BeforeVersionMetadata" ? Path.Combine(op, "candidate") : op;
                    priorMode = File.GetUnixFileMode(denied); File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                }
            }));
            Assert.NotNull(denied); Assert.True(Directory.Exists(denied));
        }
        finally { if (denied != null) File.SetUnixFileMode(denied, priorMode); }
        new PortableInstallationTransaction(Live).ReadCommitted();
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
    }
    [Theory]
    [InlineData("BeforePreviousRename")][InlineData("CandidateRenamed")][InlineData("BeforeCommitted")]
    public async Task Offline_rollback_failure_restores_current_generation_not_old_saves(string point)
    {
        await Initial(); Zip("game.exe", "changed.txt");
        await GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2"); File.Delete(Source); var before = Snapshot(Live);
        Assert.Throws<IOException>(() => new PortableInstallationTransaction(Live, new() { TransactionBoundary = p => { if (p == point) throw new IOException("Synthetic rollback failure"); } }).Rollback());
        Assert.Equal("v2", new PortableInstallationTransaction(Live).ReadCommitted()!.SelectedVersion);
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
    }
    [Fact]
    public async Task Selection_is_committed_and_cannot_target_unowned_or_nonexecutable_files()
    {
        await Initial(); var tx = new PortableInstallationTransaction(Live); var before = Snapshot(Live);
        Assert.Throws<IOException>(() => tx.SelectExecutable("data.txt")); Assert.Throws<InvalidDataException>(() => tx.SelectExecutable("../outside.exe"));
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
        tx.SelectExecutable("other.exe"); Assert.Equal("other.exe", tx.ReadCommitted()!.SelectedExecutable);
        Assert.False(File.Exists(Path.Combine(Live, "selected_executable.txt"))); Assert.Equal("v1", File.ReadAllText(Path.Combine(Live, "version.txt")));
        var oldId = tx.ReadCommitted()!.OperationId; tx.SelectExecutable("game.exe");
        Assert.Throws<IOException>(() => PortableInstallationLifetime.Begin(Live, oldId));
    }
    [Theory]
    [InlineData("new-directory")][InlineData("build-metadata")][InlineData("forged-policy")][InlineData("missing-commit")]
    public async Task Unknown_layout_or_metadata_change_blocks_readiness_and_update(string fault)
    {
        await Initial(); var manifestPath = Path.Combine(Live, PortableInstallationTransaction.ManifestFileName);
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        if (fault == "new-directory") Directory.CreateDirectory(Path.Combine(Live, "unknown-empty"));
        if (fault == "build-metadata") File.WriteAllText(Path.Combine(Live, "installed-build.txt"), "unknown identity");
        if (fault == "forged-policy") { manifest["dataPaths"] = new JsonArray("data.txt"); File.WriteAllText(manifestPath, manifest.ToJsonString()); }
        if (fault == "missing-commit") File.Delete(Path.Combine(new PortableInstallationTransaction(Live).OperationsPath, manifest["operationId"]!.GetValue<string>(), "journal.json"));
        var before = Snapshot(Live);
        var error = Record.Exception(() => new PortableInstallationTransaction(Live).ReadCommitted()); Assert.NotNull(error);
        var updateError = await Record.ExceptionAsync(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2")); Assert.NotNull(updateError);
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
    }
    [Fact]
    public async Task Mod_writer_held_before_game_update_excludes_game_without_changing_siblings()
    {
        await Initial(); var before = Snapshot(Live);
        using (ModInstallTransaction.Acquire(Live))
            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2"));
        Assert.Equal(before.OrderBy(e => e.Key), Snapshot(Live).OrderBy(e => e.Key));
    }
    [Fact]
    public async Task Writer_in_the_two_rename_gap_is_retained_as_unknown_not_deleted()
    {
        await Initial(); Zip("game.exe", "changed.txt");
        var ex = await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(Source, Live, "fixture.zip", "v2", new() { TransactionBoundary = p =>
        {
            if (p != "PreviousRenamed") return;
            using (ModInstallTransaction.Acquire(Live)) File.WriteAllText(Path.Combine(Live, "external-mod.txt"), "external synthetic mod");
        } }));
        Assert.Contains("recovery is needed", ex.Message); Assert.Equal("external synthetic mod", File.ReadAllText(Path.Combine(Live, "external-mod.txt")));
        Assert.Throws<IOException>(() => new PortableInstallationTransaction(Live).ReadCommitted());
        Assert.Equal(2, Directory.GetFiles(new PortableInstallationTransaction(Live).OperationsPath, "game.exe", SearchOption.AllDirectories).Select(Path.GetDirectoryName).Count(p => p!.EndsWith("previous") || p.EndsWith("candidate")));
    }
}
