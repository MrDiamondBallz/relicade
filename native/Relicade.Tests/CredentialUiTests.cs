using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.Services.Credentials;
using Relicade.Views;

namespace Relicade.Tests;

public class CredentialUiTests
{
    [AvaloniaFact]
    public async Task Failed_clear_keeps_the_visible_saved_token_and_no_success_event()
    {
        var root = Path.Combine(Path.GetTempPath(), "credential-ui", Guid.NewGuid().ToString("N"));
        var previousRoot = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = "synthetic-ui-saved-token";
        var store = new Store(new(vault));
        using var manager = new GameManager(store);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        try
        {
            window.Show(); view.OpenGitHubApiTokenSettings(); Dispatcher.UIThread.RunJobs();
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var token = settings.FindControl<TextBox>("GitHubTokenTextBox")!;
            var successes = 0; settings.Model.CredentialsChanged += _ => successes++;
            Assert.Equal("synthetic-ui-saved-token", token.Text);
            var savesBeforeClear = store.Saves; // Opening settings legitimately persists noncredential presentation state.
            vault.Failure = new CredentialVaultUnavailableException(CredentialVaultFailure.Denied);
            settings.ClearGitHubToken_Click(settings, new RoutedEventArgs()); Dispatcher.UIThread.RunJobs();
            Assert.Equal("synthetic-ui-saved-token", token.Text);
            Assert.Equal("synthetic-ui-saved-token", store.Current.GitHubApiToken);
            Assert.Equal(0, successes); Assert.Equal(savesBeforeClear, store.Saves);
        }
        finally
        {
            window.Close(); await view.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = previousRoot;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Store(CredentialSession credentials) : ISettingsStore, ICredentialSettingsStore
    {
        public CredentialSession Credentials => credentials;
        public bool HasLegacyCredentials => false;
        public AppSettings Current { get; } = new() { FirstStartup = false };
        public int Saves;
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
}
