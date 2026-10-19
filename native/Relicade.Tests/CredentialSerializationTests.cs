using System.Text.Json;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public class CredentialSerializationTests
{
    private const string Canary = "synthetic-vault-boundary-canary";

    [Fact]
    public void Provider_credentials_are_not_part_of_settings_or_nested_exports()
    {
        var settings = new AppSettings { GitHubApiToken = Canary, GitLabApiToken = Canary, CodebergApiToken = Canary };
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(settings));
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(new { Settings = settings, Library = new[] { settings } }));
    }

    [Fact]
    public void General_settings_save_never_creates_secret_bearing_files_or_update_backups()
    {
        var root = Path.Combine(Path.GetTempPath(), "credential-serialization", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            var settings = new AppSettings { GitHubApiToken = Canary, GitLabApiToken = Canary, CodebergApiToken = Canary };
            store.Save(settings);
            LauncherUpdateBackup.Create(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                Assert.DoesNotContain(Canary, File.ReadAllText(file));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Token_save_does_not_write_the_general_settings_store()
    {
        var store = new CountingStore();
        new SettingsViewModel(store).SaveApiToken("github", Canary);
        Assert.Equal(0, store.Saves);
    }

    private sealed class CountingStore : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public int Saves;
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
}
