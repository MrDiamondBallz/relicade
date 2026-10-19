using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Relicade.Services.Credentials;

namespace Relicade.Services;

public class FileSettingsStore : ISettingsStore, ISettingsRecoveryState, ICredentialSettingsStore
{
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _settingsPath;
    private readonly object _gate;
    private AppSettings _current;
    private bool _hasSeenDocument;
    public CredentialSession Credentials { get; }
    public bool HasLegacyCredentials { get; private set; }
    public bool IsReadOnly => ReadError != null || HasLegacyCredentials;
    public Exception? ReadError { get; private set; }

    public FileSettingsStore(string? settingsPath = null, CredentialSession? credentials = null)
    {
        Credentials = credentials ?? CredentialVaultProvider.Session;
        _settingsPath = Path.GetFullPath(settingsPath ?? RelicadePaths.SettingsJsonPath);
        _gate = PathLocks.GetOrAdd(_settingsPath, _ => new object());
        _current = CreateDefault();
        Load();
    }

    public AppSettings Current => _current;

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                _current = ReadFromDisk();
                ReadError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                // Defaults are only a read-only display fallback. Never discard the last good in-memory document.
                ReadError = ex;
            }
            Credentials.Attach(_current);
            if (ReadError == null) Credentials.InitializeAsync().GetAwaiter().GetResult();
            return _current;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (ReadError is { } unresolved) throw new SettingsRecoveryRequiredException(unresolved);
            if (HasLegacyCredentials) throw new LegacyCredentialCleanupRequiredException();
            var directory = Path.GetDirectoryName(_settingsPath)!;
            // Validate before creating anything, and again inside the interprocess writer lease.
            ValidateExisting();
            Directory.CreateDirectory(directory);
            using var lease = AcquireWriteLease();
            ValidateExisting();
            settings.EnsureInitialized();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
            WriteAtomic(bytes);
            _hasSeenDocument = true;
            _current = settings;
            Credentials.Attach(_current);
        }
    }

    private void ValidateExisting()
    {
        try
        {
            _ = ReadFromDisk();
            if (HasLegacyCredentials) throw new LegacyCredentialCleanupRequiredException();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            ReadError = ex;
            throw new SettingsRecoveryRequiredException(ex);
        }
    }

    private FileStream AcquireWriteLease()
    {
        // Never remove the lock file: every cooperating process must lock the same inode.
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(_settingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 50) { Thread.Sleep(40); }
        }
    }

    private void WriteAtomic(byte[] bytes)
    {
        var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_settingsPath)) File.Replace(temporary, _settingsPath, null);
            else File.Move(temporary, _settingsPath);
        }
        finally
        {
            // Only our own temporary file; never remove another writer's work or the original.
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private AppSettings ReadFromDisk()
    {
        string json;
        try
        {
            using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            json = reader.ReadToEnd();
            _hasSeenDocument = true;
        }
        catch (FileNotFoundException) when (!_hasSeenDocument && ReadError == null) { return CreateDefault(); }
        catch (DirectoryNotFoundException) when (!_hasSeenDocument && ReadError == null) { return CreateDefault(); }
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException("The existing settings document is empty.");
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new JsonException("The existing settings document is null.");
        using var document = JsonDocument.Parse(json);
        // Inspect names/presence only. Never hydrate, import, print, back up or silently scrub a legacy secret.
        HasLegacyCredentials = document.RootElement.EnumerateObject().Any(property =>
            new[] { "GitHubApiToken", "GitLabApiToken", "CodebergApiToken" }.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
            && property.Value.ValueKind != JsonValueKind.Null
            && (property.Value.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(property.Value.GetString())));
        settings.EnsureInitialized();
        return settings;
    }

    private static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        return settings;
    }
}
