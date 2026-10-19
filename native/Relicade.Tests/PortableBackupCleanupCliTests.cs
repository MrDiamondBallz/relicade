using System.Text.Json;
using System.Text.RegularExpressions;
using Relicade.Models;
using Relicade.Services;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableBackupCleanupCliTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> InvalidArgs()
    {
        foreach (var args in new[] {
            new[] { "--preview-portable-backups" }, new[] { "--preview-portable-backups", "" }, new[] { "--preview-portable-backups", "bad\nkey" },
            new[] { "--preview-portable-backups", "a", "extra" }, new[] { "--preview-portable-backups=a" }, new[] { "--PREVIEW-PORTABLE-BACKUPS", "a" },
            new[] { "--preview-portable-backups\n", "a" }, new[] { "--cleanup-portable-backups\t", "a" },
            new[] { "--preview-portable-backups", "a", "--kiosk" }, new[] { "--kiosk", "--preview-portable-backups", "a" },
            new[] { "--list", "--preview-portable-backups", "a" }, new[] { "--launch-instance", "--preview-portable-backups", "a" },
            new[] { "--preview-portable-backups", "a", "--preview-portable-backups", "a" },
            new[] { "--cleanup-portable-backups", "a" }, new[] { "--cleanup-portable-backups", "a", "--confirm", "short" },
            new[] { "--cleanup-portable-backups", "a", "--confirm", new string('A',64) }, new[] { "--cleanup-portable-backups", "a", "--confirm=" + new string('a',64) },
            new[] { "--cleanup-portable-backups", "a", "--confirm", new string('a',64), "--confirm", new string('a',64) },
            new[] { "--cleanup-portable-backups", "a", "--confirm", new string('a',64), "--preview-portable-backups", "a" }
        }) foreach (var injected in new[] { false, true }) yield return [injected, args];
    }
    [Theory, MemberData(nameof(InvalidArgs))]
    public async Task Invalid_exact_shapes_fail_before_header_and_all_initialization(bool injected, string[] args)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await s.Install("1");
        var before = s.Snapshot(); var result = await s.Execute(injected, args);
        Assert.NotEqual(0, result.Code); Assert.DoesNotContain("Launcher Version:", result.Text); Assert.Equal(0, s.Network.Calls); Assert.Equal(before, s.Snapshot());
    }
    [Fact]
    public void Original_kiosk_or_mixed_arguments_cannot_be_hidden_by_dispatch()
    {
        var args = new[] { "--preview-portable-backups", "key" };
        Assert.True(PortableBackupCleanupCommand.ValidOriginalArguments(["Relicade", ..args], args));
        Assert.False(PortableBackupCleanupCommand.ValidOriginalArguments(["Relicade", "--kiosk", ..args], args));
        Assert.False(PortableBackupCleanupCommand.ValidOriginalArguments(["Relicade", ..args, "--kiosk"], args));
    }
    public static IEnumerable<object[]> Refusals()
    {
        foreach (var injected in new[] { false, true })
        foreach (var kind in new[] { "missing-key", "name", "repository", "key-case", "duplicate-key", "corrupt-library", "linked-library", "denied-library", "corrupt-settings", "kiosk", "stale-library", "stale-settings", "stale-root", "current-save", "writer", "mod", "launch", "default-located", "shared", "manual", "MSI", "Flatpak" }) yield return [injected, kind];
    }
    [Theory, MemberData(nameof(Refusals))]
    public async Task Public_command_refuses_ambiguous_stale_and_unsafe_context_without_requests_or_writes(bool injected, string kind)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await s.Install("1"); await s.Install("2"); await s.Install("3");
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]); Assert.Equal(0, preview.Code);
        var hash = Regex.Match(preview.Text, @"Plan SHA256: ([a-f0-9]{64})").Groups[1].Value;
        var key = s.Game.InstanceKey; UnixFileMode? mode = null;
        switch (kind)
        {
            case "missing-key": key = "missing"; break;
            case "name": key = s.Game.Name!; break;
            case "repository": key = s.Game.Repository!; break;
            case "key-case": key = key.ToUpperInvariant(); break;
            case "duplicate-key": s.SaveLibrary(s.Game); break;
            case "corrupt-library": File.WriteAllText(s.Library, "{broken"); break;
            case "linked-library": File.Move(s.Library, Path.Combine(s.Catalog, "outside")); File.CreateSymbolicLink(s.Library, Path.Combine(s.Catalog, "outside")); break;
            case "denied-library": mode = File.GetUnixFileMode(s.Library); File.SetUnixFileMode(s.Library, UnixFileMode.None); break;
            case "corrupt-settings": File.WriteAllText(s.Settings, "{broken"); break;
            case "kiosk": File.WriteAllText(s.Settings, JsonSerializer.Serialize(new AppSettings { AppsPath = Path.Combine(s.Root, "Apps"), KioskMode = true })); break;
            case "stale-library": File.AppendAllText(s.Library, " "); break;
            case "stale-settings": File.AppendAllText(s.Settings, " "); break;
            case "stale-root": Directory.Move(s.Live, Path.Combine(s.Root, "lost-live")); break;
            case "current-save": File.WriteAllText(Path.Combine(s.Live, "LastPlayed.txt"), "current save changed since preview"); break;
            case "launch": File.WriteAllText(Path.Combine(s.Tx.OperationsPath, "launch.json"), "stale unknown do not remove"); break;
            case "default-located": s.Game.InstallPath = Path.Combine(s.Root, "located"); Directory.CreateDirectory(s.Game.InstallPath); s.SaveLibrary(); break;
            case "shared": s.SaveLibrary(new GameInfo { Name = "Shared", Repository = "fixture/other", FolderName = "other", InstallPath = s.Live }); break;
            case "manual": s.Game.Repository = null; s.SaveLibrary(); key = s.Game.InstanceKey; break;
            case "MSI": File.WriteAllText(Path.Combine(s.Live, WindowsInstallerService.ReceiptFileName), "conflict"); break;
            case "Flatpak": File.WriteAllText(Path.Combine(s.Live, FlatpakService.ReceiptFileName), "conflict"); break;
        }
        var before = kind == "denied-library" ? null : s.Snapshot();
        using IDisposable? held = kind == "writer" ? s.Tx.AcquireLease() : kind == "mod" ? new FileStream(Path.Combine(s.Live, ".relicade-mod-writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        // Do not read a FileShare.None lock while it is held. Read back the full
        // snapshot after explicit release, without weakening any equality assertion.
        try
        {
            var result = await s.Execute(injected, ["--cleanup-portable-backups", key, "--confirm", hash]);
            output.WriteLine($"{kind}; injected={injected}; exit={result.Code}; actual_HTTP={s.Network.Calls}; {result.Text}");
            Assert.NotEqual(0, result.Code); Assert.DoesNotContain("Launcher Version:", result.Text); Assert.Equal(0, s.Network.Calls);
            held?.Dispose();
            if (before != null) Assert.Equal(before, s.Snapshot());
            Assert.Empty(Directory.GetFiles(s.Tx.OperationsPath, "backup-cleanup-*.json"));
        }
        finally { if (mode != null) File.SetUnixFileMode(s.Library, mode.Value); }
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Located_managed_instance_with_no_default_occupant_uses_both_authorities(bool injected)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        var located = Path.Combine(s.Root, "located"); s.Game.InstallPath = located; s.SaveLibrary();
        var tx = new PortableInstallationTransaction(located);
        for (var i = 1; i <= 3; i++)
        {
            var version = "v" + i;
            await tx.InstallAsync(p => { File.WriteAllBytes(Path.Combine(p, "fixture.AppImage"), PortableInstallationFixtures.NativePayload(version)); return Task.CompletedTask; }, version);
        }
        var preview = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]); Assert.Equal(0, preview.Code);
        var hash = Regex.Match(preview.Text, @"Plan SHA256: ([a-f0-9]{64})").Groups[1].Value;
        Assert.False(Directory.Exists(s.Live));
        using (s.Tx.AcquireLease())
        {
            var blocked = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash]);
            Assert.NotEqual(0, blocked.Code);
        }
        var result = await s.Execute(injected, ["--cleanup-portable-backups", s.Game.InstanceKey, "--confirm", hash]);
        Assert.Equal(0, result.Code); Assert.Equal(0, s.Network.Calls); Assert.False(Directory.Exists(s.Live)); Assert.Equal("v3", tx.ReadCommitted()!.SelectedVersion);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Unrelated_missing_managed_generation_never_recovers_or_loads_status(bool injected)
    {
        using var s = new PortableBackupCleanupBaselineScope(); await s.Install("1"); await s.Install("2"); await s.Install("3");
        var missing = Path.Combine(s.Root, "Apps", "other");
        await new PortableInstallationTransaction(missing).InstallAsync(p => { File.WriteAllBytes(Path.Combine(p, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("other")); return Task.CompletedTask; }, "other");
        Directory.Move(missing, Path.Combine(s.Root, "unavailable-other"));
        s.SaveLibrary(new GameInfo { Name = "Missing Other", Repository = "fixture/other", FolderName = "other", AutoUpdate = true });
        var before = s.Snapshot(); var result = await s.Execute(injected, ["--preview-portable-backups", s.Game.InstanceKey]);
        Assert.Equal(0, result.Code); Assert.Equal(0, s.Network.Calls); Assert.Equal(before, s.Snapshot()); Assert.False(Directory.Exists(missing));
        Assert.Contains("ALL previous generations", result.Text);
    }
}
