using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
using NavigationDirection = Relicade.Services.NavigationDirection;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupUiRound2Tests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false,360)][InlineData(true,360)][InlineData(false,760)][InlineData(true,760)]
    public async Task Actual_long_plan_controller_and_keyboard_navigation_exposes_last_row_and_returns_to_Cancel_without_approval(bool keyboard,int width)
    {
        await using var session=new LauncherSession();int applies=0;
        var rows=Enumerable.Range(0,40).Select(i=>new BackupCleanupFile("operation-"+i,"backup/game-payload-"+i,"previous/game-payload-"+i,100+i,new string('a',64))).ToArray();
        var plan=new BackupCleanupPreview(new string('b',64),rows);
        var model=new PortableBackupCleanupViewModel(session,"fixture-key",()=>plan,(_,_)=>{applies++;throw new Exception("Row navigation must not apply");});
        var dialog=new PortableBackupCleanupDialog(model){Width=width,Height=640};
        var nav=GamepadModalDialogNavigation.Instance;var old=nav.ResolveKeyboardAction;var chrome=GamepadFocusChrome.IsActive;
        try
        {
            nav.ResolveKeyboardAction=(key,_)=>key switch{Key.Up=>GamepadAction.NavUp,Key.Down=>GamepadAction.NavDown,Key.Left=>GamepadAction.NavLeft,Key.Right=>GamepadAction.NavRight,Key.Enter=>GamepadAction.Confirm,Key.Escape=>GamepadAction.Cancel,_=>null};
            GamepadFocusChrome.SetActive(true,dialog);
            dialog.Show();dialog.BeginPreview();await dialog.Work;Settle(dialog);
            Assert.Equal(width,dialog.ClientSize.Width);Assert.True(dialog.CancelButton.IsFocused);
            var scroll=dialog.GetVisualDescendants().OfType<ScrollViewer>().First(s=>s.Content is StackPanel);
            Assert.True(scroll.Extent.Height>scroll.Viewport.Height);
            var controls=GamepadModalDialogNavigation.CollectDialogFocusableControls(dialog);
            var inspectors=controls.OfType<Button>().Where(b=>b.Content is TextBlock text&&text.Text!.StartsWith("operation-")).ToArray();
            Assert.Equal(40,inspectors.Length);
            void Move(NavigationDirection direction)
            {
                if(keyboard)
                {
                    var key=direction switch{NavigationDirection.Up=>Key.Up,NavigationDirection.Down=>Key.Down,NavigationDirection.Left=>Key.Left,_=>Key.Right};
                    dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=key});
                }
                else Assert.True(nav.TryHandleNavigation(direction)); // same modal route used by InputService
                Settle(dialog);
            }
            // Enter the plan from chrome, then inspect toward both ends. No
            // synthetic Offset/ScrollIntoView/focus-row shortcut is used here.
            Move(NavigationDirection.Up);
            for(int i=0;i<45;i++)Move(NavigationDirection.Up);
            var visited=new HashSet<Button>();
            for(int i=0;i<45;i++)
            {
                if(dialog.FocusManager!.GetFocusedElement() is Button b&&inspectors.Contains(b))
                {
                    visited.Add(b);
                    if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
                    else Assert.True(nav.TryHandleConfirm());
                    Assert.True(dialog.IsVisible);Assert.Equal(0,applies);
                }
                if(ReferenceEquals(dialog.FocusManager!.GetFocusedElement(),inspectors[^1]))break;
                Move(NavigationDirection.Down);
            }
            var last=inspectors.Single(b=>((TextBlock)b.Content!).Text!.StartsWith("operation-39/"));
            Assert.Contains(last,visited);Assert.Equal(40,visited.Count);
            var point=last.TranslatePoint(new Point(0,0),scroll)!.Value;
            output.WriteLine($"keyboard={keyboard}; width={width}; rowsVisited={visited.Count}; lastY={point.Y}; lastHeight={last.Bounds.Height}; viewport={scroll.Viewport.Height}; extent={scroll.Extent.Height}; offset={scroll.Offset.Y}");
            Assert.InRange(point.Y,-1,scroll.Viewport.Height);Assert.True(point.Y+last.Bounds.Height<=scroll.Viewport.Height+1);
            Assert.Contains("previous/game-payload-39",((TextBlock)last.Content!).Text);Assert.Contains("139 bytes",((TextBlock)last.Content!).Text);
            Assert.Equal(plan.PlanSha256,model.Plan!.PlanSha256);Assert.Equal(0,applies);Assert.Null(model.Result);
            for(int i=0;i<45&&!dialog.CancelButton.IsFocused;i++)Move(NavigationDirection.Down);
            if(!dialog.CancelButton.IsFocused)Move(NavigationDirection.Left);
            Assert.True(dialog.CancelButton.IsFocused);
            if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});else Assert.True(nav.TryHandleConfirm());
            Assert.True(model.IsClosed);Assert.False(dialog.IsVisible);Assert.Equal(0,applies);
        }
        finally{dialog.Close();await dialog.Work;model.DisposeAfterDrain();nav.ResolveKeyboardAction=old;GamepadFocusChrome.SetActive(chrome);}
    }

    [Fact]
    public async Task Original_update_observer_captures_exact_multiset_under_genuinely_concurrent_calls()
    {
        using var handler=new InstalledUpdateCheckTests.Handler();using var client=new HttpClient(handler);
        const int workers=16,each=128;using var ready=new CountdownEvent(workers);using var start=new ManualResetEventSlim();
        var jobs=Enumerable.Range(0,workers).Select(worker=>Task.Factory.StartNew(()=>
        {
            ready.Signal();start.Wait();
            for(int i=0;i<each;i++)
            {
                using var response=client.GetAsync($"https://api.github.com/repos/fixture/w{worker}/releases/latest?n={i}").GetAwaiter().GetResult();
                Assert.True(response.IsSuccessStatusCode);
            }
        },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)).ToArray();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));start.Set();await Task.WhenAll(jobs);
        var expected=Enumerable.Range(0,workers).SelectMany(w=>Enumerable.Range(0,each).Select(i=>$"https://api.github.com/repos/fixture/w{w}/releases/latest?n={i}")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected,handler.Requests.Order(StringComparer.Ordinal).ToArray());
        output.WriteLine($"Simultaneously released {workers} independent worker threads captured exact {expected.Length}-request multiset; no retries/sleeps/product serialization.");
    }
    static void Settle(Window window){Dispatcher.UIThread.RunJobs();window.UpdateLayout();Dispatcher.UIThread.RunJobs();}
}
