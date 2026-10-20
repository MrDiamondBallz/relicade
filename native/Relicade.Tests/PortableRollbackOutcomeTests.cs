using System.Text.Json;
using Avalonia.Headless.XUnit;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class PortableRollbackOutcomeTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("Prepared", false)] [InlineData("Prepared", true)]
    [InlineData("CandidateRenamed", false)] [InlineData("CandidateRenamed", true)]
    [InlineData("BeforeRecoveryResolved", false)]
    [InlineData("Committed", false)] [InlineData("Committed", true)]
    public async Task Raw_journal_before_reopen_and_actual_UI_distinguish_internal_recovery_from_new_repair(string boundary, bool cancel)
    {
        using var s = new PortableBackupCleanupBaselineScope();
        await PortableBackupCleanupServiceTests.Install(s, 3);
        using var manager = PortableBackupCleanupServiceTests.Manager(s);
        var cleanup = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
        Assert.NotEmpty(cleanup.Files);
        Assert.True(PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, cleanup.PlanSha256).Completed);
        File.WriteAllText(Path.Combine(s.Live, "LastPlayed.txt"), "CURRENT_OUTCOME_NOT_PREVIOUS_SAVE");
        var before = s.Snapshot();
        var history = Directory.GetDirectories(s.Tx.OperationsPath).ToHashSet();
        var boundaries = new List<string>();
        using var cts = new CancellationTokenSource();
        var hit = false; var applies = 0;
        var tx = new PortableInstallationTransaction(s.Live, new() { TransactionBoundary = b =>
        {
            boundaries.Add(b);
            // Leave a real interrupted recovery, rather than manufacturing journal evidence.
            if (boundary == "BeforeRecoveryResolved" && b == "CandidateRenamed") throw new IOException("OUTCOME_INITIAL_RENAME_FAULT");
            if (b != boundary) return;
            hit = true;
            if (cancel) cts.Cancel(); else throw new IOException("OUTCOME_FAULT:" + b);
        }});
        await using var session = new LauncherSession();
        var model = new PortableRollbackViewModel(session, s.Game.InstanceKey,
            () => PortableRollbackCoreTests.Preview(tx),
            (hash, _) => { applies++; return PortableRollbackCoreTests.Apply(tx, hash, cts.Token); });
        try
        {
            await model.PreviewAsync(); Assert.True(model.CanApply); var plan = model.Plan!;
            Assert.Equal(before, s.Snapshot());
            await model.ConfirmAsync(); Assert.True(hit); Assert.Equal(1, applies);
            // FIRST observe raw bytes: no ReadCommitted, Recover, new Install or reopen has run.
            var created = Assert.Single(Directory.GetDirectories(s.Tx.OperationsPath).Where(d => !history.Contains(d)));
            using var journal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(created, "journal.json")));
            var raw = journal.RootElement;
            output.WriteLine("RAW_OUTCOME_BEFORE_ANY_REOPEN=" + raw.GetRawText());
            output.WriteLine("ACTUAL_BOUNDARIES=" + JsonSerializer.Serialize(boundaries));
            output.WriteLine("PLAYER_MESSAGE=" + model.Message);
            if (boundary == "Committed")
            {
                Assert.Equal("Committed", raw.GetProperty("phase").GetString());
                Assert.Null(raw.GetProperty("resolution").GetString());
                Assert.DoesNotContain("Recovering", boundaries);
                Assert.Equal(PortableRollbackUiState.Completed, model.State);
                Assert.True(model.Result!.Completed); Assert.Equal("v2", model.Result.ActualVersion);
                Assert.Equal(plan.PlanSha256, model.Result.PlanSha256);
                Assert.Equal(Path.GetFileName(created), model.Result.ActualOperationId);
                Assert.Contains("Confirmed committed", model.Message);
            }
            else
            {
                Assert.Contains("Recovering", boundaries);
                Assert.Equal(boundary == "Prepared" ? "Prepared" : "Applying", raw.GetProperty("phase").GetString());
                if (boundary == "BeforeRecoveryResolved")
                {
                    Assert.Null(raw.GetProperty("resolution").GetString()); Assert.DoesNotContain("RecoveryResolved", boundaries);
                }
                else
                {
                    Assert.Equal("Old", raw.GetProperty("resolution").GetString()); Assert.Contains("RecoveryResolved", boundaries);
                }
                if (boundary != "Prepared")
                { Assert.Contains("RecoveryCandidateRenamed", boundaries); Assert.Contains("RecoveryPreviousRenamed", boundaries); }
                Assert.Equal(PortableRollbackUiState.Error, model.State); Assert.Null(model.Result);
                Assert.Contains("incomplete or unknown", model.Message);
                Assert.Contains("Transaction-internal recovery may already have run", model.Message);
                Assert.DoesNotContain("No automatic recovery or retry was performed", model.Message);
                Assert.DoesNotContain("No automatic repair or resume", model.Message);
                Assert.Contains("No new repair, resume or retry is initiated", model.Message);
            }
            var expectedVersion = boundary == "Committed" ? "v2" : "v3";
            Assert.Equal(expectedVersion, File.ReadAllText(Path.Combine(s.Live, "version.txt")));
            Assert.Equal(PortableInstallationFixtures.NativePayload(expectedVersion), File.ReadAllBytes(Path.Combine(s.Live, "fixture.AppImage")));
            Assert.Equal("CURRENT_OUTCOME_NOT_PREVIOUS_SAVE", File.ReadAllText(Path.Combine(s.Live, "LastPlayed.txt")));
            var after = s.Snapshot();
            foreach (var row in before.Where(r => !r.Key.StartsWith("Apps/live/", StringComparison.Ordinal))) Assert.Equal(row.Value, after[row.Key]);
            var calls = boundaries.Count;
            await model.ConfirmAsync(); Assert.Equal(1, applies); Assert.Equal(after, s.Snapshot());
            if (boundary != "Committed")
            {
                // A newly opened flow only inspects; it must refuse without resolving or retrying.
                var reopened = new PortableRollbackViewModel(session, s.Game.InstanceKey,
                    () => PortableRollbackCoreTests.Preview(tx), (_, _) => throw new InvalidOperationException("Reopen must not apply"));
                try
                {
                    await reopened.PreviewAsync(); Assert.Equal(PortableRollbackUiState.Error, reopened.State);
                    Assert.Null(reopened.Plan); Assert.False(reopened.CanApply); Assert.Equal(after, s.Snapshot());
                }
                finally { reopened.Close(); reopened.DisposeAfterDrain(); }
            }
            Assert.Equal(calls, boundaries.Count); Assert.Equal(after, s.Snapshot());
            // Only NOW explicitly exercise the pre-existing compatibility recovery/readback path.
            Assert.Equal(expectedVersion, s.Tx.ReadCommitted()!.SelectedVersion);
            await s.Install("future"); s.Tx.SelectExecutable("fixture.AppImage");
            Assert.Equal("future", s.Tx.ReadCommitted()!.SelectedVersion);
            var future = s.Snapshot();
            if (boundary == "Committed")
            {
                var next = PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey);
                Assert.True(PortableBackupCleanupService.Apply(manager, s.Game.InstanceKey, next.PlanSha256).Completed);
            }
            else
            {
                Assert.ThrowsAny<IOException>(() => PortableBackupCleanupService.Preview(manager, s.Game.InstanceKey));
                Assert.Equal(future, s.Snapshot());
            }
            Assert.Equal("CURRENT_OUTCOME_NOT_PREVIOUS_SAVE", File.ReadAllText(Path.Combine(s.Live, "LastPlayed.txt")));
            Assert.Equal(0, s.Network.Calls);
        }
        finally { model.Close(); model.DisposeAfterDrain(); }
    }
}
