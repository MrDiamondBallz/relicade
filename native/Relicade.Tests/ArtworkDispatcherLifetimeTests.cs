using System.Collections.Concurrent;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Relicade.Services;
namespace Relicade.Tests;
// Gates a real pending artwork response across application lifetimes; no dispatcher worker is injected.
public class ArtworkDispatcherLifetimeTests(ITestOutputHelper output)
{
 [Fact]
 public async Task Actual_pending_artwork_completion_does_not_claim_the_next_application_dispatcher()
 {
  var trace=new ConcurrentQueue<string>();var dir=Path.Combine(Path.GetTempPath(),"artwork-lifetime-"+Guid.NewGuid().ToString("N"));
  var previousScale=LauncherArtworkLoader.DisplayScale;var previousBox=LauncherArtworkLoader.ThumbnailBox;
  var handler=new GatedResponse(trace);using var client=new HttpClient(handler);using var loader=new LauncherArtworkLoader(Path.Combine(dir,"Images"),client);
  Task<Bitmap?>? artwork=null;Dispatcher? owner=null;int setups=0,cases=0;bool shutdownDrained=false;Exception? nextError=null;
  ArtworkDispatcherLifetimeApp.Stage=()=>{
   int stage=++setups;trace.Enqueue($"BOOTSTRAP stage={stage} tid={Environment.CurrentManagedThreadId} old-owner={owner?.Thread.ManagedThreadId} old-registered={owner!=null&&Dispatcher.FromThread(owner.Thread)!=null}");
   if(stage==2){handler.Release();Assert.True(artwork!.Wait(TimeSpan.FromSeconds(10)),"actual artwork task completed before next compositor setup");Assert.NotNull(artwork.Result);trace.Enqueue($"ARTWORK-COMPLETED next-setup-tid={Environment.CurrentManagedThreadId} global-owner={Dispatcher.UIThread.Thread.ManagedThreadId}");}
  };
  await using var session=HeadlessUnitTestSession.StartNew(typeof(ArtworkDispatcherLifetimeApp));
  try{
   await session.Dispatch<int>(async()=>{
    owner=Dispatcher.UIThread;
    new SettingsViewModelTests().Scroll_speed_dropdown_navigation_selects_and_cancels_without_leaving_choices(false);cases++;
    var view=new MainView(new(){SettingsStore=new Store(),EnableInput=false,EnableMusic=false,InitializeOnOpen=false});var window=new Window{Content=view,Width=1000,Height=750};
    try{
     window.Show();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
     trace.Enqueue($"START real-loader owner={owner.Thread.ManagedThreadId} callback-target={LauncherArtworkLoader.DisplayScale.Target?.GetType().FullName}");
     artwork=loader.ProvideImageAsync("https://synthetic.invalid/pending.png");Assert.True(handler.Started);Assert.False(artwork.IsCompleted);
    }finally{window.Close();await view.ShutdownAsync();shutdownDrained=true;trace.Enqueue("REAL-HOST-CLOSED-AND-SESSION-DRAINED");}
    return 0;
   },CancellationToken.None);
   Assert.True(shutdownDrained,"actual window close and session shutdown completed before application reset");
   trace.Enqueue($"FIRST-DISPATCH-RETURNED app-teardown-complete old-registered={Dispatcher.FromThread(owner!.Thread)!=null} artwork-pending={!artwork!.IsCompleted}");
   nextError=await Record.ExceptionAsync(()=>session.Dispatch(()=>{
    new SettingsViewModelTests().Scroll_speed_dropdown_navigation_selects_and_cancels_without_leaving_choices(true);cases++;trace.Enqueue("SECOND-ORIGINAL-CASE-PASS");
   },CancellationToken.None));
   trace.Enqueue("SECOND-ERROR "+nextError);
   await session.Dispatch(()=>{new SettingsViewModelTests().Scroll_speed_dropdown_navigation_selects_and_cancels_without_leaving_choices(false);new SettingsViewModelTests().Scroll_speed_dropdown_navigation_selects_and_cancels_without_leaving_choices(true);cases+=2;},CancellationToken.None);
  }finally{handler.Release();if(artwork!=null)await artwork;ArtworkDispatcherLifetimeApp.Stage=null;LauncherArtworkLoader.DisplayScale=previousScale;LauncherArtworkLoader.ThumbnailBox=previousBox;foreach(var t in trace)output.WriteLine(t);}
  Assert.Null(nextError);Assert.Equal(4,cases);
 }
 sealed class GatedResponse(ConcurrentQueue<string> trace):HttpMessageHandler{
  readonly TaskCompletionSource<HttpResponseMessage> ready=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Started;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Started=true;trace.Enqueue($"REAL-HTTP-SCHEDULE tid={Environment.CurrentManagedThreadId} URI={request.RequestUri}\n{Environment.StackTrace}");return ready.Task;}
  public void Release(){trace.Enqueue($"HTTP-RELEASE tid={Environment.CurrentManagedThreadId}");ready.TrySetResult(new(HttpStatusCode.OK){Content=new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7S8AAAAASUVORK5CYII="))});}
 }
 sealed class Store:ISettingsStore{public AppSettings Current{get;}=new(){FirstStartup=false,AppsPath=Path.Combine(Path.GetTempPath(),"artwork-lifetime-apps")};public AppSettings Load()=>Current;public void Save(AppSettings settings)=>throw new InvalidOperationException("No save authorized");}
}
public static class ArtworkDispatcherLifetimeApp{
 public static Action? Stage;
 public static AppBuilder BuildAvaloniaApp(){var builder=HeadlessTestApp.BuildAvaloniaApp();var initialize=builder.RenderingSubsystemInitializer!;return builder.UseRenderingSubsystem(()=>{initialize();Stage?.Invoke();},builder.RenderingSubsystemName!);}
}
