using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>Shared production gate: recover BEFORE consumers read version/build/launch state.</summary>
public static class PortableInstallationRecovery
{
    public static PortableInstallationTransaction.Manifest? EnsureReadable(GameInfo game, string root)
    {
        try
        {
            if (!PortableInstallationTransaction.HasEvidence(root)) { game.PortableRecoveryError = null; game.IsManagedPortable = false; return null; }
            var manifest = new PortableInstallationTransaction(root).ReadCommitted();
            game.PortableRecoveryError = null;
            game.IsManagedPortable = manifest != null;
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            game.PortableRecoveryError = "Recovery needed: close the game and retry local recovery. Retained generations must not be removed; repair permissions or unknown changes first.";
            throw;
        }
    }
}
