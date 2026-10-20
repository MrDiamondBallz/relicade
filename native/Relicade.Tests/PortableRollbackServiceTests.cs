using System.Collections.ObjectModel;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PortableRollbackServiceTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Readable_shared_or_distinct_real_store_success_preserves_live_identity_values_and_no_network(bool distinct)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,3);
        var store=distinct?new FileSettingsStore(s.Settings):s.Store;
        store.Current.FirstStartup=false;store.Current.MusicVolume=13;
        using var manager=new GameManager(store,new HttpClient(s.Network),new AppCatalogService(dataDirectory:s.Catalog));
        manager.Games=new ObservableCollection<GameInfo>{s.Game};
        var current=store.Current;var global=s.Store.Current;var games=manager.Games;
        var values=JsonSerializer.Serialize(current);var globalValues=JsonSerializer.Serialize(global);var bytes=File.ReadAllBytes(s.Settings);var library=File.ReadAllBytes(s.Library);
        var before=s.Snapshot();var plan=PortableRollbackService.Preview(manager,s.Game.InstanceKey);
        Assert.Equal("v2",plan.TargetVersion);Assert.Equal(before,s.Snapshot());
        Assert.Same(current,store.Current);Assert.Same(global,s.Store.Current);Assert.Equal(values,JsonSerializer.Serialize(store.Current));Assert.Equal(globalValues,JsonSerializer.Serialize(global));
        var result=PortableRollbackService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256);
        Assert.True(result.Completed);Assert.Equal("v2",result.ActualVersion);Assert.Same(current,store.Current);Assert.Same(global,s.Store.Current);Assert.Same(games,manager.Games);Assert.Same(s.Game,Assert.Single(manager.Games));
        Assert.Equal(values,JsonSerializer.Serialize(store.Current));Assert.Equal(globalValues,JsonSerializer.Serialize(global));Assert.Equal(bytes,File.ReadAllBytes(s.Settings));Assert.Equal(library,File.ReadAllBytes(s.Library));Assert.Equal(0,s.Network.Calls);
    }

    [Theory]
    [InlineData("corrupt-library")][InlineData("duplicate-key")][InlineData("missing-key")][InlineData("case-key")][InlineData("name")][InlineData("repository")]
    [InlineData("shared-persisted-only")][InlineData("unknown-format")][InlineData("corrupt-settings")][InlineData("legacy-null")][InlineData("legacy-empty")]
    [InlineData("kiosk")][InlineData("manual")][InlineData("package")][InlineData("default-located")][InlineData("changed-apps-root")][InlineData("unknown-history")]
    [InlineData("missing-settings")][InlineData("oversized-settings")][InlineData("persisted-kiosk")][InlineData("persisted-apps-root")]
    public async Task Exact_persisted_and_effective_identity_policy_refuses_before_preview_or_direct_apply(string kind)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);
        var key=s.Game.InstanceKey;var plan=PortableRollbackService.Preview(manager,key); // independently valid consent before refusal setup
        switch(kind)
        {
            case "corrupt-library":File.WriteAllText(s.Library,"{broken");break;
            case "duplicate-key":s.SaveLibrary(s.Game);break;
            case "missing-key":key="missing";break;
            case "case-key":key=key.ToUpperInvariant();break;
            case "name":key=s.Game.Name!;break;
            case "repository":key=s.Game.Repository!;break;
            case "shared-persisted-only":s.SaveLibrary(new GameInfo{Name="other",Repository="fixture/other",FolderName="other",InstallPath=s.Live});break;
            case "unknown-format":File.WriteAllText(s.Library,"{\"unknown\":[]}");break;
            case "corrupt-settings":File.WriteAllText(s.Settings,"{broken");break;
            case "legacy-null":File.WriteAllText(s.Settings,"{\"GitHubToken\":null}");break;
            case "legacy-empty":File.WriteAllText(s.Settings,"{\"GitHubToken\":\"\"}");break;
            case "kiosk":s.Store.Current.KioskMode=true;break;
            case "manual":s.Game.Repository=null;break;
            case "package":File.WriteAllText(Path.Combine(s.Live,WindowsInstallerService.ReceiptFileName),"conflict");break;
            case "default-located":s.Game.InstallPath=Path.Combine(s.Root,"located");Directory.CreateDirectory(s.Game.InstallPath);break;
            case "changed-apps-root":s.Store.Current.AppsPath=Path.Combine(s.Root,"other");break;
            case "unknown-history":Directory.CreateDirectory(Path.Combine(s.Tx.OperationsPath,"unknown"));break;
            case "missing-settings":File.Delete(s.Settings);break;
            case "oversized-settings":File.WriteAllText(s.Settings,new string(' ',5*1024*1024));break;
            case "persisted-kiosk":File.WriteAllText(s.Settings,JsonSerializer.Serialize(new AppSettings{AppsPath=s.Store.Current.AppsPath,KioskMode=true}));break;
            case "persisted-apps-root":File.WriteAllText(s.Settings,JsonSerializer.Serialize(new AppSettings{AppsPath=Path.Combine(s.Root,"other")}));break;
        }
        var before=s.Snapshot();var current=s.Store.Current;var values=JsonSerializer.Serialize(current);var games=manager.Games;
        var preview=Record.Exception(()=>PortableRollbackService.Preview(manager,key));var apply=Record.Exception(()=>PortableRollbackService.Apply(manager,key,plan.PlanSha256));
        Assert.NotNull(preview);Assert.NotNull(apply);Assert.Equal(before,s.Snapshot());Assert.Same(current,s.Store.Current);Assert.Equal(values,JsonSerializer.Serialize(current));Assert.Same(games,manager.Games);Assert.Equal(0,s.Network.Calls);
    }

    [Theory][InlineData("library")][InlineData("settings")][InlineData("current-data")][InlineData("effective-nonpolicy")]
    public async Task Stale_open_consent_requires_new_preview_and_never_uses_replacement_plan(string kind)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);
        var plan=PortableRollbackService.Preview(manager,s.Game.InstanceKey);
        if(kind=="library")File.AppendAllText(s.Library," ");
        if(kind=="settings")File.AppendAllText(s.Settings," ");
        if(kind=="current-data")File.WriteAllText(Path.Combine(s.Live,"LastPlayed.txt"),"NEW_CURRENT");
        if(kind=="effective-nonpolicy")s.Store.Current.FirstStartup=!s.Store.Current.FirstStartup;
        var before=s.Snapshot();Assert.Throws<IOException>(()=>PortableRollbackService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256));Assert.Equal(before,s.Snapshot());
        var fresh=PortableRollbackService.Preview(manager,s.Game.InstanceKey);Assert.NotEqual(plan.PlanSha256,fresh.PlanSha256);Assert.Equal(before,s.Snapshot());
        Assert.True(PortableRollbackService.Apply(manager,s.Game.InstanceKey,fresh.PlanSha256).Completed);
    }

    [Theory][InlineData("initial")][InlineData("before-apply")][InlineData("callback")]
    public async Task This_injected_manager_recovery_is_rechecked_without_load_save_or_identity_replacement(string boundary)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var readable=PortableBackupCleanupServiceTests.Manager(s);
        var valid=PortableRollbackService.Preview(readable,s.Game.InstanceKey);
        var store=new RecoveryStore(s.Store.Current);using var manager=new GameManager(store,new HttpClient(s.Network),new AppCatalogService(dataDirectory:s.Catalog));manager.Games=new(){s.Game};
        var plan=boundary=="initial"?valid:PortableRollbackService.Preview(manager,s.Game.InstanceKey);
        var loads=store.Loads;var before=s.Snapshot();var current=store.Current;var games=manager.Games;var old=SettingsStoreProvider.Default;
        try
        {
            if(boundary=="callback")SettingsStoreProvider.Default=new EvidenceHook(s.Store,()=>store.ReadOnly=true,2);
            else store.ReadOnly=true;
            var error=Assert.Throws<IOException>(()=>PortableRollbackService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256));
            Assert.Contains("This library session's settings are in recovery",error.Message);
            Assert.Same(current,store.Current);Assert.Same(games,manager.Games);Assert.Equal(loads,store.Loads);Assert.Equal(0,store.Saves);Assert.Equal(before,s.Snapshot());
        }
        finally{SettingsStoreProvider.Default=old;}
        store.ReadOnly=false;Assert.True(PortableRollbackService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256).Completed);Assert.Equal(0,s.Network.Calls);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Unsupported_or_uninitialized_provider_never_initializes_or_loads(bool absent)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);var original=SettingsStoreProvider.Default;
        var unsupported=new RecoveryStore(s.Store.Current);SettingsStoreProvider.Default=absent?null!:unsupported;
        try{var before=s.Snapshot();Assert.Throws<IOException>(()=>PortableRollbackService.Preview(manager,s.Game.InstanceKey));Assert.Equal(before,s.Snapshot());Assert.Equal(0,unsupported.Loads);Assert.Equal(0,unsupported.Saves);if(absent)Assert.Null(SettingsStoreProvider.RollbackCurrent);}
        finally{SettingsStoreProvider.Default=original;}
    }

    internal sealed class RecoveryStore(AppSettings settings):ISettingsStore,ISettingsRecoveryState
    {
        public AppSettings Current=>settings;public bool ReadOnly;public bool IsReadOnly=>ReadOnly;public Exception? ReadError=>ReadOnly?new IOException("fixture recovery"):null;public int Loads,Saves;
        public AppSettings Load(){Loads++;return settings;}public void Save(AppSettings value){Saves++;}
    }
    sealed class EvidenceHook(FileSettingsStore store,Action action,int at):ISettingsStore,IPortableRollbackSettingsEvidence
    {
        int calls;public AppSettings Current=>store.Current;public AppSettings Load()=>throw new InvalidOperationException("No rollback Load authorized");public void Save(AppSettings value)=>throw new InvalidOperationException("No rollback Save authorized");
        public (AppSettings Settings,string Path,string RawSha256) ReadRollbackSettings(){if(++calls==at)action();return ((IPortableRollbackSettingsEvidence)store).ReadRollbackSettings();}
    }
}
