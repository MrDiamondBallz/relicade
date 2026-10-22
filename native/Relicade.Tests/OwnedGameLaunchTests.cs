using System.Diagnostics;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class OwnedGameLaunchTests
{
    [Theory]
    [InlineData("perfect-dark-pc-port/perfect_dark", null, true)]
    [InlineData("fgsfdsfgs/perfect_dark", "github", true)]
    [InlineData("perfect-dark-pc-port/perfect_dark", "gitlab", false)]
    [InlineData("other/perfect_dark", "github", false)]
    public void Compiled_adapter_is_bound_to_reviewed_project_and_exact_package(string repository, string? provider, bool expected)
    {
        var requirement = OwnedGameSetupService.Requirement(new Relicade.Models.GameInfo { Repository = repository, RepositorySource = provider });
        Assert.Equal(expected, requirement != null);
        if (requirement == null) return;
        Assert.Equal("ci-dev-build", requirement.ReleaseIdentity);
        Assert.Equal("pd-x86_64-linux.tar.gz|sha256:0f28fd59b488e0a919aae4350083059faebd462fce52df7bfdc9a4aec26f92ae", requirement.AssetIdentity);
        Assert.Equal("40e8e1317a712a0b29ebd715d25ab7ae366aa601444eb8dec45b97c308ddf9a5", requirement.ExecutableSha256);
        Assert.Equal(33554432, requirement.Length);
        Assert.Equal("e03b088b6ac9e0080440efed07c1e40f", requirement.Md5);
    }

    [Fact]
    public async Task Unknown_package_refuses_actual_launch_without_recent_play_or_process_start()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize(); var started = 0;
        f.Game.GameProcessStarted += _ => started++;
        var manifest = File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName));
        Assert.False(await GameLaunchService.LaunchAsync(f.Game, f.Manager.GamesFolder));
        Assert.Equal(0, started); Assert.False(File.Exists(Path.Combine(f.Live, "LastPlayed.txt")));
        Assert.False(File.Exists(Path.Combine(new PortableInstallationTransaction(f.Live).OperationsPath, "launch.json")));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(f.Live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(0, f.NetworkCalls);
    }

    [Fact]
    public async Task Unmanaged_known_project_cannot_create_a_direct_game_shortcut()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        var other = Path.Combine(f.Root, "unmanaged"); Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "launch.sh"), "#!/bin/sh\nexit 0\n");
        f.Game.InstallPath = other;
        await Assert.ThrowsAsync<IOException>(() => GameShortcutLaunch.PrepareAsync(f.Game, f.Manager.GamesFolder, f.Store.Current));
        Assert.Equal(0, f.NetworkCalls);
    }

    [Fact]
    public async Task Verified_data_argument_reaches_an_owned_test_worker_as_one_literal_argument()
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        var setup = f.Setup(); var plan = setup.Preview(f.Source, CancellationToken.None);
        setup.Apply(plan, CancellationToken.None);
        var tx = new PortableInstallationTransaction(f.Live);
        using var lifetime = PortableInstallationLifetime.Begin(f.Live);
        var validated = tx.ValidateOwnedData(f.Definition, f.Root, lifetime.CommittedManifest!);
        var worker = Path.Combine(f.Root, "owned-argument-worker.sh");
        File.WriteAllText(worker, "#!/bin/sh\nprintf '%s\\n' \"$@\"\n");
        File.SetUnixFileMode(worker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var start = new ProcessStartInfo(worker) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        GameLaunchService.ConfigureOwnedDataArguments(start, validated);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(); var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode); Assert.Equal("", error);
        Assert.Equal(new[] { "--rom-file", validated }, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
    }
}
