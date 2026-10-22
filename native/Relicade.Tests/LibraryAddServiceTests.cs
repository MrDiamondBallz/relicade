using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public class LibraryAddServiceTests
{
    [Fact]
    public void Another_app_in_the_same_folder_is_a_conflict_that_names_the_occupant()
    {
        var occupant = new GameInfo { Name = "Zelda 64: Recompiled", Repository = "Zelda64Recomp/Zelda64Recomp", FolderName = "Zelda64" };
        // Folder names compare like the file system does on Windows: trimmed and case-insensitive.
        var external = new GameInfo { Name = "2 Ship 2 Harkinian", Repository = "HarbourMasters/2ship2harkinian", FolderName = " zelda64 " };

        var result = LibraryAddService.PlanAdd([occupant], external, autoUpdate: false);

        result.Outcome.Should().Be(LibraryAddOutcome.FolderConflict);
        result.App.Should().BeNull();
        result.Library.Should().ContainSingle().Which.Should().BeSameAs(occupant);
        result.Error.Should().Contain("Zelda 64: Recompiled");
    }

    [AvaloniaFact]
    public async Task Add_saves_and_shows_the_app_without_waiting_for_the_network()
    {
        await using var launcher = new IsolatedLauncher();
        launcher.Store.Current.AutoUpdateNewlyAddedApps = true;
        var external = new GameInfo { Name = "Starship", Repository = "HarbourMasters/Starship", FolderName = "Starship" };

        // Every request hangs until the session closes, so Add can only finish if it never waits on the network.
        // The second call is a double activation: it queues behind the first and finds the saved app.
        var results = await Task.WhenAll(launcher.Service.AddAsync(external), launcher.Service.AddAsync(external))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        results.Select(r => r.Outcome).Should().Equal(LibraryAddOutcome.Added, LibraryAddOutcome.AlreadyAdded);
        var added = results[0].App!;
        added.Should().NotBeSameAs(external, "the library owns a copy, not the Browse entry");
        var saved = (await new AppCatalogService(dataDirectory: launcher.Root).LoadLocalAppsAsync()).Should().ContainSingle().Subject;
        saved.InstanceKey.Should().Be(external.InstanceKey);
        saved.AutoUpdate.Should().BeTrue();
        launcher.Manager.LibraryApps.Should().ContainSingle().Which.Should().BeSameAs(added);
        launcher.Manager.Games.Should().ContainSingle().Which.Should().BeSameAs(added);

        await launcher.Session.DisposeAsync();
        launcher.Network.Requests.Should().OnlyContain(uri => uri.AbsolutePath.StartsWith("/repos/HarbourMasters/Starship/releases") || uri.AbsolutePath == "/api/v1/release-status",
            "only the background lookups for the new app (its releases, and whether the source catalog verified one) may use the network, and closing the session cancels them");
    }

    [AvaloniaFact]
    public async Task Add_preserves_catalog_identity_before_background_enrichment()
    {
        await using var launcher = new IsolatedLauncher();
        var external = new GameInfo { Name = "Fixture", Repository = "fixture/catalog", FolderName = "Fixture", CatalogEntryId = "entry-fixture" };
        var result = await launcher.Service.AddAsync(external).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        result.App!.CatalogEntryId.Should().Be("entry-fixture");
        var saved = (await new AppCatalogService(dataDirectory: launcher.Root).LoadLocalAppsAsync()).Should().ContainSingle().Subject;
        saved.CatalogEntryId.Should().Be("entry-fixture");
        await launcher.Session.DisposeAsync();
    }

    [Fact]
    public void Manual_local_entries_do_not_inherit_a_catalog_download_identity()
    {
        var local = new GameInfo { Name = "Local", FolderName = "Local", CatalogEntryId = "stale-id" };
        local.IsManuallyManaged.Should().BeTrue();
        LibraryAddService.CloneForLocal(local).CatalogEntryId.Should().BeNull();
        CatalogReleases.HasCatalogIdentity(local).Should().BeFalse();
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    /// <summary>Records every request and never answers it before it is cancelled.</summary>
    private sealed class HangingNetwork : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    /// <summary>A launcher with its own data folder, wired the way the app wires Browse's Add.</summary>
    private sealed class IsolatedLauncher : IAsyncDisposable
    {
        private readonly string? _previousRoot = RelicadePaths.OverrideUserDataRoot;
        private readonly HttpClient _http;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "relicade-library-add-" + Guid.NewGuid().ToString("N"));
        public Store Store { get; } = new();
        public HangingNetwork Network { get; } = new();
        public LauncherSession Session { get; } = new();
        public GameManager Manager { get; }
        public LibraryAddService Service { get; }

        public IsolatedLauncher()
        {
            Directory.CreateDirectory(Root);
            RelicadePaths.OverrideUserDataRoot = Root;
            Store.Current.AppsPath = Path.Combine(Root, "Apps");
            _http = new HttpClient(Network);
            Manager = new GameManager(Store, _http)
            {
                UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask(),
            };
            Service = new LibraryAddService(Manager, new SettingsViewModel(Store), Session);
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            Manager.Dispose();
            _http.Dispose();
            RelicadePaths.OverrideUserDataRoot = _previousRoot;
            Directory.Delete(Root, true);
        }
    }
}
