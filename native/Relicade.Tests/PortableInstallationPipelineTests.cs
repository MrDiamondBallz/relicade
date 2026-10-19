using Relicade.Core.Services;
using Xunit;

namespace Relicade.Tests;

public sealed class PortableInstallationPipelineTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portable-" + Guid.NewGuid().ToString("N"));
    public PortableInstallationPipelineTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task Production_entry_commits_complete_manifest_before_success()
    {
        var source = Path.Combine(root, "fixture.exe");
        File.WriteAllText(source, "synthetic payload; never executed");
        var live = Path.Combine(root, "live");
        await GameInstallationService.InstallOrUpdateGameAsync(source, live, "fixture.exe", "v1");
        Assert.True(File.Exists(Path.Combine(live, ".relicade-portable.json")), "Install published version without a complete payload manifest");
        Assert.Equal("v1", File.ReadAllText(Path.Combine(live, "version.txt")));
    }

    [Fact]
    public async Task Legacy_folder_is_not_destructively_adopted_by_install()
    {
        var source = Path.Combine(root, "fixture.exe");
        File.WriteAllText(source, "new synthetic payload");
        var live = Path.Combine(root, "live"); Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "fixture.exe"), "old synthetic payload");
        File.WriteAllText(Path.Combine(live, "save.dat"), "precious synthetic save");
        await Assert.ThrowsAnyAsync<IOException>(() => GameInstallationService.InstallOrUpdateGameAsync(source, live, "fixture.exe", "v2"));
        Assert.Equal("old synthetic payload", File.ReadAllText(Path.Combine(live, "fixture.exe")));
        Assert.Equal("precious synthetic save", File.ReadAllText(Path.Combine(live, "save.dat")));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
