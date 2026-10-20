using System.Collections.ObjectModel;
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

namespace Relicade.Tests;

public sealed class PortableBackupCleanupUiTests
{
    [AvaloniaFact]
    public async Task Real_dialog_preview_cancel_default_focus_escape_no_confirm_and_empty_are_readonly()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s);
        using var manager = PortableBackupCleanupServiceTests.Manager(s);
        await using var session = new LauncherSession();
        var before = s.Snapshot(); var model = new PortableBackupCleanupViewModel(session, manager, s.Game.InstanceKey);
        var dialog = new PortableBackupCleanupDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work; Settle(dialog);
            Assert.Equal(PortableBackupCleanupUiState.Ready, model.State);
            Assert.True(model.CanApply); Assert.True(dialog.CancelButton.IsDefault); Assert.False(dialog.ApplyButton.IsDefault);
            Assert.True(dialog.CancelButton.IsFocused);
            Assert.Contains(model.Plan!.PlanSha256, dialog.Hash.Text);
            Assert.Equal(model.Plan.Files.Count, dialog.Files.Items.Count);
            Assert.Contains(model.Plan.ReclaimableBytes.ToString(), dialog.Summary.Text);
            Assert.Same(dialog, GamepadModalDialogNavigation.Instance.ActiveDialog);
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Settle(dialog);
            Assert.False(dialog.IsVisible); Assert.True(model.IsClosed);
            Assert.NotSame(dialog, GamepadModalDialogNavigation.Instance.ActiveDialog);
            Assert.Equal(before, s.Snapshot()); Assert.Equal(0, s.Network.Calls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
        using var empty = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(empty, 2);
        using var em = PortableBackupCleanupServiceTests.Manager(empty);
        var emptyModel = new PortableBackupCleanupViewModel(session, em, empty.Game.InstanceKey);
        var emptyDialog = new PortableBackupCleanupDialog(emptyModel);
        try
        {
            var snapshot = empty.Snapshot(); emptyDialog.Show(); emptyDialog.BeginPreview(); await emptyDialog.Work;
            Assert.Equal(PortableBackupCleanupUiState.Empty, emptyModel.State); Assert.False(emptyDialog.ApplyButton.IsEnabled);
            await emptyModel.ConfirmAsync(); Assert.Equal(snapshot, empty.Snapshot());
        }
        finally { emptyDialog.Close(); await emptyDialog.Work; emptyModel.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Actual_confirm_click_once_truthful_result_duplicate_clicks_and_same_instance_are_local()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s);
        using var manager = PortableBackupCleanupServiceTests.Manager(s); await using var session = new LauncherSession();
        var key = s.Game.InstanceKey; var model = new PortableBackupCleanupViewModel(session, manager, key);
        var dialog = new PortableBackupCleanupDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work; var plan = model.Plan!;
            var other = new GameInfo { Name = "Later selected tile", Repository = "fixture/other", FolderName = "other" };
            manager.Games.Add(other); // New selection/another tile is not the captured cleanup identity.
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var work = dialog.Work;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await work; Settle(dialog);
            Assert.Equal(key, model.InstanceKey); Assert.Equal(PortableBackupCleanupUiState.Completed, model.State);
            Assert.Equal(new BackupCleanupResult(plan.PlanSha256, plan.Files.Count, plan.ReclaimableBytes, true), model.Result);
            Assert.Contains("THIS invocation", dialog.Status.Text); Assert.False(model.CanApply); Assert.False(model.CanResume);
            Assert.Equal(0, s.Network.Calls); Assert.Empty(Directory.GetFiles(s.Tx.OperationsPath, "*.pending"));
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Stale_open_dialog_errors_never_auto_reconfirm_and_new_preview_requires_new_click()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s);
        using var manager = PortableBackupCleanupServiceTests.Manager(s); await using var session = new LauncherSession();
        var model = new PortableBackupCleanupViewModel(session, manager, s.Game.InstanceKey);
        var dialog = new PortableBackupCleanupDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work; var oldHash = model.Plan!.PlanSha256;
            File.AppendAllText(s.Library, " "); var before = s.Snapshot();
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(PortableBackupCleanupUiState.Error, model.State); Assert.Null(model.Result);
            Assert.False(model.CanApply); Assert.False(model.CanResume); Assert.Equal(before, s.Snapshot());
            await model.ConfirmAsync(); Assert.Equal(before, s.Snapshot());
            dialog.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(PortableBackupCleanupUiState.Ready, model.State); Assert.NotEqual(oldHash, model.Plan!.PlanSha256);
            Assert.Equal(before, s.Snapshot()); // Refresh alone cannot apply.
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_partial_service_cancel_or_failure_displays_real_counts_and_explicit_same_hash_resume(bool failure)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 5);
        using var manager = PortableBackupCleanupServiceTests.Manager(s); await using var session = new LauncherSession();
        var original = SettingsStoreProvider.Default; var rawLibrary = File.ReadAllBytes(s.Library);
        var plan = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        var first = Path.Combine(s.Tx.OperationsPath, plan.Files[0].OperationId, plan.Files[0].BackupRelativePath);
        var signalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var continueWorker = new ManualResetEventSlim(); bool triggered = false;
        var hook = new HookStore(s.Store, () =>
        {
            if (triggered || File.Exists(first)) return;
            triggered = true; signalled.TrySetResult(); continueWorker.Wait();
            if (failure) File.WriteAllText(s.Library, "{changed-during-apply");
        });
        SettingsStoreProvider.Default = hook;
        var model = new PortableBackupCleanupViewModel(session, manager, s.Game.InstanceKey);
        var dialog = new PortableBackupCleanupDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work;
            Assert.Equal(plan.PlanSha256, model.Plan!.PlanSha256);
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var apply = dialog.Work;
            await signalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (!failure) dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible); Assert.False(model.CanResume); Assert.False(apply.IsCompleted);
            continueWorker.Set(); await apply;
            Assert.Equal(PortableBackupCleanupUiState.Interrupted, model.State);
            Assert.NotNull(model.Result); Assert.False(model.Result.Completed); Assert.True(model.Result.DeletedFiles > 0);
            Assert.True(model.Result.DeletedFiles < plan.Files.Count); Assert.True(model.Result.ReclaimedBytes > 0);
            Assert.Equal(plan.PlanSha256, model.Result.PlanSha256); Assert.True(model.CanResume);
            Assert.Contains("Incomplete", dialog.Status.Text); Assert.Contains("THIS invocation", dialog.Status.Text);
            var interrupted = s.Snapshot(); await Task.Yield(); Assert.Equal(interrupted, s.Snapshot()); // No automatic retry.
            File.WriteAllBytes(s.Library, rawLibrary); SettingsStoreProvider.Default = original;
            var removed = model.Result.DeletedFiles;
            dialog.ResumeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(PortableBackupCleanupUiState.Completed, model.State); Assert.Equal(plan.PlanSha256, model.Result!.PlanSha256);
            Assert.Equal(plan.Files.Count - removed, model.Result.DeletedFiles);
            Assert.Equal(new BackupCleanupResult(plan.PlanSha256,0,0,true), PortableBackupCleanupService.Apply(manager,s.Game.InstanceKey,plan.PlanSha256));
            Assert.Equal(0,s.Network.Calls);
        }
        finally { continueWorker.Set(); SettingsStoreProvider.Default = original; dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Blocked_hashing_keeps_dispatcher_responsive_close_or_session_shutdown_drains_without_late_UI(bool shutdown)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s);
        using var manager = PortableBackupCleanupServiceTests.Manager(s); var session = new LauncherSession();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); int changes = 0; int applyCalls = 0;
        var model = new PortableBackupCleanupViewModel(session, s.Game.InstanceKey,
            () => { started.TrySetResult(); release.Wait(); return PortableBackupCleanupService.Preview(manager,s.Game.InstanceKey); },
            (hash,token) => { Interlocked.Increment(ref applyCalls); return PortableBackupCleanupService.Apply(manager,s.Game.InstanceKey,hash,token); });
        var dialog = new PortableBackupCleanupDialog(model);
        using var lifetime = new LauncherDialogLifetime(session.Token, action => Dispatcher.UIThread.Post(action)); lifetime.Track(dialog);
        Task? drain = null;
        try
        {
            var before = s.Snapshot(); dialog.Show(); dialog.BeginPreview(); await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bool dispatcherResponded = false; await Dispatcher.UIThread.InvokeAsync(() => dispatcherResponded = true);
            Assert.True(dispatcherResponded); Assert.False(dialog.Work.IsCompleted);
            if (shutdown) { drain = session.DisposeAsync().AsTask(); Assert.False(drain.IsCompleted); await Dispatcher.UIThread.InvokeAsync(() => { }); }
            else dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(model.IsClosed); Assert.False(dialog.IsVisible);
            model.PropertyChanged += (_, _) => changes++;
            var replacementSession = new LauncherSession();
            var replacement = new PortableBackupCleanupViewModel(replacementSession,manager,s.Game.InstanceKey);
            await replacement.PreviewAsync(); Assert.Equal(PortableBackupCleanupUiState.Ready,replacement.State);
            release.Set(); await dialog.Work; if (drain != null) await drain;
            Assert.Equal(0, changes); Assert.Null(model.Plan); Assert.Equal(0,applyCalls);
            Assert.Equal(before,s.Snapshot()); Assert.Equal(PortableBackupCleanupUiState.Ready,replacement.State);
            replacement.Close(); replacement.DisposeAfterDrain(); await replacementSession.DisposeAsync();
        }
        finally { release.Set(); dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); await session.DisposeAsync(); }
    }

    [AvaloniaTheory]
    [InlineData("ListViewTemplate")][InlineData("GridViewTemplate")][InlineData("CompactGridViewTemplate")]
    public async Task Actual_all_layout_menu_click_routes_captured_exact_key_and_kiosk_recovery_refuses(string template)
    {
        var store = new MemoryStore();
        store.Current.KioskMode=true;
        var main = new MainView(new() { SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        main.SettingsModel.UnlockKioskSession();
        var library = main.FindControl<LibraryView>("LibraryPanel")!;
        var first = new GameInfo { Name = "Same name",Repository="fixture/one",FolderName="one" };
        var later = new GameInfo { Name = "Same name",Repository="fixture/two",FolderName="two" };
        string? captured = null; int calls = 0;
        library.ConfigureBackupCleanup((game,_) => { captured=game!.InstanceKey;calls++;return Task.CompletedTask; });
        var card = ((IDataTemplate)library.Resources[template]!).Build(first)!; card.DataContext=first;
        library.FindControl<Grid>("Surface")!.Children.Add(card);
        var window = new Window { Content=main,Width=1000,Height=750 };
        try
        {
            window.Show();Settle(window);
            var menu = card.GetVisualDescendants().OfType<Control>().Prepend(card).Select(c=>c.ContextMenu).OfType<ContextMenu>().Single();
            var menuAnchor = card.GetVisualDescendants().OfType<Control>().Prepend(card).Single(c=>ReferenceEquals(c.ContextMenu,menu));
            menu.Open(menuAnchor);Settle(window);
            var item = menu.Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()=="Clean redundant backups…");
            Assert.Same(first,item.CommandParameter);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Dispatcher.UIThread.InvokeAsync(()=>{});
            Assert.Equal(first.InstanceKey,captured); Assert.Equal(1,calls);
            main.SettingsModel.LockKioskSession();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal(1,calls);
            LibraryKioskMenu.Apply(menu,true);Assert.False(item.IsEnabled);
            main.SettingsModel.UnlockKioskSession();store.ReadOnly=true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Assert.Equal(1,calls);
            Assert.NotEqual(first.InstanceKey,later.InstanceKey);
        }
        finally { window.Close();await main.ShutdownAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Session_owned_flow_rejects_duplicate_open_and_restores_origin_or_drains_shutdown(bool shutdown)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s);
        using var manager = PortableBackupCleanupServiceTests.Manager(s); var session = new LauncherSession();
        var settings = new SettingsViewModel(s.Store);
        using var lifetime = new LauncherDialogLifetime(session.Token, a => Dispatcher.UIThread.Post(a));
        var flow = new PortableBackupCleanupFlow(manager, settings, session, lifetime);
        var origin = new Button { Content = "Selected library tile" }; var host = new Window { Content = origin };
        var before = s.Snapshot();
        try
        {
            host.Show(); origin.Focus();
            var open = flow.OpenAsync(s.Game, origin);
            var dialog = Assert.IsType<PortableBackupCleanupDialog>(GamepadModalDialogNavigation.Instance.ActiveDialog);
            await dialog.Work; Settle(dialog); Assert.True(flow.IsActive);
            await flow.OpenAsync(s.Game, origin); Assert.Same(dialog, GamepadModalDialogNavigation.Instance.ActiveDialog);
            Assert.False(open.IsCompleted); Assert.Equal(before, s.Snapshot());
            if (shutdown) await session.DisposeAsync();
            else Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm()); // default Cancel, not Clean
            await open; Settle(host);
            Assert.False(flow.IsActive); Assert.True(dialog.Model.IsClosed);
            Assert.NotSame(dialog, GamepadModalDialogNavigation.Instance.ActiveDialog);
            if (!shutdown) Assert.True(origin.IsFocused);
            Assert.Equal(before, s.Snapshot()); Assert.Equal(0, s.Network.Calls);
        }
        finally { host.Close(); await session.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task Apply_session_shutdown_cancels_and_drains_without_late_result_or_disposing_manager()
    {
        using var s = new PortableBackupCleanupBaselineScope(); await PortableBackupCleanupServiceTests.Install(s, 5);
        using var manager = PortableBackupCleanupServiceTests.Manager(s); var session = new LauncherSession();
        var plan = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        var first = Path.Combine(s.Tx.OperationsPath, plan.Files[0].OperationId, plan.Files[0].BackupRelativePath);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); bool held = false; bool disposed = false; int changes = 0;
        var original = SettingsStoreProvider.Default;
        SettingsStoreProvider.Default = new HookStore(s.Store, () => { if (!held && !File.Exists(first)) { held = true; reached.TrySetResult(); release.Wait(); } });
        var model = new PortableBackupCleanupViewModel(session, manager, s.Game.InstanceKey);
        var dialog = new PortableBackupCleanupDialog(model);
        using var lifetime = new LauncherDialogLifetime(session.Token, a => Dispatcher.UIThread.Post(a)); lifetime.Track(dialog);
        session.OnShutdown(() => disposed = true);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var drain = session.DisposeAsync().AsTask(); await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.False(drain.IsCompleted); Assert.False(disposed); Assert.True(model.IsClosed); Assert.False(dialog.IsVisible);
            model.PropertyChanged += (_, _) => changes++;
            release.Set(); await dialog.Work; await drain;
            Assert.True(disposed); Assert.Equal(0, changes); Assert.Null(model.Result);
            SettingsStoreProvider.Default = original;
            var resumed = PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, plan.PlanSha256);
            Assert.True(resumed.Completed); Assert.InRange(resumed.DeletedFiles, 1, plan.Files.Count - 1);
            Assert.Equal(0, s.Network.Calls);
        }
        finally { release.Set(); SettingsStoreProvider.Default = original; dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); await session.DisposeAsync(); }
    }

    static void Settle(Window window) { Dispatcher.UIThread.RunJobs();window.UpdateLayout();Dispatcher.UIThread.RunJobs(); }
    sealed class HookStore(FileSettingsStore store,Action onLoad):ISettingsStore,IPortableBackupCleanupSettingsEvidence
    {
        public AppSettings Current=>store.Current;
        public AppSettings Load()=>throw new Exception("Unexpected settings Load");
        public (AppSettings Settings,string Path,string RawSha256) ReadBackupCleanupSettings()
        { onLoad();return ((IPortableBackupCleanupSettingsEvidence)store).ReadBackupCleanupSettings(); }
        public void Save(AppSettings settings)=>throw new Exception("Unexpected settings save");
    }
    sealed class MemoryStore:ISettingsStore,ISettingsRecoveryState
    {
        public AppSettings Current{get;}=new(){FirstStartup=false,AppsPath=Path.Combine(Path.GetTempPath(),"cleanup-ui-menu-"+Guid.NewGuid().ToString("N"))};
        public bool ReadOnly;public bool IsReadOnly=>ReadOnly;public Exception? ReadError=>ReadOnly?new IOException("Synthetic recovery"):null;
        public AppSettings Load()=>Current;public void Save(AppSettings settings)=>throw new Exception("Unexpected settings save");
    }
}
