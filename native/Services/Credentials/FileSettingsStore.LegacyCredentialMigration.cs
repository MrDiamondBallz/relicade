using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Services.Credentials;

namespace Relicade.Services;

public partial class FileSettingsStore
{
    private sealed record MigrationSnapshot(string Id, byte[] Bytes, long Created);
    private MigrationSnapshot? _migrationPreview;
    // Fault seam is internal and contains no secret/path. Production does not install callbacks.
    internal Action<string>? CredentialMigrationCheckpoint { get; set; }

    private byte[] ReadBoundedSettingsBytes()
    {
        using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > LegacyCredentialDocument.MaximumBytes) throw new JsonException("Settings exceed the supported size.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            if (output.Length + read > LegacyCredentialDocument.MaximumBytes) throw new JsonException("Settings exceed the supported size.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public LegacyCredentialMigrationPreview PreviewLegacyCredentialMigration()
    {
        lock (_gate)
        {
            _migrationPreview = null;
            try
            {
                // Revalidate the selected own store only; no directory enumeration or fallback document.
                var bytes = ReadBoundedSettingsBytes();
                using var document = LegacyCredentialDocument.ParseStrict(bytes);
                var providers = LegacyCredentialDocument.Tokens(document.RootElement).Keys.Select(p => p.ToString()).ToArray();
                var hasKeys = document.RootElement.EnumerateObject().Any(p => LegacyCredentialDocument.IsCredentialName(p.Name));
                if (ReadError != null || !hasKeys)
                    return new("", Array.Empty<string>(), 0, false, "No supported own-settings migration is available. Reload/repair settings before retrying. " + LegacyCredentialMigrationMessages.Warning);
                var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                _migrationPreview = new(id, bytes, Environment.TickCount64);
                return new(id, Array.AsReadOnly(providers), providers.Length, true,
                    $"Own Relicade settings only: {providers.Length} provider token(s). Confirmation will store and verify them in the OS vault, then atomically sanitize the active settings file. " + LegacyCredentialMigrationMessages.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            { return new("", Array.Empty<string>(), 0, false, LegacyCredentialMigrationMessages.Invalid + LegacyCredentialMigrationMessages.Warning); }
        }
    }

    public Task<LegacyCredentialMigrationResult> MigrateLegacyCredentialsAsync(string previewId, bool confirmed, CancellationToken cancellationToken = default) =>
        Task.Run(() => MigrateLegacyCredentials(previewId, confirmed, cancellationToken));

    private LegacyCredentialMigrationResult MigrateLegacyCredentials(string previewId, bool confirmed, CancellationToken token)
    {
        // No await while owning a monitor. All session continuations are context-free.
        lock (_gate)
        {
            var snapshot = _migrationPreview;
            _migrationPreview = null; // one-shot, including declined/failed attempts
            if (!confirmed || snapshot == null || snapshot.Id != previewId || Environment.TickCount64 - snapshot.Created > 5 * 60 * 1000 || ReadError != null)
                return new(false, false, false, "Migration not confirmed or preview is missing/expired. Preview own settings again; no changes were made. " + LegacyCredentialMigrationMessages.Warning);
            var changed = false;
            var committed = false;
            try
            {
                token.ThrowIfCancellationRequested();
                using var lease = AcquireWriteLease();
                RequireSnapshot(snapshot.Bytes);
                using var document = LegacyCredentialDocument.ParseStrict(snapshot.Bytes);
                var values = LegacyCredentialDocument.Tokens(document.RootElement);
                using var output = new MemoryStream();
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                        if (!LegacyCredentialDocument.IsCredentialName(property.Name)) property.WriteTo(writer);
                    writer.WriteEndObject();
                }
                var sanitized = output.ToArray();
                using var valid = LegacyCredentialDocument.ParseStrict(sanitized);
                return Credentials.WithMigrationLeaseAsync(async vault =>
                {
                    var missing = new HashSet<CredentialProvider>();
                    // Preflight every entry before any mutation: a late conflict must not cause early writes.
                    foreach (var (provider, value) in values)
                    {
                        token.ThrowIfCancellationRequested();
                        var existing = await vault.ReadAsync(provider, token).ConfigureAwait(false);
                        if (existing == null) missing.Add(provider);
                        else if (!string.Equals(existing, value, StringComparison.Ordinal))
                            return new LegacyCredentialMigrationResult(false, false, false, "Vault conflict: an existing provider credential differs. No vault entries or settings were changed. Resolve it explicitly, then preview again. " + LegacyCredentialMigrationMessages.Warning);
                    }
                    RequireSnapshot(snapshot.Bytes);
                    foreach (var (provider, value) in values)
                    {
                        token.ThrowIfCancellationRequested();
                        if (missing.Contains(provider))
                        {
                            // Recheck immediately before store; never intentionally overwrite another value.
                            var latest = await vault.ReadAsync(provider, token).ConfigureAwait(false);
                            if (latest != null && latest != value) throw new CredentialVaultUnavailableException();
                            if (latest == null)
                            {
                                changed = true; // an adapter can mutate then throw: conservative truthful outcome
                                await vault.StoreAsync(provider, value, token).ConfigureAwait(false);
                            }
                        }
                        if (await vault.ReadAsync(provider, token).ConfigureAwait(false) != value) throw new CredentialVaultUnavailableException();
                        CredentialMigrationCheckpoint?.Invoke("ProviderVerified");
                    }
                    // Verify the complete required set again after every store, not only each
                    // provider immediately after its own store (a later adapter may have changed it).
                    foreach (var (provider, value) in values)
                        if (await vault.ReadAsync(provider, token).ConfigureAwait(false) != value) throw new CredentialVaultUnavailableException();
                    token.ThrowIfCancellationRequested();
                    WriteAtomic(sanitized, () =>
                    {
                        CredentialMigrationCheckpoint?.Invoke("BeforeReplace");
                        token.ThrowIfCancellationRequested();
                        RequireSnapshot(snapshot.Bytes);
                    });
                    committed = true;
                    CredentialMigrationCheckpoint?.Invoke("AfterReplace");
                    // No cancellation checks after replacement: committed sanitization cannot be reported cancelled.
                    if (!ReadBoundedSettingsBytes().AsSpan().SequenceEqual(sanitized)) throw new IOException();
                    _current = ReadFromDisk();
                    ReadError = null;
                    Credentials.Attach(_current);
                    Credentials.PublishMigrated(values);
                    return new LegacyCredentialMigrationResult(true, true, changed, "Provider credentials verified in the vault; active own settings sanitized and reloaded. " + LegacyCredentialMigrationMessages.Warning);
                }, token).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                if (committed)
                {
                    ReadError = new IOException("Settings were sanitized, but reload could not be verified. Reopen/recover settings before further changes.");
                    return new(false, true, changed, ReadError.Message + " " + LegacyCredentialMigrationMessages.Warning);
                }
                return new(false, false, changed, LegacyCredentialMigrationMessages.Retry + LegacyCredentialMigrationMessages.Warning);
            }
        }
    }

    private void RequireSnapshot(byte[] expected)
    {
        if (!ReadBoundedSettingsBytes().AsSpan().SequenceEqual(expected))
            throw new IOException("Settings changed after preview. Preview again.");
    }
}
