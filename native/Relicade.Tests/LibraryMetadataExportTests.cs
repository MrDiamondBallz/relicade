using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Core.Models;
using Relicade.ViewModels;
using Relicade.Models;
using Relicade.Services;
using Relicade.Views;
using System.Security.Cryptography;
using System.Text.Json;

namespace Relicade.Tests;

public sealed class LibraryMetadataExportTests
{
    [AvaloniaFact]
    public void Library_has_an_explicit_metadata_export_preview_route()
    {
        // Actual unattached view only: no Configure/load/window/action/picker.
        var view = new LibraryView();
        var export = view.FindControl<Button>("ExportLibraryMetadataButton");
        Assert.NotNull(export);
        Assert.Equal("Export metadata…", export.Content);
    }

    [Fact]
    public async Task Legacy_raw_export_retains_persistence_fields_and_is_not_the_private_metadata_format()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-export-baseline-" + Guid.NewGuid().ToString("N"));
        var service = new AppCatalogService(dataDirectory: root);
        var original = Path.Combine(root, "apps.json");
        var sentinel = Path.Combine(root, "owned-user-data.keep");
        await File.WriteAllTextAsync(original, "{\"apps\":[]}");
        await File.WriteAllTextAsync(sentinel, "authored original bytes");
        var before = Snapshot(root);
        var app = new GameInfo
        {
            Name = "Authored fixture", Repository = "example/fixture", FolderName = "fixture",
            InstallPath = "/synthetic-private-user/game",
            LinuxPrefixPath = "/synthetic-private-user/prefix",
            LinuxCustomLaunchCommand = "synthetic-command-canary --secret=not-real",
            GameIconUrl = "https://invalid.example/art?token=synthetic-query-canary",
        };
        var destination = Path.Combine(root, "raw-export.json");
        // Explicit list avoids the legacy load/backup/migration route entirely.
        await service.ExportLocalAppsToFileAsync(destination, [app]);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(destination));
        var row = document.RootElement.GetProperty("apps")[0];
        Assert.Equal(app.InstallPath, row.GetProperty("installPath").GetString());
        Assert.Equal(app.LinuxPrefixPath, row.GetProperty("linuxPrefixPath").GetString());
        Assert.Equal(app.LinuxCustomLaunchCommand, row.GetProperty("linuxCustomLaunchCommand").GetString());
        Assert.Equal(app.GameIconUrl, row.GetProperty("appIconUrl").GetString());
        Assert.False(document.RootElement.TryGetProperty("format", out _));
        var after = Snapshot(root); after.Remove("raw-export.json");
        Assert.Equal(before, after);
        Assert.False(Directory.Exists(service.LibraryBackupDirectory));
        // Preserve only authored root as evidence; no real paths/files/HTTP/process.
    }

    [Theory]
    [InlineData("github", "owner/repo")][InlineData("gitlab", "group/team/repo")]
    [InlineData("codeberg", "owner/repo")][InlineData("manual", null)]
    public void Portable_document_roundtrips_only_explicit_loaded_metadata(string provider, string? repository)
    {
        var entry = new LibraryMetadataEntry(provider, repository, "Fixture", "Base name", "Base project", "shared-advisory-id",
            repository == null ? null : "release/v1.0", null, repository == null ? null : "*linux*x64*");
        var document = LibraryMetadataDocument.Create([entry], DateTimeOffset.UnixEpoch);
        var decoded = LibraryMetadataDocument.Decode(document.Encode());
        Assert.Equal(document.Entries, decoded.Entries); Assert.Equal(document.ObservedAt, decoded.ObservedAt);
        Assert.Equal("loaded-metadata-only-no-restore", decoded.Scope);
    }

    [Fact]
    public void Privacy_projection_excludes_paths_commands_urls_and_custom_presentation_without_reading_them()
    {
        using var app = new GameInfo { Name = "Base fixture", Project = "Base project", Repository = "owner/repo", FolderName = "Fixture",
            InstallPath = "/private-canary/install", LinuxPrefixPath = "/private-canary/prefix", LinuxProtonPath = "/private-canary/proton",
            LinuxCustomLaunchCommand = "private-canary-command", GameIconUrl = "https://invalid.example/?token=private-canary",
            CustomDisplayName = "private-canary-custom", Tags = ["private-canary-tag"], AutoUpdate = true };
        var document = LibraryMetadataDocument.Create([LibraryMetadataDocument.Capture(app)], DateTimeOffset.UnixEpoch);
        var json = System.Text.Encoding.UTF8.GetString(document.Encode());
        Assert.DoesNotContain("private-canary", json); Assert.DoesNotContain("autoUpdate", json);
        Assert.Contains("Base fixture", json); Assert.Contains("Base project", json);
        Assert.Equal("/private-canary/install", app.InstallPath); Assert.True(app.AutoUpdate);
    }

    [Theory]
    [InlineData("provider")][InlineData("folder")][InlineData("release")][InlineData("repository")][InlineData("control")]
    public void Unsupported_or_path_bearing_identity_is_refused_not_silently_sanitized(string change)
    {
        var row = new LibraryMetadataEntry("github", "owner/repo", "Fixture", "Fixture", null, null, "v1", null, null);
        row = change switch
        {
            "provider" => row with { Provider = "unknown" },
            "folder" => row with { FolderKey = "/private-canary/folder" },
            "release" => row with { PreferredRelease = "C:\\private-canary\\release" },
            "repository" => row with { Repository = "https://user:secret@invalid.example/repo" },
            _ => row with { Name = "Fixture\nprivate-canary" },
        };
        Assert.Throws<InvalidDataException>(() => LibraryMetadataDocument.Create([row], DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("future")][InlineData("duplicate-property")][InlineData("unknown-field")]
    public void Metadata_decoder_refuses_ambiguous_or_future_documents(string change)
    {
        var d = LibraryMetadataDocument.Create([], DateTimeOffset.UnixEpoch);
        var json = System.Text.Encoding.UTF8.GetString(d.Encode());
        json = change switch
        {
            "future" => json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            "duplicate-property" => json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"),
            _ => json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"secret\": \"fixture\""),
        };
        Assert.ThrowsAny<Exception>(() => LibraryMetadataDocument.Decode(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Distinct_filters_and_instances_keep_shared_catalog_provenance_without_duplicate_folder_claims()
    {
        var first = new LibraryMetadataEntry("github", "owner/repo", "First", "First", null, "same-catalog-id", "v1", null, "first*");
        var second = first with { FolderKey = "Second", Name = "Second", AssetFilter = "second*" };
        Assert.Equal(2, LibraryMetadataDocument.Create([first, second], DateTimeOffset.UnixEpoch).Entries.Length);
        Assert.Throws<InvalidDataException>(() => LibraryMetadataDocument.Create([first, first with { FolderKey = "FIRST" }], DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("success")][InlineData("existing")][InlineData("protected")]
    [InlineData("linked-parent")][InlineData("cancel")][InlineData("write-failure")]
    public async Task New_file_writer_preserves_originals_and_reports_only_explicit_commit(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-export-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var state = Path.Combine(root, "state"); Directory.CreateDirectory(state);
        var user = Path.Combine(root, "retained.keep"); await File.WriteAllTextAsync(user, "retained bytes");
        var destination = Path.Combine(root, "metadata.json");
        if (mode == "existing") await File.WriteAllTextAsync(destination, "existing export");
        if (mode == "protected") destination = Path.Combine(state, "metadata.json");
        if (mode == "linked-parent") { Directory.CreateSymbolicLink(Path.Combine(root, "linked"), state); destination = Path.Combine(root, "linked", "metadata.json"); }
        var before = Snapshot(root); var bytes = LibraryMetadataDocument.Create([], DateTimeOffset.UnixEpoch).Encode();
        using var cancellation = new CancellationTokenSource(); var committed = false;
        Func<Task>? boundary = mode == "cancel" ? () => { cancellation.Cancel(); return Task.CompletedTask; }
            : mode == "write-failure" ? () => throw new IOException("authored boundary failure") : null;
        var write = () => LibraryMetadataExportWriter.WriteNewAsync(bytes, destination, [state], cancellation.Token,
            commit => { commit(); committed = true; }, boundary);
        if (mode == "success")
        {
            await write(); Assert.True(committed); Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
            var after = Snapshot(root); after.Remove("metadata.json"); Assert.Equal(before, after);
        }
        else
        {
            await Assert.ThrowsAnyAsync<Exception>(write); Assert.False(committed); Assert.Equal(before, Snapshot(root));
            if (mode == "linked-parent") Assert.Equal(state, new DirectoryInfo(Path.Combine(root, "linked")).LinkTarget);
        }
    }

    [AvaloniaTheory]
    [InlineData("active")][InlineData("failed")][InlineData("overlap")][InlineData("changed-entry")][InlineData("changed-root")]
    public async Task Settled_snapshot_refuses_inflight_failed_or_changed_context(string change)
    {
        await using var f = await ExportScope.Create();
        var snapshot = f.Manager.CaptureLibraryMetadata(); Assert.Single(snapshot.Document.Entries); Assert.Empty(f.Manager.Games);
        var before = Snapshot(f.Root); long? load = null; var oldRoot = RelicadePaths.OverrideUserDataRoot;
        try
        {
            if (change is "active" or "failed" or "overlap")
            {
                load = f.Manager.BeginMetadataLoad();
                if (change == "failed") { f.Manager.CompleteMetadataLoad(load.Value, false); load = null; }
                if (change == "overlap")
                {
                    var second = f.Manager.BeginMetadataLoad(); f.Manager.CompleteMetadataLoad(load.Value, true); load = null;
                    f.Manager.CompleteMetadataLoad(second, true);
                }
            }
            if (change == "changed-entry") f.Manager.LibraryApps.Single().Name = "Changed base metadata";
            if (change == "changed-root") RelicadePaths.OverrideUserDataRoot = Path.Combine(f.Root, "different-context");
            Assert.False(f.Manager.IsCurrentMetadata(snapshot));
            Assert.Throws<InvalidOperationException>(() => f.Manager.CommitMetadataExport(snapshot, () => throw new Exception("No commit permitted.")));
            Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally { if (load != null) f.Manager.CompleteMetadataLoad(load.Value, false); RelicadePaths.OverrideUserDataRoot = oldRoot; }
    }

    [AvaloniaTheory]
    [InlineData("save")][InlineData("decline")][InlineData("close")][InlineData("changed")]
    public async Task Actual_preview_requires_explicit_file_choice_and_drains_without_false_commit(string mode)
    {
        await using var f = await ExportScope.Create();
        using var library = new LibraryViewModel(f.Manager, f.Settings);
        var view = new LibraryView(); view.Configure(library, f.Session, null!, (_, _) => { });
        await library.SetupHistory.Work;
        using var dialogs = new LauncherDialogLifetime(f.Session.Token, action => Dispatcher.UIThread.Post(action));
        var destination = Path.Combine(f.Root, "approved-metadata.json"); var choices = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string?> Choose()
        {
            choices++; entered.TrySetResult();
            if (mode is "close" or "changed") await release.Task;
            return mode == "decline" ? null : destination;
        }
        var flow = new LibraryMetadataExportFlow(f.Manager, f.Settings, f.Session, dialogs, Choose);
        var openings = new List<Task>();
        view.ConfigureMetadataExport(origin => { var work = flow.OpenAsync(origin); openings.Add(work); return work; });
        var before = Snapshot(f.Root);
        try
        {
            view.ExportLibraryMetadataButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = flow.Dialog; Assert.NotNull(dialog); Assert.True(dialog.IsVisible); Assert.True(dialog.CancelButton.IsDefault);
            Assert.Contains("not a backup", dialog.Model.Pages[0]); Assert.Contains("cannot restore", dialog.Model.Pages[0]);
            Assert.Equal(0, choices); Assert.Equal(before, Snapshot(f.Root));
            view.ExportLibraryMetadataButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Same(dialog, flow.Dialog);
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (mode == "close") { dialog.Close(); Assert.False(dialog.Model.Work.IsCompleted); }
            if (mode == "changed") f.Manager.LibraryApps.Single().Name = "newer name";
            release.TrySetResult(); await dialog.Model.Work;
            Assert.Equal(1, choices);
            if (mode == "save")
            {
                Assert.True(dialog.Model.Saved);
                var decoded = LibraryMetadataDocument.Decode(await File.ReadAllBytesAsync(destination));
                Assert.Equal("Fixture", Assert.Single(decoded.Entries).FolderKey);
                var after = Snapshot(f.Root); after.Remove("approved-metadata.json"); Assert.Equal(before, after);
            }
            else { Assert.False(dialog.Model.Saved); Assert.False(File.Exists(destination)); Assert.Equal(before, Snapshot(f.Root)); }
            dialog.Close(); await Task.WhenAll(openings); Assert.Null(flow.Dialog);
            Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally { release.TrySetResult(); flow.Dialog?.Close(); await f.Session.DisposeAsync(); await Task.WhenAll(openings); }
    }

    [Theory]
    [InlineData("same-repository-filter")][InlineData("conflicting-catalog")][InlineData("missing-property")]
    public void Ambiguous_identity_or_incomplete_schema_cannot_be_presented_as_valid_metadata(string kind)
    {
        var first = new LibraryMetadataEntry("github", "owner/repo", "First", "First", null, "shared-id", "v1", null, "first*");
        if (kind == "missing-property")
        {
            var json = System.Text.Encoding.UTF8.GetString(LibraryMetadataDocument.Create([first], DateTimeOffset.UnixEpoch).Encode());
            var changed = json.Replace("\"project\": null,", ""); Assert.NotEqual(json, changed);
            Assert.ThrowsAny<Exception>(() => LibraryMetadataDocument.Decode(System.Text.Encoding.UTF8.GetBytes(changed)));
        }
        else
        {
            var second = first with { FolderKey = "Second", Name = "Second" };
            if (kind == "conflicting-catalog") second = second with { Repository = "other/repo", AssetFilter = "other*" };
            Assert.Throws<InvalidDataException>(() => LibraryMetadataDocument.Create([first, second], DateTimeOffset.UnixEpoch));
        }
    }

    [AvaloniaFact]
    public async Task Invalid_loaded_identity_is_shown_as_a_blocked_preview_without_picker_or_write()
    {
        await using var f = await ExportScope.Create();
        f.Manager.LibraryApps.Single().FolderName = "/synthetic-private/unsupported";
        var before = Snapshot(f.Root); var choices = 0;
        using var model = new LibraryMetadataExportViewModel(f.Manager, f.Settings, f.Session,
            () => { choices++; throw new InvalidOperationException("Blocked preview must not open a picker."); });
        Assert.False(model.CanSave); Assert.Contains("Export unavailable", model.Message);
        await model.SaveAsync(); Assert.Equal(0, choices); Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.Transport.Requests);
    }

    [Theory]
    [InlineData("//synthetic-network/share/export.json")]
    [InlineData(@"\\synthetic-network\share\export.json")]
    public void Network_destination_spelling_is_refused_before_filesystem_observation(string destination)
    {
        var error = Assert.Throws<IOException>(() => LibraryMetadataExportWriter.ValidateDestination(destination, []));
        Assert.Contains("network or device path", error.Message);
    }

    internal sealed class ExportScope : IAsyncDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "relicade-export-scope-" + Guid.NewGuid().ToString("N"));
        readonly string? oldRoot = RelicadePaths.OverrideUserDataRoot;
        readonly ISettingsStore oldStore = SettingsStoreProvider.Default;
        internal ExportStore Store = null!;
        internal DenyTransport Transport = null!;
        internal GameManager Manager = null!;
        internal SettingsViewModel Settings = null!;
        internal LauncherSession Session { get; } = new();
        HttpClient? http;
        internal static async Task<ExportScope> Create()
        {
            var f = new ExportScope();
            try
            {
                var data = Path.Combine(f.Root, "data"); Directory.CreateDirectory(data); RelicadePaths.OverrideUserDataRoot = data;
                f.Store = new(data); SettingsStoreProvider.Default = f.Store;
                f.Transport = new(); f.http = new(f.Transport);
                var catalog = new AppCatalogService(dataDirectory: data);
                await File.WriteAllTextAsync(Path.Combine(data, "apps.json"), "{\"apps\":[{\"name\":\"Base fixture\",\"folderName\":\"Fixture\"}]}");
                f.Manager = new GameManager(f.Store, f.http, catalog);
                await f.Manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
                f.Settings = new(f.Store); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            try { await Session.DisposeAsync(); }
            finally
            {
                try { Manager?.Dispose(); http?.Dispose(); Transport?.Dispose(); }
                finally { SettingsStoreProvider.Default = oldStore; RelicadePaths.OverrideUserDataRoot = oldRoot; }
            }
        }
    }
    internal sealed class ExportStore(string data) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(data, "Apps"), Platform = TargetOS.LinuxX64, FirstStartup = false, LocalFirstCatalogMigrationComplete = true, ListScope = AppListScope.InstalledOnly };
        internal int Saves;
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { Saves++; throw new InvalidOperationException("Export must not save settings."); }
    }
    internal sealed class DenyTransport : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; throw new InvalidOperationException("Export must not request the network."); }
    }

    private static SortedDictionary<string, string> Snapshot(string root) => new(
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(root, path),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))), StringComparer.Ordinal);
}
