using System.Runtime.CompilerServices;
using Relicade.Services.Credentials;

namespace Relicade.Tests;

internal static class CredentialTestEnvironment
{
    [ModuleInitializer]
    internal static void DisableNativeVaultAccess()
    {
        // Test assembly only: no test may reach an actual OS vault/session bus by default.
        // Adapter tests explicitly inject native-call boundaries or an isolated synthetic helper executable.
        CredentialVaultProvider.Session = new(new UnavailableCredentialVault());
    }
}
