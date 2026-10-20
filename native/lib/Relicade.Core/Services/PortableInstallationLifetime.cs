using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relicade.Core.Services;

/// <summary>
/// Per-install launch/update exclusion for source-owned launches. A flushed Launching
/// receipt is written BEFORE process creation, so an interrupted launcher cannot silently
/// unlock an untracked game. Uncertain receipts block writes until explicit repair.
/// This is not proof of native runner/child-process lifetime on every platform.
/// </summary>
public sealed class PortableInstallationLifetime : IDisposable
{
    sealed class Receipt
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public string State { get; set; } = "Launching";
        [JsonRequired] public int? ProcessId { get; set; }
        [JsonRequired] public string? StartIdentity { get; set; }
    }
    readonly string operations;
    IDisposable? lease;
    bool transferred;
    PortableInstallationLifetime(string operations, IDisposable lease) { this.operations = operations; this.lease = lease; }
    static string ReceiptPath(string operations) => PortableInstallationTransaction.SafePath(operations, "launch.json");
    public static PortableInstallationLifetime Begin(string root, string? expectedOperationId = null)
    {
        var tx = new PortableInstallationTransaction(root); var lease = tx.AcquireLease();
        try
        {
            EnsureNotRunning(tx.OperationsPath); tx.RecoverLocked();
            var manifest = tx.ReadManifest(root, true); tx.ValidateCommittedIdentity(manifest);
            if (expectedOperationId != null && manifest?.OperationId != expectedOperationId)
                throw new IOException("The portable generation changed while preparing launch. Refresh local state and retry.");
            var lifetime = new PortableInstallationLifetime(tx.OperationsPath, lease); lifetime.Write(new()); return lifetime;
        }
        catch { lease.Dispose(); throw; }
    }
    internal static void EnsureNotRunning(string operations)
    {
        var path = ReceiptPath(operations); if (!File.Exists(path)) return;
        Receipt r;
        try { r = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(path)) ?? throw new JsonException(); }
        catch (JsonException ex) { throw new IOException("Unknown portable launch receipt; explicit recovery/repair is required before mutation.", ex); }
        if (r.SchemaVersion != 1 || r.State != "Running" || r.ProcessId is not > 0 || string.IsNullOrWhiteSpace(r.StartIdentity))
            throw new IOException("A launch was interrupted before its process could be tracked. Confirm the game is stopped and explicitly repair the retained launch receipt before updating.");
        bool exited;
        try
        {
            using var process = Process.GetProcessById(r.ProcessId.Value);
            exited = process.HasExited || Identity(process) != r.StartIdentity;
        }
        catch (ArgumentException) { exited = true; }
        catch (Exception ex) { throw new IOException("Cannot establish whether this game has stopped; update/rollback is blocked.", ex); }
        if (!exited) throw new IOException("This game is running. Close it before update or rollback.");
        // A stale completed native process is established by PID AND start identity.
        File.Delete(path); PortableInstallationTransaction.Sync(operations);
    }
    static string Identity(Process process)
    {
        bool identityIo = true;
        return Identity(process, null, ref identityIo);
    }
    static string Identity(Process process, Action? afterIdentityOpen, ref bool identityIo)
    {
        if (!OperatingSystem.IsLinux()) return process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Linux Process.StartTime is derived from wall time/uptime and can differ
        // slightly across processes. Kernel start ticks + boot ID are stable, unlike
        // an approximate DateTime comparison that could mistake a live PID for reuse.
        using var reader = File.OpenText($"/proc/{process.Id}/stat");
        if (afterIdentityOpen != null)
        {
            // The scheduling callback is not an OS identity operation. Its exceptions
            // must never authorize completed-process cleanup, even after real exit.
            identityIo = false;
            afterIdentityOpen();
            identityIo = true;
        }
        var stat = reader.ReadToEnd();
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim() + ":" + fields[22 - 3];
    }
    void Write(Receipt r)
    {
        var temp = PortableInstallationTransaction.SafePath(operations, "launch-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(JsonSerializer.SerializeToUtf8Bytes(r)); f.Flush(true); }
        File.Move(temp, ReceiptPath(operations), true); PortableInstallationTransaction.Sync(operations);
    }
    /// <summary>Transfers the lease to an independently opened native process handle.</summary>
    public void Attach(Process process) => Attach(process, null);
    // Instance-local scheduling boundary for real owned-process exit regression tests.
    internal void Attach(Process process, Action? beforeIdentity, Action? afterIdentityOpen = null)
    {
        if (process.HasExited) { Dispose(); return; }
        Process tracked;
        try { tracked = Process.GetProcessById(process.Id); }
        catch (ArgumentException) when (process.HasExited) { Dispose(); return; }
        try
        {
            beforeIdentity?.Invoke();
            string identity;
            bool identityIo = true;
            try { identity = Identity(tracked, afterIdentityOpen, ref identityIo); }
            catch (FileNotFoundException) when (identityIo && process.HasExited)
            {
                tracked.Dispose(); Dispose(); return;
            }
            catch (DirectoryNotFoundException) when (identityIo && process.HasExited)
            {
                tracked.Dispose(); Dispose(); return;
            }
            catch (IOException ex) when (identityIo && OperatingSystem.IsLinux() && ex.HResult == 3 && process.HasExited)
            {
                tracked.Dispose(); Dispose(); return;
            }
            // Only the ORIGINAL owned process can prove completion. Missing identity
            // is not authority to suppress a live/unknown launch or a receipt IO error.
            Write(new() { State = "Running", ProcessId = tracked.Id, StartIdentity = identity });
        }
        catch { tracked.Dispose(); throw; }
        transferred = true;
        _ = RetainUntilExit(tracked);
    }
    async Task RetainUntilExit(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            File.Delete(ReceiptPath(operations)); PortableInstallationTransaction.Sync(operations);
        }
        catch { /* Retain the receipt: a future writer must establish native process identity or fail closed. */ }
        finally { process.Dispose(); Interlocked.Exchange(ref lease, null)?.Dispose(); }
    }
    public void Dispose()
    {
        if (transferred) return;
        // A caller that has not spawned a process may abandon Launching. If spawning
        // throws after OS process creation, leave evidence through PreserveUncertain.
        try { File.Delete(ReceiptPath(operations)); PortableInstallationTransaction.Sync(operations); }
        finally { Interlocked.Exchange(ref lease, null)?.Dispose(); }
    }
    public void PreserveUncertain()
    {
        transferred = true; Interlocked.Exchange(ref lease, null)?.Dispose();
    }
}
