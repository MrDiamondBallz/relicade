using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PortableRollbackCoreTests
{
    internal static readonly string Context = new('a', 64);
    internal static readonly string[] Boundaries = ["BeforeVersionMetadata", "VersionMetadata", "BeforeBuildMetadata", "BuildMetadata", "BeforeCandidateManifest", "CandidateManifest", "Staged", "BackedUp", "BeforePrepared", "Prepared", "BeforeApplying", "Applying", "BeforePreviousRename", "PreviousRenamed", "BeforeCandidateRename", "CandidateRenamed", "BeforeCommitted", "Committed"];
    public static IEnumerable<object[]> Faults => Boundaries.SelectMany(b => new[] { new object[] { b, false }, new object[] { b, true } });
    internal static PortableRollbackPreview Preview(PortableInstallationTransaction tx) => tx.PreviewRollback(Context, () => { });
    internal static PortableRollbackResult Apply(PortableInstallationTransaction tx, string hash, CancellationToken token = default) => tx.RollbackConfirmed(hash, Context, () => { }, token);

    [Theory][InlineData(2)][InlineData(3)][InlineData(5)]
    public async Task Success_exact_immediate_previous_payload_preserves_current_data_history_and_future_operations(int count)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(s, count);
        File.WriteAllText(Path.Combine(s.Live, "LastPlayed.txt"), "CURRENT_NOT_OLDER");
        var current = s.Tx.ReadCommitted()!; var before = s.Snapshot();
        var plan = Preview(s.Tx);
        Assert.Equal("v" + count, plan.CurrentVersion); Assert.Equal("v" + (count - 1), plan.TargetVersion);
        Assert.Equal(current.OperationId, plan.CurrentOperationId); Assert.NotEqual(plan.CurrentOperationId, plan.RetainedOperationId);
        Assert.Equal("fixture.AppImage", plan.TargetExecutable); Assert.Contains("LastPlayed.txt", plan.PreservedDataPaths);
        Assert.Equal(plan.PlanSha256, Preview(s.Tx).PlanSha256); Assert.Equal(before, s.Snapshot());
        var result = Apply(s.Tx, plan.PlanSha256);
        Assert.True(result.Completed); Assert.Equal(plan.TargetVersion, result.ActualVersion);
        Assert.NotEqual(current.OperationId, result.ActualOperationId);
        var actual = s.Tx.ReadCommitted()!; Assert.Equal(result.ActualOperationId, actual.OperationId);
        Assert.Equal("CURRENT_NOT_OLDER", File.ReadAllText(Path.Combine(s.Live, "LastPlayed.txt")));
        var after = s.Snapshot();
        foreach (var row in before.Where(r => !r.Key.StartsWith("Apps/live/", StringComparison.Ordinal))) Assert.Equal(row.Value, after[row.Key]);
        Assert.Equal(PortableInstallationFixtures.NativePayload(plan.TargetVersion), File.ReadAllBytes(Path.Combine(s.Live, "fixture.AppImage")));
        s.Tx.SelectExecutable("fixture.AppImage");
        using var manager = PortableBackupCleanupServiceTests.Manager(s);
        var cleanup = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        Assert.True(PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, cleanup.PlanSha256).Completed);
        Assert.Equal("CURRENT_NOT_OLDER", File.ReadAllText(Path.Combine(s.Live, "LastPlayed.txt")));
        await s.Install("future"); Assert.Equal("future", s.Tx.ReadCommitted()!.SelectedVersion);
        Assert.Equal(plan.TargetVersion, Preview(s.Tx).TargetVersion);
        Assert.Equal(0, s.Network.Calls);
    }

    [Fact]
    public async Task Completed_cleanup_receipts_are_retained_and_not_replayed_by_preview_or_rollback()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 4);
        using var manager = PortableBackupCleanupServiceTests.Manager(s);
        var cleanup = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        Assert.True(PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, cleanup.PlanSha256).Completed);
        var path = Path.Combine(s.Tx.OperationsPath, "backup-cleanup-" + cleanup.PlanSha256 + ".json"); var bytes = File.ReadAllBytes(path);
        var plan = Preview(s.Tx); Assert.True(Apply(s.Tx, plan.PlanSha256).Completed);
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal("v3", s.Tx.ReadCommitted()!.SelectedVersion);
        Assert.NotNull(Preview(s.Tx));
    }

    [Theory]
    [InlineData("save")][InlineData("payload")][InlineData("retained")][InlineData("manifest")][InlineData("unknown")]
    [InlineData("launch")][InlineData("link")][InlineData("missing-journal")][InlineData("staged")][InlineData("missing-live")]
    public async Task Stale_and_uncertain_evidence_refuses_without_recovery_or_mutation(string change)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 3);
        var plan = Preview(s.Tx); var previous = Path.Combine(s.Tx.OperationsPath, plan.CurrentOperationId, "previous");
        switch (change)
        {
            case "save": File.WriteAllText(Path.Combine(s.Live,"LastPlayed.txt"),"new-current"); break;
            case "payload": File.AppendAllText(Path.Combine(s.Live,"fixture.AppImage"),"changed"); break;
            case "retained": File.AppendAllText(Path.Combine(previous,"fixture.AppImage"),"changed"); break;
            case "manifest": File.AppendAllText(Path.Combine(s.Live,PortableInstallationTransaction.ManifestFileName)," "); break;
            case "unknown": File.WriteAllText(Path.Combine(s.Tx.OperationsPath,"unknown"),"unknown"); break;
            case "launch": File.WriteAllText(Path.Combine(s.Tx.OperationsPath,"launch.json"),"stale"); break;
            case "link": File.CreateSymbolicLink(Path.Combine(s.Live,"link"),Path.Combine(previous,"fixture.AppImage")); break;
            case "missing-journal": File.Delete(Path.Combine(s.Tx.OperationsPath,plan.CurrentOperationId,"journal.json")); break;
            case "staged": Directory.CreateDirectory(Path.Combine(s.Tx.OperationsPath,Guid.NewGuid().ToString("N"),"candidate")); break;
            case "missing-live": File.Delete(Path.Combine(s.Live,PortableInstallationTransaction.ManifestFileName)); break;
        }
        var before = s.Snapshot();
        Assert.ThrowsAny<IOException>(() => Apply(s.Tx,plan.PlanSha256));
        Assert.Equal(before,s.Snapshot());
    }

    [Theory][InlineData("writer")][InlineData("mod")][InlineData("previous-mod")][InlineData("backup-mod")]
    public async Task Authority_is_owned_and_existing_locks_fail_closed(string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 3);
        var plan = Preview(s.Tx); var path = kind switch { "mod" => Path.Combine(s.Live,".relicade-mod-writer.lock"),
            "previous-mod" => Path.Combine(s.Tx.OperationsPath,plan.CurrentOperationId,"previous",".relicade-mod-writer.lock"),
            "backup-mod" => Path.Combine(s.Tx.OperationsPath,plan.CurrentOperationId,"backup",".relicade-mod-writer.lock"), _ => "" };
        using var held = kind == "writer" ? s.Tx.AcquireLease() : new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var before = s.Snapshot(); Assert.ThrowsAny<IOException>(() => Preview(s.Tx)); Assert.ThrowsAny<IOException>(() => Apply(s.Tx,plan.PlanSha256));
        held.Dispose(); Assert.Equal(before,s.Snapshot()); Assert.True(Apply(s.Tx,Preview(s.Tx).PlanSha256).Completed);
    }

    [Fact]
    public async Task Wrong_hash_context_and_cancel_before_staging_are_readonly_and_context_validates_under_writer()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 2);
        var plan=Preview(s.Tx); var before=s.Snapshot();
        Assert.Throws<IOException>(()=>Apply(s.Tx,new string('0',64)));
        Assert.Throws<IOException>(()=>s.Tx.RollbackConfirmed(plan.PlanSha256,new string('b',64),()=>{}));
        using var cts=new CancellationTokenSource();cts.Cancel(); Assert.Throws<OperationCanceledException>(()=>Apply(s.Tx,plan.PlanSha256,cts.Token));
        var calls=0;
        s.Tx.PreviewRollback(Context,()=>{ calls++; Assert.ThrowsAny<IOException>(()=>s.Tx.AcquireLease()); });
        Assert.True(calls>=2); Assert.Equal(before,s.Snapshot());
    }

    [Theory][MemberData(nameof(Faults))]
    public async Task Every_existing_journal_boundary_actually_reaches_confirmed_rollback_and_preserves_current_data(string boundary,bool cancel)
    {
        using var s=new PortableBackupCleanupBaselineScope();
        // Real build metadata ensures both optional metadata boundaries execute.
        async Task Install(string version) => await new PortableInstallationTransaction(s.Live,new(){InstalledBuildText="build-"+version})
            .InstallAsync(p=>{File.WriteAllBytes(Path.Combine(p,"fixture.AppImage"),PortableInstallationFixtures.NativePayload(version));return Task.CompletedTask;},version);
        await Install("v1");await Install("v2");
        File.WriteAllText(Path.Combine(s.Live,"LastPlayed.txt"),"CURRENT");
        using var cts=new CancellationTokenSource();bool reached=false;
        var tx=new PortableInstallationTransaction(s.Live,new(){TransactionBoundary=b=>{if(b!=boundary)return;reached=true;if(cancel)cts.Cancel();else throw new IOException("FAULT:"+b);}});
        var plan=Preview(tx);PortableRollbackResult? result=null;
        var error=Record.Exception(()=>result=Apply(tx,plan.PlanSha256,cts.Token));
        Assert.True(reached,"Requested confirmed rollback boundary was not executed: "+boundary);
        if(result!=null){Assert.True(result.Completed);Assert.Equal("v1",result.ActualVersion);}
        else {Assert.IsType<IOException>(error);Assert.Contains("incomplete or unknown",error!.Message);}
        var reopened=s.Tx.ReadCommitted()!;
        Assert.Contains(reopened.SelectedVersion,new[]{"v1","v2"});
        Assert.Equal("CURRENT",File.ReadAllText(Path.Combine(s.Live,"LastPlayed.txt")));
        Assert.Equal(PortableInstallationFixtures.NativePayload(reopened.SelectedVersion),File.ReadAllBytes(Path.Combine(s.Live,reopened.SelectedExecutable)));
        var snapshot=s.Snapshot();var previewError=Record.Exception(()=>Preview(s.Tx));
        Assert.Equal(snapshot,s.Snapshot()); // Preview must not resolve discarded/prepared/staged evidence.
        if(previewError!=null)Assert.IsAssignableFrom<IOException>(previewError);
        await s.Install("future"); Assert.Equal("future",s.Tx.ReadCommitted()!.SelectedVersion);
        Assert.Equal("CURRENT",File.ReadAllText(Path.Combine(s.Live,"LastPlayed.txt")));
    }

    [Fact]
    public async Task Callback_context_and_current_data_changes_during_staging_cannot_activate()
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);
        var calls=0; var tx=new PortableInstallationTransaction(s.Live,new(){TransactionBoundary=b=>{if(b=="Staged")File.WriteAllText(Path.Combine(s.Live,"LastPlayed.txt"),"CONCURRENT");}});
        var plan=tx.PreviewRollback(Context,()=>calls++);
        Assert.Throws<IOException>(()=>tx.RollbackConfirmed(plan.PlanSha256,Context,()=>calls++));
        Assert.True(calls>2);Assert.Equal("v2",s.Tx.ReadCommitted()!.SelectedVersion);Assert.Equal("CONCURRENT",File.ReadAllText(Path.Combine(s.Live,"LastPlayed.txt")));
    }
}
