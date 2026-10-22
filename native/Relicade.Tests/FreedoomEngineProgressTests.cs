using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
using Fixture = Relicade.Tests.FreedoomEngineHandoffTests.Fixture;

namespace Relicade.Tests;

public sealed class FreedoomEngineProgressTests
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static FreedoomEngineProgressViewModel Model(Fixture f, LauncherSession session) =>
        OwnedGameSetupFlow.CreateEngineProgress(f.Game, f.Manager, f.Store.Current, session, f.Dialogs, f.Definition);
    static void Show(FreedoomEngineProgressDialog window)
    { window.Closed += (_, _) => GamepadFocusChrome.SetActive(false); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    static void Activate(FreedoomEngineProgressDialog window, Button button, string mode)
    {
        if (mode == "controller")
        {
            GamepadFocusChrome.SetActive(true, window);
            Assert.True(GamepadModalDialogNavigation.Instance.TryHandleNavigation(Relicade.Services.NavigationDirection.Right));
            Assert.True(button.IsFocused); Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm()); return;
        }
        Assert.True(button.Focus());
        if (mode == "keyboard") { window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); }
        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    static void Cancel(FreedoomEngineProgressDialog window, string mode)
    {
        if (mode == "keyboard") { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); if (window.IsVisible) window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); }
        else if (mode == "controller") Assert.True(GamepadModalDialogNavigation.Instance.TryHandleCancel());
        else if (mode == "controller-confirm") { Assert.True(window.CancelButton.IsFocused); Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm()); }
        else if (mode == "window") window.Close();
        else window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaTheory]
    [InlineData("mouse")][InlineData("keyboard")][InlineData("controller")]
    public async Task Explicit_native_confirmation_installs_pinned_fixture_and_only_then_allows_handoff(string input)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); Assert.True(window.CancelButton.IsFocused);
            Assert.Equal(0, f.MetadataCalls + f.DownloadCalls); Assert.False(model.Succeeded);
            Activate(window, window.InstallButton, input); await model.Work.WaitAsync(Deadline);
            Assert.True(model.Succeeded, model.Message); Assert.False(window.IsVisible); Assert.False(model.IsBusy);
            Assert.Equal(1, f.DownloadCalls); Assert.Equal(f.Payload, File.ReadAllBytes(Path.Combine(f.Live, Fixture.Asset)));
            Assert.NotNull(new PortableInstallationTransaction(f.Live).ReadCommitted());
            Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt"))); Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
            Assert.False(session.Token.IsCancellationRequested); Assert.False(model.CancellationRequested);
        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("mouse")][InlineData("keyboard")][InlineData("controller")][InlineData("window")]
    public async Task Cancel_before_confirmation_does_not_start_or_close_the_launcher(string input)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); Cancel(window, input); await model.Work;
            Assert.False(window.IsVisible); Assert.False(model.Succeeded); Assert.False(model.CanStart);
            Assert.Equal(0, f.MetadataCalls + f.DownloadCalls); Assert.False(session.Token.IsCancellationRequested);
            Assert.True(await session.RunAsync(() => Task.FromResult(!LauncherSession.OperationCancellation.IsCancellationRequested)));
        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("mouse")][InlineData("keyboard")][InlineData("controller")][InlineData("window")]
    [InlineData("controller-confirm")]
    public async Task Cancel_waits_for_owned_uncooperative_transfer_and_does_not_cancel_sibling_work(string input)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var entered = Signal(); var release = Signal(); var observed = Signal(); var transportEnded = Signal();
        f.OnDownload = async token =>
        {
            using var registration = token.Register(() => observed.TrySetResult()); entered.TrySetResult();
            try { await release.Task; token.ThrowIfCancellationRequested(); }
            finally { transportEnded.TrySetResult(); }
        };
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); Activate(window, window.InstallButton, input == "controller-confirm" ? "controller" : "mouse"); await entered.Task.WaitAsync(Deadline);
            Cancel(window, input); Assert.True(model.CancellationRequested); await observed.Task.WaitAsync(Deadline);
            Assert.True(window.IsVisible); Assert.True(model.IsBusy); Assert.False(model.Work.IsCompleted);
            Assert.Contains("Waiting", model.Message); Assert.False(model.CanStart); Assert.False(model.Succeeded);
            Assert.Same(model.Work, model.StartAsync()); Assert.Equal(1, f.DownloadCalls);
            Assert.False(session.Token.IsCancellationRequested);
            Assert.True(await session.RunAsync(() => Task.FromResult(!LauncherSession.OperationCancellation.IsCancellationRequested)));
            Assert.Throws<InvalidOperationException>(model.DisposeAfterDrain);
            release.TrySetResult(); await model.Work.WaitAsync(Deadline); await transportEnded.Task.WaitAsync(Deadline);
            Assert.False(model.IsBusy); Assert.False(model.Succeeded); Assert.True(window.IsVisible);
            Assert.Contains("Retained files or recovery work may remain", model.Message);
            Assert.False(File.Exists(Path.Combine(f.Live, Fixture.Asset)));
            Assert.Null(new PortableInstallationTransaction(f.Live).ReadCommitted());
        }
        finally { release.TrySetResult(); await model.Work.WaitAsync(Deadline); window.Close(); model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Cancel_after_real_commit_retains_engine_and_suppresses_success_handoff()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var committed = Signal(); var release = Signal();
        var model = new FreedoomEngineProgressViewModel(session, f.Game, async token =>
        {
            var result = await f.Install(token, (_, _) => Task.FromResult(true));
            committed.TrySetResult(); await release.Task; return result;
        });
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); Activate(window, window.InstallButton, "mouse"); await committed.Task.WaitAsync(Deadline);
            Assert.True(f.Game.IsInstalled); var before = File.ReadAllBytes(Path.Combine(f.Live, Fixture.Asset));
            Cancel(window, "mouse"); Assert.False(model.Work.IsCompleted); release.TrySetResult(); await model.Work.WaitAsync(Deadline);
            Assert.False(model.Succeeded); Assert.Contains("engine remains installed", model.Message);
            Assert.True(window.IsVisible); Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Live, Fixture.Asset)));
            Assert.NotNull(new PortableInstallationTransaction(f.Live).ReadCommitted());
            Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt"))); Assert.False(Directory.Exists(Path.Combine(f.Root, "owned-game-data")));
        }
        finally { release.TrySetResult(); await model.Work.WaitAsync(Deadline); window.Close(); model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Session_shutdown_closes_progress_only_after_owned_transfer_drains()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        using var lifetime = new LauncherDialogLifetime(session.Token, action => Dispatcher.UIThread.Post(action));
        var entered = Signal(); var release = Signal();
        f.OnDownload = async token => { entered.TrySetResult(); await release.Task; token.ThrowIfCancellationRequested(); };
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model); lifetime.Track(window);
        try
        {
            Show(window); Activate(window, window.InstallButton, "mouse"); await entered.Task.WaitAsync(Deadline);
            var close = session.DisposeAsync().AsTask(); Dispatcher.UIThread.RunJobs();
            Assert.False(close.IsCompleted); Assert.True(window.IsVisible); Assert.False(model.Work.IsCompleted);
            release.TrySetResult(); await model.Work.WaitAsync(Deadline); await close.WaitAsync(Deadline);
            Assert.False(window.IsVisible); Assert.False(model.Succeeded); Assert.False(model.IsBusy);
        }
        finally { release.TrySetResult(); await model.Work.WaitAsync(Deadline); window.Close(); model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Offline_failure_allows_a_new_explicit_attempt_only_after_terminal_close()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession(); f.Offline = true;
        var failed = Model(f, session); var first = new FreedoomEngineProgressDialog(failed);
        try
        {
            Show(first); Activate(first, first.InstallButton, "mouse"); await failed.Work.WaitAsync(Deadline);
            Assert.False(failed.Succeeded); Assert.False(failed.CanStart); Assert.Contains("stopped", failed.Message);
            Assert.Same(failed.Work, failed.StartAsync()); Assert.Equal(0, f.DownloadCalls);
        }
        finally { first.Close(); await failed.Work; failed.DisposeAfterDrain(); }
        f.Offline = false;
        var retry = Model(f, session); var second = new FreedoomEngineProgressDialog(retry);
        try { Show(second); Activate(second, second.InstallButton, "keyboard"); await retry.Work.WaitAsync(Deadline); Assert.True(retry.Succeeded, retry.Message); Assert.Equal(1, f.DownloadCalls); }
        finally { second.Close(); await retry.Work; retry.DisposeAfterDrain(); }
    }

    [Fact]
    public async Task Scoped_cancellation_restores_ambient_operation_and_preserves_sibling_tokens()
    {
        await using var session = new LauncherSession(); using var cancel = new CancellationTokenSource();
        await session.RunAsync(async () =>
        {
            var outer = LauncherSession.OperationCancellation;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LauncherSession.WithOperationCancellationAsync(cancel.Token, () =>
            { var inner = LauncherSession.OperationCancellation; Assert.NotEqual(outer, inner); cancel.Cancel(); inner.ThrowIfCancellationRequested(); return Task.FromResult(true); }));
            Assert.Equal(outer, LauncherSession.OperationCancellation); Assert.False(outer.IsCancellationRequested);
        });
        Assert.False(LauncherSession.OperationCancellation.CanBeCanceled);
        Assert.True(await session.RunAsync(() => Task.FromResult(!LauncherSession.OperationCancellation.IsCancellationRequested)));
    }

    [AvaloniaTheory]
    [InlineData("saved-kiosk")][InlineData("removed-entry")][InlineData("saved-folder")]
    public async Task Progress_confirmation_preserves_original_library_context(string change)
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window);
            if (change == "saved-kiosk") { f.Store.Current.KioskMode = true; f.Store.Save(f.Store.Current); }
            if (change == "removed-entry") f.Manager.Games.Remove(f.Game);
            if (change == "saved-folder") { f.Game.FolderName = "Changed"; await f.Manager.CatalogService.SaveLocalAppsAsync([f.Game]); }
            Activate(window, window.InstallButton, "mouse"); await model.Work.WaitAsync(Deadline);
            Assert.False(model.Succeeded); Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
            Assert.False(File.Exists(Path.Combine(f.Root, "Apps", "Changed", Fixture.Asset)));
            Assert.False(File.Exists(Path.Combine(f.Live, Fixture.Asset)));
        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Preflight_refuses_existing_payload_before_showing_a_confirmation()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        Directory.CreateDirectory(f.Live); var path = Path.Combine(f.Live, "personal.txt"); File.WriteAllText(path, "keep me");
        FreedoomEngineProgressViewModel? model = null;
        try
        {
            Assert.ThrowsAny<IOException>(() => model = Model(f, session));
            Assert.Equal("keep me", File.ReadAllText(path)); Assert.Equal(0, f.MetadataCalls + f.DownloadCalls);
        }
        finally { model?.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task New_operation_does_not_present_stale_download_percentage_as_current_progress()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        f.Game.DownloadProgress = 73;
        var entered = Signal(); var release = Signal();
        f.OnMetadata = async token => { entered.TrySetResult(); await release.Task; token.ThrowIfCancellationRequested(); };
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); Assert.False(window.TransferProgress.IsVisible);
            Activate(window, window.InstallButton, "mouse"); await entered.Task.WaitAsync(Deadline);
            Assert.Equal(0, model.Progress); Assert.True(window.TransferProgress.IsIndeterminate);
            release.TrySetResult(); await model.Work.WaitAsync(Deadline); Assert.True(model.Succeeded, model.Message);
        }
        finally { release.TrySetResult(); await model.Work.WaitAsync(Deadline); window.Close(); model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("keyboard")][InlineData("controller")]
    public async Task Long_error_details_are_reachable_without_dismissing_the_minimum_dialog(string input)
    {
        await using var session = new LauncherSession();
        var model = new FreedoomEngineProgressViewModel(session, new(), _ => Task.FromException<bool>(new IOException(new string('x', 1200))));
        var window = new FreedoomEngineProgressDialog(model) { Width = 580, Height = 440 };
        try
        {
            Show(window); await model.StartAsync(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.NextDetailsButton.IsVisible); Assert.True(window.CancelButton.IsFocused);
            if (input == "controller")
            {
                GamepadFocusChrome.SetActive(true, window);
                Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(window.CancelButton));
                Assert.True(GamepadModalDialogNavigation.Instance.TryHandleNavigation(Relicade.Services.NavigationDirection.Left));
            }
            else { window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null); window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null); }
            Assert.True(window.NextDetailsButton.IsFocused);
            var pages = new List<string> { window.DiagnosticText.Text! };
            void Advance(Button button)
            {
                Assert.True(button.IsFocused);
                if (input == "controller") Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm());
                else { window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); }
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var point = window.DiagnosticText.TranslatePoint(default, window)!.Value;
                Assert.InRange(point.Y, 0, window.Height - 80 - window.DiagnosticText.Bounds.Height);
                Assert.True(window.IsVisible); Assert.False(model.Succeeded);
            }
            while (window.NextDetailsButton.IsEnabled)
            {
                Advance(window.NextDetailsButton); pages.Add(window.DiagnosticText.Text!);
                Assert.InRange(pages.Count, 1, 60);
            }
            Assert.Equal(model.Message, string.Concat(pages)); Assert.True(window.PreviousDetailsButton.IsFocused);
            for (var i = pages.Count - 2; i >= 0; i--)
            {
                Advance(window.PreviousDetailsButton); Assert.Equal(pages[i], window.DiagnosticText.Text);
            }
            Assert.False(window.PreviousDetailsButton.IsEnabled); Assert.True(window.NextDetailsButton.IsFocused);
            Cancel(window, input); Assert.False(window.IsVisible); Assert.False(model.Succeeded);

        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("\n", 100, "keyboard")][InlineData("\r\n", 100, "controller")]
    [InlineData("\u2028", 100, "keyboard")][InlineData("\u2029", 100, "controller")]
    [InlineData("\u0085", 100, "keyboard")][InlineData("\n", 30, "controller")]
    [InlineData("\r\n", 20, "keyboard")][InlineData("\u2028", 30, "controller")]
    public async Task Multiline_pages_preserve_all_text_and_fit_without_scrolling(string separator, int lines, string input)
    {
        await using var session = new LauncherSession();
        var message = string.Concat(Enumerable.Repeat("detail" + separator, lines));
        var model = new FreedoomEngineProgressViewModel(session, new(), _ => Task.FromException<bool>(new IOException(message)));
        var window = new FreedoomEngineProgressDialog(model) { Width = 580, Height = 440 };
        try
        {
            Show(window); await model.StartAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(window.GetValue(DesktopInterfaceScaling.ManagesScrollingProperty));
            if (lines <= 30) Assert.True(model.Message.Length <= 280);
            var viewport = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>());
            var pages = new List<string>();
            while (true)
            {
                var text = window.DiagnosticText;
                Assert.True(text.IsVisible);
                var point = text.TranslatePoint(default, viewport)!.Value;
                Assert.InRange(point.Y, 0, viewport.Bounds.Height - text.Bounds.Height);
                pages.Add(text.Text!); Assert.InRange(pages.Count, 1, 60);
                if (separator == "\r\n") Assert.False(text.Text!.EndsWith('\r'));
                if (!window.NextDetailsButton.IsEnabled) break;
                if (input == "controller")
                {
                    GamepadFocusChrome.SetActive(true, window);
                    Assert.True(GamepadModalDialogNavigation.Instance.FocusDialogControl(window.NextDetailsButton));
                    Assert.True(GamepadModalDialogNavigation.Instance.TryHandleConfirm());
                }
                else
                {
                    Assert.True(window.NextDetailsButton.Focus());
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                }
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.True(window.IsVisible);
            }
            Assert.Equal(model.Message, string.Concat(pages));
            Assert.Contains(message, model.Message); Assert.False(model.Succeeded);
        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Oversized_diagnostic_is_explicitly_bounded_without_splitting_unicode()
    {
        await using var session = new LauncherSession();
        var message = string.Concat(Enumerable.Repeat("👾", 5000)) + "END_MARKER";
        var model = new FreedoomEngineProgressViewModel(session, new(), _ => Task.FromException<bool>(new IOException(message)));
        var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); await model.StartAsync();
            var pages = new List<string> { window.DiagnosticText.Text! };
            while (window.NextDetailsButton.IsEnabled)
            {
                window.NextDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pages.Add(window.DiagnosticText.Text!); Assert.InRange(pages.Count, 1, 60);
            }
            var displayed = string.Concat(pages);
            Assert.EndsWith("[Diagnostic display truncated.]", displayed);
            Assert.DoesNotContain("END_MARKER", displayed); Assert.Contains("END_MARKER", model.Message);
            _ = new System.Text.UTF8Encoding(false, true).GetBytes(displayed);
            Assert.True(window.IsVisible); Assert.False(model.Succeeded);
        }
        finally { window.Close(); await model.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Canceling_shared_metadata_waiter_does_not_claim_ownership_of_transport_or_allow_late_install()
    {
        using var f = new Fixture(); await f.Initialize(); await using var session = new LauncherSession();
        var entered = Signal(); var release = Signal(); var ended = Signal(); var observed = Signal();
        f.OnMetadata = async token =>
        {
            using var registration = token.Register(() => observed.TrySetResult()); entered.TrySetResult();
            try { await release.Task; token.ThrowIfCancellationRequested(); }
            finally { ended.TrySetResult(); }
        };
        var model = Model(f, session); var window = new FreedoomEngineProgressDialog(model);
        try
        {
            Show(window); Activate(window, window.InstallButton, "mouse"); await entered.Task.WaitAsync(Deadline);
            Cancel(window, "mouse"); await model.Work.WaitAsync(Deadline); await observed.Task.WaitAsync(Deadline);
            Assert.False(ended.Task.IsCompleted); Assert.False(model.IsBusy); Assert.False(model.Succeeded);
            Assert.Equal(0, f.DownloadCalls); Assert.False(session.Token.IsCancellationRequested);
            release.TrySetResult(); await ended.Task.WaitAsync(Deadline);
            Assert.False(model.Succeeded); Assert.Equal(0, f.DownloadCalls); Assert.False(Directory.Exists(f.Live));
        }
        finally { release.TrySetResult(); await ended.Task.WaitAsync(Deadline); await model.Work.WaitAsync(Deadline); window.Close(); model.DisposeAfterDrain(); }
    }
}
