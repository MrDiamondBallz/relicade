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
