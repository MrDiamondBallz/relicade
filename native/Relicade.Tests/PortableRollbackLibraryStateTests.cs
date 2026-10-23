using Relicade.Core.Models;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class PortableRollbackLibraryStateTests
{
    [AvaloniaTheory]
    [InlineData("build")]
    [InlineData("executable")]
    [InlineData("readiness")]
    public async Task Confirmed_restore_does_not_keep_metadata_from_the_replaced_generation(string field)
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        scope.Game.Repository = "perfect-dark-pc-port/perfect_dark";
        scope.SaveLibrary();
        async Task Install(string version, string name, char digest)
        {
            var transaction = new Relicade.Core.Services.PortableInstallationTransaction(scope.Live, new()
            {
                AssetIdentity = name + "|sha256:" + new string(digest, 64),
                InstalledBuildText = name + "\n" + new string(digest, 64) + "\n"
            });
            await transaction.InstallAsync(path =>
            {
                File.WriteAllBytes(Path.Combine(path, name), PortableInstallationFixtures.NativePayload(version));
                return Task.CompletedTask;
            }, version);
        }
        await Install("v1", "old.AppImage", 'a');
        await Install("v2", "new.AppImage", 'b');
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        var game = scope.Game;
        game.InstalledVersion = "v2";
        game.LatestVersion = "v2";
        game.Status = GameStatus.Installed;
        game.InstalledBuild = new InstalledBuild("new.AppImage", new string('b', 64));
        game.SelectedExecutable = Path.Combine(scope.Live, "new.AppImage");
        // Deliberately stale cached readiness, not evidence of a real compatible ROM.
        game.OwnedDataReady = true;
        var starts = 0;
        game.GameProcessStarted += _ => starts++;
        await using var session = new LauncherSession();
        var model = new PortableRollbackViewModel(session, manager, game.InstanceKey);
        var dialog = new PortableRollbackDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await dialog.Work;
            Assert.True(model.Result!.Completed);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(scope.Live, "version.txt")).Trim());
            Assert.Equal(0, scope.Network.Calls); Assert.Equal(0, starts);
            if (field == "build") Assert.Equal(new InstalledBuild("old.AppImage", new string('a', 64)), game.InstalledBuild);
            if (field == "executable") Assert.Equal(Path.Combine(scope.Live, "old.AppImage"), game.SelectedExecutable);
            if (field == "readiness") Assert.False(game.OwnedDataReady);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("removed")]
    [InlineData("replaced")]
    [InlineData("root")]
    [InlineData("newer-version")]
    [InlineData("newer-selection")]
    [InlineData("updating")]
    [InlineData("kiosk")]
    [InlineData("closed")]
    [InlineData("cancel-after-commit")]
    public async Task Committed_restore_does_not_overwrite_changed_context_or_closed_participation(string change)
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(scope, 2);
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        var game = scope.Game;
        game.InstalledVersion = "v2"; game.LatestVersion = "v2"; game.Status = GameStatus.Installed;
        var committed = new TaskCompletionSource<Relicade.Core.Services.PortableRollbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var session = new LauncherSession();
        var model = new PortableRollbackViewModel(session, manager, game.InstanceKey, (hash, token) =>
        {
            var result = PortableRollbackService.Apply(manager, game.InstanceKey, hash, token);
            committed.TrySetResult(result);
            release.Wait();
            return result;
        });
        var dialog = new PortableRollbackDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            var result = await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Completed);
            Assert.False(dialog.Work.IsCompleted);
            switch (change)
            {
                case "removed": manager.Games.Clear(); break;
                case "replaced": manager.Games[0] = new GameInfo { Repository = game.Repository, FolderName = game.FolderName, InstalledVersion = "replacement" }; break;
                case "root": game.InstallPath = Path.Combine(scope.Root, "different"); break;
                case "newer-version": game.InstalledVersion = "v3"; break;
                case "newer-selection": game.SelectedExecutable = "newer-user-selection"; break;
                case "updating": game.Status = GameStatus.Updating; break;
                case "kiosk": scope.Store.Current.KioskMode = true; break;
                case "closed": dialog.Close(); break;
                case "cancel-after-commit": model.Cancel(); break;
            }
            release.Set(); await dialog.Work;
            Assert.Equal("v1", File.ReadAllText(Path.Combine(scope.Live, "version.txt")).Trim());
            Assert.Equal(0, scope.Network.Calls);
            if (change == "closed") { Assert.True(model.IsClosed); Assert.Null(model.Result); }
            else
            {
                Assert.Same(result, model.Result); Assert.Equal(PortableRollbackUiState.Completed, model.State);
                if (change != "cancel-after-commit") Assert.Contains("Library state changed", model.Message);
            }
            Assert.Equal(change == "cancel-after-commit" ? "v1" : change == "newer-version" ? "v3" : "v2", game.InstalledVersion);
            if (change == "newer-selection") Assert.Equal("newer-user-selection", game.SelectedExecutable);
            if (change == "updating") Assert.Equal(GameStatus.Updating, game.Status);
            if (change == "replaced") Assert.Equal("replacement", Assert.Single(manager.Games).InstalledVersion);
        }
        finally { release.Set(); dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("same-values-new-generation")]
    public async Task A_new_generation_with_identical_cached_values_rejects_the_older_rollback_result(string change)
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(scope, 2);
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        var game = scope.Game;
        game.InstalledVersion = "v2"; game.LatestVersion = "v2"; game.Status = GameStatus.Installed;
        var committed = new TaskCompletionSource<Relicade.Core.Services.PortableRollbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var session = new LauncherSession();
        var model = new PortableRollbackViewModel(session, manager, game.InstanceKey, (hash, token) =>
        {
            var result = PortableRollbackService.Apply(manager, game.InstanceKey, hash, token);
            committed.TrySetResult(result);
            release.Wait();
            return result;
        });
        var dialog = new PortableRollbackDialog(model);
        try
        {
            dialog.Show(); dialog.BeginPreview(); await dialog.Work;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            var result = await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Completed);
            Assert.False(dialog.Work.IsCompleted);
            switch (change)
            {
                case "same-values-new-generation": await scope.Install("v2"); break;
                case "removed": manager.Games.Clear(); break;
                case "replaced": manager.Games[0] = new GameInfo { Repository = game.Repository, FolderName = game.FolderName, InstalledVersion = "replacement" }; break;
                case "root": game.InstallPath = Path.Combine(scope.Root, "different"); break;
                case "newer-version": game.InstalledVersion = "v3"; break;
                case "newer-selection": game.SelectedExecutable = "newer-user-selection"; break;
                case "updating": game.Status = GameStatus.Updating; break;
                case "kiosk": scope.Store.Current.KioskMode = true; break;
                case "closed": dialog.Close(); break;
                case "cancel-after-commit": model.Cancel(); break;
            }
            release.Set(); await dialog.Work;
            Assert.Equal(change == "same-values-new-generation" ? "v2" : "v1", File.ReadAllText(Path.Combine(scope.Live, "version.txt")).Trim());
            Assert.Equal("v2", game.InstalledVersion);
            Assert.Equal(0, scope.Network.Calls);
            if (change == "closed") { Assert.True(model.IsClosed); Assert.Null(model.Result); }
            else
            {
                Assert.Same(result, model.Result); Assert.Equal(PortableRollbackUiState.Completed, model.State);
                if (change != "cancel-after-commit") Assert.Contains("Library state changed", model.Message);
            }
            Assert.Equal(change == "cancel-after-commit" ? "v1" : change == "newer-version" ? "v3" : "v2", game.InstalledVersion);
            if (change == "newer-selection") Assert.Equal("newer-user-selection", game.SelectedExecutable);
            if (change == "updating") Assert.Equal(GameStatus.Updating, game.Status);
            if (change == "replaced") Assert.Equal("replacement", Assert.Single(manager.Games).InstalledVersion);
        }
        finally { release.Set(); dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("callback-failure")]
    [InlineData("cancel-during-callback")]
    public async Task Observation_authority_spans_actual_callback_drain_without_blocking_dispatcher(string outcome)
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(scope, 2);
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        var plan = PortableRollbackService.Preview(manager, scope.Game.InstanceKey);
        var result = PortableRollbackService.Apply(manager, scope.Game.InstanceKey, plan.PlanSha256);
        File.WriteAllText(Path.Combine(scope.Live, "LastPlayed.txt"), "CURRENT_DATA_AFTER_COMMIT");
        var before = scope.Snapshot();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Task.Run(() => scope.Tx.PublishObservedRollbackAsync(result, async () =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => entered.TrySetResult());
            var answer = await release.Task;
            if (outcome == "callback-failure") throw new IOException("SYNTHETIC_PUBLICATION_FAILURE");
            return answer;
        }, cancellation.Token));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var responsive = false;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => responsive = true);
            Assert.True(responsive);
            if (outcome == "cancel-during-callback") cancellation.Cancel();
            Assert.False(work.IsCompleted);
            Assert.ThrowsAny<IOException>(() => { using var refused = scope.Tx.AcquireLease(); });
            release.TrySetResult(outcome != "cancel-during-callback");
            if (outcome == "callback-failure") await Assert.ThrowsAsync<IOException>(() => work);
            else Assert.Equal(outcome == "success", await work);
            using (scope.Tx.AcquireLease()) { }
            Assert.Equal(before, scope.Snapshot());
            Assert.True(result.Completed); Assert.Equal(0, scope.Network.Calls);
        }
        finally { release.TrySetResult(false); try { await work; } catch (IOException) { } }
    }

    [AvaloniaTheory]
    [InlineData("pending")]
    [InlineData("missing-manifest")]
    [InlineData("busy")]
    public async Task Uncertain_observation_never_repairs_or_publishes_and_preserves_committed_result(string uncertainty)
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(scope, 2);
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        var plan = PortableRollbackService.Preview(manager, scope.Game.InstanceKey);
        var result = PortableRollbackService.Apply(manager, scope.Game.InstanceKey, plan.PlanSha256);
        var pendingPath = Path.Combine(scope.Tx.OperationsPath, Guid.NewGuid().ToString("N"));
        if (uncertainty == "pending") Directory.CreateDirectory(pendingPath);
        if (uncertainty == "missing-manifest") File.Move(Path.Combine(scope.Live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName), Path.Combine(scope.Root, "retained-manifest"));
        var before = scope.Snapshot();
        var calls = 0;
        using (var held = uncertainty == "busy" ? scope.Tx.AcquireLease() : null)
        {
            await Assert.ThrowsAnyAsync<IOException>(() => Task.Run(() => scope.Tx.PublishObservedRollbackAsync(result,
                () => { calls++; return Task.FromResult(true); })));
        }
        Assert.Equal(0, calls); Assert.Equal(before, scope.Snapshot());
        if (uncertainty == "pending") Assert.True(Directory.Exists(pendingPath));
        Assert.True(result.Completed); Assert.Equal(0, scope.Network.Calls);
    }

    [AvaloniaFact]
    public async Task Confirmed_restore_updates_the_same_live_library_version_without_reloading_or_launching()
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(scope, 2);
        File.WriteAllText(Path.Combine(scope.Live, "LastPlayed.txt"), "CURRENT_USER_DATA");
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        var game = scope.Game;
        game.InstalledVersion = "v2";
        game.LatestVersion = "v2";
        game.Status = GameStatus.Installed;
        var collection = manager.Games;
        var settings = File.ReadAllBytes(scope.Settings);
        var library = File.ReadAllBytes(scope.Library);
        var starts = 0;
        game.GameProcessStarted += _ => starts++;
        await using var session = new LauncherSession();
        var model = new PortableRollbackViewModel(session, manager, game.InstanceKey);
        var dialog = new PortableRollbackDialog(model);
        try
        {
            dialog.Show();
            dialog.BeginPreview();
            await dialog.Work;
            Assert.Equal("v2", model.Plan!.CurrentVersion);
            Assert.Equal("v1", model.Plan.TargetVersion);
            Assert.Equal("v2", game.InstalledVersion);
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await dialog.Work;
            Assert.True(model.Result!.Completed);
            Assert.Equal("v1", model.Result.ActualVersion);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(scope.Live, "version.txt")).Trim());
            Assert.Equal("CURRENT_USER_DATA", File.ReadAllText(Path.Combine(scope.Live, "LastPlayed.txt")));
            Assert.Same(collection, manager.Games);
            Assert.Same(game, Assert.Single(manager.Games));
            Assert.Equal(settings, File.ReadAllBytes(scope.Settings));
            Assert.Equal(library, File.ReadAllBytes(scope.Library));
            Assert.Equal(0, scope.Network.Calls);
            Assert.Equal(0, starts);
            Assert.Equal("v1", game.InstalledVersion);
            Assert.Equal(GameStatus.UpdateAvailable, game.Status);
        }
        finally
        {
            dialog.Close();
            await dialog.Work;
            model.DisposeAfterDrain();
        }
    }
}
