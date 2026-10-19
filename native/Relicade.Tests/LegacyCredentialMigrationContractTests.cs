using Relicade.Services;
using Relicade.Services.Credentials;

namespace Relicade.Tests;

public class LegacyCredentialMigrationContractTests
{
    [Fact]
    public async Task Confirmed_own_legacy_document_must_migrate_and_reopen_without_plaintext()
    {
        var root = Path.Combine(Path.GetTempPath(), "migration-contract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            const string token = "synthetic-migration-contract-secret";
            File.WriteAllText(path, "{\"GitHubApiToken\":\"" + token + "\",\"SortBy\":\"Name\",\"future\":{\"keep\":true}}");
            var vault = new ControlledCredentialVault();
            var store = new FileSettingsStore(path, new(vault));
            var previewMethod = store.GetType().GetMethod("PreviewLegacyCredentialMigration");
            Assert.True(previewMethod != null, "Explicit own-settings migration must be functional, not detection-only read-only state.");
            var preview = previewMethod.Invoke(store, null)!;
            var id = (string)preview.GetType().GetProperty("PreviewId")!.GetValue(preview)!;
            var method = store.GetType().GetMethod("MigrateLegacyCredentialsAsync")!;
            var task = (Task)method.Invoke(store, [id, true, CancellationToken.None])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert.True((bool)result.GetType().GetProperty("Success")!.GetValue(result)!);
            Assert.Equal(token, vault.Values[CredentialProvider.GitHub]);
            Assert.DoesNotContain(token, File.ReadAllText(path));
            Assert.Contains("future", File.ReadAllText(path));
            Assert.False(new FileSettingsStore(path, new(vault)).IsReadOnly);
        }
        finally { Directory.Delete(root, true); }
    }
}
