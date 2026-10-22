using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class FreedoomSetupTests
{
    [Fact]
    public void Optional_project_route_never_replaces_normal_engine_launch_requirements()
    {
        var game = new GameInfo { Repository = "UZDoom/UZDoom" };
        Assert.True(game.OffersFreedoomSetup); Assert.False(game.HasOwnedDataAdapter);
        Assert.Null(OwnedGameSetupService.Requirement(game));
        game.Repository = ""; Assert.False(game.OffersFreedoomSetup);
        game.Repository = "UZDoom/UZDoom"; game.RepositorySource = "gitlab"; Assert.False(game.OffersFreedoomSetup);
        game.RepositorySource = "github"; game.Repository = "someone/UZDoom"; Assert.False(game.OffersFreedoomSetup);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Data_argument_is_literal_and_does_not_move_working_directory(bool freedoom)
    {
        var info = new ProcessStartInfo { FileName = "own-fixture", WorkingDirectory = "/unchanged/save-root" };
        const string data = "/retained data/\";other --argument.wad";
        GameLaunchService.ConfigureOwnedDataArguments(info, data, freedoom);
        Assert.Equal(new[] { freedoom ? "-iwad" : "--rom-file", data }, info.ArgumentList);
        Assert.Equal("/unchanged/save-root", info.WorkingDirectory); Assert.Empty(info.Arguments);
    }

    [Fact]
    public async Task Unknown_engine_package_refuses_actual_optional_launch_without_start_or_history()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); f.Game.Repository = "UZDoom/UZDoom";
        var started = 0; f.Game.GameProcessStarted += _ => started++;
        var manifest = File.ReadAllBytes(Path.Combine(f.Live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName));
        Assert.False(await GameLaunchService.LaunchFreedoomAsync(f.Game, f.Manager.GamesFolder));
        Assert.Equal(0, started); Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt")));
        Assert.False(File.Exists(Path.Combine(new Relicade.Core.Services.PortableInstallationTransaction(f.Live).OperationsPath, "launch.json")));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(f.Live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(0, f.NetworkCalls);
    }

    [Fact]
    public async Task Verified_optional_data_reaches_owned_worker_as_literal_iwad_without_cwd_change()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        f.Definition = f.Definition with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(f.Data)) };
        var setup = f.Setup(); var plan = setup.Preview(f.Source, CancellationToken.None); setup.Apply(plan, CancellationToken.None);
        using var lifetime = Relicade.Core.Services.PortableInstallationLifetime.Begin(f.Live);
        var tx = new Relicade.Core.Services.PortableInstallationTransaction(f.Live);
        var data = tx.ValidateOwnedData(f.Definition, f.Root, lifetime.CommittedManifest!);
        var worker = Path.Combine(f.Root, "own-freedoom-argument-worker.sh");
        File.WriteAllText(worker, "#!/bin/sh\nprintf '%s\\n' \"$PWD\" \"$@\"\n");
        File.SetUnixFileMode(worker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var start = new ProcessStartInfo(worker) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = f.Live };
        GameLaunchService.ConfigureOwnedDataArguments(start, data, true);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(); var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode); Assert.Empty(error);
        Assert.Equal(new[] { f.Live, "-iwad", data }, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(0, f.NetworkCalls); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Browser_open_failure_is_reported_without_copy_launch_or_unhandled_exception(bool throws)
    {
        await using var session = new LauncherSession(); var calls = 0;
        var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic data only", (_, _) => throw new Exception(), (_, _) => throw new Exception(), () => Task.CompletedTask);
        model.ConfigureFreedoom(() => throw new Exception("Must not launch"), false);
        var dialog = new OwnedGameSetupDialog(model);
        model.OpenDownload = () => { calls++; return throws ? Task.FromException<bool>(new IOException("Synthetic browser unavailable")) : Task.FromResult(false); };
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(1, calls); Assert.Contains(OwnedGameSetupViewModel.OfficialFreedoomRelease, model.Message);
            Assert.True(model.CanChoose); Assert.False(model.CanPlay); Assert.Null(model.Plan);
            Assert.Equal(OwnedGameSetupUiState.Choose, model.State);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Actual_optional_dialog_requires_copy_confirmation_then_explicit_play()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        f.Definition = f.Definition with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(f.Data)) };
        var setup = f.Setup(); await using var session = new LauncherSession(); var launched = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        model.ConfigureFreedoom(() => { setup.ValidateReady(); launched++; return Task.FromResult(true); }, false);
        var dialog = new OwnedGameSetupDialog(model); model.PickFile = () => Task.FromResult<string?>(f.Source);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("*.wad", model.FilePattern); Assert.False(dialog.PlayButton.IsVisible); Assert.False(dialog.PlayButton.IsEnabled);
            Assert.True(dialog.CloseButton.IsDefault); Assert.False(dialog.PlayButton.IsDefault);
            dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.False(File.Exists(model.Plan!.Destination)); Assert.Equal(0, launched);
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal("freedoom2.wad", Path.GetFileName(model.Plan.Destination)); Assert.Equal(0, launched);
            Assert.True(model.CanPlay); Assert.True(dialog.PlayButton.IsEnabled); Assert.True(dialog.PlayButton.IsVisible);
            Assert.False(dialog.DownloadButton.IsVisible); Assert.False(dialog.ChooseButton.IsVisible);
            dialog.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(1, launched); Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
            Assert.Contains("Launch started", model.Message);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("data")][InlineData("library")][InlineData("kiosk")]
    public async Task Play_rechecks_ready_data_and_library_before_starting(string change)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); var setup = f.Setup();
        var plan = setup.Preview(f.Source, CancellationToken.None); setup.Apply(plan, CancellationToken.None);
        await using var session = new LauncherSession(); var launched = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        model.ConfigureFreedoom(() => { setup.ValidateReady(); launched++; return Task.FromResult(true); }, true);
        if (change == "data") File.WriteAllBytes(plan.Destination, [1, 2, 3, 4]);
        if (change == "library") File.AppendAllText(f.Manager.CatalogService.AppsConfigPath, " ");
        if (change == "kiosk") f.Store.Current.KioskMode = true;
        await model.PlayAsync(); Assert.Equal(0, launched); Assert.Equal(OwnedGameSetupUiState.Error, model.State);
        Assert.False(model.CanPlay); Assert.Equal(0, f.NetworkCalls); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
        model.Close(); model.DisposeAfterDrain();
    }

    [AvaloniaFact]
    public async Task Closed_optional_dialog_never_starts_play()
    {
        await using var session = new LauncherSession(); var launched = 0;
        var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic data only", (_, _) => throw new Exception(), (_, _) => throw new Exception(), () => Task.CompletedTask);
        model.ConfigureFreedoom(() => { launched++; return Task.FromResult(true); }, true);
        model.Close(); await model.PlayAsync(); Assert.Equal(0, launched); model.DisposeAfterDrain();
    }
}
