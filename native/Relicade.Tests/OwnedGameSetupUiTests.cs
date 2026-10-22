using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Core.Services;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

internal sealed class OwnedSetupFixture : IDisposable
{
    readonly string? priorRoot = RelicadePaths.OverrideUserDataRoot;
    readonly ISettingsStore priorStore = SettingsStoreProvider.Default;
    internal readonly string Root = Path.Combine(Path.GetTempPath(), "owned-setup-ui-" + Guid.NewGuid().ToString("N"));
    internal readonly GameInfo Game = new() { Name = "Synthetic setup fixture", Repository = "perfect-dark-pc-port/perfect_dark", FolderName = "Fixture", IsCustom = true };
    internal readonly byte[] Data = [10, 20, 30, 40];
    internal FileSettingsStore Store = null!;
    internal GameManager Manager = null!;
    internal OwnedDataRequirement Definition = null!;
    internal string Source => Path.Combine(Root, "original.z64");
    internal string Live => Path.Combine(Root, "Apps", "Fixture");
    internal int NetworkCalls;
    sealed class DenyNetwork(OwnedSetupFixture owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { owner.NetworkCalls++; throw new InvalidOperationException("Unexpected external request in isolated setup fixture."); }
    }
    internal async Task Initialize()
    {
        Directory.CreateDirectory(Root); RelicadePaths.OverrideUserDataRoot = Root;
        Store = new FileSettingsStore(Path.Combine(Root, "settings.json"));
        Store.Current.AppsPath = Path.Combine(Root, "Apps"); Store.Save(Store.Current); SettingsStoreProvider.Default = Store;
        Manager = new GameManager(Store, new HttpClient(new DenyNetwork(this)), new AppCatalogService(dataDirectory: Root));
        Manager.Games = new ObservableCollection<GameInfo> { Game };
        await Manager.CatalogService.SaveLocalAppsAsync([Game]);
        File.WriteAllBytes(Source, Data);
        var executable = PortableInstallationFixtures.NativePayload("owned setup UI");
        Definition = new("synthetic-ui", "Synthetic fixture, not game data", "fixture-release", "fixture-asset",
            Convert.ToHexStringLower(SHA256.HashData(executable)), Data.Length, Convert.ToHexStringLower(MD5.HashData(Data)));
        await new PortableInstallationTransaction(Live, new() { ReleaseIdentity = Definition.ReleaseIdentity, AssetIdentity = Definition.AssetIdentity })
            .InstallAsync(stage => { File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), executable); return Task.CompletedTask; }, "fixture-release");
        Game.InstalledVersion = "fixture-release"; Game.Status = GameStatus.Installed;
    }
    internal OwnedGameSetupSession Setup() => new(Manager, Game.InstanceKey, Definition);
    public void Dispose()
    { Manager?.Dispose(); SettingsStoreProvider.Default = priorStore; RelicadePaths.OverrideUserDataRoot = priorRoot; if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

public sealed class OwnedGameSetupUiTests
{
    [AvaloniaFact]
    public async Task Actual_picker_preview_and_confirm_controls_copy_only_after_explicit_click()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var setup = f.Setup(); var applied = 0;
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, "Synthetic fixture, not game data", setup.Preview,
            (plan, token) => { applied++; return setup.Apply(plan, token); }, () => Task.CompletedTask);
        var dialog = new OwnedGameSetupDialog(model); model.PickFile = () => Task.FromResult<string?>(f.Source);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.CloseButton.IsDefault); Assert.False(dialog.ConfirmButton.IsDefault); Assert.False(model.CanConfirm);
            dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Confirm, model.State); Assert.Equal(0, applied);
            Assert.False(File.Exists(model.Plan!.Destination)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var work = dialog.Work;
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await work;
            Assert.Equal(1, applied); Assert.Equal(OwnedGameSetupUiState.Completed, model.State);
            Assert.Equal(f.Data, File.ReadAllBytes(model.Plan.Destination)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
            Assert.Equal(0, f.NetworkCalls); Assert.False(model.CanConfirm);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("library")][InlineData("kiosk")][InlineData("saved-kiosk")][InlineData("root")][InlineData("provider")]
    public async Task Actual_caller_refuses_changed_context_after_preview_without_copy(string change)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); var setup = f.Setup();
        var plan = setup.Preview(f.Source, CancellationToken.None);
        if (change == "library") File.AppendAllText(f.Manager.CatalogService.AppsConfigPath, " ");
        if (change == "kiosk") f.Store.Current.KioskMode = true;
        if (change == "saved-kiosk")
        {
            var other = new FileSettingsStore(Path.Combine(f.Root, "settings.json")); var settings = other.Load(); settings.KioskMode = true; other.Save(settings);
        }
        if (change == "provider") f.Game.RepositorySource = "gitlab";
        if (change == "root") f.Game.InstallPath = Path.Combine(f.Root, "other");
        Assert.ThrowsAny<IOException>(() => setup.Apply(plan, CancellationToken.None));
        Assert.False(File.Exists(plan.Destination)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
    }

    [AvaloniaFact]
    public async Task Closing_during_inspection_drains_without_late_preview_or_copy()
    {
        await using var session = new LauncherSession(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); var applied = 0; var lateChanges = 0;
        var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic fixture", (_, token) =>
        { started.TrySetResult(); release.Wait(); token.ThrowIfCancellationRequested(); throw new InvalidOperationException(); },
            (_, _) => { applied++; throw new InvalidOperationException(); }, () => Task.CompletedTask);
        var dialog = new OwnedGameSetupDialog(model); model.PickFile = () => Task.FromResult<string?>("synthetic-file");
        try
        {
            dialog.Show(); dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(dialog.Work.IsCompleted); bool responsive = false; await Dispatcher.UIThread.InvokeAsync(() => responsive = true); Assert.True(responsive);
            dialog.Close(); model.PropertyChanged += (_, _) => lateChanges++;
            release.Set(); await dialog.Work;
            Assert.True(model.IsClosed); Assert.Null(model.Plan); Assert.Equal(0, applied); Assert.Equal(0, lateChanges);
        }
        finally { release.Set(); dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaFact]
    public async Task Mouse_controller_and_menu_primary_actions_route_missing_game_data_to_setup()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); await using var session = new LauncherSession();
        var anchor = new Button(); var opened = 0;
        var controller = new LibraryLaunchController(f.Manager, new SettingsViewModel(f.Store), session, null!, (_, _) => anchor,
            (_, _) => throw new Exception("Unexpected menu"), (_, _) => throw new Exception("Unexpected error"), _ => throw new Exception("Unexpected folder"),
            () => { }, _ => throw new Exception("No game was launched"), () => 800);
        controller.ConfigureOwnedGameSetup((game, _) => { Assert.Same(f.Game, game); opened++; return Task.CompletedTask; });
        Assert.Equal("Finish game setup", f.Game.ButtonText); Assert.False(f.Game.CanLaunch);
        await controller.PerformGamePrimaryActionAsync(f.Game, anchor);
        await controller.PerformSelectedGameActionAsync(f.Game);
        await controller.LaunchFromMenuAsync(f.Game, anchor);
        Assert.Equal(3, opened); Assert.Equal(0, f.NetworkCalls);
    }
}
