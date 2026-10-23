using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using System.Security.Cryptography;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class RetainedOwnedDataAdoptionTests
{
    [AvaloniaFact]
    public async Task Setup_offers_explicit_retained_file_preview_for_a_new_compatible_generation()
    {
        using var fixture = new OwnedSetupFixture();
        await fixture.Initialize();
        var initial = fixture.Setup();
        var imported = initial.Preview(fixture.Source, CancellationToken.None);
        Assert.True(initial.Apply(imported, CancellationToken.None).Completed);
        var retainedBytes = File.ReadAllBytes(imported.Destination);
        var oldReceipt = Path.Combine(Path.GetDirectoryName(imported.Destination)!, imported.OperationId + ".json");
        var oldReceiptBytes = File.ReadAllBytes(oldReceipt);
        var executable = PortableInstallationFixtures.NativePayload("owned setup UI");
        await new PortableInstallationTransaction(fixture.Live, new()
        {
            ReleaseIdentity = fixture.Definition.ReleaseIdentity,
            AssetIdentity = fixture.Definition.AssetIdentity
        }).InstallAsync(stage =>
        {
            File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), executable);
            return Task.CompletedTask;
        }, fixture.Definition.ReleaseIdentity);
        File.Move(fixture.Source, fixture.Source + ".unavailable");
        var setup = fixture.Setup();
        Assert.ThrowsAny<IOException>(() => setup.ValidateReady());
        Assert.ThrowsAny<IOException>(() => setup.Preview(imported.Destination, CancellationToken.None));
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, fixture.Game.InstanceKey, setup);
        var dialog = new OwnedGameSetupDialog(model);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
            Assert.False(model.CanConfirm); Assert.True(model.CanChoose);
            Assert.Equal(retainedBytes, File.ReadAllBytes(imported.Destination));
            Assert.Equal(oldReceiptBytes, File.ReadAllBytes(oldReceipt));
            Assert.Equal(fixture.Data, File.ReadAllBytes(fixture.Source + ".unavailable"));
            Assert.Equal(0, fixture.NetworkCalls);
            Assert.Contains(dialog.GetVisualDescendants().OfType<Button>(), button =>
                button.IsVisible && button.IsEnabled && Equals(button.Content, "Use retained game files…"));
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    static async Task<OwnedDataPreview> PrepareRetained(OwnedSetupFixture f)
    {
        await f.Initialize();
        var setup = f.Setup();
        var imported = setup.Preview(f.Source, CancellationToken.None);
        Assert.True(setup.Apply(imported, CancellationToken.None).Completed);
        await NewGeneration(f);
        File.Move(f.Source, f.Source + ".unavailable");
        return imported;
    }
    static Task NewGeneration(OwnedSetupFixture f) => new PortableInstallationTransaction(f.Live, new()
        { ReleaseIdentity = f.Definition.ReleaseIdentity, AssetIdentity = f.Definition.AssetIdentity })
        .InstallAsync(stage =>
        {
            File.WriteAllBytes(Path.Combine(stage, "fixture.AppImage"), PortableInstallationFixtures.NativePayload("owned setup UI"));
            return Task.CompletedTask;
        }, f.Definition.ReleaseIdentity);
    static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + "|" +
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    static string Receipt(RetainedOwnedDataPreview plan) => Path.Combine(Path.GetDirectoryName(plan.Destination)!, plan.OperationId + ".json");

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_retained_preview_requires_separate_confirmation_and_preserves_every_existing_file(bool cancel)
    {
        using var f = new OwnedSetupFixture(); var imported = await PrepareRetained(f);
        var before = Snapshot(f.Root); var setup = f.Setup();
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        var dialog = new OwnedGameSetupDialog(model);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.CloseButton.IsDefault); Assert.False(dialog.ConfirmButton.IsDefault);
            dialog.RetainedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            var plan = Assert.IsType<RetainedOwnedDataPreview>(model.RetainedPlan);
            Assert.Equal(OwnedGameSetupUiState.Confirm, model.State);
            Assert.Equal("Confirm lawful use and reuse", dialog.ConfirmButton.Content);
            Assert.False(f.Game.OwnedDataReady); Assert.True(model.CanChoose);
            Assert.Equal(before, Snapshot(f.Root)); Assert.False(File.Exists(Receipt(plan)));
            Assert.ThrowsAny<IOException>(() => setup.Preview(imported.Destination, CancellationToken.None));
            if (cancel)
            {
                dialog.Close(); await dialog.Work;
                Assert.Equal(before, Snapshot(f.Root)); Assert.False(f.Game.OwnedDataReady);
            }
            else
            {
                dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var work = dialog.Work;
                dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await work;
                Assert.Equal(OwnedGameSetupUiState.Completed, model.State); Assert.True(f.Game.OwnedDataReady);
                Assert.True(File.Exists(Receipt(plan))); setup.ValidateReady();
                var after = Snapshot(f.Root);
                Assert.Equal(before.Length + 1, after.Length);
                Assert.All(before, entry => Assert.Contains(entry, after));
                Assert.Equal(f.Data, File.ReadAllBytes(imported.Destination));
                Assert.Equal(f.Data, File.ReadAllBytes(f.Source + ".unavailable"));
                var repeated = setup.PreviewRetained(CancellationToken.None); Assert.True(repeated.ReceiptExists);
                var result = await Task.Run(() => setup.AdoptRetainedAsync(repeated, () => true, CancellationToken.None));
                Assert.True(result.ReceiptConfirmed); Assert.False(result.ReceiptCreated); Assert.True(result.ReadyPublished);
                Assert.Equal(after, Snapshot(f.Root));
            }
            Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("generation")][InlineData("data")][InlineData("receipt")]
    [InlineData("context")][InlineData("saved-context")][InlineData("selection")]
    public async Task Changed_preview_authority_refuses_without_any_additional_mutation(string change)
    {
        using var f = new OwnedSetupFixture(); await PrepareRetained(f);
        var setup = f.Setup(); var plan = setup.PreviewRetained(CancellationToken.None);
        if (change == "generation") await NewGeneration(f);
        if (change == "data") File.WriteAllBytes(plan.Destination, [40, 30, 20, 10]);
        if (change == "receipt") File.WriteAllText(Receipt(plan), "conflicting receipt");
        if (change == "context") f.Store.Current.KioskMode = true;
        if (change == "saved-context") File.AppendAllText(f.Manager.CatalogService.AppsConfigPath, " ");
        if (change == "selection") f.Game.SelectedExecutable = "newer-selection";
        var before = Snapshot(f.Root);
        await Assert.ThrowsAnyAsync<IOException>(() => Task.Run(() => setup.AdoptRetainedAsync(plan, () => true, CancellationToken.None)));
        Assert.Equal(before, Snapshot(f.Root)); Assert.False(f.Game.OwnedDataReady); Assert.Equal(0, f.NetworkCalls);
    }

    [AvaloniaTheory]
    [InlineData("corrupt")][InlineData("missing")][InlineData("pending")][InlineData("missing-writer")][InlineData("link")]
    public async Task Uncertain_retained_input_preserves_evidence_and_original_chooser(string uncertainty)
    {
        using var f = new OwnedSetupFixture(); var imported = await PrepareRetained(f);
        var folder = Path.GetDirectoryName(imported.Destination)!;
        if (uncertainty == "corrupt") File.WriteAllBytes(imported.Destination, [40, 30, 20, 10]);
        if (uncertainty == "missing") File.Move(imported.Destination, imported.Destination + ".preserved");
        if (uncertainty == "pending") File.WriteAllText(Path.Combine(folder, "receipt-pending.tmp"), "preserve pending work");
        if (uncertainty == "missing-writer")
        {
            var writer = Path.Combine(new PortableInstallationTransaction(folder).OperationsPath, "writer.lock");
            File.Move(writer, writer + ".preserved");
        }
        if (uncertainty == "link")
        {
            File.Move(imported.Destination, imported.Destination + ".preserved");
            File.CreateSymbolicLink(imported.Destination, imported.Destination + ".preserved");
        }
        var before = Snapshot(f.Root);
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, f.Setup());
        var dialog = new OwnedGameSetupDialog(model);
        try
        {
            dialog.Show(); dialog.RetainedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Error, model.State); Assert.Null(model.RetainedPlan);
            Assert.True(model.CanChoose); Assert.True(dialog.ChooseButton.IsEnabled); Assert.False(model.CanConfirm);
            Assert.False(f.Game.OwnedDataReady); Assert.Equal(before, Snapshot(f.Root)); Assert.Equal(0, f.NetworkCalls);
            if (uncertainty == "link") Assert.Equal(imported.Destination + ".preserved", new FileInfo(imported.Destination).LinkTarget);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData("BeforeReceipt")][InlineData("ReceiptCommitted")]
    public async Task Cancellation_distinguishes_no_receipt_from_already_committed_receipt(string point)
    {
        using var f = new OwnedSetupFixture(); await PrepareRetained(f);
        var tx = new PortableInstallationTransaction(f.Live);
        var plan = tx.PreviewRetainedOwnedData(f.Definition, f.Root); var before = Snapshot(f.Root);
        using var cancellation = new CancellationTokenSource(); var callbacks = 0;
        var task = Task.Run(() => tx.AdoptRetainedOwnedDataAsync(f.Definition, f.Root, plan,
            () => { callbacks++; return Task.FromResult(true); }, cancellation.Token,
            boundary => { if (boundary == point) cancellation.Cancel(); }));
        if (point == "BeforeReceipt")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(before, Snapshot(f.Root)); Assert.False(File.Exists(Receipt(plan)));
        }
        else
        {
            var result = await task;
            Assert.True(result.ReceiptConfirmed); Assert.True(result.ReceiptCreated); Assert.False(result.ReadyPublished);
            var after = Snapshot(f.Root); Assert.Equal(before.Length + 1, after.Length);
            Assert.All(before, entry => Assert.Contains(entry, after)); Assert.True(File.Exists(Receipt(plan)));
        }
        Assert.Equal(0, callbacks); Assert.False(f.Game.OwnedDataReady); Assert.Equal(0, f.NetworkCalls);
    }

    [AvaloniaTheory]
    [InlineData("success")][InlineData("cancel")][InlineData("failure")]
    public async Task Both_writer_authorities_span_callback_drain_and_leave_dispatcher_responsive(string outcome)
    {
        using var f = new OwnedSetupFixture(); await PrepareRetained(f);
        var tx = new PortableInstallationTransaction(f.Live);
        var plan = tx.PreviewRetainedOwnedData(f.Definition, f.Root);
        var dataTx = new PortableInstallationTransaction(Path.GetDirectoryName(plan.Destination)!);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var task = Task.Run(() => tx.AdoptRetainedOwnedDataAsync(f.Definition, f.Root, plan, async () =>
        {
            entered.TrySetResult(); await release.Task;
            if (outcome == "failure") throw new IOException("Synthetic publication failure");
            return !cancellation.IsCancellationRequested;
        }, cancellation.Token));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (outcome == "cancel") cancellation.Cancel();
            Assert.False(task.IsCompleted);
            Assert.ThrowsAny<IOException>(() => { using var lease = tx.AcquireLease(); });
            Assert.ThrowsAny<IOException>(() => { using var lease = dataTx.AcquireLease(); });
            var responsive = false; await Dispatcher.UIThread.InvokeAsync(() => responsive = true); Assert.True(responsive);
            release.TrySetResult(); var result = await task;
            Assert.True(result.ReceiptConfirmed); Assert.Equal(outcome == "success", result.ReadyPublished);
            using (tx.AcquireLease()) { }
            using (dataTx.AcquireLease()) { }
            Assert.Equal(0, f.NetworkCalls);
        }
        finally { release.TrySetResult(); await task; }
    }

    [AvaloniaFact]
    public async Task Closed_participation_keeps_committed_receipt_without_publishing_ready()
    {
        using var f = new OwnedSetupFixture(); await PrepareRetained(f);
        var setup = f.Setup(); var plan = setup.PreviewRetained(CancellationToken.None);
        var result = await Task.Run(() => setup.AdoptRetainedAsync(plan, () => false, CancellationToken.None));
        Assert.True(result.ReceiptConfirmed); Assert.True(result.ReceiptCreated); Assert.False(result.ReadyPublished);
        Assert.True(File.Exists(Receipt(plan))); Assert.False(f.Game.OwnedDataReady); Assert.Equal(0, f.NetworkCalls);
    }

    [AvaloniaTheory]
    [InlineData("close")][InlineData("context")]
    public async Task Actual_confirmation_close_or_context_change_drains_after_commit_without_ready(string change)
    {
        using var f = new OwnedSetupFixture(); await PrepareRetained(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var setup = new OwnedGameSetupSession(f.Manager, f.Game.InstanceKey, f.Definition, point =>
        {
            if (point == "ReceiptCommitted") { entered.TrySetResult(); release.Wait(); }
        });
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        var dialog = new OwnedGameSetupDialog(model);
        try
        {
            dialog.Show(); dialog.RetainedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            var plan = Assert.IsType<RetainedOwnedDataPreview>(model.RetainedPlan);
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(File.Exists(Receipt(plan))); Assert.False(dialog.Work.IsCompleted);
            var lateChanges = 0;
            if (change == "close") { dialog.Close(); model.PropertyChanged += (_, _) => lateChanges++; }
            else f.Game.SelectedExecutable = "newer-selection";
            var responsive = false; await Dispatcher.UIThread.InvokeAsync(() => responsive = true); Assert.True(responsive);
            release.Set(); await dialog.Work;
            Assert.False(f.Game.OwnedDataReady); Assert.True(File.Exists(Receipt(plan)));
            Assert.Equal(f.Data, File.ReadAllBytes(plan.Destination));
            if (change == "close") { Assert.True(model.IsClosed); Assert.Equal(0, lateChanges); }
            else
            {
                Assert.Equal(OwnedGameSetupUiState.Error, model.State);
                Assert.Contains("receipt is retained", model.Message);
                Assert.Equal("newer-selection", f.Game.SelectedExecutable); Assert.True(model.CanChoose);
            }
            Assert.Equal(0, f.NetworkCalls);
        }
        finally { release.Set(); dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Headered_and_bare_synthetic_NES_body_reuse_binds_complete_retained_bytes(bool header)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        uint crc = uint.MaxValue;
        foreach (var value in f.Data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        f.Definition = f.Definition with { FileName = "synthetic.nes", NesBody = new(
            Convert.ToHexStringLower(SHA1.HashData(f.Data)), crc ^ uint.MaxValue) };
        var original = new byte[f.Data.Length + (header ? 16 : 0)];
        if (header) "NES\u001a"u8.CopyTo(original);
        f.Data.CopyTo(original, header ? 16 : 0); File.WriteAllBytes(f.Source, original);
        var initial = f.Setup(); var imported = initial.Preview(f.Source, CancellationToken.None);
        Assert.True(initial.Apply(imported, CancellationToken.None).Completed);
        await NewGeneration(f); File.Move(f.Source, f.Source + ".unavailable");
        var setup = f.Setup(); var plan = setup.PreviewRetained(CancellationToken.None);
        Assert.Equal(original.Length, plan.Length);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), plan.Sha256);
        var result = await Task.Run(() => setup.AdoptRetainedAsync(plan, () => true, CancellationToken.None));
        Assert.True(result.ReceiptConfirmed); Assert.True(result.ReadyPublished); Assert.True(f.Game.OwnedDataReady);
        setup.ValidateReady(); Assert.Equal(original, File.ReadAllBytes(plan.Destination));
        Assert.Equal(original, File.ReadAllBytes(f.Source + ".unavailable")); Assert.Equal(0, f.NetworkCalls);
    }
}
