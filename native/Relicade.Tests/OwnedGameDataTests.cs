using System.Security.Cryptography;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class OwnedGameDataTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "owned-game-data-" + Guid.NewGuid().ToString("N"));
    readonly byte[] input = Enumerable.Range(0, 200000).Select(i => (byte)(i % 251)).ToArray();
    string Install => Path.Combine(root, "Apps", "Game");
    string Data => Path.Combine(root, "UserData");
    string Source => Path.Combine(root, "original.z64");
    PortableInstallationTransaction Tx => new(Install);
    OwnedDataRequirement requirement = null!;
    async Task Prepare()
    {
        OwnedDataFixturePlatform.RequireLinuxX64();
        Directory.CreateDirectory(root); File.WriteAllBytes(Source, input);
        var executable = PortableInstallationFixtures.NativePayload("setup fixture");
        requirement = new("synthetic-test", "synthetic test input", "test-release", "test-asset|sha256:" + new string('a', 64),
            Convert.ToHexStringLower(SHA256.HashData(executable)), input.Length, Convert.ToHexStringLower(MD5.HashData(input)));
        var tx = new PortableInstallationTransaction(Install, new() { ReleaseIdentity = requirement.ReleaseIdentity, AssetIdentity = requirement.AssetIdentity });
        await tx.InstallAsync(stage => { File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), executable); return Task.CompletedTask; }, "test-release");
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task Compiled_wad_filename_and_sha256_are_checked_on_import_and_launch()
    {
        await Prepare();
        requirement = requirement with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(input)) };
        var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        Assert.Equal("freedoom2.wad", Path.GetFileName(plan.Destination));
        Tx.ImportOwnedData(requirement, Data, plan);
        Assert.Equal(plan.Destination, Tx.ValidateOwnedData(requirement, Data, Tx.ReadCommitted()!));
        var wrong = requirement with { ExpectedSha256 = new string('0', 64) };
        Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(wrong, Data, Source));
        Assert.ThrowsAny<IOException>(() => Tx.ValidateOwnedData(wrong, Data, Tx.ReadCommitted()!));
        Assert.Equal(input, File.ReadAllBytes(Source)); Assert.Equal(input, File.ReadAllBytes(plan.Destination));
    }

    [Theory]
    [InlineData("../escape.wad")][InlineData("nested/data.wad")][InlineData("/absolute.wad")][InlineData("data\\escape.wad")]
    public async Task Adapter_data_filename_cannot_leave_its_dedicated_folder(string name)
    {
        await Prepare(); requirement = requirement with { FileName = name };
        if (name == "nested/data.wad") Assert.Throws<IOException>(() => Tx.PreviewOwnedData(requirement, Data, Source));
        else Assert.Throws<InvalidDataException>(() => Tx.PreviewOwnedData(requirement, Data, Source));
        Assert.False(Directory.Exists(Data)); Assert.Equal(input, File.ReadAllBytes(Source));
    }

    [Fact]
    public async Task Confirmed_copy_preserves_original_and_manifest_and_validates_for_launch()
    {
        await Prepare();
        var manifestBytes = File.ReadAllBytes(Path.Combine(Install, PortableInstallationTransaction.ManifestFileName));
        var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        Assert.False(Directory.Exists(Data)); Assert.Equal(input.Length, plan.BytesToCopy);
        var result = Tx.ImportOwnedData(requirement, Data, plan);
        Assert.True(result.Completed); Assert.Equal(input, File.ReadAllBytes(Source));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(result.Destination));
        Assert.Equal(input, File.ReadAllBytes(result.Destination));
        Assert.Equal(manifestBytes, File.ReadAllBytes(Path.Combine(Install, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal(result.Destination, Tx.ValidateOwnedData(requirement, Data, Tx.ReadCommitted()!));
    }

    [Theory]
    [InlineData("wrong-size")][InlineData("wrong-variant")][InlineData("link")][InlineData("directory")]
    public async Task Invalid_input_is_rejected_without_creating_a_data_store(string fault)
    {
        await Prepare();
        if (fault == "wrong-size") File.WriteAllBytes(Source, input[..100]);
        if (fault == "wrong-variant") File.WriteAllBytes(Source, input.Reverse().ToArray());
        if (fault == "link") { File.Move(Source, Source + ".original"); File.CreateSymbolicLink(Source, Source + ".original"); }
        if (fault == "directory") { File.Delete(Source); Directory.CreateDirectory(Source); }
        if (fault == "link")
        {
            Assert.Throws<InvalidDataException>(() => Tx.PreviewOwnedData(requirement, Data, Source));
            Assert.Equal(input, File.ReadAllBytes(Source + ".original"));
        }
        else Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(requirement, Data, Source));
        Assert.False(Directory.Exists(Data));
    }

    [Theory]
    [InlineData("Copying")][InlineData("BeforeCommit")][InlineData("DataCommitted")]
    public async Task Cancellation_preserves_original_and_any_committed_copy(string boundary)
    {
        await Prepare(); var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        using var cancel = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => Tx.ImportOwnedData(requirement, Data, plan, cancel.Token,
            p => { if (p == boundary) cancel.Cancel(); }));
        Assert.Equal(input, File.ReadAllBytes(Source));
        Assert.Empty(Directory.GetFiles(Data, "*.tmp", SearchOption.AllDirectories));
        if (boundary == "DataCommitted")
        {
            Assert.Equal(input, File.ReadAllBytes(plan.Destination));
            var fresh = Tx.PreviewOwnedData(requirement, Data, Source);
            Assert.True(fresh.ReuseExisting); Assert.Equal(0, fresh.BytesToCopy);
            Assert.True(Tx.ImportOwnedData(requirement, Data, fresh).Completed);
        }
        else Assert.False(File.Exists(plan.Destination));
    }

    [Fact]
    public async Task Existing_different_data_is_never_overwritten()
    {
        await Prepare(); var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Destination)!); File.WriteAllText(plan.Destination, "retained other data");
        Assert.ThrowsAny<IOException>(() => Tx.ImportOwnedData(requirement, Data, plan));
        Assert.Equal("retained other data", File.ReadAllText(plan.Destination)); Assert.Equal(input, File.ReadAllBytes(Source));
    }

    [Theory]
    [InlineData("receipt")][InlineData("writer")][InlineData("package")][InlineData("executable")]
    public async Task Uncertain_or_mismatched_installations_cannot_import(string fault)
    {
        await Prepare(); IDisposable? writer = null;
        if (fault == "receipt") { using var lifetime = PortableInstallationLifetime.Begin(Install); lifetime.PreserveUncertain(); }
        if (fault == "writer") writer = Tx.AcquireLease();
        if (fault == "package") requirement = requirement with { AssetIdentity = "different" };
        if (fault == "executable") requirement = requirement with { ExecutableSha256 = new string('b', 64) };
        try { Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(requirement, Data, Source)); }
        finally { writer?.Dispose(); }
        Assert.False(Directory.Exists(Data)); Assert.Equal(input, File.ReadAllBytes(Source));
    }

    [Fact]
    public async Task Corruption_after_setup_blocks_launch_validation_without_erasure()
    {
        await Prepare(); var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        Tx.ImportOwnedData(requirement, Data, plan); File.WriteAllText(plan.Destination, "corrupt but retained");
        Assert.ThrowsAny<IOException>(() => Tx.ValidateOwnedData(requirement, Data, Tx.ReadCommitted()!));
        Assert.Equal("corrupt but retained", File.ReadAllText(plan.Destination));
    }
    [Fact]
    public async Task Locked_input_has_no_copy_side_effects()
    {
        await Prepare(); using var held = new FileStream(Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => Tx.PreviewOwnedData(requirement, Data, Source));
        Assert.False(Directory.Exists(Data));
    }

    [Fact]
    public async Task Source_changed_after_preview_is_not_adopted()
    {
        await Prepare(); var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        File.WriteAllBytes(Source, input.Reverse().ToArray());
        Assert.ThrowsAny<IOException>(() => Tx.ImportOwnedData(requirement, Data, plan));
        Assert.False(Directory.Exists(Data)); Assert.Equal(input.Reverse().ToArray(), File.ReadAllBytes(Source));
    }

    [Fact]
    public async Task Existing_conflicting_receipt_is_retained_without_overwrite()
    {
        await Prepare(); var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        Tx.ImportOwnedData(requirement, Data, plan);
        var receipt = Path.Combine(Path.GetDirectoryName(plan.Destination)!, plan.OperationId + ".json");
        File.WriteAllText(receipt, "retained conflicting receipt");
        var fresh = Tx.PreviewOwnedData(requirement, Data, Source);
        Assert.ThrowsAny<IOException>(() => Tx.ImportOwnedData(requirement, Data, fresh));
        Assert.Equal("retained conflicting receipt", File.ReadAllText(receipt)); Assert.Equal(input, File.ReadAllBytes(plan.Destination));
    }

    [Theory]
    [InlineData("install")][InlineData("managed-ancestor")][InlineData("linked-store")]
    public async Task Unsafe_data_destination_refuses_before_child_store_creation(string fault)
    {
        await Prepare(); var dataRoot = Data;
        if (fault == "install") dataRoot = Install;
        if (fault == "managed-ancestor")
        {
            var other = Path.Combine(root, "other-game");
            await new PortableInstallationTransaction(other).InstallAsync(stage =>
            { File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("other")); return Task.CompletedTask; }, "other");
            dataRoot = Path.Combine(other, "nested-data");
        }
        if (fault == "linked-store") { Directory.CreateDirectory(Data + "-target"); Directory.CreateSymbolicLink(Data, Data + "-target"); }
        var error = Record.Exception(() => Tx.PreviewOwnedData(requirement, dataRoot, Source));
        Assert.True(error is IOException or InvalidDataException, error?.ToString());
        Assert.False(Directory.Exists(Path.Combine(dataRoot, "owned-game-data"))); Assert.Equal(input, File.ReadAllBytes(Source));
    }

    [Theory]
    [InlineData("Copying")][InlineData("DataCommitted")][InlineData("BeforeReceipt")]
    public async Task Injected_write_boundary_failure_retains_original_and_known_committed_copy(string boundary)
    {
        await Prepare(); var plan = Tx.PreviewOwnedData(requirement, Data, Source);
        Assert.Throws<IOException>(() => Tx.ImportOwnedData(requirement, Data, plan, boundary: p =>
        { if (p == boundary) throw new IOException("Synthetic write boundary failure"); }));
        Assert.Equal(input, File.ReadAllBytes(Source)); Assert.Empty(Directory.GetFiles(Data, "*.tmp", SearchOption.AllDirectories));
        if (boundary == "Copying") Assert.False(File.Exists(plan.Destination));
        else Assert.Equal(input, File.ReadAllBytes(plan.Destination));
    }

}
