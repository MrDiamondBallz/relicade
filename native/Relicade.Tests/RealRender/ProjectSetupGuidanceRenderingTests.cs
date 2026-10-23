using System.Net;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
namespace Relicade.Tests;
[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class ProjectSetupGuidanceRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData("troyedwardsjr/z2rs",1280,860,false,100)]
    [InlineData("troyedwardsjr/z2rs",750,490,true,100)]
    [InlineData("troyedwardsjr/z2rs",1100,760,false,200)]
    [InlineData("taisei-project/taisei",1280,860,false,100)]
    [InlineData("perfect-dark-pc-port/perfect_dark",750,490,true,100)]
    [InlineData("UZDoom/UZDoom",1100,760,true,200)]
    [InlineData("fixture/unknown",750,490,false,100)]
    public async Task Preparation_advice_is_readable_before_any_install(string repository,int width,int height,bool light,int scale)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            var root=Path.Combine(Path.GetTempPath(),"project-guidance-render-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var previous=RelicadePaths.OverrideUserDataRoot;RelicadePaths.OverrideUserDataRoot=root;
            using var http=new HttpClient(new Offline());var store=new Store();store.Current.AppsPath=Path.Combine(root,"Apps");
            store.Current.PrimaryColor=light?"#e5e5e5":"#18181b";store.Current.InterfaceScalePercent=scale;
            using var manager=new GameManager(store,http,new AppCatalogService(dataDirectory:root));
            var view=new MainView(RealRenderTestApp.Dependencies(store,manager));
            var window=new Window { Content=view,Width=width,Height=height };
            using var scaling=new DesktopInterfaceScaling(window,new SettingsViewModel(store),()=>new Size(3840,2160));
            using var game=new GameInfo { Name=repository.Split('/')[1],Repository=repository,RepositorySource="github",FolderName="Fixture" };
            try
            {
                window.Show();Dispatcher.UIThread.RunJobs();
                view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                typeof(MainView).GetMethod("OpenBrowseDetails",BindingFlags.Instance|BindingFlags.NonPublic,[typeof(BrowseItem),typeof(bool)])!.Invoke(view,[BrowseItem.FromList(game),false]);
                window.UpdateLayout();Dispatcher.UIThread.RunJobs();
                var details=view.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
                var panel=details.FindControl<Border>("BrowseDetailsSetupPanel")!;
                var description=details.FindControl<TextBlock>("BrowseDetailsSetupDescription")!;
                Assert.True(panel.IsEffectivelyVisible);Assert.True(panel.Bounds.Width>0);Assert.True(panel.Bounds.Height>0);
                panel.BringIntoView();window.UpdateLayout();Dispatcher.UIThread.RunJobs();
                var visual=ElementComposition.GetElementVisual(view)!;var commit=visual.Compositor.RequestCommitAsync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);await commit.WaitAsync(TimeSpan.FromSeconds(10));
                var scroll=details.FindControl<ScrollViewer>("BrowseDetailsScrollViewer")!;
                Assert.True(scroll.Viewport.Height>0);Assert.True(description.Bounds.Height>0);
                Assert.True(panel.Bounds.Height<=scroll.Viewport.Height,$"Guidance panel {panel.Bounds.Height} exceeds viewport {scroll.Viewport.Height}");
                Assert.True(description.Bounds.Height<=panel.Bounds.Height);
                var top = panel.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(top.Y, -1, scroll.Viewport.Height - panel.Bounds.Height + 1);
                Assert.InRange(top.X, -1, scroll.Viewport.Width - panel.Bounds.Width + 1);
                Assert.False(game.IsInstalled);Assert.Empty(manager.LibraryApps);
                using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);
                var output=Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if(!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output);using var file=new FileStream(Path.Combine(output,$"25-setup-advice-{repository.Replace('/','-')}-{width}x{height}-light-{light}-scale-{scale}.png"),FileMode.CreateNew,FileAccess.Write);frame.Save(file); }
            }
            finally {window.Close();await view.ShutdownAsync();RelicadePaths.OverrideUserDataRoot=previous;Directory.Delete(root,true);}
            return 0;
        },TestContext.Current.CancellationToken);
    }
    sealed class Offline:HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    sealed class Store:ISettingsStore
    { public AppSettings Current {get;}=new(){FirstStartup=false,LocalFirstCatalogMigrationComplete=true,AutoUpdateNewlyAddedApps=false};public AppSettings Load()=>Current;public void Save(AppSettings settings){} }
}
