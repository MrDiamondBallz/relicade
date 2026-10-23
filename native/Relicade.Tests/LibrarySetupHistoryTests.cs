using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Interactivity;
using System.Text.Json;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class LibrarySetupHistoryTests
{
    [AvaloniaFact]
    public async Task Saved_setup_is_discoverable_even_when_its_loaded_entry_is_filtered_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "library-setup-history-" + Guid.NewGuid().ToString("N"));
        var previousRoot = RelicadePaths.OverrideUserDataRoot;
        var previousStore = SettingsStoreProvider.Default;
        Window? window = null;
        try
        {
            Directory.CreateDirectory(root); RelicadePaths.OverrideUserDataRoot = root;
            var store = new Store(root); SettingsStoreProvider.Default = store;
            using var handler = new DenyTransport(); using var http = new HttpClient(handler);
            using var manager = new GameManager(store, http, new AppCatalogService(dataDirectory: root));
            using var game = new GameInfo { Name = "Saved synthetic Perfect Dark setup", RepositorySource = "github",
                Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "SyntheticEngine", IsCustom = true };
            manager.SetCatalogAppsAndFilter([game], store.Current);
            Assert.Same(game, Assert.Single(manager.LibraryApps)); Assert.Empty(manager.Games);
            var definition = OwnedGameSetupService.PerfectDark;
            var asset = definition.AssetIdentity.Split("|sha256:", StringSplitOptions.None);
            var identity = new GuidedInstallIntent.Identity(Hash(game.InstanceKey),
                Hash(Path.GetFullPath(root) + "\n" + Path.GetFullPath(game.GetInstallPath(manager.GamesFolder))),
                game.Repository, definition.ReleaseIdentity, asset[0], asset[1], definition.ExecutableSha256);
            using (var attempt = GuidedInstallIntent.Begin(identity)) { }
            Assert.Equal("Resolving", GuidedInstallIntent.Inspect(identity)!.Stage);
            var before = Snapshot(root);
            using var model = new LibraryViewModel(manager, new SettingsViewModel(store));
            await using var session = new LauncherSession();
            var view = new LibraryView(); view.Configure(model, session, null!, (_, _) => { });
            window = new Window { Content = view, Width = 900, Height = 650 };
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                Assert.Equal(0, handler.Requests);
                Assert.Equal(before, Snapshot(root));
                var review = view.FindControl<Button>("LibrarySetupHistoryReviewButton");
                Assert.NotNull(review);
                Assert.True(review.IsVisible); Assert.True(review.IsEnabled);
                Assert.Empty(manager.Games); // The notice must not silently change filters.
            }
            finally { window.Close(); await session.DisposeAsync(); }
        }
        finally
        {
            window?.Close(); SettingsStoreProvider.Default = previousStore;
            RelicadePaths.OverrideUserDataRoot = previousRoot;
            // Keep authored history and receipts; never delete evidence on a failed assertion.
        }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Explicit_review_shows_the_filtered_current_entry_without_setup_or_transfer(bool controller)
    {
        await using var f = await DiscoveryFixture.Create();
        await f.Model.SetupHistory.Work;
        Assert.Same(f.Game, Assert.Single(f.Manager.LibraryApps)); Assert.Empty(f.Manager.Games);
        Assert.True(f.Model.SetupHistory.IsVisible); Assert.Single(f.Model.SetupHistory.Rows);
        var before = Snapshot(f.Root); var requests = 0;
        f.View.ConfigureOwnedGameSetup((_, _) => { requests++; throw new InvalidOperationException("No setup action permitted."); });
        if (controller) { f.Navigation.RestoreFocus(); Assert.True(f.Navigation.Confirm()); }
        else f.View.LibrarySetupHistoryReviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dialog = f.View.SetupHistoryDialog; Assert.NotNull(dialog);
        await f.Model.SetupHistory.Work;
        Assert.Contains("may have stopped or still be active", Assert.Single(f.Model.SetupHistory.Rows).Message);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.ShowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout();
        Assert.Same(f.Game, Assert.Single(f.Manager.Games)); Assert.True(f.Manager.HasLibraryReveal);
        Assert.Equal(AppListScope.InstalledOnly, f.Settings.Current.ListScope);
        Assert.Equal(0, requests); Assert.Equal(0, f.Transport.Requests); Assert.Equal(before, Snapshot(f.Root));
        Assert.Equal(0, f.Host.Navigation.LibrarySelectedIndex);
    }

    [AvaloniaTheory]
    [InlineData("schema")][InlineData("partial")][InlineData("identity")]
    [InlineData("Committing")][InlineData("Completed")]
    public async Task Unknown_or_uncertain_history_remains_visible_without_mutation_or_retry(string change)
    {
        await using var f = await DiscoveryFixture.Create(); await f.Model.SetupHistory.Work;
        var path = Directory.GetFiles(Path.Combine(f.Root, "DownloadIntents"), "*.json").Single();
        var record = JsonSerializer.Deserialize<GuidedInstallIntent.Record>(File.ReadAllBytes(path))!;
        if (change == "partial") File.WriteAllText(path + ".retained.tmp", "authored unfinished evidence");
        else
        {
            record = change switch
            {
                "schema" => record with { SchemaVersion = 77 },
                "identity" => record with { Intent = record.Intent with { AssetSha256 = new string('0', 64) } },
                _ => record with { Stage = change, Sequence = change == "Committing" ? 4 : 5 }
            };
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(record));
        }
        var before = Snapshot(f.Root); f.Model.SetupHistory.Refresh(); await f.Model.SetupHistory.Work;
        Assert.True(f.Model.SetupHistory.IsVisible); var row = Assert.Single(f.Model.SetupHistory.Rows);
        Assert.Contains(change is "Committing" or "Completed" ? "may have committed" : "Inspection is needed", row.Message);
        Assert.False(f.Model.SetupHistory.HasMore); Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
    }

    [AvaloniaFact]
    public async Task Explicit_entry_batches_keep_large_libraries_inspectable_without_automatic_continuation()
    {
        await using var f = await DiscoveryFixture.Create(); await f.Model.SetupHistory.Work;
        var baseEntry = Assert.Single(f.Model.SetupHistory.Rows).Captured;
        var entries = Enumerable.Range(0, 257).Select(i => baseEntry with
        { Identity = baseEntry.Identity with { EntryKey = Hash("entry-" + i) }, Name = "Synthetic " + i }).ToArray();
        using (var attempt = GuidedInstallIntent.Begin(entries[^1].Identity)) { }
        var before = Snapshot(f.Root);
        using var reader = new LibrarySetupHistoryInspection(f.Root, entries);
        var first = reader.Next(CancellationToken.None); Assert.True(first.More); Assert.Empty(first.Findings);
        var second = reader.Next(CancellationToken.None); Assert.False(second.More); Assert.Single(second.Findings);
        Assert.Equal(entries[^1], second.Findings[0].Entry); Assert.Equal(before, Snapshot(f.Root));
        Assert.Equal(0, f.Transport.Requests);
    }

    [AvaloniaFact]
    public async Task Directory_batches_do_not_claim_a_partial_chain_is_complete()
    {
        await using var f = await DiscoveryFixture.Create(); await f.Model.SetupHistory.Work;
        var entry = Assert.Single(f.Model.SetupHistory.Rows).Captured;
        var directory = Path.Combine(f.Root, "DownloadIntents");
        for (var i = 0; i < 1025; i++) File.WriteAllText(Path.Combine(directory, "unrelated-" + i), "retained");
        var before = Snapshot(f.Root); using var reader = new LibrarySetupHistoryInspection(f.Root, [entry]);
        var first = reader.Next(CancellationToken.None); Assert.True(first.More); Assert.Empty(first.Findings);
        var final = reader.Next(CancellationToken.None); Assert.False(final.More); Assert.Single(final.Findings);
        Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
    }


    [AvaloniaTheory]
    [InlineData("root")][InlineData("entry")][InlineData("closed")]
    public async Task Changed_context_or_closed_view_cannot_publish_a_delayed_actual_read(string change)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LibrarySetupHistoryInspection? held = null; var calls = 0;
        async Task<LibrarySetupHistoryInspection.Step> Read(LibrarySetupHistoryInspection reader, CancellationToken token)
        {
            var result = await Task.Run(() => reader.Next(token), token);
            if (Interlocked.Increment(ref calls) == 1)
            { held = reader; entered.TrySetResult(); await release.Task; }
            return result;
        }
        await using var f = await DiscoveryFixture.Create(Read, () => release.TrySetResult());
        GameInfo? replacement = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var before = Snapshot(f.Root);
            var priorWork = f.Model.SetupHistory.Work;
            if (change == "root")
            {
                RelicadePaths.OverrideUserDataRoot = f.Root + "-other";
                f.Model.SetupHistory.InvalidateIfChanged();
            }
            else if (change == "entry")
            {
                replacement = new GameInfo { Name = "Different current entry", RepositorySource = "github",
                    Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Different", IsCustom = true };
                f.Manager.SetCatalogAppsAndFilter([replacement], f.Settings.Current);
                f.Model.SetupHistory.InvalidateIfChanged();
            }
            else f.Model.Dispose();
            Assert.False(priorWork.IsCompleted); Assert.NotNull(held); Assert.False(held.IsDisposed);
            Assert.Empty(f.Model.SetupHistory.Rows);
            release.TrySetResult(); await priorWork; await f.Model.SetupHistory.Work;
            Assert.True(held.IsDisposed); Assert.Empty(f.Model.SetupHistory.Rows);
            Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
            if (change == "root") Assert.False(Directory.Exists(f.Root + "-other"));
        }
        finally
        { release.TrySetResult(); await f.Model.SetupHistory.Work; RelicadePaths.OverrideUserDataRoot = f.Root; replacement?.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Actual_dialog_close_waits_for_read_drain_and_repeated_review_keeps_one_dialog()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<LibrarySetupHistoryInspection.Step> Read(LibrarySetupHistoryInspection reader, CancellationToken token)
        {
            var result = await Task.Run(() => reader.Next(token), token);
            if (Interlocked.Increment(ref calls) == 2) { entered.TrySetResult(); await release.Task; }
            return result;
        }
        await using var f = await DiscoveryFixture.Create(Read, () => release.TrySetResult()); await f.Model.SetupHistory.Work;
        var before = Snapshot(f.Root);
        LibrarySetupHistoryDialog? dialog = null;
        try
        {
            f.View.LibrarySetupHistoryReviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = f.View.SetupHistoryDialog; Assert.NotNull(dialog);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var work = f.Model.SetupHistory.Work;
            f.View.LibrarySetupHistoryReviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(dialog, f.View.SetupHistoryDialog); Assert.Same(work, f.Model.SetupHistory.Work);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult(); dialog.Close();
            Assert.False(closed.Task.IsCompleted); Assert.False(work.IsCompleted); Assert.True(dialog.IsVisible);
            release.TrySetResult(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await work;
            Assert.Empty(f.Model.SetupHistory.Rows); Assert.False(f.Model.SetupHistory.IsBusy);
            Assert.Contains("stopped before completion", f.Model.SetupHistory.Message);
            Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests); Assert.Empty(f.Manager.Games);
        }
        finally { release.TrySetResult(); await f.Model.SetupHistory.Work; (dialog ?? f.View.SetupHistoryDialog)?.Close(); }
    }

    [AvaloniaFact]
    public async Task Repeated_show_click_navigates_only_once_after_the_dialog_closes()
    {
        await using var f = await DiscoveryFixture.Create(); await f.Model.SetupHistory.Work;
        var count = 0; var dialogVisible = false;
        var dialog = new LibrarySetupHistoryDialog(f.Model.SetupHistory, f.Session, row =>
        { Assert.False(dialogVisible); Assert.True(f.Model.SetupHistory.TryGetCurrent(row, out _)); count++; });
        Task? shown = null;
        try
        {
            shown = dialog.ShowDialog(f.Window); dialogVisible = true;
            dialog.Closed += (_, _) => dialogVisible = false;
            dialog.ShowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.ShowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await shown; Assert.Equal(1, count); Assert.Equal(0, f.Transport.Requests);
        }
        finally { dialog.Close(); if (shown != null) await shown; }
    }

    [AvaloniaFact]
    public async Task Completed_installed_history_is_not_an_interrupted_transfer_or_readiness_grant()
    {
        await using var f = await DiscoveryFixture.Create(); await f.Model.SetupHistory.Work;
        var path = Directory.GetFiles(Path.Combine(f.Root, "DownloadIntents"), "*.json").Single();
        var record = JsonSerializer.Deserialize<GuidedInstallIntent.Record>(File.ReadAllBytes(path))!;
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(record with { Stage = "Completed", Sequence = 5 }));
        f.Game.Status = GameStatus.Installed; var before = Snapshot(f.Root);
        f.Model.SetupHistory.Refresh(); await f.Model.SetupHistory.Work;
        Assert.Empty(f.Model.SetupHistory.Rows); Assert.False(f.Model.SetupHistory.IsVisible);
        Assert.False(f.Game.OwnedDataReady); Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
    }

    [AvaloniaFact]
    public async Task Per_entry_safety_refusal_does_not_hide_other_valid_saved_history()
    {
        await using var f = await DiscoveryFixture.Create(); await f.Model.SetupHistory.Work;
        var first = Assert.Single(f.Model.SetupHistory.Rows).Captured;
        var second = first with { Name = "Other retained setup", Identity = first.Identity with { EntryKey = Hash("other-entry") } };
        using (var attempt = GuidedInstallIntent.Begin(second.Identity)) { }
        var directory = Path.Combine(f.Root, "DownloadIntents");
        for (var i = 0; i < 257; i++) File.WriteAllText(Path.Combine(directory, first.Identity.EntryKey + "-extra-" + i + ".json"), "authored unknown record");
        var before = Snapshot(f.Root); using var reader = new LibrarySetupHistoryInspection(f.Root, [first, second]);
        var result = reader.Next(CancellationToken.None);
        Assert.False(result.More); Assert.Equal(2, result.Findings.Count);
        Assert.Contains("safety limit", result.Findings[0].Message); Assert.Contains("Previous setup: Resolving", result.Findings[1].Message);
        Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
    }

    [AvaloniaFact]
    public async Task Closed_continue_disposes_actual_incomplete_reader_after_drain()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LibrarySetupHistoryInspection? held = null;
        var calls = 0;
        async Task<LibrarySetupHistoryInspection.Step> Read(LibrarySetupHistoryInspection reader, CancellationToken token)
        {
            var result = await Task.Run(() => reader.Next(token), token);
            if (Interlocked.Increment(ref calls) == 3)
            {
                Assert.True(result.More);
                held = reader; entered.TrySetResult(); await release.Task;
            }
            return result;
        }
        await using var f = await DiscoveryFixture.Create(Read, () => release.TrySetResult());
        try
        {
            await f.Model.SetupHistory.Work;
            var directory = Path.Combine(f.Root, "DownloadIntents");
            for (var i = 0; i < 2050; i++) File.WriteAllText(Path.Combine(directory, "unrelated-probe-" + i), "retained synthetic evidence");
            var before = Snapshot(f.Root);
            f.Model.SetupHistory.Refresh(); await f.Model.SetupHistory.Work;
            Assert.True(f.Model.SetupHistory.HasMore);
            f.Model.SetupHistory.Continue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var work = f.Model.SetupHistory.Work;
            f.Model.Dispose();
            Assert.False(work.IsCompleted); Assert.NotNull(held); Assert.False(held.IsDisposed);
            release.TrySetResult(); await work;
            Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
            Assert.Empty(f.Model.SetupHistory.Rows);
            Assert.True(held.IsDisposed);
        }
        finally
        {
            try { release.TrySetResult(); await f.Model.SetupHistory.Work; }
            finally { held?.Dispose(); }
        }
    }

    sealed class DiscoveryFixture : IAsyncDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "library-history-journey-" + Guid.NewGuid().ToString("N"));
        readonly string? priorRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore priorStore = SettingsStoreProvider.Default;
        internal Store Settings = null!;
        internal readonly DenyTransport Transport = new();
        HttpClient? http;
        internal GameManager Manager = null!;
        internal readonly GameInfo Game = new() { Name = "Synthetic saved setup", RepositorySource = "github", Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Fixture", IsCustom = true };
        internal LibraryViewModel Model = null!;
        internal LibraryView View = null!;
        internal Window Window = null!;
        internal readonly LauncherSession Session = new();
        internal readonly NavigationHost Host = new();
        internal LibraryNavigation Navigation = null!;
        private DiscoveryFixture() { }
        internal static async Task<DiscoveryFixture> Create(
            Func<LibrarySetupHistoryInspection, CancellationToken, Task<LibrarySetupHistoryInspection.Step>>? read = null,
            Action? releaseOnConstructionFailure = null)
        {
            var f = new DiscoveryFixture();
            try
            {
                Directory.CreateDirectory(f.Root); RelicadePaths.OverrideUserDataRoot = f.Root;
                f.Settings = new Store(f.Root); SettingsStoreProvider.Default = f.Settings;
                f.http = new HttpClient(f.Transport);
                f.Manager = new GameManager(f.Settings, f.http, new AppCatalogService(dataDirectory: f.Root));
                f.Manager.SetCatalogAppsAndFilter([f.Game], f.Settings.Current);
                var identity = GuidedInstallIntent.ForCaptured(f.Game.InstanceKey, f.Root,
                    f.Game.GetInstallPath(f.Manager.GamesFolder), GuidedEngineInstallService.ProjectFor(f.Game));
                using (var attempt = GuidedInstallIntent.Begin(identity)) { }
                f.Model = new LibraryViewModel(f.Manager, new SettingsViewModel(f.Settings), read);
                f.View = new LibraryView(); f.View.Configure(f.Model, f.Session, null!, (_, _) => { });
                f.Navigation = new LibraryNavigation(f.View, f.Host, f.Session, () => true, () => true, () => false,
                    _ => { }, _ => throw new InvalidOperationException("No launch is permitted."));
                f.View.Navigation = f.Navigation;
                f.Window = new Window { Content = f.View, Width = 900, Height = 650 };
                f.Window.Show(); Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout(); return f;
            }
            catch { releaseOnConstructionFailure?.Invoke(); await f.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Window?.Close(); await Session.DisposeAsync(); Model?.Dispose(); Manager?.Dispose(); http?.Dispose(); Game.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot;
        }
    }
    sealed class NavigationHost : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.Library;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) { Navigation.ActiveZone = transition.Zone; return true; }
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }
    static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static string[] Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + "|" + File.GetLastWriteTimeUtc(p).Ticks + "|" +
            (Directory.Exists(p) ? "directory" : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))))).ToArray();
    sealed class Store(string root) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps"), Platform = TargetOS.LinuxX64,
            FirstStartup = false, LocalFirstCatalogMigrationComplete = true, ListScope = AppListScope.InstalledOnly };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => throw new InvalidOperationException("Discovery must not save settings.");
    }
    sealed class DenyTransport : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; throw new InvalidOperationException("No transport is permitted in passive history discovery."); }
    }
}
