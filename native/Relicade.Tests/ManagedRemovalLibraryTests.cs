using System.Net;
using Relicade.Core.Models;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class ManagedRemovalLibraryTests
{
    public sealed class Store(string root) : ISettingsStore
    {
        public int Saves;
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(root, "Apps") };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
    public sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new HttpRequestException("Unexpected application request; never sent"); }
    }
    public sealed class Harness : IAsyncDisposable
    {
        readonly CredentialVaultTestScope credentials = new();
        public ManagedRemovalFixture Fixture { get; }
        public Store Settings { get; }
        public Handler Network { get; } = new();
        public AppCatalogService Catalog { get; }
        public GameManager Manager { get; }
        public LibraryViewModel Library { get; }
        public LauncherSession Session { get; } = new();
        public GameInfo Game { get; }
        public List<string> Errors { get; } = [];
        public List<string> Callbacks { get; } = [];
        public int Questions;
        public int Opened;
        public Func<Task<bool>> Confirm = () => Task.FromResult(true);
        public Action<string>? Mutation;
        public Harness(ManagedRemovalFixture fixture, bool located, bool kioskArmed = false)
        {
            Fixture = fixture; Settings = new(fixture.Root); Settings.Current.KioskMode = kioskArmed;
            Catalog = new(dataDirectory: Path.Combine(fixture.Root, "catalog"));
            Manager = new(Settings, new HttpClient(Network), catalogService: Catalog);
            Library = new(Manager, new SettingsViewModel(Settings));
            if (kioskArmed) Library.Settings.UnlockKioskSession();
            Game = new() { Name = "Synthetic removal", Repository = "fixture/removal", FolderName = "live", InstallPath = located ? fixture.Live : null,
                IsInLocalAppsJson = true, Status = GameStatus.Installed, SelectedExecutable = "fixture.exe", InstalledVersion = "old" };
            Manager.Games.Add(Game);
        }
        public async Task Save() => await Catalog.SaveLocalAppsAsync([Game]);
        public Task Remove() => Actions().UninstallAsync(Game);
        public LibraryActions Actions() => new(Manager, new LibraryPersistenceService(Manager), Library.Settings, Session, Library,
            () => throw new Exception("No picker"), async (message, _, question, _) =>
            { if (question) { Questions++; return await Confirm(); } Errors.Add(message); return true; },
            _ => Opened++, () => Task.CompletedTask, path =>
            {
                Callbacks.Add(path);
                Assert.ThrowsAny<IOException>(() => { using var writer = new PortableInstallationTransaction(Fixture.Live).AcquireLease(); });
                Mutation?.Invoke(path);
            });
        public async ValueTask DisposeAsync() { await Session.DisposeAsync(); Library.Dispose(); Manager.Dispose(); credentials.Dispose(); Fixture.Dispose(); }
    }
    [Theory]
    [InlineData(false, false, false)][InlineData(false, true, false)][InlineData(true, false, false)][InlineData(true, true, false)]
    [InlineData(false, false, true)][InlineData(false, true, true)][InlineData(true, false, true)][InlineData(true, true, true)]
    public async Task Disk_authority_refuses_managed_despite_stale_flags_and_NotInstalled(bool located, bool flag, bool notInstalled)
    {
        using var f = new ManagedRemovalFixture(); await f.Install("old"); await f.Install("new");
        await using var h = new Harness(f, located); await h.Save(); h.Game.IsManagedPortable = flag;
        if (notInstalled) h.Game.Status = GameStatus.NotInstalled;
        h.Game.IsLoading = true; // another owner may still be projecting a live operation
        var status = h.Game.Status; var before = f.Snapshot(); await h.Remove();
        Assert.Equal(before, f.Snapshot()); Assert.Empty(h.Callbacks); Assert.Single(h.Errors); Assert.Contains("Remove from Library", h.Errors[0]);
        Assert.Equal(status, h.Game.Status); Assert.Equal("fixture.exe", h.Game.SelectedExecutable); Assert.Equal(0, h.Network.Calls); Assert.Equal(0, h.Settings.Saves);
        Assert.Equal(located ? f.Live : null, h.Game.InstallPath); Assert.Equal(0, h.Questions); Assert.Equal(0, h.Opened); Assert.True(h.Game.IsLoading);
    }
    [Theory]
    [InlineData("MSI")][InlineData("Flatpak")][InlineData("stale-location-MSI")][InlineData("stale-location-Flatpak")]
    public async Task Conflicting_OS_receipts_cannot_bypass_portable_ownership(string route)
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        await using var h = new Harness(f, true);
        if (route.StartsWith("stale-location"))
        {
            h.Game.InstallPath = Path.Combine(f.Root, "external"); Directory.CreateDirectory(h.Game.InstallPath);
            using var lease = new PortableInstallationTransaction(h.Game.InstallPath).AcquireLease();
        }
        var target = h.Game.InstallPath!;
        if (route.EndsWith("MSI")) WindowsInstallerService.WriteReceipt(target, new("Linked", "v1", Path.Combine(target, "fixture.exe")));
        else File.WriteAllText(Path.Combine(target, "flatpak-install.json"), "synthetic conflicting receipt");
        await h.Save(); var before = f.Snapshot(); await h.Remove();
        Assert.Equal(before, f.Snapshot()); Assert.Empty(h.Callbacks); Assert.Single(h.Errors); Assert.Equal(0, h.Opened); Assert.Equal(0, h.Network.Calls);
    }
    [Theory]
    [InlineData(false, "cancel")][InlineData(true, "cancel")][InlineData(false, "close")][InlineData(true, "close")]
    [InlineData(false, "managed")][InlineData(true, "managed")][InlineData(false, "location")][InlineData(true, "location")]
    [InlineData(false, "kiosk")][InlineData(true, "kiosk")]
    [InlineData(false, "replace-unmanaged")][InlineData(true, "replace-unmanaged")]
    [InlineData(false, "receipt")][InlineData(true, "receipt")]
    [InlineData(false, "history")][InlineData(true, "history")]
    [InlineData(false, "running")][InlineData(true, "running")]
    public async Task Pending_consent_holds_no_lease_and_revalidates_after_decision(bool located, string change)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live); File.WriteAllText(Path.Combine(f.Live, "version.txt"), "old");
        await using var h = new Harness(f, located, kioskArmed: change == "kiosk"); await h.Save();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Confirm = () => { entered.SetResult(); return answer.Task; };
        var pending = h.Remove(); await entered.Task;
        using (f.Tx.AcquireLease()) { } // actual writer acquisition while prompt pending
        Task? shutdown = null;
        if (change == "managed")
        {
            File.Delete(Path.Combine(f.Live, "version.txt")); Directory.Delete(f.Live);
            await f.Install(); // real committed root replaces the unmanaged preview
        }
        if (change == "replace-unmanaged")
        {
            Directory.Move(f.Live, Path.Combine(f.Root, "previewed-unmanaged")); Directory.CreateDirectory(f.Live);
            File.WriteAllText(Path.Combine(f.Live, "unrelated-new-owner"), "keep new occupant");
            Directory.SetLastWriteTimeUtc(f.Live, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        if (change == "receipt") WindowsInstallerService.WriteReceipt(f.Live, new("Linked", "v1", Path.Combine(f.Live, "fixture.exe")));
        if (change == "history") Directory.CreateDirectory(Path.Combine(f.Tx.OperationsPath, Guid.NewGuid().ToString("N"), "candidate"));
        if (change == "running") { var lifetime = PortableInstallationLifetime.Begin(f.Live); lifetime.PreserveUncertain(); }
        if (change == "location") h.Game.InstallPath = Path.Combine(f.Root, "different");
        if (change == "close") shutdown = h.Session.DisposeAsync().AsTask();
        if (change == "kiosk") h.Library.Settings.LockKioskSession();
        var before = f.Snapshot(); answer.SetResult(change != "cancel"); await pending;
        if (shutdown != null) await shutdown;
        Assert.Equal(before, f.Snapshot()); Assert.Empty(h.Callbacks); Assert.Equal(0, h.Network.Calls);
        Assert.Equal(GameStatus.Installed, h.Game.Status);
        if (change is not ("cancel" or "close" or "kiosk")) Assert.Single(h.Errors); else Assert.Empty(h.Errors);
    }
    [Theory]
    [InlineData(false, false, false)][InlineData(true, false, false)][InlineData(false, true, false)][InlineData(true, true, false)]
    [InlineData(false, false, true)][InlineData(true, false, true)][InlineData(false, true, true)][InlineData(true, true, true)]
    public async Task Allowed_unmanaged_paths_hold_exclusion_and_callback_failure_stays_visible(bool located, bool fail, bool manual)
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE fixture");
        File.WriteAllText(Path.Combine(f.Live, "other"), "keep");
        File.WriteAllText(Path.Combine(f.Live, "version.txt"), "old");
        File.WriteAllText(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        await using var h = new Harness(f, located); h.Game.IsManagedPortable = true;
        if (manual) h.Game.Repository = null;
        await h.Save();
        h.Mutation = path =>
        {
            if (fail) throw new IOException("benign injected failure");
            if (located) File.Move(path, Path.Combine(f.Root, "benign-trash"));
            else Directory.Move(path, Path.Combine(f.Root, "benign-trash"));
        };
        var before = f.Snapshot(); await h.Remove();
        Assert.Equal(new[] { located ? Path.Combine(f.Live, "fixture.exe") : f.Live }, h.Callbacks);
        Assert.Equal(0, h.Network.Calls); Assert.False(h.Game.IsLoading);
        if (fail)
        {
            Assert.Equal(before, f.Snapshot()); Assert.Single(h.Errors); Assert.Equal(GameStatus.Installed, h.Game.Status);
            Assert.Equal(located ? f.Live : null, h.Game.InstallPath);
        }
        else
        {
            Assert.Empty(h.Errors); Assert.True(File.Exists(Path.Combine(f.Root, "sibling-canary")));
            if (located) { Assert.Null(h.Game.InstallPath); Assert.True(File.Exists(Path.Combine(f.Live, "other"))); Assert.False(File.Exists(Path.Combine(f.Live, "version.txt"))); }
            else Assert.False(Directory.Exists(f.Live));
        }
    }
    [Fact]
    public async Task Missing_unmanaged_folder_does_not_invent_success_or_clear_status()
    {
        using var f = new ManagedRemovalFixture(); await using var h = new Harness(f, false); await h.Save();
        var previous = Telemetry.Current; var sent = new List<string>();
        using var telemetry = new Telemetry("phc_test_synthetic", "https://fixture.invalid", async (request, _) =>
        { sent.Add(await request.Content!.ReadAsStringAsync()); return new HttpResponseMessage(HttpStatusCode.OK); });
        Telemetry.Current = telemetry; telemetry.Configure(true, "synthetic-removal-id");
        try
        {
            var before = f.Snapshot(); await h.Remove(); await telemetry.FlushAsync();
            Assert.Single(h.Errors); Assert.Empty(h.Callbacks); Assert.Empty(sent); Assert.Equal(0, h.Network.Calls);
            Assert.Equal(GameStatus.Installed, h.Game.Status); Assert.Equal(before, f.Snapshot());
        }
        finally { Telemetry.Current = previous; }
    }
    [Fact]
    public async Task Shared_located_library_removal_keeps_all_shared_bytes_and_only_detaches_link()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, "version.txt"), "old"); File.WriteAllText(Path.Combine(f.Live, "fixture.exe"), "NONEXECUTABLE");
        File.WriteAllText(Path.Combine(f.Live, InstalledBuild.FileNameOnDisk), "fixture.exe\n" + new string('a', 64) + "\n");
        await using var h = new Harness(f, true); await h.Save();
        h.Manager.Games.Add(new() { FolderName = "other", Name = "Other synthetic", Repository = "fixture/other", InstallPath = f.Live });
        var before = Directory.GetFiles(f.Live).ToDictionary(p => p, File.ReadAllText);
        await h.Remove(); Assert.Empty(h.Callbacks); Assert.Empty(h.Errors); Assert.Null(h.Game.InstallPath);
        Assert.Equal(before.OrderBy(p => p.Key), Directory.GetFiles(f.Live).ToDictionary(p => p, File.ReadAllText).OrderBy(p => p.Key));
        Assert.Equal(0, h.Network.Calls);
    }
    [Fact]
    public async Task Unmanaged_MSI_keeps_OS_ownership_without_native_dispatch_on_Linux()
    {
        Assert.True(OperatingSystem.IsLinux()); // fixture environment, not Windows acceptance
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        WindowsInstallerService.WriteReceipt(f.Live, new("Linked", "v1", Path.Combine(f.Live, "fixture.exe")));
        await using var h = new Harness(f, false); await h.Save();
        var before = f.Snapshot(); await h.Remove();
        Assert.Empty(h.Callbacks); Assert.Equal(0, h.Opened); Assert.Single(h.Errors); Assert.Contains("Windows", h.Errors[0]);
        Assert.Equal(before, f.Snapshot()); Assert.Equal(0, h.Network.Calls);
    }
    [Fact]
    public async Task Unmanaged_Flatpak_cancel_does_not_dispatch_real_package_runner()
    {
        using var f = new ManagedRemovalFixture(); Directory.CreateDirectory(f.Live);
        File.WriteAllText(Path.Combine(f.Live, FlatpakService.ReceiptFileName), "synthetic receipt; cancellation prevents parsing/OS dispatch");
        await using var h = new Harness(f, false); await h.Save(); h.Confirm = () => Task.FromResult(false);
        var before = f.Snapshot(); await h.Remove();
        Assert.Equal(1, h.Questions); Assert.Empty(h.Errors); Assert.Empty(h.Callbacks); Assert.Equal(before, f.Snapshot()); Assert.Equal(0, h.Network.Calls);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Remove_from_library_keeps_managed_generations_and_files(bool conflictingReceipt)
    {
        using var f = new ManagedRemovalFixture(); await f.Install();
        if (conflictingReceipt) WindowsInstallerService.WriteReceipt(f.Live, new("Linked", "v1", Path.Combine(f.Live, "fixture.exe")));
        await using var h = new Harness(f, true); await h.Save();
        var before = Directory.GetFiles(f.Live, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
        await h.Actions().RemoveEntryAsync(h.Game);
        Assert.Empty(await h.Catalog.LoadLocalAppsAsync()); Assert.Empty(h.Callbacks);
        Assert.Equal(before.OrderBy(p => p.Key), Directory.GetFiles(f.Live, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText).OrderBy(p => p.Key));
    }
}
