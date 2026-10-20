using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupServiceRound2Tests
{
    static (AppSettings Settings, string Path, string RawSha256) Read(FileSettingsStore store) =>
        ((IPortableBackupCleanupSettingsEvidence)store).ReadBackupCleanupSettings();

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Preview_cancel_apply_and_interrupted_explicit_resume_preserve_live_values_during_every_read(bool distinct)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 5);
        using var vault = new CredentialVaultTestScope();
        var own = distinct ? new FileSettingsStore(s.Settings) : s.Store;
        using var manager = new GameManager(own, new HttpClient(s.Network), new AppCatalogService(dataDirectory:s.Catalog));
        manager.Games = new ObservableCollection<Relicade.Models.GameInfo> { s.Game };
        var effective = own.Current; var global = s.Store.Current; var games = manager.Games;
        effective.FirstStartup = false; effective.EnableGamepadInput = false; effective.ActionButtonSize = 73;
        global.FirstStartup = false; global.ActionButtonSize = 61;
        var live = JsonSerializer.Serialize(effective); var globalLive = JsonSerializer.Serialize(global);
        var disk = File.ReadAllBytes(s.Settings); var reads = vault.Vault.Reads;
        void Preserved()
        {
            Assert.Same(effective, own.Current); Assert.Same(global, s.Store.Current);
            Assert.Equal(live, JsonSerializer.Serialize(own.Current)); Assert.Equal(globalLive, JsonSerializer.Serialize(s.Store.Current));
            Assert.Same(games, manager.Games); Assert.Same(s.Game, Assert.Single(manager.Games));
            Assert.Equal(disk, File.ReadAllBytes(s.Settings));
            Assert.Equal(reads, vault.Vault.Reads); Assert.Equal(0, vault.Vault.Stores); Assert.Equal(0, vault.Vault.Deletes);
            Assert.Equal(0, s.Network.Calls);
        }
        var hook = new EvidenceStore(s.Store, Preserved); SettingsStoreProvider.Default = hook;
        var before = s.Snapshot(); var plan = PortableBackupCleanupService.Preview(manager,s.Game.InstanceKey);
        Preserved(); Assert.Equal(before,s.Snapshot());
        await using (var session = new LauncherSession())
        {
            var model = new Relicade.ViewModels.PortableBackupCleanupViewModel(session,manager,s.Game.InstanceKey);
            await model.PreviewAsync(); model.Cancel(); await model.ConfirmAsync();
            Assert.Equal(before,s.Snapshot()); model.DisposeAfterDrain();
        }
        using var cts = new CancellationTokenSource();
        var first = Path.Combine(s.Tx.OperationsPath,plan.Files[0].OperationId,plan.Files[0].BackupRelativePath);
        hook.OnRead = () => { Preserved(); if (!File.Exists(first)) cts.Cancel(); };
        var interrupted = Assert.Throws<BackupCleanupInterruptedException>(() => PortableBackupCleanupService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256,cts.Token));
        Assert.False(interrupted.Result.Completed); Assert.InRange(interrupted.Result.DeletedFiles,1,plan.Files.Count-1);
        Preserved(); var stopped = s.Snapshot(); Preserved(); Assert.Equal(stopped,s.Snapshot());
        hook.OnRead = Preserved;
        var result = PortableBackupCleanupService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256);
        Assert.True(result.Completed); Assert.Equal(plan.Files.Count-interrupted.Result.DeletedFiles,result.DeletedFiles);
        Preserved(); Assert.Equal(new BackupCleanupResult(plan.PlanSha256,0,0,true),PortableBackupCleanupService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256));
        var approved = plan.Files.Select(f=>Path.GetRelativePath(s.Root,Path.Combine(s.Tx.OperationsPath,f.OperationId,f.BackupRelativePath))).ToHashSet();
        var after = s.Snapshot();
        foreach (var item in before) if (approved.Contains(item.Key)) Assert.False(after.ContainsKey(item.Key)); else Assert.Equal(item.Value,after[item.Key]);
        Assert.True(hook.Reads>4); Assert.Equal(0,hook.Loads); Assert.Equal(0,hook.Saves);
    }

    [Theory]
    [InlineData("malformed")][InlineData("missing")][InlineData("oversize")][InlineData("legacy-null")][InlineData("legacy-empty")]
    [InlineData("legacy-case")][InlineData("duplicate")][InlineData("persisted-kiosk")][InlineData("linked")]
    public async Task Invalid_persisted_evidence_refuses_without_mutating_live_store_or_flags(string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s);
        using var manager = PortableBackupCleanupServiceTests.Manager(s);
        var current = s.Store.Current; var serialized = JsonSerializer.Serialize(current); var raw = File.ReadAllText(s.Settings);
        switch(kind)
        {
            case "malformed": File.WriteAllText(s.Settings,"{broken");break;
            case "missing": File.Delete(s.Settings);break;
            case "oversize": File.WriteAllText(s.Settings,new string(' ',4*1024*1024+1));break;
            case "legacy-null": File.WriteAllText(s.Settings,raw.TrimEnd()[..^1]+",\"GitHubApiToken\":null}");break;
            case "legacy-empty": File.WriteAllText(s.Settings,raw.TrimEnd()[..^1]+",\"GitHubApiToken\":\"\"}");break;
            case "legacy-case": File.WriteAllText(s.Settings,raw.TrimEnd()[..^1]+",\"gItHuBaPiToKeN\":null}");break;
            case "duplicate": File.WriteAllText(s.Settings,"{\"FirstStartup\":true,\"firststartup\":false}");break;
            case "persisted-kiosk": var parsed=JsonSerializer.Deserialize<AppSettings>(raw)!;parsed.KioskMode=true;File.WriteAllText(s.Settings,JsonSerializer.Serialize(parsed));break;
            case "linked": var outside=Path.Combine(s.Root,"outside-settings.json");File.Move(s.Settings,outside);File.CreateSymbolicLink(s.Settings,outside);break;
        }
        var before=s.Snapshot();
        Assert.ThrowsAny<Exception>(()=>PortableBackupCleanupService.Preview(manager,s.Game.InstanceKey));
        Assert.Same(current,s.Store.Current);Assert.Equal(serialized,JsonSerializer.Serialize(current));
        Assert.False(s.Store.IsReadOnly);Assert.False(s.Store.HasLegacyCredentials);Assert.Null(s.Store.ReadError);
        Assert.Equal(before,s.Snapshot());Assert.Equal(0,s.Network.Calls);
    }

    [Theory]
    [InlineData("unsupported")][InlineData("uninitialized")]
    public async Task Missing_safe_evidence_capability_refuses_without_Load_Save_or_provider_initialization(string kind)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s);
        using var manager=PortableBackupCleanupServiceTests.Manager(s);
        var unsupported=new Unsupported(s.Store.Current);var before=s.Snapshot();
        SettingsStoreProvider.Default=kind=="uninitialized"?null!:unsupported;
        var error=Assert.Throws<IOException>(()=>PortableBackupCleanupService.Preview(manager,s.Game.InstanceKey));
        Assert.Contains("safe read-only persisted evidence",error.Message);
        Assert.Equal(0,unsupported.Loads);Assert.Equal(0,unsupported.Saves);
        if(kind=="uninitialized")Assert.Null(SettingsStoreProvider.BackupCleanupCurrent);
        Assert.Equal(before,s.Snapshot());Assert.Equal(0,s.Network.Calls);
    }

    [Fact]
    public void Detached_helper_absent_first_document_is_readonly_and_does_not_mark_a_document_seen()
    {
        using var vault=new CredentialVaultTestScope();
        var path=Path.Combine(Path.GetTempPath(),"cleanup-detached-"+Guid.NewGuid().ToString("N"),"settings.json");
        var store=new FileSettingsStore(path);var current=store.Current;var reads=vault.Vault.Reads;
        var first=Read(store);Assert.Equal("absent",first.RawSha256);Assert.NotSame(current,first.Settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"{}");
        var present=Read(store);Assert.NotEqual("absent",present.RawSha256);File.Delete(path);
        Assert.Equal("absent",Read(store).RawSha256); // detached read never writes _hasSeenDocument
        Assert.Same(current,store.Current);Assert.Null(store.ReadError);Assert.False(store.IsReadOnly);
        Assert.Equal(reads,vault.Vault.Reads);Assert.Equal(0,vault.Vault.Stores);Assert.Equal(0,vault.Vault.Deletes);
    }

    [Fact]
    public async Task Detached_canonical_context_equals_frozen_CLI_envelope_for_unchanged_evidence()
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s);
        using var manager=PortableBackupCleanupServiceTests.Manager(s);
        static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
        var global=s.Store.Load();var path=RelicadePaths.SettingsJsonPath;var raw=Hash(File.ReadAllBytes(path));
        var settings=JsonSerializer.SerializeToUtf8Bytes(new{global,manager=manager.CurrentSettings,path,raw});
        var instanceKey=s.Game.InstanceKey;var effective=Path.TrimEndingDirectorySeparator(Path.GetFullPath(s.Live));var defaultRoot=effective;
        var libraryPath=manager.CatalogService.AppsConfigPath;var librarySha256=Hash(File.ReadAllBytes(libraryPath));
        var context=Hash(JsonSerializer.SerializeToUtf8Bytes(new{instanceKey,effective,defaultRoot,libraryPath,librarySha256,settingsSha256=Hash(settings)}));
        var expected=s.Tx.PreviewRedundantBackups(context,()=>{},alternativeRoot:defaultRoot);
        var actual=PortableBackupCleanupService.Preview(manager,instanceKey);
        Assert.Equal(expected.PlanSha256,actual.PlanSha256);Assert.Equal(expected.Files,actual.Files);
    }

    internal sealed class EvidenceStore(FileSettingsStore store,Action onRead):ISettingsStore,IPortableBackupCleanupSettingsEvidence
    {
        public Action OnRead=onRead;public int Reads,Loads,Saves;public AppSettings Current=>store.Current;
        public AppSettings Load(){Loads++;throw new Exception("No cleanup Load permitted");}
        public void Save(AppSettings settings){Saves++;throw new Exception("No cleanup Save permitted");}
        public (AppSettings Settings,string Path,string RawSha256) ReadBackupCleanupSettings(){Reads++;OnRead();return Read(store);}
    }
    sealed class Unsupported(AppSettings settings):ISettingsStore
    {
        public int Loads,Saves;public AppSettings Current=>settings;
        public AppSettings Load(){Loads++;throw new Exception("Unexpected Load");}
        public void Save(AppSettings value){Saves++;throw new Exception("Unexpected Save");}
    }
}
