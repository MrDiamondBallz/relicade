using Relicade.Services.Credentials;

namespace Relicade.Tests;

// Controlled synthetic boundary only. Not registered by any product/platform host.
internal sealed class ControlledCredentialVault : ICredentialVault
{
    public readonly Dictionary<CredentialProvider, string> Values = [];
    public Exception? Failure;
    public bool LieAboutStore, LieAboutDelete;
    public int Stores, Deletes, Reads;
    public Task<string?> ReadAsync(CredentialProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Reads++;
        if (Failure != null) throw Failure;
        return Task.FromResult(Values.GetValueOrDefault(provider));
    }
    public Task StoreAsync(CredentialProvider provider, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Stores++;
        if (Failure != null) throw Failure;
        if (!LieAboutStore) Values[provider] = value;
        return Task.CompletedTask;
    }
    public Task DeleteAsync(CredentialProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Deletes++;
        if (Failure != null) throw Failure;
        if (!LieAboutDelete) Values.Remove(provider);
        return Task.CompletedTask;
    }
}
internal sealed class CredentialVaultTestScope : IDisposable
{
    private readonly CredentialSession _previous = CredentialVaultProvider.Session;
    public ControlledCredentialVault Vault { get; } = new();
    public CredentialVaultTestScope() => CredentialVaultProvider.Session = new(Vault);
    public void Dispose() => CredentialVaultProvider.Session = _previous;
}
