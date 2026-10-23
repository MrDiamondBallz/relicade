using Relicade.Core.Services;
using Relicade.Models;
using Relicade.Services;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Relicade.Tests;

public sealed class Z2rsSetupTests
{
    [Fact]
    public void Official_project_has_a_package_bound_setup_definition()
    {
        var definition = OwnedGameSetupService.Requirement(new GameInfo { Repository = "troyedwardsjr/z2rs" });
        Assert.NotNull(definition);
        Assert.Equal("v0.6.0", definition.ReleaseIdentity);
        Assert.Equal("z2rs-0.6.0-x86_64-unknown-linux-gnu.tar.gz|sha256:356d9624eb1795179f2457e693901577b3343f931b162d24daca53dac9b99458", definition.AssetIdentity);
        Assert.Equal("a064a4587b23f0a4276ea81a2824a1ccf2e89be962454f54cb425b2b871081df", definition.ExecutableSha256);
        Assert.Equal("zelda2.nes", definition.FileName);
    }
    [Theory][InlineData("manual")][InlineData("linked")][InlineData("provider")][InlineData("project")]
    public void Unrelated_or_user_managed_games_keep_their_existing_launch_flow(string kind)
    {
        var game = new GameInfo { Repository = "troyedwardsjr/z2rs" };
        if (kind == "manual") game.Repository = "";
        if (kind == "linked") game.InstallPath = "/synthetic/existing";
        if (kind == "provider") game.RepositorySource = "gitlab";
        if (kind == "project") game.Repository = "another/z2rs";
        Assert.Null(OwnedGameSetupService.Requirement(game));
    }
    [Theory][InlineData("valid")][InlineData("tag")][InlineData("archive")]
    public void First_install_target_is_bound_to_exact_release_archive_and_executable(string kind)
    {
        var game = new GameInfo { Repository = "troyedwardsjr/z2rs" }; var rule = OwnedGameSetupService.Z2rs;
        var target = OwnedGameSetupService.PinnedInstallTarget(game,
            kind == "tag" ? "v0.6.1" : rule.ReleaseIdentity, kind == "archive" ? "other" : rule.AssetIdentity);
        if (kind != "valid") { Assert.Null(target); return; }
        Assert.NotNull(target); Assert.Equal("z2rs", target.RelativePath); Assert.Equal(37783632, target.Length);
        Assert.Equal(rule.ExecutableSha256, target.Sha256);
    }

}

