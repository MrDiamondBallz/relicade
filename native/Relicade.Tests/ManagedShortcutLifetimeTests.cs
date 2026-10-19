using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using Relicade.Core.Services;
using Relicade.Services;

namespace Relicade.Tests;

// Nonexecuted format fixtures are NOT a native executable or descendant-lifetime proof.
public sealed class ManagedShortcutLifetimeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "shortcut-lifetime-" + Guid.NewGuid().ToString("N"));
    public ManagedShortcutLifetimeTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    internal static byte[] NativeShape()
    {
        var bytes = new byte[128];
        new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 2); // ET_EXEC
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? (ushort)183 : (ushort)62);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(52), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(54), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 1); // PT_LOAD
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 5); // PF_R | PF_X
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(96), 128);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(104), 128);
        return bytes;
    }

    [Theory]
    [InlineData("script", "runner.AppImage")]
    [InlineData("script", "native")]
    [InlineData("text", "fixture.AppImage")]
    [InlineData("truncated", "fixture.AppImage")]
    [InlineData("appimage", "native")]
    [InlineData("wrong-machine", "fixture.AppImage")]
    [InlineData("shared-library", "fixture.AppImage")]
    [InlineData("bad-program-table", "fixture.AppImage")]
    [InlineData("no-load", "fixture.AppImage")]
    [InlineData("native", "fixture.exe")]
    [InlineData("native", "fixture.app")]
    public void Unknown_or_runner_formats_refuse_independent_of_name(string kind, string name)
    {
        var bytes = NativeShape();
        switch (kind)
        {
            case "script": bytes = "#!/bin/sh\nexit 0\n"u8.ToArray(); break;
            case "text": bytes = "NONEXECUTED FIXTURE"u8.ToArray(); break;
            case "truncated": bytes = bytes[..20]; break;
            case "appimage": bytes[8] = (byte)'A'; bytes[9] = (byte)'I'; bytes[10] = 2; break;
            case "wrong-machine": bytes[18] = 0; bytes[19] = 0; break;
            case "shared-library": bytes[16] = 1; break;
            case "bad-program-table": BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), ulong.MaxValue); break;
            case "no-load": bytes[64] = 0; break;
        }
        var path = Path.Combine(root, name); File.WriteAllBytes(path, bytes);
        Assert.Throws<NotSupportedException>(() => ManagedShortcutLaunch.EnsureSupportedLifetime(path));
    }

    [Fact]
    public void Missing_target_is_not_evidence_of_native_lifetime() =>
        Assert.Throws<NotSupportedException>(() => ManagedShortcutLaunch.EnsureSupportedLifetime(Path.Combine(root, "missing")));

    [Theory] [InlineData("fixture.AppImage")] [InlineData("renamed.sh")]
    public void Native_shape_is_a_dispatch_format_only_not_filename_or_lifetime_proof(string name)
    {
        var path = Path.Combine(root, name); File.WriteAllBytes(path, NativeShape());
        ManagedShortcutLaunch.EnsureSupportedLifetime(path);
    }

    [Fact]
    public async Task Observing_direct_PID_exit_does_not_claim_established_descendant_exit()
    {
        var method = typeof(ManagedShortcutLaunch).GetMethod("ObserveUnestablishedExitAsync", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        // Own benign host sleep process; no fixture script/game/payload executed.
        using var process = Process.Start(new ProcessStartInfo("sleep", "0.1") { UseShellExecute = false })!;
        var task = (Task)method.Invoke(null, [process])!;
        await Assert.ThrowsAsync<IOException>(() => task);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Unestablished_started_process_retains_uncertain_receipt_after_exit_and_reopen()
    {
        var install = Path.Combine(root, "apps", "tile"); var source = Path.Combine(root, "fixture.AppImage");
        File.WriteAllBytes(source, NativeShape());
        await GameInstallationService.InstallOrUpdateGameAsync(source, install, "fixture.AppImage", "v1");
        var method = typeof(GameLaunchService).GetMethod("TrackStartedProcess", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var tx = new PortableInstallationTransaction(install);
        using (var lifetime = PortableInstallationLifetime.Begin(install, tx.ReadCommitted()!.OperationId))
        using (var process = Process.Start(new ProcessStartInfo("sleep", "0.1") { UseShellExecute = false })!)
        {
            method.Invoke(null, [lifetime, process, true]);
            await process.WaitForExitAsync();
        }
        var receipt = Path.Combine(tx.OperationsPath, "launch.json");
        Assert.Contains("Launching", File.ReadAllText(receipt));
        Assert.NotNull(new PortableInstallationTransaction(install).ReadCommitted()); // healthy read-only recovery is allowed
        Assert.Throws<IOException>(() => new PortableInstallationTransaction(install).Rollback());
        Assert.Throws<IOException>(() => new PortableInstallationTransaction(install).SelectExecutable("fixture.AppImage"));
        Assert.Throws<IOException>(() => new PortableInstallationTransaction(install).AcquireModWriterLease());
        Assert.Throws<IOException>(() => PortableInstallationLifetime.Begin(install));
        Assert.True(File.Exists(receipt));
        var before = File.ReadAllBytes(receipt);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(ManagedShortcutLifetimeTests).Assembly.Location);
        foreach (var arg in new[] { "-noLogo", "-noColor", "-parallel", "none", "-method", "Relicade.Tests.ManagedShortcutLifetimeTests.Independent_uncertain_writer_control" }) info.ArgumentList.Add(arg);
        info.Environment["RELICADE_SHORTCUT_UNCERTAIN_FIXTURE"] = install;
        using var writer = Process.Start(info)!;
        var stdout = writer.StandardOutput.ReadToEndAsync(); var stderr = writer.StandardError.ReadToEndAsync();
        try { await writer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!writer.HasExited) { writer.Kill(true); await writer.WaitForExitAsync(); } }
        Assert.True(writer.ExitCode == 0, await stdout + await stderr);
        Assert.Equal(before, File.ReadAllBytes(receipt));
    }

    [Fact]
    public async Task Independent_uncertain_writer_control()
    {
        var install = Environment.GetEnvironmentVariable("RELICADE_SHORTCUT_UNCERTAIN_FIXTURE");
        if (install == null) return; // worker-entry no-op is NOT scenario coverage
        var tx = new PortableInstallationTransaction(install);
        var manifest = File.ReadAllBytes(Path.Combine(install, PortableInstallationTransaction.ManifestFileName));
        Assert.Throws<IOException>(() => PortableInstallationLifetime.Begin(install));
        Assert.Throws<IOException>(() => tx.Rollback());
        Assert.Throws<IOException>(() => tx.SelectExecutable("fixture.AppImage"));
        Assert.Throws<IOException>(() => new Relicade.Services.Mods.InstalledModsStore().Save(install, new()));
        var source = Path.Combine(Path.GetDirectoryName(install)!, "next.AppImage"); File.WriteAllBytes(source, NativeShape());
        await Assert.ThrowsAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(source, install, "next.AppImage", "v2"));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(install, PortableInstallationTransaction.ManifestFileName)));
        Assert.Contains("Launching", File.ReadAllText(Path.Combine(tx.OperationsPath, "launch.json")));
        Console.WriteLine("separate uncertain writer: begin/rollback/selection/mod/update refused; manifest and receipt retained after observed PID exit");
    }
}
