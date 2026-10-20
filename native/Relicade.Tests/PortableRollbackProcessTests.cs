using System.Diagnostics;
using System.Text.Json;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableRollbackProcessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("BeforeVersionMetadata")][InlineData("VersionMetadata")][InlineData("BeforeBuildMetadata")][InlineData("BuildMetadata")]
    [InlineData("BeforeCandidateManifest")][InlineData("CandidateManifest")][InlineData("Staged")][InlineData("BackedUp")][InlineData("BeforePrepared")]
    [InlineData("Prepared")][InlineData("BeforeApplying")][InlineData("Applying")][InlineData("BeforePreviousRename")][InlineData("PreviousRenamed")]
    [InlineData("BeforeCandidateRename")][InlineData("CandidateRenamed")][InlineData("BeforeCommitted")][InlineData("Committed")]
    public async Task Actual_termination_then_separate_reopen_validates_complete_generation_and_current_data(string boundary)
    {
        using var s=new PortableBackupCleanupBaselineScope();
        async Task Install(string version)=>await new PortableInstallationTransaction(s.Live,new(){InstalledBuildText="build-"+version})
            .InstallAsync(p=>{File.WriteAllBytes(Path.Combine(p,"fixture.AppImage"),PortableInstallationFixtures.NativePayload(version));return Task.CompletedTask;},version);
        await Install("v1");await Install("v2");File.WriteAllText(Path.Combine(s.Live,"LastPlayed.txt"),"CURRENT_PROCESS_DATA");
        var plan=PortableRollbackCoreTests.Preview(s.Tx);var coord=Path.Combine(s.Root,"rollback-coord");Directory.CreateDirectory(coord);
        using(var child=Start(s.Root,boundary,plan.PlanSha256))
        {
            try
            {
                var clock=Stopwatch.StartNew();while(!File.Exists(Path.Combine(coord,"ready")))
                {if(child.HasExited)throw new Exception(await child.StandardOutput.ReadToEndAsync()+await child.StandardError.ReadToEndAsync());if(clock.Elapsed>TimeSpan.FromSeconds(30))throw new TimeoutException("Confirmed rollback boundary not reached");await Task.Delay(10);}
                Assert.ThrowsAny<IOException>(()=>PortableRollbackCoreTests.Preview(s.Tx));
                child.Kill(true);await child.WaitForExitAsync();Assert.NotEqual(0,child.ExitCode);
                File.WriteAllText(Path.Combine(coord,"killed-output"),await child.StandardOutput.ReadToEndAsync()+await child.StandardError.ReadToEndAsync());
                output.WriteLine($"Actual killed confirmed-rollback worker pid={child.Id}; boundary={boundary}; exit={child.ExitCode}");
            }
            finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
        }
        var before=s.Snapshot();var refused=Record.Exception(()=>PortableRollbackCoreTests.Preview(s.Tx));
        Assert.Equal(before,s.Snapshot()); // No repair from preview, including pre-Prepared evidence.
        if(boundary!="Committed")Assert.NotNull(refused);
        using var reopen=Start(s.Root,"reopen",plan.PlanSha256);var text=await reopen.StandardOutput.ReadToEndAsync();var errors=await reopen.StandardError.ReadToEndAsync();await reopen.WaitForExitAsync();File.WriteAllText(Path.Combine(coord,"reopen-output"),text+errors);Assert.Equal(0,reopen.ExitCode);
        var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(coord,"reopened.json")));var version=result.RootElement.GetProperty("version").GetString();Assert.Contains(version,new[]{"v1","v2"});
        if(boundary=="Committed")Assert.Equal("v1",version);else Assert.Equal("v2",version);
        Assert.Equal("CURRENT_PROCESS_DATA",File.ReadAllText(Path.Combine(s.Live,"LastPlayed.txt")));
        await s.Install("future");Assert.Equal("future",s.Tx.ReadCommitted()!.SelectedVersion);s.Tx.SelectExecutable("fixture.AppImage");Assert.Equal("CURRENT_PROCESS_DATA",File.ReadAllText(Path.Combine(s.Live,"LastPlayed.txt")));
    }
    static Process Start(string root,string mode,string hash)
    {
        var coord=Path.Combine(root,"rollback-coord");var home=Path.Combine(coord,"home-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);
        var psi=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        if(Path.GetFileNameWithoutExtension(psi.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))psi.ArgumentList.Add(typeof(PortableRollbackProcessTests).Assembly.Location);
        foreach(var arg in new[]{"-noLogo","-noColor","-parallel","none","-method","Relicade.Tests.PortableRollbackProcessTests.Worker_also_executes_real_rollback_in_ordinary_suite","-xml",Path.Combine(coord,"worker-"+Guid.NewGuid().ToString("N")+".xml")})psi.ArgumentList.Add(arg);
        psi.Environment["RELICADE_ROLLBACK_ROOT"]=root;psi.Environment["RELICADE_ROLLBACK_MODE"]=mode;psi.Environment["RELICADE_ROLLBACK_HASH"]=hash;psi.Environment["HOME"]=home;psi.Environment["DOTNET_CLI_HOME"]=home;return Process.Start(psi)!;
    }
    [Fact]
    public async Task Worker_also_executes_real_rollback_in_ordinary_suite()
    {
        var root=Environment.GetEnvironmentVariable("RELICADE_ROLLBACK_ROOT");
        if(root==null)
        {
            using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);var p=PortableRollbackCoreTests.Preview(s.Tx);var r=PortableRollbackCoreTests.Apply(s.Tx,p.PlanSha256);Assert.True(r.Completed);Assert.Equal("v1",r.ActualVersion);return;
        }
        var mode=Environment.GetEnvironmentVariable("RELICADE_ROLLBACK_MODE")!;var live=Path.Combine(root,"Apps/live");var coord=Path.Combine(root,"rollback-coord");
        if(mode=="reopen")
        {
            // Explicit recovery in this test is separately requested, NEVER the
            // new preview/apply UI's implicit behavior.
            var tx=new PortableInstallationTransaction(live);var manifest=tx.ReadCommitted()!;Assert.Equal("CURRENT_PROCESS_DATA",File.ReadAllText(Path.Combine(live,"LastPlayed.txt")));
            Assert.Equal(PortableInstallationFixtures.NativePayload(manifest.SelectedVersion),File.ReadAllBytes(Path.Combine(live,manifest.SelectedExecutable)));
            File.WriteAllText(Path.Combine(coord,"reopened.json"),JsonSerializer.Serialize(new{version=manifest.SelectedVersion,operation=manifest.OperationId,manifest.SelectedExecutable}));return;
        }
        var work=new PortableInstallationTransaction(live,new(){TransactionBoundary=b=>{if(b!=mode)return;File.WriteAllText(Path.Combine(coord,"ready"),b);Thread.Sleep(Timeout.Infinite);}});
        var result=PortableRollbackCoreTests.Apply(work,Environment.GetEnvironmentVariable("RELICADE_ROLLBACK_HASH")!);Assert.True(result.Completed);
    }
}
