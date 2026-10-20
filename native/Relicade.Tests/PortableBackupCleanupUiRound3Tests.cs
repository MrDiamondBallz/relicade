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

public sealed class PortableBackupCleanupUiRound3Tests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false,360)][InlineData(true,360)][InlineData(false,760)][InlineData(true,760)]
    public async Task Actual_service_nested_paths_have_complete_visible_content_coverage_by_keyboard_and_controller(bool keyboard,int width)
    {
        using var scope=new PortableBackupCleanupBaselineScope();
        // Four genuine portable generations, never execute the synthetic payload.
        var path=string.Concat(Enumerable.Repeat(new string('p',60)+"/",16));
        for(int generation=1;generation<=4;generation++)
        {
            var version="longpath-"+generation;
            await scope.Tx.InstallAsync(root=>
            {
                File.WriteAllBytes(Path.Combine(root,"fixture.AppImage"),PortableInstallationFixtures.NativePayload(version));
                var nested=Path.Combine(root,path,"game-payload.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
                File.WriteAllText(nested,"synthetic immutable "+version);
                return Task.CompletedTask;
            },version);
        }
        using var manager=PortableBackupCleanupServiceTests.Manager(scope);
        var before=scope.Snapshot();var settings=manager.CurrentSettings;var games=manager.Games;
        var plan=PortableBackupCleanupService.Preview(manager,scope.Game.InstanceKey);
        Assert.Equal(4,plan.Files.Count);Assert.Contains(plan.Files,f=>f.BackupRelativePath.Length==999);
        await using var session=new LauncherSession();int applies=0;
        var model=new PortableBackupCleanupViewModel(session,scope.Game.InstanceKey,()=>plan,(_,_)=>{applies++;throw new Exception("Inspection never authorizes cleanup");});
        var dialog=new PortableBackupCleanupDialog(model){Width=width,Height=640};
        var nav=GamepadModalDialogNavigation.Instance;var old=nav.ResolveKeyboardAction;var chrome=GamepadFocusChrome.IsActive;
        try
        {
            nav.ResolveKeyboardAction=(key,_)=>key switch{Key.Up=>GamepadAction.NavUp,Key.Down=>GamepadAction.NavDown,Key.Left=>GamepadAction.NavLeft,Key.Enter=>GamepadAction.Confirm,Key.Escape=>GamepadAction.Cancel,_=>null};
            GamepadFocusChrome.SetActive(true,dialog);dialog.Show();dialog.BeginPreview();await dialog.Work;Settle(dialog);
            Assert.Equal(width,dialog.ClientSize.Width);Assert.True(dialog.CancelButton.IsFocused);
            Assert.True(dialog.CancelButton.IsDefault);Assert.False(dialog.ApplyButton.IsDefault);
            Assert.Contains(plan.PlanSha256,dialog.Hash.Text);
            var scroll=dialog.GetVisualDescendants().OfType<ScrollViewer>().First(s=>s.Content is StackPanel);
            var text=dialog.Files.GetVisualDescendants().OfType<TextBlock>().Where(t=>!string.IsNullOrEmpty(t.Text)).ToArray();
            // Concatenating display subtargets must preserve every character of every
            // operation, backup/retained path, length and SHA, without ellipsis.
            Assert.Equal(string.Concat(plan.Files.Select(Description)),string.Concat(text.Select(t=>t.Text)));
            var seen=text.ToDictionary(t=>t,_=>new List<(double Start,double End)>());
            var controls=GamepadModalDialogNavigation.CollectDialogFocusableControls(dialog);
            void Observe()
            {
                foreach(var t in text)
                {
                    var point=t.TranslatePoint(new Point(0,0),scroll)!.Value;
                    var start=Math.Max(0,-point.Y);var end=Math.Min(t.Bounds.Height,scroll.Viewport.Height-point.Y);
                    if(end>start)
                    {
                        Assert.InRange(point.X,-1,scroll.Viewport.Width);
                        Assert.True(point.X+t.Bounds.Width<=scroll.Viewport.Width+1);
                        seen[t].Add((start,end));
                    }
                }
            }
            void Move(NavigationDirection direction)
            {
                if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=direction==NavigationDirection.Up?Key.Up:Key.Down});
                else Assert.True(nav.TryHandleNavigation(direction));
                Settle(dialog);Observe();
                if(dialog.FocusManager!.GetFocusedElement() is Button b&&b.Content is TextBlock)
                {
                    if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
                    else Assert.True(nav.TryHandleConfirm());
                    Assert.True(dialog.IsVisible);Assert.Equal(0,applies);Assert.Null(model.Result);
                }
            }
            Observe();
            // Physical modal input only: no direct focus/Offset/BringIntoView in
            // the test. Traverse both directions, observing settled geometry.
            for(int i=0;i<controls.Count+2;i++)Move(NavigationDirection.Up);
            for(int i=0;i<controls.Count+2;i++)Move(NavigationDirection.Down);
            foreach(var t in text)
            {
                double covered=0;
                foreach(var interval in seen[t].OrderBy(i=>i.Start))
                {
                    Assert.True(interval.Start<=covered+1,$"Unseen content at {covered}..{interval.Start}: {t.Text}");
                    covered=Math.Max(covered,interval.End);
                }
                output.WriteLine($"keyboard={keyboard}; width={width}; chars={t.Text!.Length}; height={t.Bounds.Height}; visibleThrough={covered}; viewport={scroll.Viewport.Height}");
                Assert.True(covered>=t.Bounds.Height-1,$"Unseen content at {covered}..{t.Bounds.Height}: {t.Text}");
            }
            // Return through the existing modal route, not a focus shortcut.
            for(int i=0;i<controls.Count+2&&!dialog.CancelButton.IsFocused;i++)Move(NavigationDirection.Down);
            if(!dialog.CancelButton.IsFocused)
            {
                if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Left});
                else Assert.True(nav.TryHandleNavigation(NavigationDirection.Left));
                Settle(dialog);
            }
            Assert.True(dialog.CancelButton.IsFocused);Assert.Equal(plan.PlanSha256,model.Plan!.PlanSha256);
            if(keyboard)dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});else Assert.True(nav.TryHandleConfirm());
            Assert.True(model.IsClosed);Assert.False(dialog.IsVisible);Assert.NotSame(dialog,nav.ActiveDialog);
            Assert.Equal(0,applies);Assert.Equal(before,scope.Snapshot());Assert.Same(settings,manager.CurrentSettings);Assert.Same(games,manager.Games);Assert.Equal(0,scope.Network.Calls);
        }
        finally{dialog.Close();await dialog.Work;model.DisposeAfterDrain();nav.ResolveKeyboardAction=old;GamepadFocusChrome.SetActive(chrome);}
    }
    static string Description(BackupCleanupFile file)=>$"{file.OperationId}/{file.BackupRelativePath}\nRetained: {file.OperationId}/{file.RetainedRelativePath}\n{file.Length} bytes · SHA256 {file.Sha256}";
    static void Settle(Window window){Dispatcher.UIThread.RunJobs();window.UpdateLayout();Dispatcher.UIThread.RunJobs();}
}
