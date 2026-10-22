using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Relicade.Core.Services;

namespace Relicade.Services;

internal sealed record FreeDataMember(string Name, long Length, string Sha256);
internal sealed record FreeDataArchive(Uri Url, long Length, string Sha256, FreeDataMember Data, FreeDataMember[] Notices);
internal sealed record FreeDataDownloadResult(string Source, string Archive, bool Reused);

// A compiled, opt-in free-data source. Catalog fields cannot supply URLs, paths or hashes.
internal static class FreedoomDataDownload
{
    internal static readonly FreeDataArchive Definition = new(
        new Uri("https://github.com/freedoom/freedoom/releases/download/v0.13.0/freedoom-0.13.0.zip"), 24143781,
        "3f9b264f3e3ce503b4fb7f6bdcb1f419d93c7b546f4df3e874dd878db9688f59",
        new("freedoom-0.13.0/freedoom2.wad", 28787748, "a8772e088847032510d97ba2312406a6998f21cbab44d4ff10696faa9c0ecd4b"),
        [new("freedoom-0.13.0/COPYING.txt", 1644, "7c62f2c520769c798774f416637eec4921e5f0aafdac1245ae7c8a8cf65fe102"),
         new("freedoom-0.13.0/CREDITS.txt", 15872, "72364333d295c598d75ef9553d3b3661ad0b60e27271c87b7637c74b2f46918a"),
         new("freedoom-0.13.0/CREDITS-MUSIC.txt", 9388, "2bb77677bbc4e587bba44ccb6514ee0f5397c7bb8ae9580b9188d3d6a57c0a31")]);
    static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static Task<FreeDataDownloadResult> DownloadAsync(IProgress<double>? progress, CancellationToken token) =>
        DownloadAsync(Client, Definition, Path.Combine(RelicadePaths.UserDataRoot, "free-game-downloads", "freedoom-0.13.0"), progress, token);

    internal static async Task<FreeDataDownloadResult> DownloadAsync(HttpClient client, FreeDataArchive definition,
        string directory, IProgress<double>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5)); token = deadline.Token;
        // The same nonrecovering authority protects cache roots and enclosing installations.
        // No cache overwrite, recovery or recursive cleanup is implicit in this action.
        using var authority = new PortableInstallationTransaction(directory).AcquireUnmanagedRemovalLease();
        PortableInstallationTransaction.CheckLinks(directory);
        Directory.CreateDirectory(directory);
        var archive = PortableInstallationTransaction.SafePath(directory, "freedoom-0.13.0.zip");
        var data = PortableInstallationTransaction.SafePath(directory, "freedoom2.wad");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            if (entry != archive && entry != data)
                throw new IOException("The free-data download folder contains pending or unknown files. They were preserved; inspect them before downloading again.");
        var reused = Exists(archive);
        if (!reused)
        {
            using var response = await GetResponseAsync(client, definition.Url, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != definition.Length)
                throw new InvalidDataException("The official archive size differs from this supported version. No data was installed.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await WriteVerifiedAsync(input, archive, definition.Length, definition.Sha256, progress, token).ConfigureAwait(false);
        }
        PortableInstallationTransaction.RequireRegularEvidenceFile(archive);
        await using var archiveFile = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        await VerifyAsync(archiveFile, definition.Length, definition.Sha256, token).ConfigureAwait(false);
        archiveFile.Position = 0;
        using var zip = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: true);
        // Check the retained legal material as well as the selected data. Other entries
        // are never extracted, interpreted as paths or executed.
        foreach (var notice in definition.Notices)
        {
            using var input = FindMember(zip, notice).Open();
            await VerifyAsync(input, notice.Length, notice.Sha256, token).ConfigureAwait(false);
        }
        var member = FindMember(zip, definition.Data);
        if (Exists(data))
        {
            PortableInstallationTransaction.RequireRegularEvidenceFile(data);
            await using var input = new FileStream(data, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            await VerifyAsync(input, definition.Data.Length, definition.Data.Sha256, token).ConfigureAwait(false);
        }
        else
        {
            using var input = member.Open();
            await WriteVerifiedAsync(input, data, definition.Data.Length, definition.Data.Sha256, null, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested(); progress?.Report(1);
        return new(data, archive, reused);
    }

    static bool Exists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    static ZipArchiveEntry FindMember(ZipArchive zip, FreeDataMember member)
    {
        var matches = zip.Entries.Where(entry => string.Equals(entry.FullName, member.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].FullName != member.Name || matches[0].Length != member.Length)
            throw new InvalidDataException("The free-data archive does not contain the exact supported data and notices.");
        return matches[0];
    }

    internal static async Task<HttpResponseMessage> GetResponseAsync(HttpClient client, Uri initial, CancellationToken token)
    {
        var current = initial;
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            if (current.Scheme != Uri.UriSchemeHttps || !current.IsDefaultPort || current.UserInfo.Length != 0 || current.Fragment.Length != 0 ||
                !(current.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || current.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("The free-data download redirected outside the supported official HTTPS hosts.");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location == null) throw new IOException("The free-data download redirect has no destination.");
                current = new Uri(current, location); continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new IOException("The free-data download exceeded its redirect limit.");
    }

    static async Task WriteVerifiedAsync(Stream input, string destination, long length, string sha256,
        IProgress<double>? progress, CancellationToken token)
    {
        var temporary = PortableInstallationTransaction.SafePath(Path.GetDirectoryName(destination)!, Guid.NewGuid().ToString("N") + ".part");
        var owned = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                owned = true;
                await TransferAsync(input, output, length, sha256, progress, token).ConfigureAwait(false);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); PortableInstallationTransaction.CheckLinks(destination);
            File.Move(temporary, destination, false); owned = false;
        }
        finally
        {
            if (owned)
            {
                PortableInstallationTransaction.RequireRegularEvidenceFile(temporary);
                File.Delete(temporary); // Only this operation's incomplete file, never a retained download.
            }
        }
    }

    static Task VerifyAsync(Stream input, long length, string sha256, CancellationToken token) =>
        TransferAsync(input, null, length, sha256, null, token);

    static async Task TransferAsync(Stream input, Stream? output, long length, string sha256,
        IProgress<double>? progress, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (read == 0) break;
            total = checked(total + read);
            if (total > length) throw new InvalidDataException("The free-data content exceeds the pinned size limit.");
            hash.AppendData(buffer, 0, read);
            if (output != null) await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            progress?.Report(total / (double)length);
        }
        if (total != length || Convert.ToHexStringLower(hash.GetHashAndReset()) != sha256)
            throw new InvalidDataException("The free-data content does not match this supported version. Existing files were preserved.");
    }
}
