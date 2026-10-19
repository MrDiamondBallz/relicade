using System.Text.Json;
using Relicade.Services;
using Relicade.Services.Credentials;
using Relicade.ViewModels;

namespace Relicade.Tests;

public class CredentialVaultTests
{
    private const string Old = "synthetic-previous-vault-token", New = "synthetic-next-vault-token";
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public int Saves;
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { Saves++; throw new IOException("General settings must not be used for credentials."); }
    }

    [Theory]
    [InlineData(CredentialProvider.GitHub)]
    [InlineData(CredentialProvider.GitLab)]
    [InlineData(CredentialProvider.Codeberg)]
    public async Task Save_delete_readback_and_fresh_session_preserve_the_provider_identity(CredentialProvider provider)
    {
        var vault = new ControlledCredentialVault(); var session = new CredentialSession(vault); var store = new Store();
        var model = new SettingsViewModel(store, session); var events = 0;
        model.CredentialsChanged += actual => { Assert.Equal(CredentialIdentity.Account(provider), actual); events++; };
        Assert.True(await model.SaveApiTokenAsync(CredentialIdentity.Account(provider), " " + New + " "));
        Assert.Equal(New, vault.Values[provider]); Assert.Equal(New, session.Get(provider)); Assert.Equal(1, events);
        Assert.Equal(0, store.Saves);
        var reopened = new CredentialSession(vault); await reopened.InitializeAsync();
        Assert.Equal(New, reopened.Get(provider));
        var snapshot = new AppSettings(); session.Attach(snapshot);
        Assert.DoesNotContain(New, JsonSerializer.Serialize(new { Export = snapshot, Settings = store.Current }));
        Assert.True(await model.SaveApiTokenAsync(CredentialIdentity.Account(provider), ""));
        Assert.False(vault.Values.ContainsKey(provider)); Assert.Equal("", session.Get(provider)); Assert.Equal(2, events);
        Assert.Equal("", provider switch { CredentialProvider.GitHub => snapshot.GitHubApiToken, CredentialProvider.GitLab => snapshot.GitLabApiToken, _ => snapshot.CodebergApiToken });
        await reopened.InitializeAsync(refresh: true); Assert.Equal("", reopened.Get(provider));
    }

    [Theory]
    [InlineData(CredentialVaultFailure.Unavailable, false)]
    [InlineData(CredentialVaultFailure.Locked, false)]
    [InlineData(CredentialVaultFailure.Denied, false)]
    [InlineData(CredentialVaultFailure.Unavailable, true)]
    [InlineData(CredentialVaultFailure.Locked, true)]
    [InlineData(CredentialVaultFailure.Denied, true)]
    public async Task Failed_save_or_delete_keeps_previous_runtime_value_and_emits_no_success(CredentialVaultFailure failure, bool delete)
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Old;
        var model = new SettingsViewModel(new Store(), new(vault)); var changed = 0; Exception? reported = null;
        model.CredentialsChanged += _ => changed++; model.SaveFailed += ex => reported = ex;
        vault.Failure = new CredentialVaultUnavailableException(failure);
        Assert.False(await model.SaveApiTokenAsync("github", delete ? "" : New));
        Assert.Equal(Old, model.Current.GitHubApiToken); Assert.Equal(Old, vault.Values[CredentialProvider.GitHub]); Assert.Equal(0, changed);
        Assert.Equal(failure, Assert.IsType<CredentialVaultUnavailableException>(reported).Failure);
        Assert.DoesNotContain(Old, reported!.ToString()); Assert.DoesNotContain(New, reported.ToString());
        vault.Failure = null;
        Assert.True(await model.SaveApiTokenAsync("github", delete ? "" : New));
        Assert.Equal(delete ? "" : New, model.Current.GitHubApiToken); Assert.Equal(1, changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unconfirmed_mutation_is_not_reported_as_saved_or_deleted(bool delete)
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Old;
        var model = new SettingsViewModel(new Store(), new(vault));
        vault.LieAboutStore = vault.LieAboutDelete = true;
        Assert.False(await model.SaveApiTokenAsync("github", delete ? "" : New));
        Assert.Equal(Old, model.Current.GitHubApiToken);
    }

    [Fact]
    public async Task Adapter_exception_with_a_secret_is_redacted_without_inner_exception()
    {
        var vault = new ControlledCredentialVault(); var model = new SettingsViewModel(new Store(), new(vault));
        vault.Failure = new IOException(New + " native diagnostic"); Exception? reported = null;
        model.SaveFailed += ex => reported = ex;
        Assert.False(await model.SaveApiTokenAsync("github", New));
        Assert.Null(reported!.InnerException); Assert.DoesNotContain(New, reported.ToString());
        Assert.Contains("No file fallback", reported.Message);
    }

    [Theory]
    [InlineData(CredentialVaultFailure.Locked, CredentialStatus.Locked)]
    [InlineData(CredentialVaultFailure.Denied, CredentialStatus.Denied)]
    [InlineData(CredentialVaultFailure.Unavailable, CredentialStatus.Unavailable)]
    public async Task Read_failure_is_distinct_from_missing_and_tokenless_settings_still_work(CredentialVaultFailure failure, CredentialStatus expected)
    {
        var vault = new ControlledCredentialVault { Failure = new CredentialVaultUnavailableException(failure) };
        var session = new CredentialSession(vault); await session.InitializeAsync();
        Assert.Equal(expected, session.Status(CredentialProvider.GitHub)); Assert.Equal("", session.Get(CredentialProvider.GitHub));
        var root = Path.Combine(Path.GetTempPath(), "vault-unavailable", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"), session);
            Assert.False(store.IsReadOnly); store.Current.SortBy = "Name"; store.Save(store.Current);
            Assert.Equal("Name", store.Load().SortBy); Assert.Equal("", store.Current.GitHubApiToken);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        vault.Failure = null; await session.InitializeAsync(refresh: true);
        Assert.Equal(CredentialStatus.Missing, session.Status(CredentialProvider.GitHub));
    }

    [Fact]
    public async Task Empty_runtime_delete_still_deletes_the_persisted_entry()
    {
        var vault = new ControlledCredentialVault(); var session = new CredentialSession(vault); await session.InitializeAsync();
        vault.Values[CredentialProvider.GitHub] = New; // Another session stored it after this session's initial read.
        await session.SaveAsync(CredentialProvider.GitHub, "");
        Assert.Empty(vault.Values); Assert.Equal(1, vault.Deletes);
    }

    [Fact]
    public async Task Cancelling_before_mutation_has_no_success_or_general_settings_write()
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Old;
        var model = new SettingsViewModel(new Store(), new(vault)); var changed = 0; model.CredentialsChanged += _ => changed++;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.False(await model.SaveApiTokenAsync("github", New, cancellation.Token));
        Assert.Equal(Old, model.Current.GitHubApiToken); Assert.Equal(0, vault.Stores); Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Concurrent_save_then_delete_and_refresh_are_serialized_without_stale_rehydration()
    {
        var vault = new BarrierVault(); var session = new CredentialSession(vault); await session.InitializeAsync();
        var saving = session.SaveAsync(CredentialProvider.GitHub, New);
        await vault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deleting = session.SaveAsync(CredentialProvider.GitHub, "");
        var refresh = session.InitializeAsync(refresh: true);
        Assert.False(deleting.IsCompleted); Assert.False(refresh.IsCompleted);
        vault.Release.SetResult(); await Task.WhenAll(saving, deleting, refresh);
        Assert.Null(vault.Value); Assert.Equal("", session.Get(CredentialProvider.GitHub)); Assert.Equal(1, vault.MaxActive);
    }
    private sealed class BarrierVault : ICredentialVault
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Value;
        private int _active;
        public int MaxActive;
        public Task<string?> ReadAsync(CredentialProvider provider, CancellationToken ct) => Task.FromResult(provider == CredentialProvider.GitHub ? Value : null);
        public async Task StoreAsync(CredentialProvider provider, string value, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _active); MaxActive = Math.Max(MaxActive, n);
            try { Entered.SetResult(); await Release.Task.WaitAsync(ct); Value = value; }
            finally { Interlocked.Decrement(ref _active); }
        }
        public Task DeleteAsync(CredentialProvider provider, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _active); MaxActive = Math.Max(MaxActive, n);
            Value = null; Interlocked.Decrement(ref _active); return Task.CompletedTask;
        }
    }
}
