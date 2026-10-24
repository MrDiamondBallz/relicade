using System.Runtime.InteropServices;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class WindowsFreedoomSetupTests
{
    [Fact]
    public void Guided_UZDoom_confirmation_on_Windows_pins_the_approved_ZIP_and_distinct_executable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "The Windows guided-setup contract must run on Windows x64; a skip is not runtime acceptance.");
        var game = new GameInfo { Name = "UZDoom", Repository = "UZDoom/UZDoom", RepositorySource = "github", FolderName = "uzdoom" };
        var project = GuidedEngineInstallService.ProjectFor(game);
        Assert.Equal("5.0.3", project.Requirement.ReleaseIdentity);
        Assert.Equal("Windows-UZDoom-Release-x86_64.zip|sha256:3a41131845415bc4bb5474f8de301a77736bd858e2ab489926325a2675e8263f", project.Requirement.AssetIdentity);
        Assert.Equal("e6df57d1027cee089d33388ab743817a236148992ac3881d9141ea2b547677d9", project.Requirement.ExecutableSha256);
        Assert.Equal(29_337_585, project.MaximumBytes);
        Assert.Contains("Windows x64", project.Confirmation);
        Assert.DoesNotContain("Linux x64", project.Confirmation);
        game.Status = GameStatus.Installed;
        Assert.Equal("Manage Freedoom data…", game.FreedoomActionLabel);
    }

    [Fact]
    public void Existing_Linux_Freedoom_definition_retains_its_reviewed_AppImage_contract()
    {
        var definition = OwnedGameSetupService.FreedoomPhase2;
        Assert.Equal("uzdoom-5-0-3-freedoom-0-13-phase2-linux-x64", definition.Id);
        Assert.Equal("Linux-UZDoom-Release-x86_64.AppImage|sha256:9c5166febc09cbf80c24612a78829037c953f38c4d986e36f8475ddd507e0228", definition.AssetIdentity);
        Assert.Equal("9c5166febc09cbf80c24612a78829037c953f38c4d986e36f8475ddd507e0228", definition.ExecutableSha256);
        Assert.Equal("freedoom2.wad", definition.FileName);
        Assert.Equal("a8772e088847032510d97ba2312406a6998f21cbab44d4ff10696faa9c0ecd4b", definition.ExpectedSha256);
    }
}
