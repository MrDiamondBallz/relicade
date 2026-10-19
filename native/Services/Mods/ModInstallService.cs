using Relicade.Services.Mods.Providers.Thunderstore;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Relicade.Services.Mods;

public sealed class ModInstallService
{
    private readonly ModProviderRegistry _registry;
    private readonly InstalledModsStore _store = new();
    internal Action<string>? TransactionBoundary { get; set; }

    public ModInstallService(ModProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string GetModsDirectory(string installRoot, string modsPath)
    {
        if (Path.IsPathRooted(modsPath) || modsPath.Contains(':'))
            throw new InvalidDataException("Mods path must be relative to the installation.");
        var normalized = GameModsConfig.NormalizePath(modsPath);
        if (normalized.Length == 0)
            throw new InvalidOperationException("Mods path is not configured.");

        if (normalized.Split('/')[0].StartsWith(".relicade-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mods path overlaps launcher state.");

        return ModInstallTransaction.SafePath(installRoot, normalized);
    }

    public async Task<InstalledModRecord> InstallAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IModProvider provider,
        ModDownloadFile? selectedFile = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(provider);
        ValidateSelection(package, provider, selectedFile);

        if (selectedFile != null)
            package = Providers.GameBanana.GameBananaModProvider.WithSelectedFile(package, selectedFile);

        if (package.LatestVersion == null || string.IsNullOrWhiteSpace(package.LatestVersion.DownloadUrl))
            throw new InvalidOperationException($"Package '{package.FullName}' has no downloadable version.");

        cancellationToken.ThrowIfCancellationRequested();
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        var document = InstalledModsStore.LoadCore(installRoot);
        var modsDir = GetModsDirectory(installRoot, modsPath);
        var operation = ModInstallTransaction.CreateOperation(installRoot);
        try
        {
            await using var archiveStream = await provider.DownloadAsync(package.LatestVersion, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var download = ModInstallTransaction.SafePath(operation, "download.archive");
            await using (var saved = new FileStream(download, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await archiveStream.CopyToAsync(saved, cancellationToken).ConfigureAwait(false);
                saved.Flush(flushToDisk: true);
            }
            var staged = ModInstallTransaction.SafePath(operation, "staged");
            var wrap = GameModsConfig.IsFolderPerMod(modsLayout)
                ? GameModsConfig.ResolveWrapFolderName(selectedFile?.FileName, package.Name, package.Id) : null;
            List<string> files;
            using (var downloaded = File.OpenRead(download))
                files = ExtractPayloadFiles(downloaded, staged, provider.GetArchiveMetadataFileNames(), modsLayout, wrap);
            if (files.Count == 0) throw new InvalidDataException("The mod archive contains no supported payload files.");
            cancellationToken.ThrowIfCancellationRequested();
            var record = new InstalledModRecord
            {
                Provider = package.ProviderId, SourceKey = package.SourceKey, Id = package.Id,
                FullName = package.FullName, Owner = package.Owner, Name = package.Name,
                Version = package.LatestVersion.Version, DownloadFileId = selectedFile?.Id,
                DownloadFileName = selectedFile?.FileName, Files = files,
            };
            var replaced = document.Mods.Where(m => ModCatalogListBuilder.RecordMatchesPackageFile(m, package, selectedFile?.Id)).ToList();
            // The commit is deliberately noninterruptible after this last safe cancellation boundary.
            TransactionBoundary?.Invoke("Staged");
            cancellationToken.ThrowIfCancellationRequested();
            ModInstallTransaction.Commit(installRoot, Path.GetRelativePath(installRoot, modsDir).Replace('\\', '/'), operation, document, replaced, record, TransactionBoundary);
            return record;
        }
        catch
        {
            if (Directory.Exists(operation) && !File.Exists(Path.Combine(operation, "journal.json")))
                ModInstallTransaction.Cleanup(operation); // No Prepared journal means no live payload writes.
            throw;
        }
    }

    static void ValidateSelection(ModPackage package, IModProvider provider, ModDownloadFile? selectedFile)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(provider);
        if (!string.Equals(package.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected provider does not own this package.");
        if (selectedFile != null && !package.DownloadFiles.Any(f => string.Equals(f.Id, selectedFile.Id, StringComparison.Ordinal)
            && string.Equals(f.DownloadUrl, selectedFile.DownloadUrl, StringComparison.Ordinal)))
            throw new InvalidOperationException("The selected download file is not available in this package; choose a file explicitly.");
    }

    /// <summary>
    /// Direct Thunderstore requirements that are not already in the install sidecar,
    /// using catalog names when available.
    /// </summary>
    public IReadOnlyList<string> ListMissingDirectDependencies(
        InstalledModsDocument document,
        ModPackage package,
        IReadOnlyList<ModPackage>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(package);

        var deps = package.LatestVersion?.Dependencies ?? [];
        if (deps.Count == 0)
            return [];

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in deps)
        {
            if (!ThunderstoreModProvider.TryParseDependencyString(dep, out var depFullName, out _))
                continue;

            if (!seen.Add(depFullName))
                continue;

            if (_store.FindByFullName(document, package.ProviderId, package.SourceKey, depFullName) != null)
                continue;

            missing.Add(ResolveDependencyDisplayName(package, depFullName, catalog));
        }

        return missing;
    }

    static string ResolveDependencyDisplayName(
        ModPackage parent,
        string depFullName,
        IReadOnlyList<ModPackage>? catalog)
    {
        var catalogMatch = catalog?.FirstOrDefault(p =>
            string.Equals(p.ProviderId, parent.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.SourceKey, parent.SourceKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.FullName, depFullName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(catalogMatch?.Name))
            return catalogMatch.Name;

        if (ThunderstoreModProvider.TrySplitPackageFullName(depFullName, out _, out var name) &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return depFullName;
    }

    public async Task InstallWithDependenciesAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IReadOnlyList<ModPackage> catalog,
        IModProvider provider,
        ModDownloadFile? selectedFile = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null,
        bool installDependencies = true)
    {
        ValidateSelection(package, provider, selectedFile);
        var document = _store.Load(installRoot);
        var plan = new List<ModPackage>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Resolve(ModPackage item, bool root)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"{item.ProviderId}|{item.SourceKey}|{item.FullName}";
            if (visiting.Contains(key)) throw new InvalidOperationException("Mod dependency cycle detected.");
            if (!visited.Add(key)) return;
            visiting.Add(key);
            if (root && selectedFile != null)
                item = Providers.GameBanana.GameBananaModProvider.WithSelectedFile(item, selectedFile);
            item = await EnsureDownloadableAsync(item, provider, cancellationToken).ConfigureAwait(false);
            if (item.LatestVersion == null || string.IsNullOrWhiteSpace(item.LatestVersion.DownloadUrl))
                throw new InvalidOperationException($"Mod '{item.FullName}' is unavailable for download.");
            foreach (var dependency in installDependencies ? item.LatestVersion.Dependencies : [])
            {
                if (!ThunderstoreModProvider.TryParseDependencyString(dependency, out var fullName, out var version))
                    throw new InvalidDataException("Invalid mod dependency identity.");
                var installed = _store.FindByFullName(document, item.ProviderId, item.SourceKey, fullName);
                if (installed != null)
                {
                    if (version != null && !string.Equals(installed.Version, version, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Installed dependency '{fullName}' has a different version; explicit resolution is required.");
                    continue;
                }
                var child = catalog.FirstOrDefault(p => string.Equals(p.ProviderId, item.ProviderId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.SourceKey, item.SourceKey, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.FullName, fullName, StringComparison.OrdinalIgnoreCase))
                    ?? CreateThunderstoreDependencyStub(item, fullName)
                    ?? throw new InvalidOperationException($"Dependency '{fullName}' is unavailable.");
                child = await EnsureDownloadableAsync(child, provider, cancellationToken).ConfigureAwait(false);
                if (version != null && !string.Equals(child.LatestVersion?.Version, version, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Dependency '{fullName}' requires an unavailable version.");
                await Resolve(child, false).ConfigureAwait(false);
            }
            visiting.Remove(key);
            var existing = ModCatalogListBuilder.FindMatchingRecord(document, item, root ? selectedFile?.Id : null);
            var target = root && !string.IsNullOrWhiteSpace(selectedFile?.Version) ? selectedFile.Version : item.LatestVersion.Version;
            if (existing == null || !string.Equals(existing.Version, target, StringComparison.OrdinalIgnoreCase)) plan.Add(item);
        }
        await Resolve(package, true).ConfigureAwait(false);
        var committed = new List<string>();
        try
        {
            foreach (var item in plan)
            {
                await InstallAsync(installRoot, modsPath, item, provider,
                    ModCatalogListBuilder.PackagesMatch(item, package) ? selectedFile : null,
                    progress, cancellationToken, modsLayout).ConfigureAwait(false);
                committed.Add(item.FullName);
            }
        }
        catch (OperationCanceledException ex)
        {
            ex.Data["CommittedMods"] = committed.ToArray();
            throw;
        }
        catch (Exception ex) when (committed.Count > 0)
        {
            throw new IOException($"Mod installation failed; already committed dependencies/files remain installed: {string.Join(", ", committed)}. The failed replacement retained its previous generation or recovery evidence.", ex);
        }
    }

    /// <summary>
    /// Installs each selected download file as its own sidecar record.
    /// Null or empty <paramref name="selectedFiles"/> keeps the single-file (Thunderstore) path.
    /// </summary>
    public async Task InstallSelectedFilesAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IReadOnlyList<ModPackage> catalog,
        IModProvider provider,
        IReadOnlyList<ModDownloadFile>? selectedFiles,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null,
        bool installDependencies = true)
    {
        if (selectedFiles == null || selectedFiles.Count == 0)
        {
            await InstallWithDependenciesAsync(
                    installRoot,
                    modsPath,
                    package,
                    catalog,
                    provider,
                    selectedFile: null,
                    progress,
                    cancellationToken,
                    modsLayout,
                    installDependencies)
                .ConfigureAwait(false);
            return;
        }

        foreach (var file in selectedFiles)
        {
            await InstallWithDependenciesAsync(
                    installRoot,
                    modsPath,
                    package,
                    catalog,
                    provider,
                    file,
                    progress,
                    cancellationToken,
                    modsLayout,
                    installDependencies)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Listing stubs have empty download URLs; enrich Thunderstore packages via experimental API.
    /// </summary>
    private static async Task<ModPackage> EnsureDownloadableAsync(
        ModPackage package,
        IModProvider provider,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(package.LatestVersion?.DownloadUrl))
            return package;

        if (provider is not ThunderstoreModProvider thunderstore)
            return package;

        var enriched = await thunderstore
            .EnrichForInstallAsync(package, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(enriched.LatestVersion?.DownloadUrl))
        {
            throw new InvalidOperationException(
                $"Package '{package.FullName}' could not be resolved for download.");
        }

        return enriched;
    }

    internal static ModPackage? CreateThunderstoreDependencyStub(ModPackage parent, string depFullName)
    {
        if (!string.Equals(parent.ProviderId, ModProviderIds.Thunderstore, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!ThunderstoreModProvider.TrySplitPackageFullName(depFullName, out var owner, out var name))
            return null;

        return new ModPackage
        {
            ProviderId = parent.ProviderId,
            SourceKey = parent.SourceKey,
            SourceDisplayLabel = parent.SourceDisplayLabel,
            Id = depFullName,
            Owner = owner,
            Name = name,
            FullName = depFullName,
            PackagePageUrl = ThunderstoreModProvider.BuildPackagePageUrl(parent.SourceKey, owner, name),
            LatestVersion = new ModPackageVersion
            {
                Version = string.Empty,
                DownloadUrl = string.Empty,
            },
        };
    }

    public bool Uninstall(string installRoot, string modsPath, string providerId, string packageId)
    {
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        var document = InstalledModsStore.LoadCore(installRoot);
        var records = document.Mods.Where(m =>
            string.Equals(m.Provider, providerId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(m.Id, packageId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(m.FullName, packageId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (records.Count == 0)
            return false;

        return UninstallRecords(installRoot, modsPath, document, records);
    }

    public bool UninstallMatching(string installRoot, string modsPath, ModPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        var document = InstalledModsStore.LoadCore(installRoot);
        var records = ModCatalogListBuilder.FindMatchingRecords(document, package);
        if (records.Count == 0)
            return false;

        return UninstallRecords(installRoot, modsPath, document, records);
    }

    public bool UninstallMatchingFile(
        string installRoot,
        string modsPath,
        ModPackage package,
        string? downloadFileId)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        var document = InstalledModsStore.LoadCore(installRoot);
        var record = ModCatalogListBuilder.FindMatchingRecord(document, package, downloadFileId);
        if (record == null)
            return false;

        return UninstallRecords(installRoot, modsPath, document, [record]);
    }

    public bool UninstallMatchingRecord(string installRoot, string modsPath, ModPackage package, InstalledModRecord selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        using var writer = ModInstallTransaction.Acquire(installRoot);
        ModInstallTransaction.Recover(installRoot);
        var document = InstalledModsStore.LoadCore(installRoot);
        var matches = document.Mods.Where(m => ModCatalogListBuilder.RecordMatchesPackage(m, package)
            && string.Equals(m.DownloadFileId, selected.DownloadFileId, StringComparison.Ordinal)
            && string.Equals(m.DownloadFileName, selected.DownloadFileName, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1) throw new InvalidOperationException("Installed file identity changed or is ambiguous; reload before removing it.");
        return UninstallRecords(installRoot, modsPath, document, matches);
    }

    private bool UninstallRecords(
        string installRoot,
        string modsPath,
        InstalledModsDocument document,
        IReadOnlyList<InstalledModRecord> records)
    {
        if (records.Count == 0)
            return false;

        var modsDir = GetModsDirectory(installRoot, modsPath);
        var operation = ModInstallTransaction.CreateOperation(installRoot);
        try
        {
            ModInstallTransaction.Commit(installRoot, Path.GetRelativePath(installRoot, modsDir).Replace('\\', '/'),
                operation, document, records, null, TransactionBoundary);
            return true;
        }
        catch
        {
            if (Directory.Exists(operation) && !File.Exists(Path.Combine(operation, "journal.json")))
                ModInstallTransaction.Cleanup(operation);
            throw;
        }
    }

    public async Task UpdateAsync(
        string installRoot,
        string modsPath,
        ModPackage package,
        IModProvider provider,
        ModDownloadFile? selectedFile = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? modsLayout = null)
    {
        await InstallAsync(
                installRoot,
                modsPath,
                package,
                provider,
                selectedFile,
                progress,
                cancellationToken,
                modsLayout)
            .ConfigureAwait(false);
    }

    public InstalledModsDocument LoadInstalled(string installRoot) => _store.Load(installRoot);

    /// <summary>
    /// Extracts non-metadata entries from a zip, 7z, or rar archive into <paramref name="modsDir"/>.
    /// Returns relative paths that were written (forward-slash normalized).
    /// When <paramref name="modsLayout"/> is folderPerMod and the archive has root-level payload
    /// files, all payload paths are prefixed with <paramref name="wrapFolderName"/>.
    /// </summary>
    public static List<string> ExtractPayloadFiles(
        Stream archiveStream,
        string modsDir,
        IReadOnlySet<string> metadataFileNames,
        string? modsLayout = null,
        string? wrapFolderName = null)
    {
        Directory.CreateDirectory(modsDir);
        var installed = new List<string>();
        var modsRootFull = Path.GetFullPath(modsDir);

        using var archive = ArchiveFactory.OpenArchive(archiveStream);
        var payloadRelatives = new List<string>();

        foreach (var entry in archive.Entries)
        {
            var attributes = Convert.ToInt32(entry.Attrib);
            if (!string.IsNullOrEmpty(entry.LinkTarget) ||
                (attributes & unchecked((int)0xF0000000)) == unchecked((int)0xA0000000) ||
                (attributes & 0xF000) == 0xA000 || (attributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked mod archive entries are not supported.");
            if (entry.IsDirectory)
                continue;

            var key = entry.Key ?? string.Empty;
            var relative = key.Replace('\\', '/');
            if (relative.Length == 0)
                continue;

            ModInstallTransaction.SafePath(modsDir, relative);
            if (payloadRelatives.Contains(relative, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Duplicate mod archive destination.");
            if (payloadRelatives.Count >= 10000 || entry.Size > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Mod archive exceeds extraction limits.");

            // Skip host metadata files only at the package root.
            if (!relative.Contains('/') && metadataFileNames.Contains(relative))
                continue;

            payloadRelatives.Add(relative);
        }

        var shouldWrap = GameModsConfig.IsFolderPerMod(modsLayout) &&
                         payloadRelatives.Any(relative => !relative.Contains('/'));
        var prefix = shouldWrap
            ? GameModsConfig.SanitizeFolderName(wrapFolderName)
            : string.Empty;
        if (shouldWrap && prefix.Length == 0)
            prefix = "mod";

        var payloadSet = new HashSet<string>(payloadRelatives, StringComparer.OrdinalIgnoreCase);
        var extractOptions = new ExtractionOptions
        {
            Overwrite = true,
            ExtractFullPath = false,
        };

        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory)
                    continue;

                if (!TryWriteModPayloadEntry(
                        reader.Entry.Key,
                        payloadSet,
                        prefix,
                        modsDir,
                        modsRootFull,
                        destination => reader.WriteEntryToFile(destination, extractOptions),
                        installed))
                {
                    continue;
                }
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory)
                    continue;

                if (!TryWriteModPayloadEntry(
                        entry.Key,
                        payloadSet,
                        prefix,
                        modsDir,
                        modsRootFull,
                        destination => entry.WriteToFile(destination, extractOptions),
                        installed))
                {
                    continue;
                }
            }
        }

        return installed;
    }

    static bool TryWriteModPayloadEntry(
        string? entryKey,
        HashSet<string> payloadSet,
        string prefix,
        string modsDir,
        string modsRootFull,
        Action<string> write,
        List<string> installed)
    {
        var relative = (entryKey ?? string.Empty).Replace('\\', '/');
        if (relative.Length == 0 || !payloadSet.Contains(relative))
            return false;

        var destRelative = prefix.Length > 0 ? $"{prefix}/{relative}" : relative;

        var destination = ModInstallTransaction.SafePath(modsDir, destRelative);

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        write(destination);
        installed.Add(destRelative);
        return true;
    }
}
