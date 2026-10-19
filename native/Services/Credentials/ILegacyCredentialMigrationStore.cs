namespace Relicade.Services.Credentials;

public interface ILegacyCredentialMigrationStore
{
    LegacyCredentialMigrationPreview PreviewLegacyCredentialMigration();
    Task<LegacyCredentialMigrationResult> MigrateLegacyCredentialsAsync(string previewId, bool confirmed, CancellationToken cancellationToken = default);
}

public sealed record LegacyCredentialMigrationPreview(string PreviewId, IReadOnlyList<string> Providers, int ProviderCount, bool CanMigrate, string Message);
public sealed record LegacyCredentialMigrationResult(bool Success, bool SettingsSanitized, bool VaultMayHaveChanged, string Message);

internal static class LegacyCredentialMigrationMessages
{
    internal const string Warning = "Existing backups and filesystem/OS remnants may still contain sensitive plaintext. Active-file replacement is not secure erasure; keep originals private. Tokens are optional; no account or network is required.";
    internal const string Invalid = "Own settings cannot be migrated: unsupported, unreadable or invalid document. Repair or restore it privately, then preview again. No settings were replaced. ";
    internal const string Retry = "Migration did not commit. Own settings remain unchanged and read-only; some vault entries may already have been saved. Unlock/check vault access and retry with a fresh preview. ";
}
