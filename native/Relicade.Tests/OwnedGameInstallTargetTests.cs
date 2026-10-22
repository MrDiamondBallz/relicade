using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class OwnedGameInstallTargetTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "owned-install-target-" + Guid.NewGuid().ToString("N"));
    readonly byte[] payload = PortableInstallationFixtures.NativePayload("owned install selection fixture");
    string Live => Path.Combine(root, "Game");
    OwnedGameSetupService.InstallTarget Target => new("us.x86_64", payload.Length, Convert.ToHexStringLower(SHA256.HashData(payload)));
    Task Stage(string path)
    {
        File.WriteAllBytes(Path.Combine(path, "us.x86_64"), payload);
        File.WriteAllBytes(Path.Combine(path, "eu.x86_64"), payload);
        return Task.CompletedTask;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData("canonical")][InlineData("former-name")][InlineData("other-project")]
    [InlineData("other-provider")][InlineData("other-release")][InlineData("other-digest")][InlineData("other-asset")]
    public void Only_the_exact_compiled_package_gets_a_first_install_target(string variant)
    {
        var rule = OwnedGameSetupService.PerfectDark;
        var game = new GameInfo { Repository = variant == "former-name" ? "fgsfdsfgs/perfect_dark" : "perfect-dark-pc-port/perfect_dark" };
        if (variant == "other-project") game.Repository = "other/perfect_dark";
        if (variant == "other-provider") game.RepositorySource = "gitlab";
        var release = variant == "other-release" ? "another-tag" : rule.ReleaseIdentity;
        var asset = variant == "other-digest" ? "pd-x86_64-linux.tar.gz|sha256:" + new string('0', 64) :
            variant == "other-asset" ? "other.tar.gz|sha256:" + rule.AssetIdentity.Split(':').Last() : rule.AssetIdentity;
        var target = OwnedGameSetupService.PinnedInstallTarget(game, release, asset);
        var accepted = OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64 && variant is "canonical" or "former-name";
        Assert.Equal(accepted, target != null);
        if (target != null)
        {
            Assert.Equal("pd.x86_64", target.RelativePath); Assert.Equal(15329872, target.Length);
            Assert.Equal(rule.ExecutableSha256, target.Sha256);
        }
    }

    [Theory]
    [InlineData("first")][InlineData("explicit")][InlineData("update")]
    public async Task First_install_default_never_replaces_an_explicit_or_existing_selection(string scenario)
    {
        var options = new GameInstallationOptions
        {
            InitialExecutableRelativePath = Target.RelativePath,
            SelectedExecutableRelativePath = scenario == "explicit" ? "eu.x86_64" : null,
            PrepareCandidate = Target.Validate
        };
        var tx = new PortableInstallationTransaction(Live, options);
        await tx.InstallAsync(Stage, "v1");
        Assert.Equal(scenario == "explicit" ? "eu.x86_64" : "us.x86_64", tx.ReadCommitted()!.SelectedExecutable);
        if (scenario == "update")
        {
            tx.SelectExecutable("eu.x86_64");
            await tx.InstallAsync(Stage, "v2");
            Assert.Equal("eu.x86_64", tx.ReadCommitted()!.SelectedExecutable);
            Assert.Equal("v2", tx.ReadCommitted()!.SelectedVersion);
        }
    }

    [Theory]
    [InlineData(null)][InlineData("missing.x86_64")][InlineData("../outside.x86_64")]
    public async Task Unknown_or_invalid_defaults_do_not_bypass_ambiguous_target_refusal(string? initial)
    {
        var tx = new PortableInstallationTransaction(Live, new() { InitialExecutableRelativePath = initial });
        await Assert.ThrowsAsync<InvalidDataException>(() => tx.InstallAsync(Stage, "v1"));
        Assert.False(File.Exists(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(new[] { ".relicade-mod-writer.lock" }, Directory.GetFileSystemEntries(Live).Select(Path.GetFileName));
            Assert.Empty(File.ReadAllBytes(Path.Combine(Live, ".relicade-mod-writer.lock")));
    }

    [Theory]
    [InlineData("valid")][InlineData("length")][InlineData("hash")][InlineData("link")][InlineData("directory")][InlineData("missing")]
    public async Task Staged_target_is_validated_before_any_activation(string fault)
    {
        var tx = new PortableInstallationTransaction(Live, new()
        {
            InitialExecutableRelativePath = Target.RelativePath,
            PrepareCandidate = Target.Validate
        });
        async Task FaultedStage(string path)
        {
            await Stage(path); var file = Path.Combine(path, Target.RelativePath);
            if (fault == "length") File.WriteAllBytes(file, payload[..^1]);
            if (fault == "hash") { var changed = payload.ToArray(); changed[^1] ^= 1; File.WriteAllBytes(file, changed); }
            if (fault is "link" or "directory" or "missing") File.Delete(file);
            if (fault == "directory") Directory.CreateDirectory(file);
            if (fault == "link") File.CreateSymbolicLink(file, Path.Combine(path, "eu.x86_64"));
        }
        if (fault == "valid")
        {
            await tx.InstallAsync(FaultedStage, "v1");
            Assert.Equal("us.x86_64", tx.ReadCommitted()!.SelectedExecutable);
            Assert.Equal(payload, File.ReadAllBytes(Path.Combine(Live, "us.x86_64")));
        }
        else
        {
            var error = await Record.ExceptionAsync(() => tx.InstallAsync(FaultedStage, "v1"));
            Assert.NotNull(error); Assert.True(error is InvalidDataException or IOException, error.ToString());
            Assert.False(File.Exists(Path.Combine(Live, PortableInstallationTransaction.ManifestFileName)));
            Assert.Equal(new[] { ".relicade-mod-writer.lock" }, Directory.GetFileSystemEntries(Live).Select(Path.GetFileName));
            Assert.Empty(File.ReadAllBytes(Path.Combine(Live, ".relicade-mod-writer.lock")));
        }
    }
}