public sealed class Z2rsOwnedBodyTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "relicade-nes-" + Guid.NewGuid().ToString("N"));
    readonly byte[] body = "123456789"u8.ToArray(); // CRC32/SHA1 standard synthetic vector, never ROM bytes.
    string Source => Path.Combine(root, "original.nes");
    string Data => Path.Combine(root, "Data");
    string Install => Path.Combine(root, "Apps", "Synthetic");
    PortableInstallationTransaction Tx => new(Install);
    OwnedDataRequirement definition = null!;
    byte[] Input(bool header)
    {
        if (!header) return body.ToArray();
        var data = new byte[body.Length + 16]; "NES\u001a"u8.CopyTo(data); body.CopyTo(data, 16); return data;
    }
    async Task Prepare(bool header)
    {
        Directory.CreateDirectory(root); File.WriteAllBytes(Source, Input(header));
        var executable = PortableInstallationFixtures.NativePayload("owned NES fixture");
        definition = new("synthetic-nes", "synthetic NES body", "test", "fixture|sha256:" + new string('a', 64),
            Convert.ToHexStringLower(SHA256.HashData(executable)), body.Length, "", "zelda2.nes",
            NesBody: new("f7c3bc1d808e04732adf679965ccc34ca7ae3441", 0xcbf43926));
        await new PortableInstallationTransaction(Install, new() { ReleaseIdentity = definition.ReleaseIdentity, AssetIdentity = definition.AssetIdentity })
            .InstallAsync(stage => { File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), executable); return Task.CompletedTask; }, "test");
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Both_forms_preserve_all_original_bytes_and_revalidate_actual_length(bool header)
    {
        await Prepare(header); var original = File.ReadAllBytes(Source);
        var plan = Tx.PreviewOwnedData(definition, Data, Source);
        Assert.False(Directory.Exists(Data)); Assert.Equal(original.Length, plan.SourceLength); Assert.Equal(original.Length, plan.BytesToCopy);
        var imported = Tx.ImportOwnedData(definition, Data, plan);
        Assert.True(imported.Completed); Assert.Equal(original, File.ReadAllBytes(Source)); Assert.Equal(original, File.ReadAllBytes(imported.Destination));
        Assert.Equal(imported.Destination, Tx.ValidateOwnedData(definition, Data, Tx.ReadCommitted()!));
        var repeat = Tx.PreviewOwnedData(definition, Data, Source); Assert.True(repeat.ReuseExisting); Assert.Equal(0, repeat.BytesToCopy);
    }

    [Theory]
    [InlineData("missing-magic")][InlineData("bad-magic")][InlineData("truncated")][InlineData("oversized")]
    [InlineData("changed-body")][InlineData("wrong-crc")][InlineData("wrong-sha")][InlineData("bare-magic")]
    public async Task Invalid_body_or_header_is_rejected_without_copy(string fault)
    {
        await Prepare(true); var bytes = File.ReadAllBytes(Source);
        switch (fault)
        {
            case "missing-magic": bytes[0] = 0; break;
            case "bad-magic": bytes[3] = 0; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "oversized": bytes = bytes.Concat(new byte[] { 0 }).ToArray(); break;
            case "changed-body": bytes[^1] ^= 1; break;
            case "wrong-crc": definition = definition with { NesBody = definition.NesBody! with { Crc32 = 0 } }; break;
            case "wrong-sha": definition = definition with { NesBody = definition.NesBody! with { Sha1 = new string('0', 40) } }; break;
            case "bare-magic": bytes = Input(false); "NES\u001a"u8.CopyTo(bytes); break;
        }
        File.WriteAllBytes(Source, bytes);
        Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(definition, Data, Source));
        Assert.False(Directory.Exists(Data)); Assert.Equal(bytes, File.ReadAllBytes(Source));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Cancellation_and_changed_preview_preserve_original_without_commit(bool header)
    {
        await Prepare(header); var plan = Tx.PreviewOwnedData(definition, Data, Source);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Tx.ImportOwnedData(definition, Data, plan, cancelled.Token));
        Assert.False(File.Exists(plan.Destination));
        File.WriteAllBytes(Source, Input(!header));
        Assert.ThrowsAny<IOException>(() => Tx.ImportOwnedData(definition, Data, plan));
        Assert.False(File.Exists(plan.Destination)); Assert.Equal(Input(!header), File.ReadAllBytes(Source));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Corrupt_copy_is_preserved_before_explicit_repair_and_form_switch_is_not_silent(bool header)
    {
        await Prepare(header); var plan = Tx.PreviewOwnedData(definition, Data, Source); Tx.ImportOwnedData(definition, Data, plan);
        var bad = File.ReadAllBytes(plan.Destination); bad[^1] ^= 1; File.WriteAllBytes(plan.Destination, bad);
        Assert.Throws<OwnedDataNeedsRepairException>(() => Tx.ValidateOwnedData(definition, Data, Tx.ReadCommitted()!));
        File.WriteAllBytes(Source, Input(!header));
        Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(definition, Data, Source));
        Assert.Equal(bad, File.ReadAllBytes(plan.Destination));
        File.WriteAllBytes(Source, Input(header)); var repair = Tx.PreviewOwnedData(definition, Data, Source);
        Assert.NotNull(repair.Repair); Assert.Equal(bad, File.ReadAllBytes(plan.Destination));
        Tx.ImportOwnedData(definition, Data, repair);
        Assert.Equal(bad, File.ReadAllBytes(repair.Repair.RetainedDestination));
        Assert.Equal(Input(header), File.ReadAllBytes(plan.Destination));
        Assert.Equal(plan.Destination, Tx.ValidateOwnedData(definition, Data, Tx.ReadCommitted()!));
    }

    [Fact]
    public void Launch_arguments_are_literal_and_disable_presence_without_coop_or_config_override()
    {
        var path = "/synthetic/a b; --config evil/zelda2.nes";
        var start = new ProcessStartInfo();
        GameLaunchService.ConfigureOwnedDataArguments(start, path, definition: OwnedGameSetupService.Z2rs);
        Assert.Equal(new[] { "--rom", path, "--no-discord" }, start.ArgumentList);
        Assert.Empty(start.Arguments);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Cancellation_during_copy_leaves_no_committed_data_and_allows_a_fresh_retry(bool header)
    {
        await Prepare(header); var plan = Tx.PreviewOwnedData(definition, Data, Source);
        using var cancellation = new CancellationTokenSource(); var copied = false;
        Assert.ThrowsAny<OperationCanceledException>(() => Tx.ImportOwnedData(definition, Data, plan, cancellation.Token,
            point => { if (point == "Copying") { copied = true; cancellation.Cancel(); } }));
        Assert.True(copied); Assert.False(File.Exists(plan.Destination)); Assert.Equal(Input(header), File.ReadAllBytes(Source));
        var fresh = Tx.PreviewOwnedData(definition, Data, Source); var result = Tx.ImportOwnedData(definition, Data, fresh);
        Assert.True(result.Completed); Assert.Equal(Input(header), File.ReadAllBytes(result.Destination));
    }

    [Fact]
    public async Task Different_valid_header_bytes_cannot_overwrite_an_existing_valid_copy()
    {
        await Prepare(true); var plan = Tx.PreviewOwnedData(definition, Data, Source); Tx.ImportOwnedData(definition, Data, plan);
        var alternate = Input(true); alternate[15] = 42; File.WriteAllBytes(Source, alternate);
        Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(definition, Data, Source));
        Assert.Equal(Input(true), File.ReadAllBytes(plan.Destination)); Assert.Equal(alternate, File.ReadAllBytes(Source));
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)] static extern int mkfifo(string path, uint mode);
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)] static extern int link(string original, string destination);
    [System.Runtime.InteropServices.DllImport("libc")] static extern int gettid();

    [Theory][InlineData("directory")][InlineData("symlink")][InlineData("hardlink")]
    public async Task Nonregular_or_linked_sources_are_preserved_and_refused(string kind)
    {
        await Prepare(true); var original = File.ReadAllBytes(Source); File.Move(Source, Source + ".original");
        if (kind == "directory") Directory.CreateDirectory(Source);
        if (kind == "symlink") File.CreateSymbolicLink(Source, Source + ".original");
        if (kind == "hardlink") Assert.Equal(0, link(Source + ".original", Source));
        var error = Record.Exception(() => Tx.PreviewOwnedData(definition, Data, Source));
        Assert.True(error is IOException or InvalidDataException); Assert.False(Directory.Exists(Data));
        Assert.Equal(original, File.ReadAllBytes(Source + ".original"));
    }

    [Fact]
    public async Task Fifo_input_is_rejected_before_open_with_controlled_failure_drain()
    {
        await Prepare(true); File.Move(Source, Source + ".original"); Assert.Equal(0, mkfifo(Source, 0x180));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Task.Run(() => { entered.SetResult(gettid()); return Record.Exception(() => Tx.PreviewOwnedData(definition, Data, Source, cancellation.Token)); });
        bool blocked = false; Exception? error;
        try
        {
            var thread = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var deadline = Stopwatch.StartNew();
            while (!operation.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.ReadAllText($"/proc/self/task/{thread}/wchan").Trim() == "wait_for_partner") { blocked = true; break; }
                await Task.Yield();
            }
            cancellation.Cancel();
        }
        finally
        {
            using var release = new FileStream(Source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            error = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(blocked); Assert.True(error is IOException or InvalidDataException);
        Assert.False(Directory.Exists(Data)); Assert.Equal(Input(true), File.ReadAllBytes(Source + ".original"));
    }

}
