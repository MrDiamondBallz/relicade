using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;

namespace Relicade.Tests;

public class ReleasePackagingTests
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(8);

    private readonly ITestOutputHelper _output;

    public ReleasePackagingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public void Native_source_does_not_activate_a_release_workflow_or_bundle_user_data()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "dotnet-desktop.yml");
        File.Exists(workflowPath).Should().BeFalse("release automation is not part of the native foundation");
        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
        props.Should().NotContain("apps.json");
        props.Should().NotContain("settings.json");
    }

    [Fact]
    public void Project_references_velopack_and_does_not_copy_apps_json()
    {
        var sharedPath = Path.Combine(RepoRoot, "Relicade.App.csproj");
        var desktopPath = Path.Combine(RepoRoot, "Relicade.Desktop", "Relicade.Desktop.csproj");
        var shared = File.ReadAllText(sharedPath);
        var desktop = File.ReadAllText(desktopPath);

        desktop.Should().Contain("Avalonia.Desktop");
        shared.Should().Contain("Velopack");
        shared.Should().NotContain(
            "<None Update=\"apps.json\">",
            "apps.json must not be copied to publish output; it is user data created at runtime");
        shared.Should().NotContain("CopyWindowsUpdater");
        desktop.Should().Contain("osx-arm64");
        desktop.Should().Contain("DestinationFiles=\"$(_AliasDir)Relicade.exe\"");
        desktop.Should().Contain("DestinationFiles=\"$(_AliasDir)Relicade\"");
        desktop.Should().Contain("!Exists('$(_AliasDir)$(AssemblyName).exe')");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Publish_output_does_not_include_apps_json()
    {
        var publishDir = Path.Combine(Path.GetTempPath(), "RelicadePublishTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(publishDir);

        Process? process = null;

        try
        {
            var runtimeIdentifier = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64"
                : "linux-x64";

            var publishArgs =
                $"publish \"{Path.Combine(RepoRoot, "Relicade.Desktop", "Relicade.Desktop.csproj")}\" -c Release -r {runtimeIdentifier} --self-contained true -p:PublishTrimmed=false -m:1 -p:UseSharedCompilation=false -o \"{publishDir}\"";

            using var timeoutCts = new CancellationTokenSource(PublishTimeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = publishArgs,
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _output.WriteLine($"Starting publish (timeout {PublishTimeout.TotalMinutes:F0} minutes)...");
            _output.WriteLine(startInfo.FileName + " " + startInfo.Arguments);

            process = Process.Start(startInfo);
            process.Should().NotBeNull();

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process!.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                stdout.AppendLine(e.Data);
                _output.WriteLine("[stdout] " + e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                stderr.AppendLine(e.Data);
                _output.WriteLine("[stderr] " + e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                throw new TimeoutException(
                    $"dotnet publish did not finish within {PublishTimeout.TotalMinutes:F0} minutes.");
            }

            process.ExitCode.Should().Be(0, because: stderr.ToString());

            File.Exists(Path.Combine(publishDir, "apps.json")).Should().BeFalse(
                "release publish output must not ship a blank apps.json that could wipe user libraries on update");

            File.Exists(Path.Combine(publishDir, "Relicade.Updater.exe")).Should().BeFalse(
                "custom Relicade.Updater.exe must not ship; Velopack owns updates");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup after timeout or test failure.
                }
            }

            process?.Dispose();

            if (Directory.Exists(publishDir))
                Directory.Delete(publishDir, true);
        }
    }
}
