using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class PortableRollbackPreservationTests
{
    [Fact]
    public async Task All_declared_nested_current_data_and_siblings_outside_and_history_survive_without_previous_save_restore()
    {
        using var s=new PortableBackupCleanupBaselineScope();
        var tx=new PortableInstallationTransaction(s.Live,new(){PreservedDataPaths=["LastPlayed.txt","saves","config","owneddata"]});
        async Task Install(string v)=>await tx.InstallAsync(p=>{File.WriteAllBytes(Path.Combine(p,"fixture.AppImage"),PortableInstallationFixtures.NativePayload(v));return Task.CompletedTask;},v);
        await Install("v1");
        foreach(var dir in new[]{"saves/nested","config","owneddata"}){Directory.CreateDirectory(Path.Combine(s.Live,dir));File.WriteAllText(Path.Combine(s.Live,dir,"fixture.dat"),"OLD_DATA");}
        await Install("v2");
        foreach(var p in Directory.GetFiles(s.Live,"fixture.dat",SearchOption.AllDirectories))File.WriteAllText(p,"CURRENT_DATA");
        File.WriteAllText(Path.Combine(s.Root,"outside-canary"),"OUTSIDE");Directory.CreateDirectory(Path.Combine(s.Root,"Apps/sibling"));File.WriteAllText(Path.Combine(s.Root,"Apps/sibling/canary"),"SIBLING");
        var before=s.Snapshot();var plan=PortableRollbackCoreTests.Preview(tx);Assert.Equal(4,plan.PreservedDataPaths.Count);
        var result=PortableRollbackCoreTests.Apply(tx,plan.PlanSha256);Assert.True(result.Completed);
        foreach(var p in Directory.GetFiles(s.Live,"fixture.dat",SearchOption.AllDirectories))Assert.Equal("CURRENT_DATA",File.ReadAllText(p));
        var after=s.Snapshot();foreach(var pair in before.Where(p=>!p.Key.StartsWith("Apps/live/",StringComparison.Ordinal)))Assert.Equal(pair.Value,after[pair.Key]);
        Assert.Equal("v1",tx.ReadCommitted()!.SelectedVersion);Assert.NotEmpty(tx.PreviewRedundantBackups().Files);
    }

    [Theory][InlineData("BackedUp")][InlineData("BeforePrepared")][InlineData("Prepared")][InlineData("BeforeApplying")]
    public async Task Application_callback_changes_at_last_interruptible_boundaries_refuse_before_activation(string boundary)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);bool valid=true;bool hit=false;
        var tx=new PortableInstallationTransaction(s.Live,new(){TransactionBoundary=b=>{if(b==boundary){hit=true;valid=false;}}});
        void Validate(){if(!valid)throw new IOException("APP_CONTEXT_CHANGED");Assert.ThrowsAny<IOException>(()=>s.Tx.AcquireLease());}
        var plan=tx.PreviewRollback(PortableRollbackCoreTests.Context,Validate);
        var error=Assert.Throws<IOException>(()=>tx.RollbackConfirmed(plan.PlanSha256,PortableRollbackCoreTests.Context,Validate));
        Assert.True(hit);Assert.Contains("incomplete or unknown",error.Message);Assert.Equal("v2",s.Tx.ReadCommitted()!.SelectedVersion);
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task Session_owned_flow_duplicate_invoke_default_cancel_or_shutdown_restores_origin_and_preserves_data(bool shutdown)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);var session=new LauncherSession();
        using var lifetime=new LauncherDialogLifetime(session.Token,a=>Dispatcher.UIThread.Post(a));var flow=new PortableRollbackFlow(manager,new SettingsViewModel(s.Store),session,lifetime);var origin=new Button{Content="Original tile"};var host=new Window{Content=origin};
        try
        {
            var before=s.Snapshot();host.Show();origin.Focus();var open=flow.OpenAsync(s.Game,origin);var dialog=Assert.IsType<PortableRollbackDialog>(GamepadModalDialogNavigation.Instance.ActiveDialog);await dialog.Work;Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
            await flow.OpenAsync(s.Game,origin);Assert.Same(dialog,GamepadModalDialogNavigation.Instance.ActiveDialog);Assert.True(flow.IsActive);Assert.False(open.IsCompleted);
            if(shutdown)await session.DisposeAsync();else Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm());
            await open;Assert.False(flow.IsActive);Assert.True(dialog.Model.IsClosed);Assert.Equal(before,s.Snapshot());if(!shutdown)Assert.True(origin.IsFocused);
        }
        finally{host.Close();await session.DisposeAsync();}
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task Inflight_apply_close_or_shutdown_cancels_drains_no_late_result_or_dependency_disposal(bool shutdown)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);var session=new LauncherSession();
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var release=new ManualResetEventSlim();bool disposed=false;int changes=0;
        var model=new PortableRollbackViewModel(session,s.Game.InstanceKey,()=>PortableRollbackService.Preview(manager,s.Game.InstanceKey),(hash,token)=>{started.TrySetResult();release.Wait();return PortableRollbackService.Apply(manager,s.Game.InstanceKey,hash,token);});
        var dialog=new PortableRollbackDialog(model);using var lifetime=new LauncherDialogLifetime(session.Token,a=>Dispatcher.UIThread.Post(a));lifetime.Track(dialog);session.OnShutdown(()=>disposed=true);Task? drain=null;
        try
        {
            var before=s.Snapshot();dialog.Show();dialog.BeginPreview();await dialog.Work;var work=model.ConfirmAsync();await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if(shutdown){drain=session.DisposeAsync().AsTask();await Dispatcher.UIThread.InvokeAsync(()=>{});Assert.False(disposed);Assert.False(drain.IsCompleted);}else dialog.Close();
            Assert.True(model.IsClosed);model.PropertyChanged+=(_,_)=>changes++;release.Set();await work;if(drain!=null)await drain;
            Assert.Equal(0,changes);Assert.Null(model.Result);Assert.Equal(before,s.Snapshot());Assert.Equal(0,s.Network.Calls);
        }
        finally{release.Set();dialog.Close();model.DisposeAfterDrain();await session.DisposeAsync();}
    }
}
