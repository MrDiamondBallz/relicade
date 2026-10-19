using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class NativeLibrarySmokeTests
{
    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            throw new HttpRequestException("The native local-library smoke must not need a network.");
        }
    }

    [AvaloniaFact]
    public async Task Local_library_survives_restart_and_shell_reflects_missing_and_detected_files_offline()
    {
        var previous = RelicadePaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "relicade-native-library-smoke-" + Guid.NewGuid().ToString("N"));
        RelicadePaths.OverrideUserDataRoot = root;
        Directory.CreateDirectory(root);
        MainView? view = null;
        Window? window = null;
        try
        {
            var store = new FileSettingsStore();
            store.Current.FirstStartup = false;
            store.Current.LocalFirstCatalogMigrationComplete = true;
            store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Save(store.Current);
            using var network = new NoNetwork();
            using var http = new HttpClient(network);
            using var manager = new GameManager(store, http);
            var entry = new GameInfo { Name = "Synthetic local app", FolderName = "SyntheticLocal", Tags = ["fixture"] };
            await manager.CatalogService.SaveLocalAppsAsync([entry]);
            var restartedStore = new AppCatalogService(dataDirectory: root);
            var persisted = (await restartedStore.LoadLocalAppsAsync()).Should().ContainSingle().Subject;
            persisted.InstanceKey.Should().Be(entry.InstanceKey);
            persisted.CustomDisplayName = "Synthetic renamed app";
            await restartedStore.SaveLocalAppsAsync([persisted]);
            Directory.GetFiles(restartedStore.LibraryBackupDirectory, "*.json").Should().NotBeEmpty();
            await manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            var loaded = manager.Games.Should().ContainSingle().Subject;
            loaded.CustomDisplayName.Should().Be("Synthetic renamed app");
            loaded.Status.Should().Be(GameStatus.NotInstalled);
            loaded.StatusText.Should().Be("Waiting for files");
            loaded.CanLaunch.Should().BeFalse();

            // Detection fixture only, never execute it and never claim game/prerequisite acceptance.
            var payload = loaded.GetInstallPath(manager.GamesFolder);
            Directory.CreateDirectory(payload);
            var fixture = Path.Combine(payload, OperatingSystem.IsWindows() ? "fixture.exe" : "fixture.x86_64");
            await File.WriteAllTextAsync(fixture, "Synthetic text for executable-path detection only.", TestContext.Current.CancellationToken);
            await GameStatusService.CheckStatusAsync(loaded, http, manager.GamesFolder, checkRemoteVersion: false, applyCachedRelease: false);
            loaded.StatusText.Should().Be("Manually managed");
            loaded.CanLaunch.Should().BeTrue("baseline executable detection, not game-data readiness or actual execution");
            File.Delete(fixture);
            await GameStatusService.CheckStatusAsync(loaded, http, manager.GamesFolder, checkRemoteVersion: false, applyCachedRelease: false);
            loaded.CanLaunch.Should().BeFalse();

            view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
            window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();
            view.Library.Games.Should().ContainSingle().Which.InstanceKey.Should().Be(entry.InstanceKey);
            network.Requests.Should().Be(0);
            var before = await File.ReadAllTextAsync(manager.CatalogService.AppsConfigPath, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(manager.CatalogService.AppsConfigPath, "{synthetic-corruption", TestContext.Current.CancellationToken);
            var reload = () => manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            await reload.Should().ThrowAsync<System.Text.Json.JsonException>();
            manager.Games.Should().ContainSingle().Which.Should().BeSameAs(loaded);
            await File.WriteAllTextAsync(manager.CatalogService.AppsConfigPath, before, TestContext.Current.CancellationToken);
            await view.ShutdownAsync();
        }
        finally
        {
            window?.Close();
            if (view != null) await view.ShutdownAsync();
            RelicadePaths.OverrideUserDataRoot = previous;
            Directory.Delete(root, true);
        }
    }
}
