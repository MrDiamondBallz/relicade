using System.Text.Json;
using Relicade.Services;
using Relicade.Services.Credentials;
using Relicade.ViewModels;

namespace Relicade.Tests;

public class LegacyCredentialTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "legacy-credential", Guid.NewGuid().ToString("N"));
    private const string Canary = "synthetic-sensitive-legacy-canary";
    private string FilePath => Path.Combine(_root, "settings.json");
    public LegacyCredentialTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("GitHubApiToken")]
    [InlineData("GitLabApiToken")]
    [InlineData("CodebergApiToken")]
    [InlineData("githubapitoken")]
    public void Legacy_plaintext_is_warned_not_used_imported_scrubbed_or_copied(string property)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { [property] = Canary, ["SortBy"] = "Name", ["unknown-preserved"] = new { important = true } });
        File.WriteAllText(FilePath, json); var original = File.ReadAllBytes(FilePath);
        var vault = new ControlledCredentialVault(); var store = new FileSettingsStore(FilePath, new(vault));
        Assert.True(store.HasLegacyCredentials); Assert.True(store.IsReadOnly); Assert.Null(store.ReadError);
        Assert.Equal("", store.Current.GitHubApiToken); Assert.Equal("", store.Current.GitLabApiToken); Assert.Equal("", store.Current.CodebergApiToken);
        Assert.Equal("Name", store.Current.SortBy); Assert.Equal(0, vault.Stores); Assert.Equal(0, vault.Deletes);
        var model = new SettingsViewModel(store);
        Assert.Contains("legacy plaintext", model.RecoveryMessage);
        Assert.DoesNotContain(Canary, model.RecoveryMessage);
        var exception = Assert.Throws<LegacyCredentialCleanupRequiredException>(() => store.Save(store.Current));
        Assert.DoesNotContain(Canary, exception.ToString()); Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(_root));
        Assert.True(new FileSettingsStore(FilePath, new(new ControlledCredentialVault())).HasLegacyCredentials);
    }

    [Theory]
    [InlineData("{\"GitHubApiToken\":\"synthetic-sensitive-legacy-canary\",broken")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Corrupt_settings_with_or_without_secret_remain_exact_read_only_bytes(string json)
    {
        File.WriteAllText(FilePath, json);
        var store = new FileSettingsStore(FilePath, new(new ControlledCredentialVault()));
        Assert.True(store.IsReadOnly); Assert.NotNull(store.ReadError);
        Assert.Throws<SettingsRecoveryRequiredException>(() => store.Save(store.Current));
        Assert.Equal(json, File.ReadAllText(FilePath)); Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void Legacy_appearing_after_load_is_detected_before_any_write()
    {
        var store = new FileSettingsStore(FilePath, new(new ControlledCredentialVault()));
        var json = "{\"GitHubApiToken\":\"" + Canary + "\",\"SortBy\":\"Name\"}";
        File.WriteAllText(FilePath, json);
        Assert.Throws<LegacyCredentialCleanupRequiredException>(() => store.Save(store.Current));
        Assert.True(store.IsReadOnly); Assert.Equal(json, File.ReadAllText(FilePath));
        Assert.DoesNotContain(Directory.GetFiles(_root), path => path.EndsWith(".tmp"));
    }

    [Fact]
    public void Runtime_snapshots_share_successful_delete_without_reload_or_legacy_hydration()
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Canary;
        var session = new CredentialSession(vault); var store = new FileSettingsStore(FilePath, session);
        var original = store.Current; store.Save(store.Current); var reloaded = store.Load();
        Assert.Equal(Canary, original.GitHubApiToken); Assert.Equal(Canary, reloaded.GitHubApiToken);
        var model = new SettingsViewModel(store); model.SaveApiToken("github", "");
        Assert.Equal("", original.GitHubApiToken); Assert.Equal("", reloaded.GitHubApiToken);
        Assert.DoesNotContain(Canary, File.ReadAllText(FilePath));
    }
}
