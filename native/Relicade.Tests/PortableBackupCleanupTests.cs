using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Relicade.Core.Services;

namespace Relicade.Tests;

internal sealed class PortableBackupCleanupFixture
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "bc-core-" + Guid.NewGuid().ToString("N"));
    internal string Live => Path.Combine(Root, "live");
    internal PortableInstallationTransaction Tx(Action<string>? boundary = null) => new(Live, Options(boundary));
    internal static GameInstallationOptions Options(Action<string>? boundary = null) => new()
    {
        PreservedDataPaths = ["saves", "config", "owneddata", "LastPlayed.txt"], TransactionBoundary = boundary,
        SelectedExecutableRelativePath = "fixture.AppImage", InstalledBuildText = "synthetic-build-evidence"
    };
    internal async Task Install(string version)
    {
        Directory.CreateDirectory(Root);
        await Tx().InstallAsync(p =>
        {
            foreach (var name in new[] { "fixture.AppImage", "other.AppImage" }) File.WriteAllBytes(Path.Combine(p, name), PortableInstallationFixtures.NativePayload(version + name));
            Directory.CreateDirectory(Path.Combine(p, "assets")); File.WriteAllText(Path.Combine(p, "assets", "immutable.bin"), "synthetic-" + version);
            return Task.CompletedTask;
        }, version);
    }
    internal void Save(string text)
    {
        foreach (var p in new[] { "saves/nested/slot.sav", "config/options.ini", "owneddata/original.bin", "LastPlayed.txt" })
        {
            var path = Path.Combine(Live, p); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text + p);
        }
    }
    internal async Task Generations(int count = 4)
    {
        await Install("v1"); Save("save-one");
        for (var i = 2; i <= count; i++) { await Install("v" + i); Save("save-" + i); }
    }
    internal static SortedDictionary<string, string> Snapshot(string root) => new(Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => !Path.GetRelativePath(root, p).StartsWith("coord/", StringComparison.Ordinal))
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))), StringComparer.Ordinal);
    internal void OnlyApprovedRemoved(SortedDictionary<string, string> before, BackupCleanupPreview plan)
    {
        var after = Snapshot(Root); var paths = plan.Files.Select(f => Path.GetRelativePath(Root, Path.Combine(Tx().OperationsPath, f.OperationId, f.BackupRelativePath))).ToHashSet();
        foreach (var row in before)
            if (paths.Contains(row.Key)) Assert.False(after.ContainsKey(row.Key), row.Key);
            else Assert.Equal(row.Value, after[row.Key]);
        Assert.All(after.Keys.Except(before.Keys), p => Assert.Equal(Path.GetRelativePath(Root, Path.Combine(Tx().OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json")), p));
    }
}

public sealed class PortableBackupCleanupTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Four_generations_preserve_every_snapshot_and_future_production_operations()
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations();
        var tx = f.Tx(); var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        var directories = Directory.GetDirectories(f.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var plan = tx.PreviewRedundantBackups(); Assert.Equal(6, plan.Files.Count); Assert.Equal(plan.PlanSha256, tx.PreviewRedundantBackups().PlanSha256);
        Assert.DoesNotContain(plan.Files, r => r.OperationId == tx.ReadCommitted()!.OperationId || r.BackupRelativePath.Contains("saves") || r.BackupRelativePath.EndsWith("LastPlayed.txt"));
        var result = tx.CleanupRedundantBackups(plan.PlanSha256);
        Assert.True(result.Completed); Assert.Equal(plan.Files.Count, result.DeletedFiles); Assert.Equal(plan.ReclaimableBytes, result.ReclaimedBytes);
        f.OnlyApprovedRemoved(before, plan); Assert.Equal(directories, Directory.GetDirectories(f.Root, "*", SearchOption.AllDirectories).Order().ToArray());
        var receipt = Path.Combine(tx.OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json");
        var approvalBytes = File.ReadAllBytes(receipt);
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, true), tx.CleanupRedundantBackups(plan.PlanSha256)); Assert.Equal(approvalBytes, File.ReadAllBytes(receipt));
        Assert.Empty(tx.PreviewRedundantBackups().Files);
        var metadataOnlyBefore = PortableBackupCleanupFixture.Snapshot(f.Root);
        var metadataOnly = tx.PreviewRedundantBackups();
        Assert.Equal(new BackupCleanupResult(metadataOnly.PlanSha256, 0, 0, true), tx.CleanupRedundantBackups(metadataOnly.PlanSha256));
        Assert.Equal(metadataOnlyBefore, PortableBackupCleanupFixture.Snapshot(f.Root));
        Assert.Equal("v4", tx.ReadCommitted()!.SelectedVersion);
        await f.Install("v5"); f.Save("NEWEST"); tx.SelectExecutable("other.AppImage");
        var saves = File.ReadAllBytes(Path.Combine(f.Live, "saves/nested/slot.sav"));
        tx.Rollback(); Assert.Equal(saves, File.ReadAllBytes(Path.Combine(f.Live, "saves/nested/slot.sav")));
        Assert.Equal("fixture.AppImage", tx.ReadCommitted()!.SelectedExecutable); Assert.NotEmpty(tx.PreviewRedundantBackups().Files);
        output.WriteLine($"approved={plan.PlanSha256}; actual_removed={result.DeletedFiles}; bytes={result.ReclaimedBytes}; snapshot equality + ReadCommitted/install/SelectExecutable/offline Rollback/current saves/newPreview exercised");
    }
    [Fact]
    public async Task Rollback_inventory_payload_flag_never_authorizes_deleting_declared_data()
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3); f.Tx().Rollback(); await f.Install("v4");
        var rollback = Directory.GetDirectories(f.Tx().OperationsPath).Select(d => (Dir: d, J: JsonNode.Parse(File.ReadAllText(Path.Combine(d, "journal.json")))!))
            .Single(t => t.J["oldFiles"]!.AsArray().Any(e => e!["path"]!.GetValue<string>() == "saves/nested/slot.sav" && e["ownership"]!.GetValue<string>() == "payload"));
        var before = PortableBackupCleanupFixture.Snapshot(f.Root); var plan = f.Tx().PreviewRedundantBackups();
        Assert.Contains(plan.Files, e => e.OperationId == Path.GetFileName(rollback.Dir));
        Assert.DoesNotContain(plan.Files, e => e.BackupRelativePath.Contains("saves/") || e.BackupRelativePath.Contains("owneddata/") || e.BackupRelativePath.Contains("config/") || e.BackupRelativePath.EndsWith("LastPlayed.txt"));
        Assert.True(f.Tx().CleanupRedundantBackups(plan.PlanSha256).Completed); f.OnlyApprovedRemoved(before, plan);
        Assert.True(File.Exists(Path.Combine(rollback.Dir, "backup/saves/nested/slot.sav")));
        f.Save("CURRENT_AFTER_CLEANUP"); f.Tx().Rollback(); Assert.StartsWith("CURRENT_AFTER_CLEANUP", File.ReadAllText(Path.Combine(f.Live, "saves/nested/slot.sav")));
    }
    [Theory]
    [InlineData(1)][InlineData(2)]
    public async Task First_and_current_only_noop_is_readonly_and_truthful(int generations)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(generations);
        var before = PortableBackupCleanupFixture.Snapshot(f.Root); var plan = f.Tx().PreviewRedundantBackups();
        Assert.Empty(plan.Files); Assert.Equal(0, plan.ReclaimableBytes);
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, true), f.Tx().CleanupRedundantBackups(plan.PlanSha256));
        Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
    }
    [Theory]
    [InlineData("live-data")][InlineData("live-payload")][InlineData("previous-data")][InlineData("backup-data")]
    [InlineData("journal")][InlineData("unknown-op")][InlineData("unknown-file")][InlineData("launch")]
    [InlineData("missing-live")][InlineData("missing-witness")][InlineData("missing-source")][InlineData("case")]
    [InlineData("symlink-source")][InlineData("symlink-previous")][InlineData("fifo")][InlineData("hardlink")]
    [InlineData("denied-source")][InlineData("denied-previous")][InlineData("denied-history")]
    [InlineData("same-bytes-replaced")][InlineData("source-mode")]
    [InlineData("pending")][InlineData("staging")][InlineData("corrupt-manifest")][InlineData("duplicate-journal")]
    public async Task Stale_changed_unknown_and_unsafe_evidence_refuses_without_any_deletion(string kind)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3); var tx = f.Tx(); var plan = tx.PreviewRedundantBackups();
        var file = plan.Files.First(); var source = Path.Combine(tx.OperationsPath, file.OperationId, file.BackupRelativePath);
        var retained = Path.Combine(tx.OperationsPath, file.OperationId, file.RetainedRelativePath); var op = Path.Combine(tx.OperationsPath, file.OperationId);
        UnixFileMode? restore = null; string? denied = null;
        switch (kind)
        {
            case "live-data": f.Save("STALE"); break;
            case "live-payload": File.AppendAllText(Path.Combine(f.Live, "fixture.AppImage"), "changed"); break;
            case "previous-data": File.AppendAllText(Path.Combine(op, "previous/LastPlayed.txt"), "changed"); break;
            case "backup-data": File.AppendAllText(Path.Combine(op, "backup/LastPlayed.txt"), "changed"); break;
            case "journal": File.WriteAllText(Path.Combine(op, "journal.json"), "{broken"); break;
            case "unknown-op": Directory.CreateDirectory(Path.Combine(tx.OperationsPath, "unknown")); break;
            case "unknown-file": File.WriteAllText(Path.Combine(tx.OperationsPath, "unknown"), "keep"); break;
            case "launch": File.WriteAllText(Path.Combine(tx.OperationsPath, "launch.json"), "unknown or stale; never clear"); break;
            case "missing-live": Directory.Move(f.Live, Path.Combine(f.Root, "removed-live")); break;
            case "missing-witness": File.Delete(retained); break;
            case "missing-source": File.Delete(source); break;
            case "case": File.WriteAllText(Path.Combine(op, "backup/FIXTURE.AppImage"), "keep case alias"); break;
            case "symlink-source": File.Delete(source); File.CreateSymbolicLink(source, retained); break;
            case "symlink-previous": Directory.Move(Path.Combine(op, "previous"), Path.Combine(f.Root, "outside")); Directory.CreateSymbolicLink(Path.Combine(op, "previous"), Path.Combine(f.Root, "outside")); break;
            case "fifo": File.Delete(source); using (var p = Process.Start("/usr/bin/mkfifo", source)!) { p.WaitForExit(); Assert.Equal(0, p.ExitCode); } break;
            case "hardlink": File.Delete(source); using (var p = Process.Start("/usr/bin/ln", new[] { retained, source })!) { p.WaitForExit(); Assert.Equal(0, p.ExitCode); } break;
            case "denied-source": denied = source; break;
            case "denied-previous": denied = retained; break;
            case "denied-history": denied = tx.OperationsPath; break;
            case "same-bytes-replaced": var bytes = File.ReadAllBytes(source); File.Move(source, Path.Combine(f.Root, "old-identical-source")); File.WriteAllBytes(source, bytes); break;
            case "source-mode": File.SetUnixFileMode(source, UnixFileMode.UserRead); break;
            case "pending": File.WriteAllText(Path.Combine(tx.OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json.pending"), "incomplete approval"); break;
            case "staging": Directory.CreateDirectory(Path.Combine(tx.OperationsPath, Guid.NewGuid().ToString("N"), "candidate")); break;
            case "corrupt-manifest": File.AppendAllText(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName), "{}"); break;
            case "duplicate-journal": var j = Path.Combine(op, "journal.json"); File.WriteAllText(j, File.ReadAllText(j).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")); break;
        }
        // Never snapshot FIFO/link/denied paths by opening them. Capture all safe
        // protected bytes before denial, then restore permission only for readback.
        var compare = kind is not ("fifo" or "symlink-source" or "symlink-previous" or "hardlink");
        var before = compare ? PortableBackupCleanupFixture.Snapshot(f.Root) : null;
        if (denied != null) { restore = File.GetUnixFileMode(denied); File.SetUnixFileMode(denied, UnixFileMode.None); }
        try { Assert.ThrowsAny<Exception>(() => tx.CleanupRedundantBackups(plan.PlanSha256)); }
        finally { if (denied != null) File.SetUnixFileMode(denied, restore!.Value); }
        if (before != null) Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
        Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json")));
        if (kind != "missing-source") Assert.All(plan.Files.Where(e => e != file), e => Assert.True(File.Exists(Path.Combine(tx.OperationsPath, e.OperationId, e.BackupRelativePath))));
    }
    [Fact]
    public async Task Cancellation_before_approval_never_writes_or_deletes()
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(); var plan = f.Tx().PreviewRedundantBackups(); var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => f.Tx().CleanupRedundantBackups(plan.PlanSha256, cts.Token)); Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
    }
    [Fact]
    public async Task Cancellation_after_durable_approval_before_first_delete_is_explicitly_resumable()
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3); var plan = f.Tx().PreviewRedundantBackups();
        using var cts = new CancellationTokenSource();
        var error = Assert.Throws<BackupCleanupInterruptedException>(() => f.Tx(p => { if (p == "BackupCleanupApproved") cts.Cancel(); }).CleanupRedundantBackups(plan.PlanSha256, cts.Token));
        Assert.Equal(new BackupCleanupResult(plan.PlanSha256, 0, 0, false), error.Result);
        Assert.Equal(plan.Files.Count, f.Tx().CleanupRedundantBackups(plan.PlanSha256).DeletedFiles);
    }
    [Theory]
    [InlineData("cancel")][InlineData("exception")][InlineData("source-change")][InlineData("data-change")]
    public async Task Interrupted_subset_is_truthful_and_explicit_resume_revalidates_everything(string kind)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(); var plan = f.Tx().PreviewRedundantBackups(); var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        using var cts = new CancellationTokenSource(); var hits = 0;
        var tx = f.Tx(p => { if (p != "BackupCleanupDeleted" || ++hits != 1) return;
            if (kind == "cancel") cts.Cancel();
            else if (kind == "exception") throw new IOException("synthetic reporting interruption");
            else if (kind == "data-change") f.Save("external concurrent change");
            else File.AppendAllText(Path.Combine(f.Tx().OperationsPath, plan.Files[1].OperationId, plan.Files[1].BackupRelativePath), "external change");
        });
        var error = Assert.Throws<BackupCleanupInterruptedException>(() => tx.CleanupRedundantBackups(plan.PlanSha256, cts.Token));
        Assert.Equal(1, error.Result.DeletedFiles); Assert.Equal(plan.Files[0].Length, error.Result.ReclaimedBytes); Assert.False(error.Result.Completed);
        if (kind is "cancel" or "exception")
        {
            Assert.Equal(plan.PlanSha256, f.Tx().PreviewRedundantBackups().PlanSha256);
            var resumed = f.Tx().CleanupRedundantBackups(plan.PlanSha256); Assert.Equal(plan.Files.Count - 1, resumed.DeletedFiles); Assert.Equal(plan.ReclaimableBytes - error.Result.ReclaimedBytes, resumed.ReclaimedBytes);
            f.OnlyApprovedRemoved(before, plan);
        }
        else
        {
            var changed = PortableBackupCleanupFixture.Snapshot(f.Root);
            Assert.ThrowsAny<IOException>(() => f.Tx().CleanupRedundantBackups(plan.PlanSha256)); Assert.Equal(changed, PortableBackupCleanupFixture.Snapshot(f.Root));
        }
    }
    [Theory]
    [InlineData("malformed")][InlineData("unknown")][InlineData("duplicate")][InlineData("wrong-hash")][InlineData("missing-witness")]
    public async Task Approval_file_alone_never_authorizes_or_establishes_completion(string kind)
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(); var plan = f.Tx().PreviewRedundantBackups();
        Assert.Throws<BackupCleanupInterruptedException>(() => f.Tx(p => { if (p == "BackupCleanupApproved") throw new IOException("stop before any delete"); }).CleanupRedundantBackups(plan.PlanSha256));
        var path = Path.Combine(f.Tx().OperationsPath, "backup-cleanup-" + plan.PlanSha256 + ".json"); var text = File.ReadAllText(path);
        switch (kind)
        {
            case "malformed": File.WriteAllText(path, "{broken"); break;
            case "unknown": File.WriteAllText(path, text.Replace("{", "{\"unknown\":true,", StringComparison.Ordinal)); break;
            case "duplicate": File.WriteAllText(path, text.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")); break;
            case "wrong-hash": File.WriteAllText(path, text.Replace(plan.PlanSha256, new string('a', 64))); break;
            case "missing-witness": File.Delete(Path.Combine(f.Tx().OperationsPath, plan.Files[0].OperationId, plan.Files[0].RetainedRelativePath)); break;
        }
        var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        Assert.ThrowsAny<Exception>(() => f.Tx().CleanupRedundantBackups(plan.PlanSha256)); Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
    }
    [Fact]
    public async Task Managed_ancestor_and_transaction_tree_cannot_create_nested_authority()
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3);
        foreach (var root in new[] { Path.Combine(f.Live, "saves"), Path.Combine(f.Tx().OperationsPath, f.Tx().ReadCommitted()!.OperationId, "previous") })
        {
            var before = PortableBackupCleanupFixture.Snapshot(f.Root);
            Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(root).PreviewRedundantBackups()); Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
        }
    }
    [Fact]
    public async Task Inventory_and_receipt_budgets_refuse_before_deletion()
    {
        var f = new PortableBackupCleanupFixture(); await f.Generations(3); var before = PortableBackupCleanupFixture.Snapshot(f.Root);
        Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(f.Live, new() { MaximumFileCount = 1 }).PreviewRedundantBackups());
        Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(f.Live, new() { MaximumExpandedBytes = 1 }).PreviewRedundantBackups());
        Assert.Equal(before, PortableBackupCleanupFixture.Snapshot(f.Root));
    }
}
