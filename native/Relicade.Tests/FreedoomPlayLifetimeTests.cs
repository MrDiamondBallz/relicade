using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class FreedoomPlayLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Production_play_composition_drains_closed_dialog_without_later_dispatch(bool closeSession)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); var setup = f.Setup();
        setup.Apply(setup.Preview(f.Source, CancellationToken.None), CancellationToken.None);
        await using var session = new LauncherSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); var dispatched = 0; var completed = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        OwnedGameSetupFlow.ConfigureFreedoomPlay(model, true,
            _ => Task.Run(() => { entered.TrySetResult(); release.Wait(); setup.ValidateReady(); }),
            _ => { dispatched++; return Task.FromResult(true); }, _ => completed++);
        var dialog = new OwnedGameSetupDialog(model); Task close = Task.CompletedTask;
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            dialog.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(dialog.Work.IsCompleted);
            dialog.Close(); Assert.True(model.IsClosed);
            if (closeSession) close = session.DisposeAsync().AsTask();
            release.Set(); await Task.WhenAll(dialog.Work, close).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, dispatched); Assert.Equal(0, completed); Assert.Equal(0, f.NetworkCalls);
        }
        finally { release.Set(); dialog.Close(); await Task.WhenAll(dialog.Work, close); model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("success", 1)][InlineData("false", 0)][InlineData("error", 0)][InlineData("closed", 0)]
    public async Task Close_after_launch_is_forwarded_only_for_successful_open_dialog(string result, int expected)
    {
        await using var session = new LauncherSession(); var completed = 0;
        var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic data only",
            (_, _) => throw new Exception(), (_, _) => throw new Exception(), () => Task.CompletedTask);
        OwnedGameSetupFlow.ConfigureFreedoomPlay(model, true, _ => Task.CompletedTask, _ =>
        {
            if (result == "error") throw new IOException("Synthetic start failure");
            if (result == "closed") model.Close();
            return Task.FromResult(result != "false");
        }, success => { Assert.True(success); completed++; });
        try { await model.PlayAsync(); Assert.Equal(expected, completed); }
        finally { model.Close(); model.DisposeAfterDrain(); }
    }

    [Fact]
    public async Task Cancellation_after_readiness_before_process_attempt_does_not_create_uncertain_receipt()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); var setup = f.Setup();
        setup.Apply(setup.Preview(f.Source, CancellationToken.None), CancellationToken.None);
        var tx = new PortableInstallationTransaction(f.Live); var manifest = File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
        using var cancel = new CancellationTokenSource(); var dispatched = 0; var tracked = 0;
        using (var lifetime = PortableInstallationLifetime.Begin(f.Live))
        {
            tx.ValidateOwnedData(f.Definition, f.Root, lifetime.CommittedManifest!);
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => GameLaunchService.StartProcessAttempt(lifetime, cancel.Token,
                () => { dispatched++; return null; }, _ => tracked++));
        }
        Assert.Equal(0, dispatched); Assert.Equal(0, tracked);
        Assert.False(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt")));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
        using var next = PortableInstallationLifetime.Begin(f.Live);
        Assert.NotNull(next.CommittedManifest); Assert.Equal(0, f.NetworkCalls);
    }

    [Fact]
    public async Task Failure_after_process_attempt_still_preserves_uncertainty()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        var tx = new PortableInstallationTransaction(f.Live);
        using (var lifetime = PortableInstallationLifetime.Begin(f.Live))
            Assert.Throws<IOException>(() => GameLaunchService.StartProcessAttempt(lifetime, CancellationToken.None,
                () => throw new IOException("Synthetic process attempt failure"), _ => throw new Exception("Must not track")));
        Assert.True(File.Exists(Path.Combine(tx.OperationsPath, "launch.json")));
        Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt"))); Assert.Equal(0, f.NetworkCalls);
    }
}
