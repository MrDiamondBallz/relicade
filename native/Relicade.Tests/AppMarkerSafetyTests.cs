using Relicade.Services;

namespace Relicade.Tests;

public sealed class AppMarkerSafetyTests
{
    static string Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-marker-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root; // Retain synthetic bytes for failure inspection.
    }

    [Theory]
    [InlineData("")]
    [InlineData("synthetic user configuration")]
    public void Removed_legacy_marker_preserves_existing_bytes(string contents)
    {
        var root = Root();
        var file = Path.Combine(root, "portable.txt");
        File.WriteAllText(file, contents);
        AppFilesToAddService.Sync(root, ["portable.txt"], []);
        Assert.True(File.Exists(file));
        Assert.Equal(contents, File.ReadAllText(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Marker_preparation_never_follows_symbolic_link(bool linkedParent)
    {
        var root = Root();
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var install = Path.Combine(root, "install");
        if (linkedParent) Directory.CreateSymbolicLink(install, outside);
        else
        {
            Directory.CreateDirectory(install);
            File.CreateSymbolicLink(Path.Combine(install, "portable.txt"), Path.Combine(outside, "portable.txt"));
        }
        AppFilesToAddService.Sync(install, null, ["portable.txt"]);
        Assert.False(File.Exists(Path.Combine(outside, "portable.txt")));
    }
    [Fact]
    public void Strict_removed_marker_reports_preservation_before_creating_next_marker()
    {
        var root = Root();
        var file = Path.Combine(root, "portable.txt");
        File.WriteAllText(file, "synthetic user configuration");
        var error = Assert.Throws<IOException>(() =>
            AppFilesToAddService.Sync(root, ["portable.txt"], ["new.txt"], throwOnFailure: true));
        Assert.Contains("preserved", error.Message);
        Assert.Contains("portable.txt", error.Message);
        Assert.Equal("synthetic user configuration", File.ReadAllText(file));
        Assert.False(File.Exists(Path.Combine(root, "new.txt")));
    }

    [Fact]
    public void Strict_preparation_creates_missing_marker_and_preserves_existing_content()
    {
        var root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "synthetic settings");
        var preserved = AppFilesToAddService.Sync(root, null, ["portable.txt", "config.ini"], throwOnFailure: true);
        Assert.Empty(preserved);
        Assert.Equal("", File.ReadAllText(Path.Combine(root, "portable.txt")));
        Assert.Equal("synthetic settings", File.ReadAllText(Path.Combine(root, "config.ini")));
        AppFilesToAddService.Sync(root, null, ["portable.txt", "config.ini"], throwOnFailure: true);
        Assert.Equal("synthetic settings", File.ReadAllText(Path.Combine(root, "config.ini")));
    }

    [Theory]
    [InlineData(".relicade-install.json")]
    [InlineData("LastPlayed.txt")]
    [InlineData("windows-installer.json")]
    [InlineData("file:stream")]
    [InlineData("NUL.txt")]
    public void Marker_names_cannot_impersonate_launcher_records_or_platform_paths(string name)
    {
        Assert.False(AppFilesToAddService.TryNormalizeFileName(name, out _));
    }

    [Fact]
    public void Strict_preparation_rejects_linked_marker_without_touching_target()
    {
        var root = Root();
        var target = Path.Combine(root, "synthetic-user-data");
        File.WriteAllText(target, "keep these bytes");
        File.CreateSymbolicLink(Path.Combine(root, "portable.txt"), target);
        Assert.Throws<InvalidDataException>(() =>
            AppFilesToAddService.Sync(root, null, ["portable.txt"], throwOnFailure: true));
        Assert.Equal("keep these bytes", File.ReadAllText(target));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Manual_preparation_rejects_linked_root_before_creating_instructions(bool missingChild)
    {
        var root = Root();
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var install = Path.Combine(root, "install");
        Directory.CreateSymbolicLink(install, outside);
        var game = new Relicade.Models.GameInfo { Name = "Synthetic manual entry", InstallPath = missingChild ? Path.Combine(install, "missing-child") : install };
        Assert.Throws<InvalidDataException>(() =>
            ManualAppFolderService.EnsurePrepared(game, root, throwOnFailure: true));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }
    [Fact]
    public void Manual_preparation_preserves_existing_instruction_content()
    {
        var root = Root();
        var path = Path.Combine(root, ManualAppFolderService.InstructionFileName);
        File.WriteAllText(path, "synthetic personal notes");
        var game = new Relicade.Models.GameInfo { Name = "Synthetic manual entry", InstallPath = root };
        Assert.True(ManualAppFolderService.EnsurePrepared(game, root, throwOnFailure: true));
        Assert.Equal("synthetic personal notes", File.ReadAllText(path));
    }
    [Fact]
    public async Task Manual_preparation_does_not_create_markers_before_failed_install_validation()
    {
        using var f = new ManagedRemovalFixture();
        await f.Install();
        var manifest = Path.Combine(f.Live, Relicade.Core.Services.PortableInstallationTransaction.ManifestFileName);
        File.WriteAllText(manifest, "synthetic damaged manifest");
        var game = new Relicade.Models.GameInfo { Name = "Synthetic manual entry", InstallPath = f.Live, FilesToAdd = ["new-marker.txt"] };
        var error = Assert.Throws<InvalidDataException>(() => ManualAppFolderService.EnsurePrepared(game, f.Root, throwOnFailure: true));
        Assert.Contains("Malformed portable recovery metadata", error.Message);
        Assert.False(File.Exists(Path.Combine(f.Live, "new-marker.txt")));
        Assert.Equal("synthetic damaged manifest", File.ReadAllText(manifest));
        Assert.Equal("current save old", File.ReadAllText(Path.Combine(f.Live, "saves", "slot")));
    }
}
