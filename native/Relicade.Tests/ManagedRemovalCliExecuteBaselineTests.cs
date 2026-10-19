using Relicade.Services;

namespace Relicade.Tests;

// Compiles against preserved c307a90; only a missing unmanaged folder can be reached.
// The original generic absent-manager wrapper is NOT safe to run as RED.
public sealed class ManagedRemovalCliExecuteBaselineTests
{
    [Theory]
    [InlineData("-x")][InlineData("--uninstall")]
    public async Task Injected_manager_cannot_bypass_settings_recovery(string command)
    {
        using var f = new ManagedRemovalFixture();
        await using var h = new ManagedRemovalLibraryTests.Harness(f, false); await h.Save(); h.Game.Status = Relicade.Core.Models.GameStatus.NotInstalled;
        var previous = SettingsStoreProvider.Default;
        var path = Path.Combine(f.Root, "settings.json"); File.WriteAllText(path, "{broken");
        try
        {
            SettingsStoreProvider.Default = new FileSettingsStore(path);
            var before = f.Snapshot(); var calls = 0;
            var cli = new CLIHandler(h.Manager, moveToTrash: _ => { calls++; throw new Exception("Never dispatch native helper"); });
            var result = await cli.Execute([command, h.Game.Name!]);
            Assert.Equal(before, f.Snapshot()); Assert.Equal(0, calls); Assert.Equal(0, h.Network.Calls);
            Assert.NotEqual(0, result);
        }
        finally { SettingsStoreProvider.Default = previous; }
    }
}
