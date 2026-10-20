using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
using NavigationDirection=Relicade.Services.NavigationDirection;

namespace Relicade.Tests;

public sealed class PortableRollbackUiTests
{
    [AvaloniaFact]
    public async Task Actual_dialog_cancel_default_no_confirm_and_stale_repreview_require_fresh_consent()
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);await using var session=new LauncherSession();
        var before=s.Snapshot();var model=new PortableRollbackViewModel(session,manager,s.Game.InstanceKey);var dialog=new PortableRollbackDialog(model);
        try
        {
            dialog.Show();dialog.BeginPreview();await dialog.Work;Settle(dialog);
            Assert.Equal(PortableRollbackUiState.Ready,model.State);Assert.True(dialog.CancelButton.IsDefault);Assert.True(dialog.CancelButton.IsFocused);Assert.False(dialog.ApplyButton.IsDefault);
            var text=string.Concat(dialog.Details.Children.OfType<Button>().Select(b=>((TextBlock)b.Content!).Text));
            Assert.Contains(model.Plan!.PlanSha256,text);Assert.Contains("v1",text);Assert.Contains("v2",text);Assert.Contains(model.Plan.CurrentOperationId,text);Assert.Contains(model.Plan.RetainedOperationId,text);Assert.Contains("LastPlayed.txt",text);
            File.AppendAllText(s.Library," ");before=s.Snapshot();
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Work;
            Assert.Equal(PortableRollbackUiState.Error,model.State);Assert.Null(model.Result);Assert.False(model.CanApply);Assert.Equal(before,s.Snapshot());
            await model.ConfirmAsync();Assert.Equal(before,s.Snapshot());
            dialog.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Work;
            Assert.Equal(PortableRollbackUiState.Ready,model.State);Assert.Equal(before,s.Snapshot());
            dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Settle(dialog);
            Assert.True(model.IsClosed);Assert.False(dialog.IsVisible);Assert.Equal(before,s.Snapshot());Assert.Equal(0,s.Network.Calls);
        }
        finally{dialog.Close();await dialog.Work;model.DisposeAfterDrain();}
    }

    [AvaloniaFact]
    public async Task Actual_click_is_single_apply_with_observed_commit_and_unavailable_history_is_not_success()
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);await using var session=new LauncherSession();
        var model=new PortableRollbackViewModel(session,manager,s.Game.InstanceKey);var dialog=new PortableRollbackDialog(model);
        try
        {
            dialog.Show();dialog.BeginPreview();await dialog.Work;var before=model.Plan!;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var work=dialog.Work;dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await work;
            Assert.Equal(PortableRollbackUiState.Completed,model.State);Assert.True(model.Result!.Completed);Assert.Equal("v1",model.Result.ActualVersion);Assert.Equal(before.PlanSha256,model.Result.PlanSha256);Assert.False(model.CanApply);
            Assert.Equal(model.Result.ActualOperationId,s.Tx.ReadCommitted()!.OperationId);Assert.Equal(0,s.Network.Calls);
        }
        finally{dialog.Close();await dialog.Work;model.DisposeAfterDrain();}
        using var empty=new PortableBackupCleanupBaselineScope();await empty.Install("only");using var em=PortableBackupCleanupServiceTests.Manager(empty);
        var unavailable=new PortableRollbackViewModel(session,em,empty.Game.InstanceKey);var snapshot=empty.Snapshot();await unavailable.PreviewAsync();
        Assert.Equal(PortableRollbackUiState.Error,unavailable.State);Assert.False(unavailable.CanApply);Assert.Null(unavailable.Plan);Assert.Contains("No complete previous",unavailable.Message);Assert.Equal(snapshot,empty.Snapshot());unavailable.Close();unavailable.DisposeAfterDrain();
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task Blocked_preview_close_or_shutdown_drains_without_late_UI_and_keeps_dispatcher_responsive(bool shutdown)
    {
        using var s=new PortableBackupCleanupBaselineScope();await PortableBackupCleanupServiceTests.Install(s,2);using var manager=PortableBackupCleanupServiceTests.Manager(s);var session=new LauncherSession();
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var release=new ManualResetEventSlim();var changes=0;var applies=0;
        var model=new PortableRollbackViewModel(session,s.Game.InstanceKey,()=>{started.TrySetResult();release.Wait();return PortableRollbackService.Preview(manager,s.Game.InstanceKey);},(_,_)=>{Interlocked.Increment(ref applies);throw new InvalidOperationException();});
        var dialog=new PortableRollbackDialog(model);using var lifetime=new LauncherDialogLifetime(session.Token,a=>Dispatcher.UIThread.Post(a));lifetime.Track(dialog);Task? drain=null;
        try
        {
            var before=s.Snapshot();dialog.Show();dialog.BeginPreview();await started.Task.WaitAsync(TimeSpan.FromSeconds(10));bool responsive=false;await Dispatcher.UIThread.InvokeAsync(()=>responsive=true);Assert.True(responsive);Assert.False(dialog.Work.IsCompleted);
            if(shutdown){drain=session.DisposeAsync().AsTask();Assert.False(drain.IsCompleted);await Dispatcher.UIThread.InvokeAsync(()=>{});}else dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(model.IsClosed);Assert.False(dialog.IsVisible);model.PropertyChanged+=(_,_)=>changes++;
            await using var replacement=new LauncherSession();var other=new PortableRollbackViewModel(replacement,manager,s.Game.InstanceKey);await other.PreviewAsync();Assert.True(other.CanApply);
            release.Set();await dialog.Work;if(drain!=null)await drain;
            Assert.Equal(0,changes);Assert.Equal(0,applies);Assert.Null(model.Plan);Assert.Equal(before,s.Snapshot());other.Close();other.DisposeAfterDrain();
        }
        finally{release.Set();dialog.Close();await dialog.Work;model.DisposeAfterDrain();await session.DisposeAsync();}
    }

    [AvaloniaTheory][InlineData("ListViewTemplate")][InlineData("GridViewTemplate")][InlineData("CompactGridViewTemplate")]
    public async Task Actual_layout_click_routes_exact_captured_key_and_locked_policy_refuses(string template)
    {
        var store=new PortableRollbackServiceTests.RecoveryStore(new AppSettings());
        var main=new MainView(new(){SettingsStore=store,EnableInput=false,EnableMusic=false,InitializeOnOpen=false});
        var library=main.FindControl<LibraryView>("LibraryPanel")!;var first=new GameInfo{Name="Same",Repository="fixture/one",FolderName="one"};var later=new GameInfo{Name="Same",Repository="fixture/two",FolderName="two"};
        string? captured=null;var calls=0;library.ConfigurePortableRollback((game,_)=>{captured=game!.InstanceKey;calls++;return Task.CompletedTask;});
        var card=((IDataTemplate)library.Resources[template]!).Build(first)!;card.DataContext=first;library.FindControl<Grid>("Surface")!.Children.Add(card);var window=new Window{Content=main,Width=1000,Height=750};
        try
        {
            window.Show();Settle(window);var item=card.GetVisualDescendants().OfType<Control>().Prepend(card).Select(c=>c.ContextMenu).OfType<ContextMenu>().SelectMany(m=>m.Items.OfType<MenuItem>()).Single(i=>i.Name=="RollbackPortableVersionMenu");
            item.CommandParameter=first;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));card.DataContext=later;Settle(window);Assert.Equal(first.InstanceKey,captured);Assert.Equal(1,calls);
            store.ReadOnly=true;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Settle(window);Assert.Equal(1,calls);
        }
        finally{window.Close();await main.ShutdownAsync();}
    }

    [AvaloniaTheory][InlineData(false,360)][InlineData(true,360)][InlineData(false,760)][InlineData(true,760)]
    public async Task Complete_oversized_identities_hash_paths_and_warnings_are_reached_by_real_keyboard_controller_without_apply(bool keyboard,int width)
    {
        await using var session=new LauncherSession();var id=new string('v',999);var path=string.Join('/',Enumerable.Repeat(new string('p',60),16));
        var plan=new PortableRollbackPreview(new string('a',64),new(){SelectedVersion=id,OperationId=new string('b',32),DataPaths=[path]},new(){SelectedVersion="target-"+id,OperationId=new string('c',32),SelectedExecutable=path});
        var applies=0;var model=new PortableRollbackViewModel(session,"fixture-key",()=>plan,(_,_)=>{applies++;throw new InvalidOperationException("Inspection cannot apply");});var dialog=new PortableRollbackDialog(model){Width=width,Height=640};
        var nav=GamepadModalDialogNavigation.Instance;var old=nav.ResolveKeyboardAction;var chrome=GamepadFocusChrome.IsActive;
        try
        {
            nav.ResolveKeyboardAction=(key,_)=>key switch{Key.Up=>GamepadAction.NavUp,Key.Down=>GamepadAction.NavDown,Key.Left=>GamepadAction.NavLeft,Key.Right=>GamepadAction.NavRight,Key.Enter=>GamepadAction.Confirm,Key.Escape=>GamepadAction.Cancel,_=>null};GamepadFocusChrome.SetActive(true,dialog);
            dialog.Show();dialog.BeginPreview();await dialog.Work;Settle(dialog);Assert.True(dialog.CancelButton.IsFocused);
            var inspectors=GamepadModalDialogNavigation.CollectDialogFocusableControls(dialog).OfType<Button>().Where(b=>b.Content is TextBlock).ToArray();Assert.True(inspectors.Length>40);
            var text=string.Concat(dialog.Details.Children.OfType<Button>().Select(b=>((TextBlock)b.Content!).Text));Assert.Contains(id,text);Assert.Contains(path,text);Assert.Contains(plan.PlanSha256,text);
            var scroll=dialog.GetVisualDescendants().OfType<ScrollViewer>().First(s=>s.Content is StackPanel);var visited=new HashSet<Button>();
            void Move(NavigationDirection d){if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=d==NavigationDirection.Up?Key.Up:d==NavigationDirection.Down?Key.Down:Key.Left});else Assert.True(nav.TryHandleNavigation(d));Settle(dialog);}
            Move(NavigationDirection.Up);for(var i=0;i<inspectors.Length+5;i++)Move(NavigationDirection.Up);
            for(var i=0;i<inspectors.Length+5;i++)
            {
                if(dialog.FocusManager!.GetFocusedElement() is Button b&&inspectors.Contains(b))
                {
                    var y=b.TranslatePoint(new Point(0,0),scroll)!.Value.Y;Assert.InRange(y,-1,scroll.Viewport.Height);Assert.True(y+b.Bounds.Height<=scroll.Viewport.Height+1);
                    visited.Add(b);if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});else Assert.True(nav.TryHandleConfirm());Assert.Equal(0,applies);Assert.True(dialog.IsVisible);
                }
                if(visited.Count==inspectors.Length)break;Move(NavigationDirection.Down);
            }
            Assert.Equal(inspectors.Length,visited.Count);for(var i=0;i<inspectors.Length+5&&!dialog.CancelButton.IsFocused;i++)Move(NavigationDirection.Down);if(!dialog.CancelButton.IsFocused)Move(NavigationDirection.Left);Assert.True(dialog.CancelButton.IsFocused);Assert.Equal(0,applies);
        }
        finally{dialog.Close();await dialog.Work;model.DisposeAfterDrain();nav.ResolveKeyboardAction=old;GamepadFocusChrome.SetActive(chrome);}
    }
    static void Settle(Window w){Dispatcher.UIThread.RunJobs();w.UpdateLayout();Dispatcher.UIThread.RunJobs();}
}
