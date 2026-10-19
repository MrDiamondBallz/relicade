using System.IO.Compression;
using Relicade.Services.Mods;
using static Relicade.Tests.ModsPreservationTests;

namespace Relicade.Tests;

public sealed class ModsPreservationEdgeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "mods-edges-" + Guid.NewGuid().ToString("N"));
    public ModsPreservationEdgeTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Sidecar_corruption_has_a_typed_recoverable_error_with_parse_cause()
    {
        var path = InstalledModsStore.GetSidecarPath(root); File.WriteAllText(path, "{broken");
        var ex = Assert.Throws<InvalidDataException>(() => new InstalledModsStore().Load(root));
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Fact]
    public void Different_volume_destination_is_rejected_before_payload_activation()
    {
        if (!OperatingSystem.IsLinux()) return;
        // The test runner's synthetic shared-memory mount is not a host device or user-data mount.
        Assert.True(Directory.Exists("/dev/shm"));
        var other = Path.Combine("/dev/shm", "mods-volume-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "untouched.txt"), "old");
            Assert.Throws<IOException>(() => ModInstallTransaction.EnsureSameVolume(root, other));
            Assert.Equal("old", File.ReadAllText(Path.Combine(other, "untouched.txt")));
            Assert.Empty(Directory.GetDirectories(other));
        }
        finally { if (Directory.Exists(other)) Directory.Delete(other, true); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Mod_view_distinguishes_error_retry_from_genuine_no_matches()
    {
        var view = new Relicade.Views.ModsView();
        view.Model.HasLoadError = true; view.UpdateModsListPlaceholder();
        Assert.Equal("Retry", Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.Button>(view, "ModsRefreshButton")!.Content);
        Assert.StartsWith("Mods unavailable", Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.TextBlock>(view, "ModsEmptyText")!.Text);
        view.Model.HasLoadError = false; view.UpdateModsListPlaceholder();
        Assert.Equal("No mods found.", Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.TextBlock>(view, "ModsEmptyText")!.Text);
    }

    [Fact]
    public async Task Decompression_failure_after_archive_open_preserves_prior_generation()
    {
        var package = Package(); var provider = new Provider(); var service = new ModInstallService(new([provider]));
        await service.InstallAsync(root, "mods", package, provider, package.DownloadFiles[0]);
        var sidecar = File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root));
        var archive = Zip("payload.txt", new string('a', 10000));
        // Corrupt deflate bytes, leaving the central directory readable.
        var nameLength = BitConverter.ToUInt16(archive, 26); var extraLength = BitConverter.ToUInt16(archive, 28);
        archive[30 + nameLength + extraLength] = 0xFF;
        using (var opened = SharpCompress.Archives.ArchiveFactory.OpenArchive(new MemoryStream(archive))) Assert.Single(opened.Entries);
        provider.Bytes = archive;
        await Assert.ThrowsAnyAsync<Exception>(() => service.InstallAsync(root, "mods", package, provider, package.DownloadFiles[0]));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "mods", "payload.txt")));
        Assert.Equal(sidecar, File.ReadAllBytes(InstalledModsStore.GetSidecarPath(root)));
    }
    [Fact]
    public async Task Retry_retains_options_filter_and_structured_source_error()
    {
        var provider = new Provider { Failure = "list" }; var loader = new ModCatalogLoader(new([provider]));
        var options = new ModListOptions { SortMode = ModListSorter.Newest, IncludeNsfw = true };
        var session = await loader.LoadSearchSessionAsync(Sources(), "fixture|source", " needle ", options: options);
        var error = Assert.Single(session.SourceStates).Error;
        Assert.NotNull(error); Assert.Equal("fixture", error.Provider); Assert.Equal("source", error.SourceKey);
        Assert.Null(error.AttemptedCursor); Assert.Contains("IOException", error.Reason); Assert.True(error.Retryable);
        provider.Failure = null;
        var retry = await loader.LoadMoreSearchSessionAsync(session, options: new() { SortMode = ModListSorter.TopRated });
        Assert.Equal("fixture|source", retry.SourceFilterKey); Assert.Equal("needle", retry.SearchQuery);
        Assert.Same(options, retry.Options); Assert.Same(options, provider.LastOptions);
        Assert.Null(Assert.Single(retry.SourceStates).Error);
    }
    [Fact]
    public void Stable_package_identity_keeps_distinct_packages_with_the_same_display_name()
    {
        ModPackage Make(string id, string source) => new() { ProviderId = "fixture", SourceKey = source, Id = id, Name = "same", Owner = "same", FullName = "same" };
        var rows = new List<ModPackage> { Make("1", "one") };
        ModCatalogListBuilder.AppendUniquePackages(rows, [Make("1", "one"), Make("2", "one"), Make("1", "two")]);
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, ModCatalogListBuilder.BuildItems(rows, new(), out _).Count);
    }
    [Fact]
    public async Task Invalid_configured_provider_is_visible_without_discarding_usable_provider_rows()
    {
        var provider = new Provider(); var loader = new ModCatalogLoader(new([provider]));
        var sources = Sources().Concat([new GameModSource { Provider = "unavailable", SourceUrl = "source" }]).ToArray();
        var result = await loader.LoadBrowseSessionAsync(sources, null);
        Assert.Single(result.Packages); Assert.True(result.HasErrors);
        var state = Assert.Single(result.SourceStates.Where(s => s.Error != null)); Assert.False(state.IsDone); Assert.False(state.Error!.Retryable);
        await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadAllPackagesAsync(sources));
    }
    [Theory]
    [InlineData("other-source", "package", "different")]
    [InlineData("source", "other-package", "different")]
    [InlineData("source", "package", "Different")]
    public void Explicit_file_lookup_requires_the_full_original_scope(string source, string id, string file)
    {
        var package = Package(); var record = Record(package); record.SourceKey = source; record.Id = id;
        record.DownloadFileId = file; record.DownloadFileName = "same.zip";
        Assert.Null(ModDownloadFileSelection.FindCatalogFile(package, record)); Assert.False(ModDownloadFileSelection.IsRecordUpdateAvailable(record, package));
        Assert.Equal(file, Assert.Single(ModDownloadFileSelection.ResolveInstalledFilesForPicker(package, [record])).Id);
    }
    [Fact]
    public void Legacy_filename_match_is_exact_and_distinct_from_no_identity_resolution()
    {
        var package = Package(); var record = Record(package); record.DownloadFileName = "same.zip";
        Assert.Equal("different", ModDownloadFileSelection.FindCatalogFile(package, record)!.Id);
        record.DownloadFileName = "Same.zip"; Assert.Null(ModDownloadFileSelection.FindCatalogFile(package, record));
        record.DownloadFileName = null; Assert.Equal("different", ModDownloadFileSelection.FindCatalogFile(package, record)!.Id);
    }
    [Fact]
    public async Task Parallel_atomic_sidecar_saves_always_reopen_as_complete_documents()
    {
        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            var store = new InstalledModsStore(); var record = Record(Package()); record.Version = i.ToString();
            store.Save(root, new() { Mods = [record] }); Assert.Single(store.Load(root).Mods);
        }));
        await Task.WhenAll(tasks); Assert.Single(new InstalledModsStore().Load(root).Mods);
    }
}
