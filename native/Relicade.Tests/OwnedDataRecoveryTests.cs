using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Relicade.Core.Services;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;

namespace Relicade.Tests;

public sealed class OwnedDataRecoveryTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_setup_initialization_keeps_changed_import_available_for_review(bool changed)
    {
        using var f = new OwnedSetupFixture(); await f.Initialize();
        f.Definition = f.Definition with { FileName = "freedoom2.wad" };
        var setup = f.Setup(); var plan = setup.Preview(f.Source, CancellationToken.None);
        setup.Apply(plan, CancellationToken.None);
        if (changed) File.WriteAllBytes(plan.Destination, [40, 30, 20, 10]);
        var retained = File.ReadAllBytes(plan.Destination);
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, setup);
        OwnedGameSetupDialog? dialog = null;
        try
        {
            // This is the production composition called before its dialog opens,
            // not a copied readiness callback or an acquired engine launch.
            await OwnedGameSetupFlow.InitializeFreedoomAsync(model, setup, session.Token,
                _ => throw new InvalidOperationException("No launch was requested"), null);
            dialog = new OwnedGameSetupDialog(model); dialog.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(!changed, model.CanPlay); Assert.Equal(changed, model.CanChoose);
            Assert.Equal(retained, File.ReadAllBytes(plan.Destination));
            Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog?.Close(); if (dialog != null) await dialog.Work; model.Close(); model.DisposeAfterDrain(); }
    }

    sealed class ChangedData(OwnedSetupFixture fixture, OwnedGameSetupSession setup, OwnedDataPreview original) : IDisposable
    {
        internal OwnedSetupFixture Fixture => fixture;
        internal OwnedGameSetupSession Setup => setup;
        internal OwnedDataPreview Original => original;
        internal byte[] Old { get; } = [40, 30, 20, 10];
        internal string Receipt => Path.Combine(Path.GetDirectoryName(original.Destination)!, original.OperationId + ".json");
        public void Dispose() => fixture.Dispose();
    }
    static async Task<ChangedData> CreateChanged()
    {
        var f = new OwnedSetupFixture();
        try
        {
            await f.Initialize();
            f.Definition = f.Definition with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(f.Data)) };
            var setup = f.Setup(); var original = setup.Preview(f.Source, CancellationToken.None);
            setup.Apply(original, CancellationToken.None);
            var result = new ChangedData(f, setup, original); File.WriteAllBytes(original.Destination, result.Old);
            return result;
        }
        catch { f.Dispose(); throw; }
    }

    [AvaloniaFact]
    public async Task Actual_dialog_previews_preservation_and_requires_confirm_before_replacement()
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        var receipt = File.ReadAllBytes(c.Receipt); var starts = 0;
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, c.Setup);
        await OwnedGameSetupFlow.InitializeFreedoomAsync(model, c.Setup, session.Token,
            _ => { starts++; return Task.FromResult(true); }, null);
        var dialog = new OwnedGameSetupDialog(model); model.PickFile = () => Task.FromResult<string?>(f.Source);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs(); Assert.False(model.CanPlay);
            dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            var repair = Assert.IsType<OwnedDataRepair>(model.Plan!.Repair);
            Assert.Equal(c.Old, File.ReadAllBytes(c.Original.Destination)); Assert.False(File.Exists(repair.RetainedDestination));
            Assert.Contains("replace", dialog.ConfirmButton.Content!.ToString()); Assert.True(dialog.CloseButton.IsDefault);
            Assert.False(dialog.ConfirmButton.IsDefault); Assert.Equal(f.Data.Length + c.Old.Length, model.Plan.BytesToCopy);
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Completed, model.State); Assert.True(model.CanPlay);
            Assert.Equal(f.Data, File.ReadAllBytes(c.Original.Destination)); Assert.Equal(c.Old, File.ReadAllBytes(repair.RetainedDestination));
            Assert.Equal(receipt, File.ReadAllBytes(c.Receipt)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
            Assert.Equal(0, starts); Assert.Equal(0, f.NetworkCalls); c.Setup.ValidateReady();
            Assert.Contains(repair.RetainedDestination, model.Message);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [AvaloniaTheory]
    [InlineData(760, 660, false)][InlineData(320, 360, false)]
    [InlineData(760, 660, true)][InlineData(320, 360, true)]
    public async Task Replacement_confirmation_stays_visible_and_operable_at_minimum_size(int width, int height, bool keyboard)
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        await using var session = new LauncherSession();
        var model = new OwnedGameSetupViewModel(session, f.Game.InstanceKey, c.Setup);
        await OwnedGameSetupFlow.InitializeFreedoomAsync(model, c.Setup, session.Token,
            _ => throw new Exception("No launch requested"), null);
        var dialog = new OwnedGameSetupDialog(model) { Width = width, Height = height };
        model.PickFile = () => Task.FromResult<string?>(f.Source);
        try
        {
            dialog.Show(); dialog.ChooseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var button in new[] { dialog.ConfirmButton, dialog.CloseButton })
            {
                var point = button.TranslatePoint(default, dialog); Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, dialog.ClientSize.Width - button.Bounds.Width);
                Assert.InRange(point.Value.Y, 0, dialog.ClientSize.Height - button.Bounds.Height);
            }
            if (keyboard)
            {
                Assert.True(dialog.ConfirmButton.Focus());
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else
            {
                var nav = GamepadModalDialogNavigation.Instance;
                Assert.True(nav.FocusDialogControl(dialog.ConfirmButton)); Assert.True(nav.TryHandleConfirm());
            }
            await dialog.Work; Assert.Equal(OwnedGameSetupUiState.Completed, model.State);
            Assert.Equal(f.Data, File.ReadAllBytes(c.Original.Destination));
            Assert.Equal(c.Old, File.ReadAllBytes(model.Plan!.Repair!.RetainedDestination)); Assert.Equal(0, f.NetworkCalls);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }

    [Theory]
    [InlineData("missing")][InlineData("malformed")][InlineData("hash")][InlineData("generation")]
    [InlineData("unknown")][InlineData("duplicate")][InlineData("oversized")]
    public async Task Changed_copy_without_exact_current_receipt_cannot_authorize_replacement(string fault)
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        if (fault == "missing") File.Delete(c.Receipt);
        else if (fault == "malformed") File.WriteAllText(c.Receipt, "{");
        else if (fault == "oversized") File.WriteAllText(c.Receipt, new string('x', 4097));
        else if (fault == "duplicate") File.WriteAllText(c.Receipt, File.ReadAllText(c.Receipt).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,"));
        else
        {
            var node = JsonNode.Parse(File.ReadAllText(c.Receipt))!;
            if (fault == "hash") node["sha256"] = new string('0', 64);
            if (fault == "generation") node["operationId"] = new string('a', 32);
            if (fault == "unknown") node["unrecognized"] = true;
            File.WriteAllText(c.Receipt, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        var before = Directory.GetFiles(Path.GetDirectoryName(c.Receipt)!).ToDictionary(x => x, File.ReadAllBytes);
        var error = Record.Exception(() => c.Setup.Preview(f.Source, CancellationToken.None));
        Assert.True(error is IOException or JsonException, error?.ToString());
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(Path.GetDirectoryName(c.Receipt)!).Order());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
    }

    [Theory]
    [InlineData("data")][InlineData("source")][InlineData("receipt")][InlineData("backup")]
    [InlineData("library")][InlineData("kiosk")][InlineData("engine")]
    public async Task Actual_apply_revalidates_the_approved_replacement_and_context(string change)
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        var plan = c.Setup.Preview(f.Source, CancellationToken.None); var repair = plan.Repair!;
        if (change == "data") File.WriteAllBytes(plan.Destination, [8, 7, 6, 5]);
        if (change == "source") File.WriteAllBytes(f.Source, c.Old);
        if (change == "receipt") File.AppendAllText(c.Receipt, " ");
        if (change == "backup") File.WriteAllBytes(repair.RetainedDestination, [9]);
        if (change == "library") File.AppendAllText(f.Manager.CatalogService.AppsConfigPath, " ");
        if (change == "kiosk") f.Store.Current.KioskMode = true;
        if (change == "engine") File.AppendAllText(Path.Combine(f.Live, "fixture.AppImage"), "changed");
        var data = File.ReadAllBytes(plan.Destination); var receipt = File.ReadAllBytes(c.Receipt);
        Assert.ThrowsAny<IOException>(() => c.Setup.Apply(plan, CancellationToken.None));
        Assert.Equal(data, File.ReadAllBytes(plan.Destination)); Assert.Equal(receipt, File.ReadAllBytes(c.Receipt));
        if (change == "backup") Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(repair.RetainedDestination));
        else Assert.False(File.Exists(repair.RetainedDestination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(plan.Destination)!, "*.tmp")); Assert.Equal(0, f.NetworkCalls);
    }

    [Theory]
    [InlineData("BeforeRepairBackup", false, false)]
    [InlineData("BackupCommitted", false, true)]
    [InlineData("BeforeRepairCommit", false, true)]
    [InlineData("DataCommitted", true, true)]
    [InlineData("BeforeReceipt", true, true)]
    public async Task Failure_preserves_old_bytes_and_reports_postcommit_state_truthfully(string point, bool committed, bool backupExists)
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        var plan = c.Setup.Preview(f.Source, CancellationToken.None); var receipt = File.ReadAllBytes(c.Receipt);
        var tx = new PortableInstallationTransaction(f.Live);
        var error = Assert.ThrowsAny<IOException>(() => tx.ImportOwnedData(f.Definition, f.Root, plan,
            boundary: reached => { if (reached == point) throw new IOException("Synthetic replacement boundary"); }));
        Assert.Contains("may already be committed", error.Message);
        Assert.Equal(committed ? f.Data : c.Old, File.ReadAllBytes(plan.Destination));
        Assert.Equal(backupExists, File.Exists(plan.Repair!.RetainedDestination));
        if (backupExists) Assert.Equal(c.Old, File.ReadAllBytes(plan.Repair.RetainedDestination));
        Assert.Equal(receipt, File.ReadAllBytes(c.Receipt)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(plan.Destination)!, "*.tmp"));
        // Recovery is a new inspection/approval, never blind reuse of the old plan.
        var fresh = c.Setup.Preview(f.Source, CancellationToken.None);
        c.Setup.Apply(fresh, CancellationToken.None); c.Setup.ValidateReady();
        Assert.Equal(f.Data, File.ReadAllBytes(plan.Destination)); Assert.Equal(c.Old, File.ReadAllBytes(plan.Repair.RetainedDestination));
        Assert.Equal(0, f.NetworkCalls);
    }

    [Theory]
    [InlineData("CopyingBackup", false, false)]
    [InlineData("BackupCommitted", false, true)]
    [InlineData("BeforeRepairCommit", false, true)]
    [InlineData("DataCommitted", true, true)]
    public async Task Cancellation_drains_without_deleting_retained_or_committed_data(string point, bool committed, bool backupExists)
    {
        using var c = await CreateChanged(); var f = c.Fixture; using var cancel = new CancellationTokenSource();
        var plan = c.Setup.Preview(f.Source, CancellationToken.None); var copyCalls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => new PortableInstallationTransaction(f.Live).ImportOwnedData(f.Definition, f.Root, plan,
            cancel.Token, reached => { if (reached == "Copying") copyCalls++; if (reached == point || point == "CopyingBackup" && reached == "Copying" && copyCalls == 2) cancel.Cancel(); }));
        Assert.Equal(committed ? f.Data : c.Old, File.ReadAllBytes(plan.Destination));
        Assert.Equal(backupExists, File.Exists(plan.Repair!.RetainedDestination));
        if (backupExists) Assert.Equal(c.Old, File.ReadAllBytes(plan.Repair.RetainedDestination));
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(plan.Destination)!, "*.tmp"));
        var fresh = c.Setup.Preview(f.Source, CancellationToken.None); c.Setup.Apply(fresh, CancellationToken.None);
        c.Setup.ValidateReady(); Assert.Equal(c.Old, File.ReadAllBytes(plan.Repair.RetainedDestination)); Assert.Equal(0, f.NetworkCalls);
    }

    [DllImport("libc", SetLastError = true)] static extern int link(string original, string destination);

    [Theory]
    [InlineData("data", "symlink")][InlineData("data", "hardlink")][InlineData("data", "directory")]
    [InlineData("receipt", "symlink")][InlineData("receipt", "hardlink")][InlineData("receipt", "directory")]
    [InlineData("backup", "symlink")][InlineData("backup", "hardlink")][InlineData("backup", "directory")]
    public async Task Replacement_refuses_linked_or_nonregular_evidence_and_retains_external_bytes(string target, string kind)
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        var plan = c.Setup.Preview(f.Source, CancellationToken.None);
        var path = target == "data" ? plan.Destination : target == "receipt" ? c.Receipt : plan.Repair!.RetainedDestination;
        var external = Path.Combine(f.Root, "external-retained");
        if (target == "backup") File.WriteAllBytes(external, c.Old); else File.Move(path, external);
        var bytes = File.ReadAllBytes(external);
        if (kind == "symlink") File.CreateSymbolicLink(path, external);
        if (kind == "hardlink") Assert.Equal(0, link(external, path));
        if (kind == "directory") Directory.CreateDirectory(path);
        var error = Record.Exception(() => c.Setup.Apply(plan, CancellationToken.None));
        Assert.True(error is IOException or InvalidDataException, error?.ToString());
        Assert.Equal(bytes, File.ReadAllBytes(external)); Assert.Equal(f.Data, File.ReadAllBytes(f.Source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(plan.Destination)!, "*.tmp")); Assert.Equal(0, f.NetworkCalls);
    }

    [Theory]
    [InlineData("data")][InlineData("receipt")][InlineData("backup")]
    public async Task Mutation_after_preservation_refuses_replacement_without_erasing_either_copy(string target)
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        var plan = c.Setup.Preview(f.Source, CancellationToken.None); var changed = new byte[] { 1, 3, 5, 7 };
        Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(f.Live).ImportOwnedData(f.Definition, f.Root, plan,
            boundary: point =>
            {
                if (point != "BeforeRepairCommit") return;
                if (target == "receipt") File.AppendAllText(c.Receipt, " ");
                else File.WriteAllBytes(target == "data" ? plan.Destination : plan.Repair!.RetainedDestination, changed);
            }));
        Assert.Equal(target == "data" ? changed : c.Old, File.ReadAllBytes(plan.Destination));
        Assert.Equal(target == "backup" ? changed : c.Old, File.ReadAllBytes(plan.Repair!.RetainedDestination));
        Assert.Equal(f.Data, File.ReadAllBytes(f.Source)); Assert.Equal(0, f.NetworkCalls);
    }

    [Fact]
    public async Task Preservation_and_commit_keep_install_and_data_writer_authority_until_drain()
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        var plan = c.Setup.Preview(f.Source, CancellationToken.None); var observed = new List<string>();
        new PortableInstallationTransaction(f.Live).ImportOwnedData(f.Definition, f.Root, plan, boundary: point =>
        {
            if (point is not ("BeforeRepairBackup" or "BackupCommitted" or "BeforeRepairCommit" or "DataCommitted")) return;
            observed.Add(point);
            Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(f.Live).AcquireLease());
            Assert.ThrowsAny<IOException>(() => new PortableInstallationTransaction(Path.GetDirectoryName(plan.Destination)!).AcquireLease());
        });
        Assert.Equal(new[] { "BeforeRepairBackup", "BackupCommitted", "BeforeRepairCommit", "DataCommitted" }, observed);
        using var install = new PortableInstallationTransaction(f.Live).AcquireLease();
        using var data = new PortableInstallationTransaction(Path.GetDirectoryName(plan.Destination)!).AcquireLease();
        Assert.Equal(c.Old, File.ReadAllBytes(plan.Repair!.RetainedDestination)); Assert.Equal(0, f.NetworkCalls);
    }

    [Theory]
    [InlineData(0)][InlineData(5)][InlineData(8)]
    public async Task Changed_size_within_bound_is_preserved_and_replacement_can_complete(int length)
    {
        using var c = await CreateChanged(); var f = c.Fixture; var changed = Enumerable.Repeat((byte)7, length).ToArray();
        File.WriteAllBytes(c.Original.Destination, changed);
        var plan = c.Setup.Preview(f.Source, CancellationToken.None);
        Assert.Equal(length + f.Data.Length, plan.BytesToCopy);
        c.Setup.Apply(plan, CancellationToken.None); c.Setup.ValidateReady();
        Assert.Equal(changed, File.ReadAllBytes(plan.Repair!.RetainedDestination)); Assert.Equal(0, f.NetworkCalls);
    }

    [Fact]
    public async Task Oversized_changed_data_is_preserved_without_unbounded_inspection_or_replacement()
    {
        using var c = await CreateChanged(); var f = c.Fixture;
        File.WriteAllBytes(c.Original.Destination, new byte[9]);
        Assert.ThrowsAny<IOException>(() => c.Setup.Preview(f.Source, CancellationToken.None));
        Assert.Equal(new byte[9], File.ReadAllBytes(c.Original.Destination)); Assert.Equal(0, f.NetworkCalls);
    }
}
