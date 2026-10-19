using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Relicade.Core.Services;

/// <summary>Untrusted portable bytes are extracted only into a new, private generation.</summary>
internal static class PortableInstallationExtraction
{
    sealed class Expansion { public long Bytes; public int Entries; }
    public static Task StageAsync(string download, string stage, string name, GameInstallationOptions options, CancellationToken token)
        => StageCoreAsync(download, stage, name, options, token, new Expansion(), 0);
    static async Task StageCoreAsync(string download, string stage, string name, GameInstallationOptions options, CancellationToken token, Expansion expansion, int depth)
    {
        var unpack = Path.Combine(Path.GetDirectoryName(stage)!, "unpack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unpack);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(download);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            // Preflight the ENTIRE archive before writes, including links and aliases.
            foreach (var e in zip.Entries)
            {
                var p = Normalize(e.FullName); if (!paths.Add(p)) throw new InvalidDataException("Case-colliding or duplicate archive entries.");
                if (((e.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Linked portable archive entries are unsupported.");
                expanded = checked(expanded + e.Length); Reserve(e.Length, unpack, options, expansion);
            }
            foreach (var e in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var dest = PortableInstallationTransaction.SafePath(unpack, Normalize(e.FullName));
                if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await using var input = e.Open();
                await using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await CopyBounded(input, output, e.Length, options.MaximumExpandedBytes, token).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows())
                {
                    var mode = (e.ExternalAttributes >> 16) & 0x1FF;
                    if (mode != 0) File.SetUnixFileMode(dest, (UnixFileMode)mode);
                }
            }
        }
        else if (GameInstallationService.IsTarballAsset(name) || name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            using var stream = File.OpenRead(download);
            using var archive = name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? ArchiveFactory.OpenArchive(stream) : null;
            using var reader = archive != null ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(stream);
            while (reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                var e = reader.Entry; if (e.LinkTarget != null) throw new InvalidDataException("Linked portable archive entries are unsupported.");
                if (e.IsDirectory && e.Key is "." or "./") continue;
                var p = Normalize(e.Key ?? ""); if (!paths.Add(p)) throw new InvalidDataException("Duplicate or case-colliding archive entry.");
                var dest = PortableInstallationTransaction.SafePath(unpack, p);
                expanded = checked(expanded + e.Size); Reserve(e.Size, unpack, options, expansion);
                if (e.IsDirectory) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = reader.OpenEntryStream();
                await using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await CopyBounded(input, output, e.Size, options.MaximumExpandedBytes - (expanded - e.Size), token).ConfigureAwait(false);
            }
        }
        else if (GameInstallationService.IsSingleFileExecutableAsset(name))
        {
            PortableInstallationTransaction.ValidateRelative(name);
            if (name.Contains('/')) throw new InvalidDataException("A binary asset must have a filename, not a path.");
            var info = new FileInfo(download); Reserve(info.Length, unpack, options, expansion);
            File.Copy(download, Path.Combine(unpack, name));
        }
        else throw new InvalidOperationException("Unsupported release asset type; use its platform adapter.");
        token.ThrowIfCancellationRequested();
        var nested = Directory.GetFiles(unpack, "*", SearchOption.AllDirectories);
        if (nested.Length == 1 && nested[0].EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            if (depth >= 2) throw new InvalidDataException("Nested archive wrapper limit exceeded.");
            await StageCoreAsync(nested[0], stage, Path.GetFileName(nested[0]), options, token, expansion, depth + 1).ConfigureAwait(false);
            return;
        }
        // Unwrap an archive wrapper ONLY in staging. No live-tree flatten/merge.
        var source = unpack;
        while (Directory.GetFiles(source).Length == 0 && Directory.GetDirectories(source) is { Length: 1 } dirs)
        {
            if (dirs[0].EndsWith(".app", StringComparison.OrdinalIgnoreCase)) break;
            source = dirs[0];
        }
        foreach (var p in Directory.GetFileSystemEntries(source))
        {
            var dest = PortableInstallationTransaction.SafePath(stage, Path.GetFileName(p));
            PortableInstallationTransaction.CheckLinks(p);
            if (Directory.Exists(p)) Directory.Move(p, dest); else File.Move(p, dest);
        }
        if (!OperatingSystem.IsWindows())
            foreach (var executable in GameInstallationService.FindExecutableCandidates(stage, SearchOption.AllDirectories, null, out _))
                if (File.Exists(executable)) File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        options.ExtractProgress?.Report(0.99);
    }
    static string Normalize(string p)
    {
        p = p.Replace('\\', '/').TrimEnd('/');
        // tar convention, not traversal: accept only an explicit leading './'.
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        PortableInstallationTransaction.ValidateRelative(p); return p;
    }
    static void Reserve(long bytes, string stage, GameInstallationOptions o, Expansion expansion)
    {
        expansion.Bytes = checked(expansion.Bytes + bytes); expansion.Entries++;
        if (bytes < 0 || expansion.Bytes > o.MaximumExpandedBytes || expansion.Entries > o.MaximumFileCount) throw new IOException("Portable expansion exceeds the configured space/file budget.");
        var available = new DriveInfo(Path.GetFullPath(stage)).AvailableFreeSpace;
        if (available < bytes) throw new IOException("Not enough free space to stage the portable generation while retaining the previous one.");
    }
    static async Task CopyBounded(Stream input, Stream output, long expected, long budget, CancellationToken token)
    {
        var buffer = new byte[81920]; long total = 0; int n;
        while ((n = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total = checked(total + n); if (total > budget || total > expected) throw new InvalidDataException("Archive expansion differs from its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, n), token).ConfigureAwait(false);
        }
        if (total != expected) throw new InvalidDataException("Archive entry is truncated.");
        await output.FlushAsync(token).ConfigureAwait(false);
    }
}
