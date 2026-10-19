using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.Services.Credentials;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public class LegacyCredentialMigrationUiTests
{
    [AvaloniaTheory]
    [InlineData("cancel")] [InlineData("denied")] [InlineData("success")]
    public async Task Accessible_readonly_action_requires_confirmation_and_presents_actual_redacted_outcome(string outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "migration-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        const string canary = "synthetic-migration-ui-canary";
        File.WriteAllText(path, "{\"GitHubApiToken\":\"" + canary + "\",\"FirstStartup\":false,\"future\":{\"keep\":true}}");
        var before = File.ReadAllBytes(path);
        var previous = RelicadePaths.OverrideUserDataRoot; RelicadePaths.OverrideUserDataRoot = root;
        var vault = new LegacyCredentialMigrationTests.Vault();
        var store = new FileSettingsStore(path, new(vault));
        using var manager = new GameManager(store);
        var main = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = main, Width = 1100, Height = 800 };
        try
        {
            window.Show(); main.OpenGitHubApiTokenSettings(); Dispatcher.UIThread.RunJobs();
            var settings = main.FindControl<SettingsView>("SettingsPanel")!;
            var button = settings.FindControl<Button>("PreviewCredentialMigrationButton")!;
            var tabs = settings.FindControl<TabControl>("SettingsTabControl")!;
            var prompt = main.GetVisualDescendants().OfType<MessagePromptView>().Single();
            // Opening the existing settings panel can report refused presentation saves
            // in read-only state. Dismiss those real unrelated errors before this operation.
            for (var n = 0; n < 20 && prompt.Model.IsOpen; n++)
            {
                Assert.Contains(prompt.Model.Title, new[] { "Save Error", "Settings Recovery Required" });
                prompt.Model.Complete(MessagePromptResult.Yes); await Task.Delay(10); Dispatcher.UIThread.RunJobs();
            }
            Assert.False(prompt.Model.IsOpen);
            var successes = 0; var failures = 0;
            settings.Model.CredentialsChanged += _ => successes++;
            settings.Model.SaveFailed += _ => failures++;
            Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEffectivelyEnabled); Assert.True(button.Focusable); Assert.True(button.MinHeight >= 44);
            Assert.False(tabs.IsEnabled);
            Assert.DoesNotContain(canary, settings.FindControl<TextBox>("GitHubTokenTextBox")!.Text ?? "");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(prompt.Model.IsOpen, $"Prompt open={prompt.Model.IsOpen}; title={prompt.Model.Title}; canPreview={settings.Model.CanPreviewCredentialMigration}; message={settings.Model.CredentialMigrationMessage}"); Assert.True(prompt.Model.IsQuestion, $"Expected confirmation; title={prompt.Model.Title}; body={prompt.Model.Body}");
            Assert.Contains("1 provider", prompt.Model.Body); Assert.Contains("Own Relicade settings only", prompt.Model.Body);
            Assert.DoesNotContain(canary, prompt.Model.Body); Assert.DoesNotContain(path, prompt.Model.Body);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Empty(vault.Values);
            if (outcome == "denied") vault.Failure = new CredentialVaultUnavailableException(CredentialVaultFailure.Denied);
            prompt.Model.Complete(outcome == "cancel" ? MessagePromptResult.No : MessagePromptResult.Yes);
            for (var n = 0; n < 500; n++)
            {
                Dispatcher.UIThread.RunJobs();
                if (outcome == "cancel" ? settings.Model.CredentialMigrationMessage?.Contains("not confirmed") == true : prompt.Model.IsOpen && !prompt.Model.IsQuestion) break;
                await Task.Delay(10);
            }
            if (outcome == "cancel")
            {
                Assert.False(prompt.Model.IsOpen); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Empty(vault.Values);
                Assert.Equal(0, successes); Assert.Equal(0, failures);
            }
            else
            {
                Assert.True(prompt.Model.IsOpen); Assert.False(prompt.Model.IsQuestion);
                Assert.Contains("not secure erasure", prompt.Model.Body); Assert.DoesNotContain(canary, prompt.Model.Body);
                if (outcome == "denied")
                {
                    Assert.Contains("did not commit", prompt.Model.Body); Assert.Equal(1, failures); Assert.Equal(0, successes);
                    Assert.Equal(before, File.ReadAllBytes(path)); Assert.False(tabs.IsEnabled);
                    Assert.Contains("read-only", settings.FindControl<TextBlock>("CredentialMigrationStatus")!.Text);
                }
                else
                {
                    Assert.Equal(3, successes); Assert.Equal(0, failures); Assert.True(tabs.IsEnabled);
                    Assert.False(settings.Model.HasLegacyCredentials); Assert.DoesNotContain(canary, File.ReadAllText(path));
                    Assert.Contains("future", File.ReadAllText(path));
                    Assert.Contains("not secure erasure", settings.Model.CredentialStorageMessage);
                }
                prompt.Model.Complete(MessagePromptResult.Yes); Dispatcher.UIThread.RunJobs();
                await Task.Delay(30); Dispatcher.UIThread.RunJobs(); Assert.False(prompt.Model.IsOpen); // no duplicate error prompt
            }
        }
        finally { window.Close(); await main.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = previous; }
    }

    [Fact]
    public async Task Kiosk_and_corrupt_state_cannot_be_bypassed_with_a_previously_issued_preview()
    {
        var root = Path.Combine(Path.GetTempPath(), "migration-policy", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{\"GitHubApiToken\":\"synthetic-policy-token\",\"KioskMode\":true}");
        var vault = new LegacyCredentialMigrationTests.Vault(); var store = new FileSettingsStore(path, new(vault));
        var model = new SettingsViewModel(store); var before = File.ReadAllBytes(path);
        Assert.True(model.KioskLocked); Assert.False(model.CanPreviewCredentialMigration);
        Assert.False(model.PreviewLegacyCredentialMigration().CanMigrate);
        var id = store.PreviewLegacyCredentialMigration().PreviewId;
        Assert.False((await model.MigrateLegacyCredentialsAsync(id,true)).Success); Assert.Empty(vault.Values); Assert.Equal(before, File.ReadAllBytes(path));
        model.UnlockKioskSession(); Assert.True(model.CanPreviewCredentialMigration);
        id = model.PreviewLegacyCredentialMigration().PreviewId;
        File.WriteAllText(path, "{broken"); store.Load();
        Assert.False(model.CanPreviewCredentialMigration); Assert.False((await model.MigrateLegacyCredentialsAsync(id,true)).Success);
        Assert.Empty(vault.Values); Assert.Equal("{broken",File.ReadAllText(path));
    }
}
