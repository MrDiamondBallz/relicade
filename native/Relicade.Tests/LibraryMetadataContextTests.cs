using Avalonia.Headless.XUnit;
using Relicade.Services;
namespace Relicade.Tests;
public sealed class LibraryMetadataContextTests
{
    [AvaloniaFact]
    public async Task Root_changed_before_first_preview_cannot_relabel_loaded_entries()
    {
        await using var f = await LibraryMetadataExportTests.ExportScope.Create();
        var original = RelicadePaths.OverrideUserDataRoot;
        var changed = Path.Combine(f.Root, "new-context-not-created");
        try
        {
            RelicadePaths.OverrideUserDataRoot = changed;
            Assert.Throws<InvalidOperationException>(() => f.Manager.CaptureLibraryMetadata());
            Assert.False(Directory.Exists(changed));
            Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally { RelicadePaths.OverrideUserDataRoot = original; }
    }
    [Fact]
    public void Filesystem_root_protection_refuses_private_descendant_destination()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-review-export-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "never-written.json");
        Assert.Throws<IOException>(() => LibraryMetadataExportWriter.ValidateDestination(destination, [Path.GetPathRoot(root)!]));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }
    [AvaloniaFact]
    public async Task Configured_apps_root_changed_before_preview_cannot_relabel_loaded_entries()
    {
        await using var f = await LibraryMetadataExportTests.ExportScope.Create();
        var original = f.Store.Current.AppsPath;
        var changed = Path.Combine(f.Root, "new-apps-not-created");
        try
        {
            f.Store.Current.AppsPath = changed;
            Assert.Throws<InvalidOperationException>(() => f.Manager.CaptureLibraryMetadata());
            Assert.False(Directory.Exists(changed));
            Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally { f.Store.Current.AppsPath = original; }
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Context_changed_while_loading_requires_a_fresh_successful_load(bool settings)
    {
        await using var f = await LibraryMetadataExportTests.ExportScope.Create();
        var originalRoot = RelicadePaths.OverrideUserDataRoot; var originalApps = f.Store.Current.AppsPath;
        var changed = Path.Combine(f.Root, "changed-during-load-not-created");
        long? generation = null;
        try
        {
            generation = f.Manager.BeginMetadataLoad();
            if (settings) f.Store.Current.AppsPath = changed; else RelicadePaths.OverrideUserDataRoot = changed;
            f.Manager.CompleteMetadataLoad(generation.Value, true); generation = null;
            f.Store.Current.AppsPath = originalApps; RelicadePaths.OverrideUserDataRoot = originalRoot;
            Assert.Throws<InvalidOperationException>(() => f.Manager.CaptureLibraryMetadata());
            // Existing explicit offline load, never an export-triggered repair or reload.
            await f.Manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            Assert.Single(f.Manager.CaptureLibraryMetadata().Document.Entries);
            Assert.False(Directory.Exists(changed));
            Assert.Equal(0, f.Transport.Requests); Assert.Equal(0, f.Store.Saves);
        }
        finally
        {
            f.Store.Current.AppsPath = originalApps; RelicadePaths.OverrideUserDataRoot = originalRoot;
            if (generation != null) f.Manager.CompleteMetadataLoad(generation.Value, false);
        }
    }
    [Theory]
    [InlineData(@"c:\Games\export.json", @"C:\", true)]
    [InlineData(@"C:\GamesOther\export.json", @"C:\Games", false)]
    [InlineData(@"D:\export.json", @"C:\", false)]
    public void Windows_drive_root_containment_is_lexical_without_filesystem_access(string destination, string root, bool expected)
    {
        Assert.Equal(expected, LibraryMetadataExportWriter.IsWithinProtectedRoot(destination, root, '\\', StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Linked_protected_root_cannot_hide_its_real_descendant_destination(bool ancestor)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-export-alias-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "authored-game"); Directory.CreateDirectory(target);
        var alias = Path.Combine(root, "authored-alias"); Directory.CreateSymbolicLink(alias, target);
        var protectedRoot = alias;
        if (ancestor)
        {
            Directory.CreateDirectory(Path.Combine(target, "child"));
            target = Path.Combine(target, "child"); protectedRoot = Path.Combine(alias, "child");
        }
        var destination = Path.Combine(target, "never-written.json");
        Assert.Throws<InvalidDataException>(() => LibraryMetadataExportWriter.ValidateDestination(destination, [protectedRoot]));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(root));
        Assert.Empty(Directory.EnumerateFiles(target));
        Assert.Equal(Path.Combine(root, "authored-game"), new DirectoryInfo(alias).LinkTarget);
    }
    [Fact]
    public async Task Protected_alias_created_after_staging_refuses_commit_and_preserves_authored_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-export-late-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "game"); Directory.CreateDirectory(target);
        var protectedRoot = Path.Combine(root, "initially-missing-game-alias");
        var sentinel = Path.Combine(target, "retained.keep"); await File.WriteAllTextAsync(sentinel, "authored retained bytes");
        var destination = Path.Combine(target, "never-written.json"); var committed = false;
        var bytes = LibraryMetadataDocument.Create([], DateTimeOffset.UnixEpoch).Encode();
        await Assert.ThrowsAsync<InvalidDataException>(() => LibraryMetadataExportWriter.WriteNewAsync(bytes, destination,
            [protectedRoot], CancellationToken.None, action => { action(); committed = true; },
            () => { Directory.CreateSymbolicLink(protectedRoot, target); return Task.CompletedTask; }));
        Assert.False(committed); Assert.False(File.Exists(destination));
        Assert.Equal("authored retained bytes", await File.ReadAllTextAsync(sentinel));
        Assert.Equal(new[] { sentinel }, Directory.EnumerateFiles(target).ToArray());
        Assert.Empty(Directory.EnumerateFiles(root));
        Assert.Equal(target, new DirectoryInfo(protectedRoot).LinkTarget);
    }

}
