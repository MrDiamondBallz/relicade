using System.IO.Compression;
using FluentAssertions;
using Relicade.Core.Services;

namespace Relicade.Tests;

public class GameInstallationServiceAppImageTests
{
    [Fact]
    public async Task Unmanaged_folder_blocks_deletes_other_top_level_appimages()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var oldAppImage = Path.Combine(gamePath, "Game-1.0.AppImage");
        var modsFile = Path.Combine(gamePath, "mods", "keep.nrm");
        var portablePath = Path.Combine(gamePath, "portable.txt");
        var newPayload = new byte[] { 9, 8, 7, 6 };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modsFile)!);
            await File.WriteAllBytesAsync(oldAppImage, [1, 2, 3]);
            await File.WriteAllTextAsync(portablePath, "portable");
            await File.WriteAllBytesAsync(modsFile, [4, 5]);
            await File.WriteAllBytesAsync(downloadPath, newPayload);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0"));

            var installedPath = Path.Combine(gamePath, "Game-2.0.AppImage");
            File.Exists(installedPath).Should().BeFalse();
            (await File.ReadAllBytesAsync(oldAppImage)).Should().Equal([1, 2, 3]);
            File.Exists(oldAppImage).Should().BeTrue();
            (await File.ReadAllTextAsync(portablePath)).Should().Be("portable");
            File.Exists(modsFile).Should().BeTrue();
            File.Exists(Path.Combine(gamePath, "version.txt")).Should().BeFalse();
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_overwrites_same_named_appimage_without_removing_other_files()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string assetName = "stable.AppImage";
        var installedPath = Path.Combine(gamePath, assetName);
        var portablePath = Path.Combine(gamePath, "portable.txt");
        var newPayload = new byte[] { 11, 12, 13 };

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(installedPath, [1, 1, 1]);
            await File.WriteAllTextAsync(portablePath, "keep");
            await File.WriteAllBytesAsync(downloadPath, newPayload);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                assetName,
                "v3.0.0"));

            File.Exists(installedPath).Should().BeTrue();
            Directory.GetFiles(gamePath, "*.AppImage", SearchOption.TopDirectoryOnly)
                .Should().ContainSingle(path => Path.GetFileName(path) == assetName);
            (await File.ReadAllBytesAsync(installedPath)).Should().Equal([1, 1, 1]);
            (await File.ReadAllTextAsync(portablePath)).Should().Be("keep");
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_does_not_delete_nested_appimages()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var nestedAppImage = Path.Combine(gamePath, "subdir", "old.AppImage");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(nestedAppImage)!);
            await File.WriteAllBytesAsync(nestedAppImage, [2, 2, 2]);
            await File.WriteAllBytesAsync(downloadPath, [3, 3, 3]);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0"));

            File.Exists(nestedAppImage).Should().BeTrue();
            File.Exists(Path.Combine(gamePath, "Game-2.0.AppImage")).Should().BeFalse();
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_retargets_stale_selected_executable()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var oldAppImage = Path.Combine(gamePath, "Game-1.0.AppImage");
        var selectedPath = Path.Combine(gamePath, "selected_executable.txt");

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(oldAppImage, [1]);
            await File.WriteAllTextAsync(selectedPath, oldAppImage);
            await File.WriteAllBytesAsync(downloadPath, [2]);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0"));

            File.Exists(oldAppImage).Should().BeTrue();
            var saved = (await File.ReadAllTextAsync(selectedPath)).Trim();
            saved.Should().Be(oldAppImage);
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_keeps_other_appimages_in_a_shared_folder()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(gamePath, "Relicade-x86_64.AppImage");
        var otherApp = Path.Combine(gamePath, "OtherRecomp.AppImage");
        var playersCopy = Path.Combine(gamePath, "Zelda64Recompiled-1.0.AppImage");
        var selectedPath = Path.Combine(gamePath, "selected_executable.txt");

        try
        {
            Directory.CreateDirectory(gamePath);
            foreach (var file in new[] { launcher, otherApp, playersCopy })
                await File.WriteAllBytesAsync(file, [1]);
            await File.WriteAllTextAsync(selectedPath, playersCopy);
            await File.WriteAllBytesAsync(downloadPath, [2]);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Zelda64Recompiled-1.1.AppImage",
                "v1.1.0",
                new GameInstallationOptions { IsSharedFolder = true }));

            var installedPath = Path.Combine(gamePath, "Zelda64Recompiled-1.1.AppImage");
            File.Exists(installedPath).Should().BeFalse();
            File.Exists(launcher).Should().BeTrue();
            File.Exists(otherApp).Should().BeTrue();
            File.Exists(playersCopy).Should().BeTrue("Relicade didn't install it, so it isn't Relicade's to delete");
            (await File.ReadAllTextAsync(selectedPath)).Trim().Should().Be(playersCopy);
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_replaces_only_the_previously_installed_appimage_in_a_shared_folder()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(gamePath, "Relicade-x86_64.AppImage");
        var previous = Path.Combine(gamePath, "Game-1.0.AppImage");

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(launcher, [1]);
            await File.WriteAllBytesAsync(previous, [1]);
            await File.WriteAllBytesAsync(downloadPath, [2]);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0",
                new GameInstallationOptions { IsSharedFolder = true, PreviousInstallFileName = "Game-1.0.AppImage" }));

            File.Exists(previous).Should().BeTrue();
            File.Exists(launcher).Should().BeTrue();
            var installedPath = Path.GetFullPath(Path.Combine(gamePath, "Game-2.0.AppImage"));
            File.Exists(installedPath).Should().BeFalse();
            File.Exists(Path.Combine(gamePath, "selected_executable.txt")).Should().BeFalse();
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_keeps_a_selected_wrapper_script()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.AppImage");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var wrapper = Path.Combine(gamePath, "run-game.sh");
        var selectedPath = Path.Combine(gamePath, "selected_executable.txt");

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllTextAsync(wrapper, "#!/bin/sh\n");
            await File.WriteAllTextAsync(selectedPath, wrapper);
            await File.WriteAllBytesAsync(downloadPath, [2]);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "Game-2.0.AppImage",
                "v2.0.0"));

            (await File.ReadAllTextAsync(selectedPath)).Trim().Should().Be(wrapper);
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_extensionless_binary_does_not_delete_sibling_appimage()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var siblingAppImage = Path.Combine(gamePath, "Game.AppImage");

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(siblingAppImage, [1, 2, 3]);
            await File.WriteAllBytesAsync(downloadPath, [4, 5, 6]);

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "CrashBandicoot_Linux",
                "1.6.1"));

            File.Exists(siblingAppImage).Should().BeTrue();
            File.Exists(Path.Combine(gamePath, "CrashBandicoot_Linux")).Should().BeFalse();
        }
        finally
        {
            Cleanup(downloadPath, gamePath);
        }
    }

    [Fact]
    public async Task Unmanaged_folder_blocks_zip_does_not_delete_sibling_appimage()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var siblingAppImage = Path.Combine(gamePath, "Game.AppImage");
        var payload = "zip-binary"u8.ToArray();

        try
        {
            Directory.CreateDirectory(gamePath);
            await File.WriteAllBytesAsync(siblingAppImage, [1, 2, 3]);
            await File.WriteAllBytesAsync(archivePath, CreateZip([("game.exe", payload)]));

            await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(
                archivePath,
                gamePath,
                "game.zip",
                "v1.0.0"));

            File.Exists(siblingAppImage).Should().BeTrue();
            File.Exists(Path.Combine(gamePath, "game.exe")).Should().BeFalse();
        }
        finally
        {
            Cleanup(archivePath, gamePath);
        }
    }

    [Theory]
    [InlineData("MyApp.AppImage", true)]
    [InlineData("MyApp.appimage", true)]
    [InlineData("game.exe", false)]
    [InlineData("payload.zip", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAppImageAsset_classifies_names(string? assetName, bool expected)
    {
        GameInstallationService.IsAppImageAsset(assetName).Should().Be(expected);
    }

    static byte[] CreateZip(IReadOnlyList<(string Path, byte[] Content)> entries)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return zipStream.ToArray();
    }

    static void Cleanup(string downloadPath, string gamePath)
    {
        if (File.Exists(downloadPath))
            File.Delete(downloadPath);
        if (Directory.Exists(gamePath))
            Directory.Delete(gamePath, true);
    }
}
