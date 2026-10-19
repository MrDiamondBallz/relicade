using Relicade.Core.Services;

namespace Relicade.Services.Credentials;

/// <summary>Serializes vault operations and publishes one shared, runtime-only view to every attached settings snapshot.</summary>
public sealed class CredentialSession(ICredentialVault vault)
{
    private sealed record State(string? Value, CredentialStatus Status);
    private readonly State[] _states = [new(null, CredentialStatus.Missing), new(null, CredentialStatus.Missing), new(null, CredentialStatus.Missing)];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public string Get(CredentialProvider provider) => Volatile.Read(ref _states[(int)provider]).Value ?? "";
    public CredentialStatus Status(CredentialProvider provider) => Volatile.Read(ref _states[(int)provider]).Status;
    internal void SetTransient(CredentialProvider provider, string? value) =>
        Volatile.Write(ref _states[(int)provider], new(value, string.IsNullOrEmpty(value) ? CredentialStatus.Missing : CredentialStatus.Available));

    public void Attach(AppSettings settings) => settings.CredentialContext = this;

    public async Task InitializeAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && !refresh) return;
            foreach (var provider in Enum.GetValues<CredentialProvider>())
            {
                try
                {
                    var value = await vault.ReadAsync(provider, cancellationToken).ConfigureAwait(false);
                    if (value != null) CredentialIdentity.ValidateToken(value);
                    var old = Get(provider);
                    Volatile.Write(ref _states[(int)provider], new(value, value == null ? CredentialStatus.Missing : CredentialStatus.Available));
                    if (old != (value ?? "")) ReleaseRequestCoordinator.InvalidateCredentialForAll(CredentialIdentity.Account(provider), old);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A locked/unavailable read is not 'missing'. Retain an already usable session value on read failure.
                    Volatile.Write(ref _states[(int)provider], new(Get(provider), ToStatus(Sanitize(ex).Failure)));
                }
            }
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(CredentialProvider provider, string? draft, CancellationToken cancellationToken = default)
    {
        _ = CredentialIdentity.Account(provider);
        var value = draft?.Trim() ?? "";
        if (value.Length > 0) CredentialIdentity.ValidateToken(value);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var old = Get(provider);
            // Always consult the vault, even when the draft equals an empty/memory-only value.
            // Once a mutating adapter returns, do not turn a committed write into a cancelled result.
            if (value.Length == 0) await vault.DeleteAsync(provider, cancellationToken).ConfigureAwait(false);
            else await vault.StoreAsync(provider, value, cancellationToken).ConfigureAwait(false);
            var stored = await vault.ReadAsync(provider, CancellationToken.None).ConfigureAwait(false);
            if (value.Length == 0 ? stored != null : stored != value) throw new CredentialVaultUnavailableException();
            Volatile.Write(ref _states[(int)provider], new(stored, stored == null ? CredentialStatus.Missing : CredentialStatus.Available));
            ReleaseRequestCoordinator.InvalidateCredentialForAll(CredentialIdentity.Account(provider), old);
        }
        catch (OperationCanceledException) { throw new OperationCanceledException("Credential operation was cancelled; persistent state may need rechecking.", cancellationToken); }
        catch (Exception ex) { throw Sanitize(ex); }
        finally { _gate.Release(); }
    }

    public static CredentialVaultUnavailableException Sanitize(Exception exception) =>
        new(exception is CredentialVaultUnavailableException failure ? failure.Failure : CredentialVaultFailure.Unavailable);
    private static CredentialStatus ToStatus(CredentialVaultFailure failure) => failure switch
    {
        CredentialVaultFailure.Locked => CredentialStatus.Locked, CredentialVaultFailure.Denied => CredentialStatus.Denied,
        _ => CredentialStatus.Unavailable
    };
}
