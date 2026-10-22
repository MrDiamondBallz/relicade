using Avalonia.Headless.XUnit;
using Relicade.Core.Services;

namespace Relicade.Tests;

public sealed class OwnedGameSetupAuthorityTests
{
    [AvaloniaTheory]
    [InlineData("managed", false)][InlineData("managed-writer", false)][InlineData("initial-writer", false)]
    [InlineData("managed", true)][InlineData("managed-writer", true)][InlineData("initial-writer", true)]
    public async Task Exact_data_destination_authority_is_checked_before_preview_or_confirm(string state, bool preview)
    {
        using var fixture = new OwnedSetupFixture(); await fixture.Initialize();
        var setup = fixture.Setup(); var plan = setup.Preview(fixture.Source, CancellationToken.None);
        var folder = Path.GetDirectoryName(plan.Destination)!;
        var protectedInstallation = new PortableInstallationTransaction(folder);
        if (state != "initial-writer")
            await protectedInstallation.InstallAsync(stage =>
            {
                File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("protected synthetic installation"));
                return Task.CompletedTask;
            }, "other", TestContext.Current.CancellationToken);
        using var held = state.EndsWith("writer", StringComparison.Ordinal) ? protectedInstallation.AcquireLease() : null;
        var manifestPath = Path.Combine(folder, PortableInstallationTransaction.ManifestFileName);
        var before = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
        var error = Record.Exception(() =>
        {
            if (preview) setup.Preview(fixture.Source, CancellationToken.None);
            else setup.Apply(plan, CancellationToken.None);
        });
        Assert.True(error is IOException or InvalidDataException);
        Assert.False(File.Exists(plan.Destination));
        Assert.False(File.Exists(Path.Combine(folder, plan.OperationId + ".json")));
        if (before != null) Assert.Equal(before, File.ReadAllBytes(manifestPath));
        else Assert.False(Directory.Exists(folder));
        Assert.Equal(fixture.Data, File.ReadAllBytes(fixture.Source)); Assert.Equal(0, fixture.NetworkCalls);
    }

    [AvaloniaFact]
    public async Task Confirmed_copy_keeps_destination_writer_lease_through_copy_and_receipt_commit()
    {
        using var fixture = new OwnedSetupFixture(); await fixture.Initialize();
        var transaction = new PortableInstallationTransaction(fixture.Live);
        var plan = transaction.PreviewOwnedData(fixture.Definition, fixture.Root, fixture.Source);
        var target = new PortableInstallationTransaction(Path.GetDirectoryName(plan.Destination)!);
        var points = new List<string>();
        var result = transaction.ImportOwnedData(fixture.Definition, fixture.Root, plan, boundary: point =>
        {
            points.Add(point);
            Assert.ThrowsAny<IOException>(() => { using var competingWriter = target.AcquireLease(); });
        });
        Assert.Contains("Copying", points); Assert.Contains("BeforeCommit", points);
        Assert.Contains("DataCommitted", points); Assert.Contains("BeforeReceipt", points);
        Assert.True(result.Completed);
        using var releasedWriter = target.AcquireLease();
        Assert.Equal(fixture.Data, File.ReadAllBytes(result.Destination));
        Assert.Equal(fixture.Data, File.ReadAllBytes(fixture.Source)); Assert.Equal(0, fixture.NetworkCalls);
    }
}
