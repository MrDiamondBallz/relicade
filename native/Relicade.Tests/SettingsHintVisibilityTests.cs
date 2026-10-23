using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.Views;
namespace Relicade.Tests;
public sealed class SettingsHintVisibilityTests
{
 [AvaloniaTheory][InlineData(false)][InlineData(true)]
 public async Task Closing_settings_restores_navigation_hints(bool sidebarToggle)
 {
  var root=Path.Combine(Path.GetTempPath(),"relicade-settings-hints-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
  var old=RelicadePaths.OverrideUserDataRoot;RelicadePaths.OverrideUserDataRoot=root;
  var store=new Store();store.Current.AppsPath=Path.Combine(root,"Apps");
  using var handler=new NoNetwork();using var http=new HttpClient(handler);using var manager=new GameManager(store,http,new AppCatalogService(dataDirectory:root));
  var view=new MainView(new(){SettingsStore=store,GameManager=manager,EnableInput=false,EnableMusic=false,InitializeOnOpen=false});
  var window=new Window{Content=view,Width=750,Height=490};
  try
  {
   window.Show();window.UpdateLayout();Dispatcher.UIThread.RunJobs();
   var hints=view.FindControl<TextBlock>("GamepadHintsBar")!;Assert.True(hints.IsVisible);
   view.OpenSettings();window.UpdateLayout();Dispatcher.UIThread.RunJobs();Assert.True(view.Shell.SettingsOpen);Assert.False(hints.IsVisible);
   var close=sidebarToggle?view.FindControl<Button>("SettingsButton")!:view.FindControl<SettingsView>("SettingsPanel")!.FindControl<Button>("CloseSettingsButton")!;
   close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();Dispatcher.UIThread.RunJobs();
   Assert.False(view.Shell.SettingsOpen);Assert.True(hints.IsVisible);Assert.Equal(0,handler.Calls);
  }
  finally{window.Close();await view.ShutdownAsync();RelicadePaths.OverrideUserDataRoot=old;Directory.Delete(root,true);}
 }
 [AvaloniaTheory]
 [InlineData(false, false, false)]
 [InlineData(false, true, false)]
 [InlineData(true, false, false)]
 [InlineData(true, true, false)]
 [InlineData(false, false, true)]
 [InlineData(false, true, true)]
 [InlineData(true, false, true)]
 [InlineData(true, true, true)]
 public async Task Repeated_close_restores_live_navigation_and_ignores_pending_open_focus(bool sidebarToggle, bool gamepad, bool pendingFocus)
 {
  var root = Path.Combine(Path.GetTempPath(), "relicade-settings-cycle-" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var old = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
  var store = new Store(); store.Current.AppsPath = Path.Combine(root, "Apps"); store.Current.EnableGamepadInput = gamepad;
  using var handler = new NoNetwork(); using var http = new HttpClient(handler);
  using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
  var view = new MainView(new() { SettingsStore = store, GameManager = manager, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
  var window = new Window { Content = view, Width = 750, Height = 490 };
  try
  {
   window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
   var host = (IFeatureNavigationHost)view;
   for (var cycle = 0; cycle < 3; cycle++)
   {
    view.OpenSettings();
    Assert.True(view.Shell.SettingsOpen); Assert.Equal(GamepadNavigationZone.Settings, host.Navigation.ActiveZone);
    if (!pendingFocus) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    var close = sidebarToggle ? view.FindControl<Button>("SettingsButton")! : view.FindControl<SettingsView>("SettingsPanel")!.FindControl<Button>("CloseSettingsButton")!;
    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
    Assert.False(view.Shell.SettingsOpen);
    Assert.True(view.FindControl<TextBlock>("GamepadHintsBar")!.IsVisible);
    Assert.Equal(GamepadNavigationZone.Library, host.Navigation.ActiveZone);
    Assert.False(view.FindControl<SettingsView>("SettingsPanel")!.IsVisible);
   }
   Assert.Equal(0, handler.Calls);
  }
  finally { window.Close(); await view.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = old; Directory.Delete(root, true); }
 }
 sealed class Store:ISettingsStore
 {
  public AppSettings Current{get;}=new(){FirstStartup=false,LocalFirstCatalogMigrationComplete=true,AutoUpdateNewlyAddedApps=false};
  public AppSettings Load()=>Current;public void Save(AppSettings s){}
 }
 sealed class NoNetwork:HttpMessageHandler
 {
  public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;throw new InvalidOperationException("No network in fixture");}
 }
}
