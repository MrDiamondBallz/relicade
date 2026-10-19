using System.Threading.Channels;
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
    [InlineData("cancel", false)] [InlineData("denied", false)] [InlineData("success", false)]
    [InlineData("cancel", true)] [InlineData("denied", true)] [InlineData("success", true)]
    public async Task Accessible_readonly_action_requires_confirmation_and_presents_actual_redacted_outcome(string outcome, bool queuedSaveErrors)
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
        var prompt = main.FindControl<MessagePromptView>("MessagePromptOverlay")!;
        using var prompts = new PromptSequence(prompt.Model);
        try
        {
            window.Show(); main.OpenGitHubApiTokenSettings();
            var settings = main.FindControl<SettingsView>("SettingsPanel")!;
            var button = settings.FindControl<Button>("PreviewCredentialMigrationButton")!;
            var tabs = settings.FindControl<TabControl>("SettingsTabControl")!;
            var startupFailures = 0;
            settings.Model.SaveFailed += CountStartupFailure;
            void CountStartupFailure(Exception _) => startupFailures++;
            if (queuedSaveErrors)
            {
                // Real refused presentation saves queue behind the already open recovery
                // prompt. No fake host, suppressed error, delay or retry is involved.
                settings.Model.UseGridView = !settings.Model.UseGridView;
                settings.Model.UseGridView = !settings.Model.UseGridView;
                Assert.True(startupFailures >= 2);
            }
            // Finish posted initialization at every priority, then put a real FIFO prompt
            // behind its messages. IsOpen=false alone is NOT a queue-idle boundary.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
            const string initialized = "Migration fixture initialization complete";
            var initialization = main.ShowScrollableMessageAsync("Queue barrier, not a product outcome.", initialized);
            var initialErrors = new List<string>();
            while (true)
            {
                var shown = await prompts.NextAsync();
                Assert.Equal(before, File.ReadAllBytes(path)); Assert.Empty(vault.Values);
                if (shown.Title == initialized) break;
                Assert.Contains(shown.Title, new[] { "Save Error", "Settings Recovery Required" });
                Assert.False(shown.IsQuestion);
                Assert.Contains("read-only", shown.Body); Assert.Contains("not secure erasure", shown.Body);
                Assert.DoesNotContain(canary, shown.Body); Assert.DoesNotContain(path, shown.Body);
                initialErrors.Add(shown.Title);
                prompt.Model.Complete(MessagePromptResult.Yes);
            }
            Assert.Contains("Settings Recovery Required", initialErrors);
            if (queuedSaveErrors) Assert.True(initialErrors.Count(t => t == "Save Error") >= 2);
            prompt.Model.Complete(MessagePromptResult.Yes);
            await initialization.WaitAsync(TimeSpan.FromSeconds(10));
            settings.Model.SaveFailed -= CountStartupFailure;
            Assert.False(prompt.Model.IsOpen); Assert.False(prompts.HasPending);
            var successes = 0; var failures = 0;
            settings.Model.CredentialsChanged += _ => successes++;
            settings.Model.SaveFailed += _ => failures++;
            Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEffectivelyEnabled); Assert.True(button.Focusable); Assert.True(button.MinHeight >= 44);
            Assert.False(tabs.IsEnabled);
            Assert.DoesNotContain(canary, settings.FindControl<TextBox>("GitHubTokenTextBox")!.Text ?? "");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var confirmation = await prompts.NextAsync();
            Assert.True(prompt.Model.IsOpen);
            Assert.Equal("Confirm own-settings migration", confirmation.Title); Assert.True(confirmation.IsQuestion);
            Assert.Contains("1 provider", confirmation.Body); Assert.Contains("Own Relicade settings only", confirmation.Body);
            Assert.DoesNotContain(canary, confirmation.Body); Assert.DoesNotContain(path, confirmation.Body);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Empty(vault.Values);
            if (outcome == "denied") vault.Failure = new CredentialVaultUnavailableException(CredentialVaultFailure.Denied);
            // Register before answering: cancellation can complete synchronously, whereas
            // vault work can yield. Await the operation's notification, not wall-clock time.
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void MigrationChanged(object? _, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(SettingsViewModel.CredentialMigrationMessage)) completed.TrySetResult();
            }
            settings.Model.PropertyChanged += MigrationChanged;
            try
            {
                prompt.Model.Complete(outcome == "cancel" ? MessagePromptResult.No : MessagePromptResult.Yes);
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { settings.Model.PropertyChanged -= MigrationChanged; }
            if (outcome == "cancel")
            {
                Assert.Contains("not confirmed", settings.Model.CredentialMigrationMessage);
                Assert.False(prompt.Model.IsOpen); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Empty(vault.Values);
                Assert.Equal(0, successes); Assert.Equal(0, failures);
            }
            else
            {
                var result = await prompts.NextAsync();
                Assert.True(prompt.Model.IsOpen); Assert.Equal("Credential migration", result.Title); Assert.False(result.IsQuestion);
                Assert.Contains("not secure erasure", result.Body); Assert.DoesNotContain(canary, result.Body); Assert.DoesNotContain(path, result.Body);
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
                prompt.Model.Complete(MessagePromptResult.Yes);
            }
            // A second acknowledged queue barrier proves there was no duplicate error
            // between the measured outcome and the end of this interaction.
            var drained = main.ShowScrollableMessageAsync("Queue barrier, not a product outcome.", "Migration fixture finished");
            var final = await prompts.NextAsync();
            Assert.Equal("Migration fixture finished", final.Title); Assert.False(final.IsQuestion);
            prompt.Model.Complete(MessagePromptResult.Yes);
            await drained.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(prompt.Model.IsOpen); Assert.False(prompts.HasPending);
        }
        finally { window.Close(); await main.ShutdownAsync(); RelicadePaths.OverrideUserDataRoot = previous; }
    }

    // Opened is raised only after title/body/question and completion are installed.
    // Keep immutable snapshots: reading mutable IsOpen immediately after Complete can
    // observe a gap while the next semaphore waiter has not run yet.
    private sealed class PromptSequence : IDisposable
    {
        private readonly MessagePromptViewModel _model;
        private readonly Channel<ShownPrompt> _shown = Channel.CreateUnbounded<ShownPrompt>();
        public PromptSequence(MessagePromptViewModel model)
        {
            _model = model;
            _model.Opened += Capture;
            if (_model.IsOpen) Capture(false);
        }
        private void Capture(bool _) => Assert.True(_shown.Writer.TryWrite(new(_model.Title, _model.Body, _model.IsQuestion)));
        public bool HasPending => _shown.Reader.TryPeek(out _);
        public async Task<ShownPrompt> NextAsync() => await _shown.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        public void Dispose() => _model.Opened -= Capture;
    }
    private sealed record ShownPrompt(string Title, string Body, bool IsQuestion);

    [AvaloniaFact]
    public async Task Closed_visible_prompt_does_not_mean_queued_initialization_or_confirmation_is_finished()
    {
        var model = new MessagePromptViewModel();
        using var prompts = new PromptSequence(model);
        var first = model.ShowAsync("refused save", "Save Error", false, false, false, default);
        var second = model.ShowAsync("read-only recovery", "Settings Recovery Required", false, false, false, default);
        var confirmation = model.ShowAsync("explicit consent", "Confirm own-settings migration", true, false, false, default);
        Assert.Equal("Save Error", (await prompts.NextAsync()).Title);
        model.Complete(MessagePromptResult.Yes);
        // Deterministic negative control for the old current-IsOpen drain: it would
        // stop here, even though both recovery and migration are still queued.
        Assert.False(model.IsOpen); Assert.False(second.IsCompleted); Assert.False(confirmation.IsCompleted);
        var recovery = await prompts.NextAsync();
        Assert.Equal("Settings Recovery Required", recovery.Title); Assert.False(recovery.IsQuestion);
        model.Complete(MessagePromptResult.Yes);
        var consent = await prompts.NextAsync();
        Assert.Equal("Confirm own-settings migration", consent.Title); Assert.True(consent.IsQuestion);
        Assert.False(confirmation.IsCompleted);
        model.Complete(MessagePromptResult.No);
        Assert.Equal(MessagePromptResult.Yes, await first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(MessagePromptResult.Yes, await second.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(MessagePromptResult.No, await confirmation.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(model.IsOpen); Assert.False(prompts.HasPending);
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
