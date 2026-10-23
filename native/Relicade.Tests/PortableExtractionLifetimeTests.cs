using System.Formats.Tar;
using System.IO.Compression;
using Relicade.Core.Services;
namespace Relicade.Tests;

public sealed class PortableExtractionLifetimeTests
{
    [Theory][InlineData("zip")][InlineData("tar.gz")][InlineData("AppImage")][InlineData("nested")]
    public async Task Successful_staging_removes_only_its_consumed_private_wrappers(string format)
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-extraction-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var stage = Path.Combine(root, "candidate"); Directory.CreateDirectory(stage);
            var retained = Path.Combine(root, "unpack-unrelated"); Directory.CreateDirectory(retained);
            File.WriteAllText(Path.Combine(retained, "keep.txt"), "unrelated evidence");
            var payload = PortableInstallationFixtures.NativePayload("inert fixture, never launched");
            var name = format == "nested" ? "wrapper.zip" : "game." + format;
            var download = Path.Combine(root, name);
            if (format == "AppImage") File.WriteAllBytes(download, payload);
            else if (format == "tar.gz") File.WriteAllBytes(download, Tar(payload));
            else
            {
                using var zip = ZipFile.Open(download, ZipArchiveMode.Create);
                using var output = zip.CreateEntry(format == "nested" ? "outer/game.tar.gz" : "outer/inner/game").Open();
                output.Write(format == "nested" ? Tar(payload) : payload);
            }
            var original = File.ReadAllBytes(download);
            await PortableInstallationExtraction.StageAsync(download, stage, name, new(), CancellationToken.None);
            Assert.Equal(payload, File.ReadAllBytes(Path.Combine(stage, format == "AppImage" ? name : "game")));
            Assert.Equal(original, File.ReadAllBytes(download));
            Assert.Equal(new[] { retained }, Directory.GetDirectories(root, "unpack-*"));
            Assert.Equal("unrelated evidence", File.ReadAllText(Path.Combine(retained, "keep.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Failed_extraction_retains_its_private_evidence_without_sweeping_other_operations()
    {
        var root = Path.Combine(Path.GetTempPath(), "relicade-extraction-failure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var stage = Path.Combine(root, "candidate"); Directory.CreateDirectory(stage);
            var download = Path.Combine(root, "bad.zip");
            using (var zip = ZipFile.Open(download, ZipArchiveMode.Create))
            { using var output = zip.CreateEntry("../outside").Open(); output.Write([1, 2, 3]); }
            var bytes = File.ReadAllBytes(download);
            await Assert.ThrowsAnyAsync<Exception>(() => PortableInstallationExtraction.StageAsync(download, stage, "bad.zip", new(), CancellationToken.None));
            Assert.Single(Directory.GetDirectories(root, "unpack-*")); Assert.Empty(Directory.GetFileSystemEntries(stage));
            Assert.Equal(bytes, File.ReadAllBytes(download)); Assert.False(File.Exists(Path.Combine(root, "outside")));
        }
        finally { Directory.Delete(root, true); }
    }
    static byte[] Tar(byte[] payload)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        using (var body = new MemoryStream(payload))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "outer/inner/game") { DataStream = body });
        return bytes.ToArray();
    }
}
