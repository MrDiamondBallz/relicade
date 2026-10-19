using System.Text;
using System.Text.Json;
using Relicade.Services;

namespace Relicade.Tests;

public class CredentialLegacyBackupTests
{
    [Theory]
    [InlineData("GitHubApiToken", false)]
    [InlineData("GitLabApiToken", false)]
    [InlineData("CodebergApiToken", false)]
    [InlineData("GitHubApiToken", true)]
    [InlineData("gItHuBaPiToKeN", false)]
    public void New_update_snapshot_must_not_duplicate_sensitive_legacy_settings(string property, bool escapedName)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { [property] = "synthetic-legacy-backup-canary", ["SortBy"] = "Name" });
        if (escapedName) json = json.Replace("GitHub", "\\u0047itHub", StringComparison.Ordinal);
        AssertSnapshotBlocked(Encoding.UTF8.GetBytes(json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Damaged_settings_with_credential_marker_are_preserved_without_new_sensitive_snapshot(bool unicodeWithoutBom)
    {
        const string json = "{\"GitHubApiToken\":\"synthetic-legacy-backup-canary\",broken";
        AssertSnapshotBlocked((unicodeWithoutBom ? Encoding.Unicode : Encoding.UTF8).GetBytes(json));
    }
    private static void AssertSnapshotBlocked(byte[] bytes)
    {
        var root = Path.Combine(Path.GetTempPath(), "credential-legacy-backup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json"); File.WriteAllBytes(path, bytes);
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => LauncherUpdateBackup.Create(root));
            Assert.Equal("LegacyCredentialCleanupRequiredException", exception.GetType().Name);
            Assert.DoesNotContain("synthetic-legacy-backup-canary", exception.ToString());
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetDirectories(root));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
