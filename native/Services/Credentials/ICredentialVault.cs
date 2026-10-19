namespace Relicade.Services.Credentials;

public enum CredentialProvider { GitHub, GitLab, Codeberg }
public enum CredentialVaultFailure { Unavailable, Locked, Denied }
public enum CredentialStatus { Missing, Available, Unavailable, Locked, Denied }

public interface ICredentialVault
{
    Task<string?> ReadAsync(CredentialProvider provider, CancellationToken cancellationToken);
    Task StoreAsync(CredentialProvider provider, string value, CancellationToken cancellationToken);
    Task DeleteAsync(CredentialProvider provider, CancellationToken cancellationToken);
}

/// <summary>Only fixed, redacted diagnostics cross the vault boundary. Never attach a native/helper exception.</summary>
public sealed class CredentialVaultUnavailableException : Exception
{
    public CredentialVaultFailure Failure { get; }
    public CredentialVaultUnavailableException(CredentialVaultFailure failure = CredentialVaultFailure.Unavailable)
        : base(failure switch
        {
            CredentialVaultFailure.Locked => "The OS credential vault is locked. Unlock it and retry. The previous session token is unchanged; persistent state may need rechecking.",
            CredentialVaultFailure.Denied => "Access to the OS credential vault was denied. Allow access and retry. The previous session token is unchanged; persistent state may need rechecking.",
            _ => "The OS credential vault is unavailable or the operation could not be confirmed. Check platform support and retry. The previous session token is unchanged; persistent state may need rechecking. No file fallback was used."
        }) => Failure = failure;
}

public static class CredentialIdentity
{
    public const string Service = "Relicade.ProviderToken";
    public static string Account(CredentialProvider provider) => provider switch
    {
        CredentialProvider.GitHub => "github", CredentialProvider.GitLab => "gitlab", CredentialProvider.Codeberg => "codeberg",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    public static CredentialProvider Parse(string provider) => provider switch
    {
        "github" => CredentialProvider.GitHub, "gitlab" => CredentialProvider.GitLab, "codeberg" => CredentialProvider.Codeberg,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    public static void ValidateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(value) > 2048)
            throw new CredentialVaultUnavailableException();
    }
}

/// <summary>No platform support means unavailable, never an in-memory or file-backed pretend vault.</summary>
public sealed class UnavailableCredentialVault : ICredentialVault
{
    public Task<string?> ReadAsync(CredentialProvider provider, CancellationToken cancellationToken) => Task.FromException<string?>(new CredentialVaultUnavailableException());
    public Task StoreAsync(CredentialProvider provider, string value, CancellationToken cancellationToken) => Task.FromException(new CredentialVaultUnavailableException());
    public Task DeleteAsync(CredentialProvider provider, CancellationToken cancellationToken) => Task.FromException(new CredentialVaultUnavailableException());
}

public static class CredentialVaultProvider
{
    private static CredentialSession _session = new(CreatePlatformVault());
    public static CredentialSession Session
    {
        get => Volatile.Read(ref _session);
        set => Interlocked.Exchange(ref _session, value ?? throw new ArgumentNullException(nameof(value)));
    }
    public static ICredentialVault CreatePlatformVault()
    {
        if (OperatingSystem.IsAndroid()) return new UnavailableCredentialVault(); // Keystore host adapter not yet implemented.
        if (OperatingSystem.IsWindows()) return new NativeCredentialVault(new WindowsCredentialApi());
        if (OperatingSystem.IsMacOS()) return new NativeCredentialVault(new MacKeychainCredentialApi());
        if (OperatingSystem.IsLinux()) return new SecretServiceCredentialVault();
        return new UnavailableCredentialVault();
    }
}

public interface ICredentialSettingsStore
{
    CredentialSession Credentials { get; }
    bool HasLegacyCredentials { get; }
}

public sealed class LegacyCredentialCleanupRequiredException : InvalidOperationException
{
    public LegacyCredentialCleanupRequiredException() : base("Sensitive legacy plaintext provider credentials remain in this Relicade settings file. They were not used, imported or removed. Settings are read-only until you explicitly repair the file. Vault migration/cleanup is not implemented; keep sensitive originals private.") { }
}
