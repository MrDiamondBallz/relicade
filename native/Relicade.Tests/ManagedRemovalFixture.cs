using System.Security.Cryptography;
using System.Text.Json;
using Relicade.Core.Services;

namespace Relicade.Tests;

// Every byte is synthetic and nonexecutable. Retain fixtures + snapshots for review.
public sealed class ManagedRemovalFixture : IDisposable
{
    public string Root { get; }
    public string Live => Path.Combine(Root, "Apps", "live");
    public PortableInstallationTransaction Tx => new(Live);
    int snapshot;
    public ManagedRemovalFixture(string? root = null, bool initialize = true)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "managed-removal-" + Guid.NewGuid().ToString("N"));
        if (!initialize) return;
        Directory.CreateDirectory(Path.Combine(Root, "Apps"));
        using var lease = Tx.AcquireLease();
        File.WriteAllText(Path.Combine(Root, "sibling-canary"), "MR01 synthetic sibling canary");
    }
    public static Task InstallAt(string live, string version, Action<string>? boundary = null) =>
        new PortableInstallationTransaction(live, new() { PreservedDataPaths = ["saves", "owned-data", "LastPlayed.txt"],
            SelectedExecutableRelativePath = "fixture.exe", TransactionBoundary = boundary,
            InstalledBuildText = "fixture.exe\n" + new string('a', 64) + "\n" }).InstallAsync(candidate =>
        {
            File.WriteAllText(Path.Combine(candidate, "fixture.exe"), "NONEXECUTABLE synthetic " + version);
            return Task.CompletedTask;
        }, version);
    public async Task Install(string version = "old")
    {
        await InstallAt(Live, version);
        Directory.CreateDirectory(Path.Combine(Live, "saves")); File.WriteAllText(Path.Combine(Live, "saves", "slot"), "current save " + version);
        Directory.CreateDirectory(Path.Combine(Live, "owned-data")); File.WriteAllText(Path.Combine(Live, "owned-data", "original"), "synthetic owned original");
        File.WriteAllText(Path.Combine(Live, "LastPlayed.txt"), "synthetic play record");
    }
    public string[] Snapshot()
    {
        var rows = new List<string>();
        void Walk(string dir)
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
            {
                var attr = File.GetAttributes(p); var name = Path.GetRelativePath(Root, p);
                if ((attr & FileAttributes.ReparsePoint) != 0) rows.Add("link " + name + " " + new FileInfo(p).LinkTarget);
                else if ((attr & FileAttributes.Directory) != 0) { rows.Add("dir " + name); Walk(p); }
                else if (Path.GetFileName(p) is "writer.lock" or ".relicade-mod-writer.lock")
                { Assert.Equal(0, new FileInfo(p).Length); rows.Add("empty stable lock " + name); }
                else rows.Add("file " + name + " " + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
            }
        }
        Walk(Root);
        var receipts = Path.Combine(Path.GetTempPath(), "removal-snapshots", Path.GetFileName(Root)); Directory.CreateDirectory(receipts);
        File.WriteAllText(Path.Combine(receipts, Environment.ProcessId + "-" + snapshot++ + ".json"), JsonSerializer.Serialize(rows));
        return rows.ToArray();
    }
    public void Dispose() { /* Retained own synthetic fixture; no trash/cleanup of evidence. */ }
}
