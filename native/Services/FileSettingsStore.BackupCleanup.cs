using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;
using Relicade.Services.Credentials;

namespace Relicade.Services;

// Optional cleanup-only capability. Current alone is not persisted evidence, and
// Load may replace live settings or initialize credentials. Neither is a fallback.
internal interface IPortableBackupCleanupSettingsEvidence
{
    (AppSettings Settings, string Path, string RawSha256) ReadBackupCleanupSettings();
}

public partial class FileSettingsStore : IPortableBackupCleanupSettingsEvidence
{
    (AppSettings Settings, string Path, string RawSha256) IPortableBackupCleanupSettingsEvidence.ReadBackupCleanupSettings()
    {
        lock (_gate)
        {
            if (ReadError != null || HasLegacyCredentials)
                throw new IOException("Settings recovery or legacy credential cleanup is required before backup cleanup.");
            PortableInstallationTransaction.CheckLinks(_settingsPath);
            byte[] bytes;
            try { bytes = ReadBoundedSettingsBytes(); }
            catch (FileNotFoundException) when (!_hasSeenDocument && ReadError == null)
            { return (CreateDefault(), _settingsPath, "absent"); }
            catch (DirectoryNotFoundException) when (!_hasSeenDocument && ReadError == null)
            { return (CreateDefault(), _settingsPath, "absent"); }
            using var document = LegacyCredentialDocument.ParseStrict(bytes);
            if (document.RootElement.EnumerateObject().Any(p => LegacyCredentialDocument.IsCredentialName(p.Name)))
                throw new IOException("Legacy credential cleanup is required before backup cleanup.");
            var detached = JsonSerializer.Deserialize<AppSettings>(bytes)!;
            detached.EnsureInitialized();
            PortableInstallationTransaction.CheckLinks(_settingsPath);
            return (detached, _settingsPath, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }
}
