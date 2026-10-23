using System.IO.Compression;
using System.Security.Cryptography;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class PortableExtractionSpaceTests
{
    [Fact]
    public async Task Zip_preflight_requires_the_whole_archive_to_fit_before_any_payload_write()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-zip-space-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var retained = Path.Combine(root, "retained-user-data"); Directory.CreateDirectory(retained);
        File.WriteAllText(Path.Combine(retained, "save.dat"), "authored retained save");
        var stage = Path.Combine(root, "candidate"); Directory.CreateDirectory(stage);
        var archive = Path.Combine(root, "authored.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (var name in new[] { "first.bin", "second.bin" })
            { using var output = zip.CreateEntry(name).Open(); output.Write(new byte[96]); }
        var original = SHA256.HashData(File.ReadAllBytes(archive));
        var save = File.ReadAllBytes(Path.Combine(retained, "save.dat"));
        var observations = 0;
        // No real low-space volume or payload acquisition: this scoped observation
        // supplies 128 available bytes; each entry fits, but 192 total bytes do not.
        var error = await Assert.ThrowsAsync<IOException>(() => PortableInstallationExtraction.StageAsync(
            archive, stage, "authored.zip", new(), CancellationToken.None, path =>
            { Assert.StartsWith(root + Path.DirectorySeparatorChar, path); observations++; return 128; }));
        Assert.Contains("space", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(observations > 0); Assert.Empty(Directory.GetFileSystemEntries(stage));
        foreach (var unpack in Directory.GetDirectories(root, "unpack-*"))
            Assert.Empty(Directory.GetFileSystemEntries(unpack));
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(archive)));
        Assert.Equal(save, File.ReadAllBytes(Path.Combine(retained, "save.dat")));
        // Retain the authored input and all outcomes; no cleanup or process launch.
    }

    [Theory][InlineData(192L)][InlineData(193L)]
    public async Task Exact_fit_and_extra_space_stage_each_entry_without_double_charging_budgets(long available)
    {
        var (root, stage, archive) = Fixture();
        var source = File.ReadAllBytes(archive);
        await PortableInstallationExtraction.StageAsync(archive, stage, "authored.zip",
            new() { MaximumExpandedBytes = 192, MaximumFileCount = 2 }, CancellationToken.None, _ => available);
        Assert.Equal(new byte[96], File.ReadAllBytes(Path.Combine(stage, "first.bin")));
        Assert.Equal(new byte[96], File.ReadAllBytes(Path.Combine(stage, "second.bin")));
        Assert.Equal(source, File.ReadAllBytes(archive));
    }

    [Theory][InlineData("bytes")][InlineData("entries")][InlineData("negative")][InlineData("unavailable")]
    public async Task Safety_limits_and_uncertain_space_refuse_before_payload_writes(string reason)
    {
        var (root, stage, archive) = Fixture(); var source = File.ReadAllBytes(archive);
        var options = new GameInstallationOptions { MaximumExpandedBytes = reason == "bytes" ? 191 : 192,
            MaximumFileCount = reason == "entries" ? 1 : 2 };
        await Assert.ThrowsAnyAsync<IOException>(() => PortableInstallationExtraction.StageAsync(archive, stage, "authored.zip", options,
            CancellationToken.None, _ => reason == "unavailable" ? throw new IOException("Authored unavailable observation") : reason == "negative" ? -1 : 192));
        AssertNoPayload(root, stage); Assert.Equal(source, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task Declared_zip64_size_overflow_refuses_before_payload_open_or_large_allocation()
    {
        var (root, stage, archive) = Fixture();
        // Only central-directory declarations change; the actual authored file
        // remains a few hundred bytes. No huge buffer or real disk fill is used.
        WithZip64Lengths(archive, long.MaxValue - 1, 3);
        using (var declared = ZipFile.OpenRead(archive))
            Assert.Equal(new[] { long.MaxValue - 1, 3L }, declared.Entries.Select(e => e.Length));
        var source = File.ReadAllBytes(archive);
        await Assert.ThrowsAsync<OverflowException>(() => PortableInstallationExtraction.StageAsync(archive, stage, "authored.zip",
            new() { MaximumExpandedBytes = long.MaxValue }, CancellationToken.None, _ => long.MaxValue));
        AssertNoPayload(root, stage); Assert.Equal(source, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task Insufficient_zip_space_does_not_reach_commit_or_change_current_generation_data_or_old_history()
    {
        var (root, stage, archive) = Fixture(); var live = Path.Combine(root, "live");
        var transaction = new PortableInstallationTransaction(live);
        await transaction.InstallAsync(path =>
        { File.WriteAllBytes(Path.Combine(path, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("old")); return Task.CompletedTask; }, "old");
        File.WriteAllText(Path.Combine(live, "LastPlayed.txt"), "current authored save");
        var before = Snapshot(root); var manifest = File.ReadAllBytes(Path.Combine(live, PortableInstallationTransaction.ManifestFileName));
        var boundaries = new List<string>();
        var update = new PortableInstallationTransaction(live, new() { TransactionBoundary = boundaries.Add });
        var error = await Assert.ThrowsAsync<IOException>(() => update.InstallAsync(path =>
            PortableInstallationExtraction.StageAsync(archive, path, "authored.zip", new(), CancellationToken.None, _ => 128), "new"));
        Assert.Contains("192", error.Message); Assert.Contains("128", error.Message);
        Assert.DoesNotContain("Staged", boundaries); Assert.DoesNotContain("Prepared", boundaries); Assert.DoesNotContain("Committed", boundaries);
        var after = Snapshot(root); foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(live, PortableInstallationTransaction.ManifestFileName)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(live, "version.txt")));
        Assert.Empty(Directory.GetFiles(update.OperationsPath, "*.bin", SearchOption.AllDirectories));
        // The newly created failed candidate/history is retained, never swept.
    }

    static (string Root, string Stage, string Archive) Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-zip-space-controls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var stage = Path.Combine(root, "candidate"); Directory.CreateDirectory(stage);
        var archive = Path.Combine(root, "authored.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (var name in new[] { "first.bin", "second.bin" })
            { using var output = zip.CreateEntry(name, CompressionLevel.NoCompression).Open(); output.Write(new byte[96]); }
        return (root, stage, archive);
    }
    static void AssertNoPayload(string root, string stage)
    {
        Assert.Empty(Directory.GetFileSystemEntries(stage));
        foreach (var unpack in Directory.GetDirectories(root, "unpack-*")) Assert.Empty(Directory.GetFileSystemEntries(unpack));
    }
    static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => File.GetLastWriteTimeUtc(path).Ticks + "|" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    static void WithZip64Lengths(string archive, params long[] lengths)
    {
        var original = File.ReadAllBytes(archive); var end = original.Length - 22;
        Assert.Equal(0x06054b50u, BitConverter.ToUInt32(original, end));
        var offset = checked((int)BitConverter.ToUInt32(original, end + 16));
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(original, 0, offset); var cursor = offset;
        foreach (var length in lengths)
        {
            Assert.Equal(0x02014b50u, BitConverter.ToUInt32(original, cursor));
            var nameLength = BitConverter.ToUInt16(original, cursor + 28);
            Assert.Equal(0, BitConverter.ToUInt16(original, cursor + 30)); Assert.Equal(0, BitConverter.ToUInt16(original, cursor + 32));
            var header = original.AsSpan(cursor, 46).ToArray();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), uint.MaxValue);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 12);
            writer.Write(header); writer.Write(original, cursor + 46, nameLength);
            writer.Write((ushort)1); writer.Write((ushort)8); writer.Write(length);
            cursor += 46 + nameLength;
        }
        Assert.Equal(end, cursor);
        var endRecord = original.AsSpan(end).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(endRecord.AsSpan(12), checked((uint)(output.Length - offset)));
        writer.Write(endRecord); File.WriteAllBytes(archive, output.ToArray());
    }
}
