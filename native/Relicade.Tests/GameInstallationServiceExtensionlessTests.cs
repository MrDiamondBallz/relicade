using FluentAssertions;
using Relicade.Core.Services;

namespace Relicade.Tests;

public class GameInstallationServiceExtensionlessTests
{
    [Theory]
    [InlineData("CrashBandicoot_Linux", true)]
    [InlineData("P.T.PC.Port.Setup-linux", true)]
    [InlineData("Game.x86_64", true)]
    [InlineData("Game.arm64", true)]
    [InlineData("CrashBandicoot_win.exe", true)]
    [InlineData("MyApp.AppImage", true)]
    [InlineData("payload.zip", false)]
    [InlineData("game.tar.gz", false)]
    [InlineData("notes.txt", false)]
    [InlineData("latest.json", false)]
    [InlineData("setup.msi", false)]
    public void IsSingleFileExecutableAsset_classifies_known_shapes(string assetName, bool expected)
    {
        GameInstallationService.IsSingleFileExecutableAsset(assetName).Should().Be(expected);
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_moves_extensionless_linux_binary()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string assetName = "CrashBandicoot_Linux";
        var payload = PortableInstallationFixtures.NativePayload("synthetic extensionless payload");

        try
        {
            await File.WriteAllBytesAsync(downloadPath, payload);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                assetName,
                "1.6.1");

            var installedPath = Path.Combine(gamePath, assetName);
            File.Exists(installedPath).Should().BeTrue();
            File.Exists(downloadPath).Should().BeTrue("source bytes remain until the caller cleans staging after a durable commit");
            (await File.ReadAllBytesAsync(installedPath)).Should().Equal(payload);
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("1.6.1");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_moves_linux_binary_with_dots_in_its_name()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string assetName = "P.T.PC.Port.Setup-linux";

        try
        {
            await File.WriteAllBytesAsync(downloadPath, PortableInstallationFixtures.NativePayload("synthetic dotted payload"));

            await GameInstallationService.InstallOrUpdateGameAsync(downloadPath, gamePath, assetName, "v1.0.1");

            File.Exists(Path.Combine(gamePath, assetName)).Should().BeTrue();
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v1.0.1");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_rejects_unsupported_asset_extension()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllBytesAsync(downloadPath, [1, 2, 3, 4]);

            var act = async () => await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "payload.txt",
                "v1.0.0");

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Unsupported release asset type*");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }
}
