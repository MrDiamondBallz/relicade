using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class ManagedRemovalCompositionAuthorityTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Canonical_duplicates_and_reversed_roots_share_only_the_current_operations_handles(bool reverse)
    {
        using var f = new ManagedRemovalFixture();
        var apps = Path.Combine(f.Root, "Apps"); var linked = Path.Combine(apps, "linked"); Directory.CreateDirectory(linked);
        using (new PortableInstallationTransaction(apps).AcquireLease()) { }
        using (new PortableInstallationTransaction(linked).AcquireLease()) { }
        var mod = Path.Combine(apps, ".relicade-mod-writer.lock"); File.WriteAllText(mod, "");
        var paths = new[] { f.Live, linked, Path.Combine(apps, ".", "live") + Path.DirectorySeparatorChar, linked + Path.DirectorySeparatorChar };
        var before = f.Snapshot();
        using (PortableInstallationTransaction.AcquireUnmanagedRemovalLeases(reverse ? paths.Reverse() : paths))
        {
            // Even another call in the SAME thread cannot borrow the held handles.
            Assert.ThrowsAny<IOException>(() => { using var separate = PortableInstallationTransaction.AcquireUnmanagedRemovalLeases(paths); });
            foreach (var p in new[] { apps, f.Live, linked }) Assert.ThrowsAny<IOException>(() => { using var other = new PortableInstallationTransaction(p).AcquireLease(); });
            Assert.ThrowsAny<IOException>(() => { using var other = new FileStream(mod, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            Assert.Equal(before, f.Snapshot());
        }
        using var after = PortableInstallationTransaction.AcquireUnmanagedRemovalLeases(paths);
    }
    [Theory]
    [InlineData("marker")][InlineData("history")][InlineData("alias")][InlineData("ancestor-marker")]
    public void Every_target_is_validated_and_partial_acquisition_is_released(string evidence)
    {
        using var f = new ManagedRemovalFixture();
        var first = Path.Combine(f.Root, "aaa-first"); var last = Path.Combine(f.Root, "zzz-last"); Directory.CreateDirectory(first); Directory.CreateDirectory(last);
        foreach (var p in new[] { first, last }) { using var held = new PortableInstallationTransaction(p).AcquireLease(); }
        if (evidence == "marker") File.WriteAllText(Path.Combine(last, PortableInstallationTransaction.ManifestFileName), "{unknown");
        if (evidence == "history") File.WriteAllText(Path.Combine(new PortableInstallationTransaction(last).OperationsPath, "unknown"), "retained");
        if (evidence == "alias") { var alias = Path.Combine(f.Root, "zzz-alias"); Directory.CreateSymbolicLink(alias, last); last = alias; }
        if (evidence == "ancestor-marker") File.WriteAllText(Path.Combine(f.Root, PortableInstallationTransaction.ManifestFileName), "{unknown");
        var before = f.Snapshot();
        Assert.ThrowsAny<Exception>(() => { using var held = PortableInstallationTransaction.AcquireUnmanagedRemovalLeases([first, last]); });
        Assert.Equal(before, f.Snapshot());
        using var releasedFirst = new PortableInstallationTransaction(first).AcquireLease();
    }
    [Fact]
    public void Empty_target_set_is_not_success()
    {
        Assert.Throws<IOException>(() => { using var ignored = PortableInstallationTransaction.AcquireUnmanagedRemovalLeases([]); });
    }
}
