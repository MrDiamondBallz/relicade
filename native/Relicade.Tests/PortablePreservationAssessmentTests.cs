using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Views;
using System.ComponentModel;
using Relicade.Core.Services;
using Relicade.Core.Models;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class PortablePreservationAssessmentTests
{
    [AvaloniaFact]
    public async Task Managed_first_install_has_an_explicit_preservation_inspection_route_without_removal()
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await scope.Install("v1");
        var before = scope.Snapshot();
        var times = Times(scope.Root);
        var view = new LibraryView();
        // Inspect the actual resource without invoking the menu Opening handler.
        // No card/window is attached, no action clicked and no status loader invoked.
        var menu = Assert.IsType<ContextMenu>(view.Resources["LibraryCardMenu"]);
        var inspection = menu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Name == "InspectPortablePreservationMenu");
        Assert.Equal(before, scope.Snapshot());
        Assert.Equal(times, Times(scope.Root));
        Assert.Equal(0, scope.Network.Calls);
        Assert.NotNull(inspection);
        Assert.Equal("Inspect preservation…", inspection.Header);
    }

    [AvaloniaFact]
    public async Task Opening_local_maintenance_menu_does_not_request_catalog_releases()
    {
        using var scope = new PortableBackupCleanupBaselineScope();
        await scope.Install("v1");
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        scope.Game.GameManager = manager;
        scope.Game.CatalogSlug = "synthetic-preservation";
        var before = scope.Snapshot(); var times = Times(scope.Root);
        var view = new LibraryView();
        var menu = Assert.IsType<ContextMenu>(view.Resources["LibraryCardMenu"]);
        menu.DataContext = scope.Game;
        // Actual synchronous XAML Opening handler, no native popup/card/artwork or
        // action click. Its removed prefetch has no detached task left to drain.
        view.LibraryCardMenu_Opening(menu, new CancelEventArgs());
        Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root));
        Assert.Equal(0, scope.Network.Calls);
    }

    [Theory]
    [InlineData("known")][InlineData("changed")][InlineData("extra")][InlineData("data")][InlineData("unknown-history")]
    public async Task Exact_manifest_intersection_never_claims_changed_extra_or_data_files_as_payload(string kind)
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        var payload = Path.Combine(scope.Live, "fixture.AppImage");
        if (kind == "changed") File.AppendAllText(payload, "authored change");
        if (kind == "extra") File.WriteAllText(Path.Combine(scope.Live, "user-save.extra"), "unlisted user bytes");
        if (kind == "data") File.WriteAllText(Path.Combine(scope.Live, "LastPlayed.txt"), "new current data");
        if (kind == "unknown-history") File.WriteAllText(Path.Combine(scope.Tx.OperationsPath, "unknown.record"), "retain unknown history");
        var before = scope.Snapshot(); var times = Times(scope.Root); PortablePreservationAssessment? result = null;
        await scope.Tx.ObservePreservationAsync(value => { result = value; return Task.CompletedTask; });
        Assert.NotNull(result); Assert.Equal("v1", result.Version); Assert.False(result.IsPartial);
        var target = Assert.Single(result.Rows.Where(row => row.Area == "installation" && row.Path == "fixture.AppImage"));
        Assert.Equal(kind == "changed" ? "Changed listed file — preserve / inspect" : "Unchanged listed payload", target.Classification);
        if (kind == "extra") Assert.Equal("Unlisted file — preserve / inspect", Assert.Single(result.Rows.Where(row => row.Path == "user-save.extra")).Classification);
        if (kind == "data") Assert.Equal("Preserved current declared data", Assert.Single(result.Rows.Where(row => row.Path == "LastPlayed.txt")).Classification);
        if (kind == "unknown-history") Assert.Equal("Unknown history — preserve / inspect", Assert.Single(result.Rows.Where(row => row.Path == "unknown.record")).Classification);
        Assert.Contains(result.Rows, row => row.Area == "history" && row.Path.EndsWith("/journal.json", StringComparison.Ordinal));
        Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
        Assert.Throws<IOException>(() => { using var refused = PortableInstallationTransaction.AcquireUnmanagedRemovalLeases([scope.Live]); });
    }

    [Theory][InlineData("entries")][InlineData("hash")]
    public async Task Safety_limits_report_partial_observation_without_claiming_complete_totals(string kind)
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        var before = scope.Snapshot(); var times = Times(scope.Root); PortablePreservationAssessment? result = null;
        var limits = kind == "entries" ? new PreservationLimits(Entries: 2) : new PreservationLimits(HashBytes: 1);
        await scope.Tx.ObservePreservationAsync(value => { result = value; return Task.CompletedTask; }, limits: limits);
        Assert.NotNull(result); Assert.True(result.IsPartial); Assert.DoesNotContain(result.Rows, row => row.Classification == "Unchanged listed payload");
        Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
    }

    [Theory][InlineData("manifest")][InlineData("pending")][InlineData("link")]
    public async Task Uncertain_metadata_or_links_never_repair_or_publish_an_ownership_claim(string kind)
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        if (kind == "manifest") File.AppendAllText(Path.Combine(scope.Live, PortableInstallationTransaction.ManifestFileName), "broken");
        if (kind == "pending")
        {
            var journal = Directory.GetFiles(scope.Tx.OperationsPath, "journal.json", SearchOption.AllDirectories).Single();
            File.WriteAllText(journal, File.ReadAllText(journal).Replace("Committed", "Applying", StringComparison.Ordinal));
        }
        var link = Path.Combine(scope.Live, "linked-extra");
        if (kind == "link") File.CreateSymbolicLink(link, Path.Combine(scope.Live, "fixture.AppImage"));
        var before = scope.Snapshot(); var times = Times(scope.Root); var publications = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => scope.Tx.ObservePreservationAsync(_ => { publications++; return Task.CompletedTask; }));
        Assert.Equal(0, publications); Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
        if (kind == "link") Assert.Equal(Path.Combine(scope.Live, "fixture.AppImage"), new FileInfo(link).LinkTarget);
    }

    [Fact]
    public async Task Existing_authority_remains_held_until_cancelled_callback_drains()
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        var before = scope.Snapshot(); var times = Times(scope.Root);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Task.Run(() => scope.Tx.ObservePreservationAsync(async _ => { entered.TrySetResult(); await release.Task; }, cancellation.Token));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
            Assert.False(work.IsCompleted); Assert.Throws<IOException>(() => { using var refused = scope.Tx.AcquireLease(); });
            release.TrySetResult(); await work;
            Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
        }
        finally { release.TrySetResult(); await work; }
    }

    [AvaloniaTheory][InlineData("root")][InlineData("removed")][InlineData("closed")]
    public async Task Captured_context_rejects_later_selection_changes_without_publication(string kind)
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        scope.Game.Status = GameStatus.Installed; scope.Game.InstalledVersion = "v1";
        var closed = false;
        var observe = PortablePreservationService.Capture(manager, scope.Game.InstanceKey, () => closed);
        if (kind == "root") scope.Game.InstallPath = Path.Combine(scope.Root, "different");
        if (kind == "removed") manager.Games.Clear();
        if (kind == "closed") closed = true;
        var before = scope.Snapshot(); var times = Times(scope.Root); var publications = 0;
        await observe(_ => { publications++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(0, publications); Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
    }

    [Theory][InlineData("payload")][InlineData("new-entry")]
    public async Task Later_external_changes_are_uncertain_before_observation_is_published(string kind)
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        var afterMutation = scope.Snapshot(); string[]? times = null;
        PortablePreservationAssessment? result = null;
        await scope.Tx.ObservePreservationAsync(value => { result = value; return Task.CompletedTask; }, beforeFinalObservation: () =>
        {
            if (kind == "payload") File.AppendAllText(Path.Combine(scope.Live, "fixture.AppImage"), "later external bytes");
            else
            {
                var previous = Directory.GetLastWriteTimeUtc(scope.Live);
                File.WriteAllText(Path.Combine(scope.Live, "later-user-save"), "external addition");
                Directory.SetLastWriteTimeUtc(scope.Live, previous.AddSeconds(2)); // deterministic metadata change, not a sleep
            }
            var changedPath = Path.Combine(scope.Live, kind == "payload" ? "fixture.AppImage" : "later-user-save");
            afterMutation[Path.GetRelativePath(scope.Root, changedPath)] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(changedPath)));
            times = Times(scope.Root);
        });
        Assert.NotNull(result); Assert.True(result.IsPartial);
        if (kind == "payload")
        {
            var row = Assert.Single(result.Rows.Where(row => row.Area == "installation" && row.Path == "fixture.AppImage"));
            Assert.Equal("Changing file — preserve / inspect", row.Classification); Assert.Null(row.Sha256); Assert.Null(row.ObservedBytes);
        }
        Assert.Equal(afterMutation, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
    }

    [AvaloniaFact]
    public async Task Actual_menu_flow_opens_one_inspection_and_refreshes_without_removal_or_transfer()
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        using var manager = PortableBackupCleanupServiceTests.Manager(scope);
        scope.Game.Status = GameStatus.Installed; scope.Game.InstalledVersion = "v1"; scope.Game.GameManager = manager;
        scope.Game.CatalogSlug = "synthetic-preservation";
        var settings = new SettingsViewModel(scope.Store);
        using var libraryModel = new LibraryViewModel(manager, settings);
        await using var session = new LauncherSession();
        using var dialogs = new LauncherDialogLifetime(session.Token, action => Dispatcher.UIThread.Post(action));
        var flow = new PortablePreservationFlow(manager, settings, session, dialogs);
        var view = new LibraryView(); view.Configure(libraryModel, session, null!, (_, _) => { });
        var openings = new List<Task>();
        view.ConfigurePreservationInspection((game, anchor) => { var work = flow.OpenAsync(game, anchor); openings.Add(work); return work; });
        var menu = Assert.IsType<ContextMenu>(view.Resources["LibraryCardMenu"]); menu.DataContext = scope.Game;
        var item = Assert.Single(menu.Items.OfType<MenuItem>(), entry => entry.Name == "InspectPortablePreservationMenu");
        item.CommandParameter = scope.Game;
        var before = scope.Snapshot(); var times = Times(scope.Root);
        try
        {
            view.LibraryCardMenu_Opening(menu, new CancelEventArgs());
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialog = flow.Dialog; Assert.NotNull(dialog); await dialog.Work;
            Assert.True(dialog.IsVisible); Assert.NotNull(dialog.Model.Assessment); Assert.True(dialog.CloseButton.IsDefault);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Same(dialog, flow.Dialog);
            dialog.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.NotNull(dialog.Model.Assessment); Assert.Equal("v1", dialog.Model.Assessment.Version);
            var inspected = new List<string>();
            for (var page = 0; page < 128; page++)
            {
                inspected.Add(string.Concat(dialog.Details.Children.OfType<Button>().Select(button => ((TextBlock)button.Content!).Text)));
                if (!dialog.NextButton.IsEnabled) break;
                dialog.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.False(dialog.NextButton.IsEnabled);
            Assert.Contains(inspected, text => text.Contains("not a deletion plan", StringComparison.Ordinal));
            Assert.Contains(inspected, text => text.Contains("fixture.AppImage", StringComparison.Ordinal));
            dialog.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.WhenAll(openings);
            Assert.Null(flow.Dialog); Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
        }
        finally { flow.Dialog?.Close(); await session.DisposeAsync(); await Task.WhenAll(openings); }
    }

    [AvaloniaFact]
    public async Task Actual_dialog_close_revokes_publication_and_drains_the_held_observation()
    {
        using var scope = new PortableBackupCleanupBaselineScope(); await scope.Install("v1");
        var before = scope.Snapshot(); var times = Times(scope.Root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new LauncherSession();
        var model = new PortablePreservationViewModel(session, scope.Game.InstanceKey, (publish, token) =>
            Task.Run(() => scope.Tx.ObservePreservationAsync(async result =>
            {
                entered.TrySetResult(); await release.Task;
                await Dispatcher.UIThread.InvokeAsync((Func<Task>)(() => publish(result)));
            }, token)));
        PortablePreservationDialog? dialog = null;
        try
        {
            dialog = new PortablePreservationDialog(model);
            dialog.Show(); _ = dialog.BeginInspection(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            dialog.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(model.IsClosed); Assert.False(dialog.IsVisible); Assert.False(dialog.Work.IsCompleted);
            Assert.Throws<IOException>(() => { using var refused = scope.Tx.AcquireLease(); });
            release.TrySetResult(); await dialog.Work; Assert.Null(model.Assessment);
            Assert.Equal(before, scope.Snapshot()); Assert.Equal(times, Times(scope.Root)); Assert.Equal(0, scope.Network.Calls);
        }
        finally
        {
            release.TrySetResult();
            try { dialog?.Close(); await model.Work; }
            finally { try { model.DisposeAfterDrain(); } finally { await session.DisposeAsync(); } }
        }
    }

    static string[] Times(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + "|" + File.GetLastWriteTimeUtc(path).Ticks).ToArray();
}
