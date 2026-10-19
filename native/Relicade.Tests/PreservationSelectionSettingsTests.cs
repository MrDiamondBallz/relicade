using System.Text.Json;
using Relicade.Services;
using Relicade.ViewModels;

namespace Relicade.Tests;

public sealed class PreservationSelectionSettingsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "settings-preservation-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(root, "settings.json");
    public PreservationSelectionSettingsTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    // Reflection keeps the failure-first fixture buildable against a version without the additive recovery API.
    private static bool ReadOnly(FileSettingsStore store) =>
        (bool?)store.GetType().GetProperty("IsReadOnly")?.GetValue(store) ?? false;
    private static Exception? ReadError(FileSettingsStore store) =>
        store.GetType().GetProperty("ReadError")?.GetValue(store) as Exception;
    private static void AssertBlocked(FileSettingsStore store)
    {
        var error = Record.Exception(() => store.Save(store.Current));
        Assert.NotNull(error);
        Assert.IsAssignableFrom<IOException>(error);
        Assert.Equal("SettingsRecoveryRequiredException", error.GetType().Name);
        Assert.NotNull(error.InnerException);
        Assert.True(ReadOnly(store));
        Assert.NotNull(ReadError(store));
    }

    [Theory]
    [InlineData("{unfinished")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"SlotSize\":\"wrong type\"}")]
    public void Existing_invalid_document_is_read_only_preserved_across_save_and_reopen(string bytes)
    {
        File.WriteAllText(SettingsPath, bytes);
        var store = new FileSettingsStore(SettingsPath);
        AssertBlocked(store);
        Assert.Equal(bytes, File.ReadAllText(SettingsPath));
        AssertBlocked(new FileSettingsStore(SettingsPath));
        Assert.Equal(bytes, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Corruption_after_construction_cannot_be_overwritten_and_last_good_survives_failed_reload()
    {
        var store = new FileSettingsStore(SettingsPath);
        store.Save(new AppSettings { SortBy = "LastGood" });
        File.WriteAllText(SettingsPath, "{broken-later");
        store.Load();
        Assert.Equal("LastGood", store.Current.SortBy);
        AssertBlocked(store);
        Assert.Equal("{broken-later", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Save_revalidates_disk_even_without_reload()
    {
        var store = new FileSettingsStore(SettingsPath);
        store.Save(new AppSettings { SortBy = "LastGood" });
        File.WriteAllText(SettingsPath, "null");
        AssertBlocked(store);
        Assert.Equal("null", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void External_valid_repair_requires_load_before_saving_defaults()
    {
        File.WriteAllText(SettingsPath, "{broken");
        var store = new FileSettingsStore(SettingsPath);
        File.WriteAllText(SettingsPath, "{\"SortBy\":\"Restored\",\"FirstStartup\":false}");
        AssertBlocked(store);
        Assert.Equal("Restored", store.Load().SortBy);
        Assert.False(ReadOnly(store));
        Assert.Null(ReadError(store));
        store.Current.SortBy = "Retried";
        store.Save(store.Current);
        Assert.Equal("Retried", new FileSettingsStore(SettingsPath).Load().SortBy);
    }

    [Fact]
    public void Missing_first_use_is_writable_but_disappearance_after_recovery_is_not_a_reset()
    {
        var store = new FileSettingsStore(SettingsPath);
        Assert.False(ReadOnly(store));
        Assert.True(store.Current.FirstStartup);
        store.Save(store.Current);
        File.WriteAllText(SettingsPath, "null");
        store.Load();
        File.Delete(SettingsPath);
        store.Load();
        AssertBlocked(store);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Read_denied_is_not_missing_and_valid_repair_can_be_retried()
    {
        if (OperatingSystem.IsWindows()) return; // Unix permission fixture; Windows ACL gate is separate.
        File.WriteAllText(SettingsPath, "{\"SortBy\":\"Private\"}");
        var mode = File.GetUnixFileMode(SettingsPath);
        try
        {
            File.SetUnixFileMode(SettingsPath, UnixFileMode.None);
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(SettingsPath));
            var store = new FileSettingsStore(SettingsPath);
            AssertBlocked(store);
            Assert.IsAssignableFrom<UnauthorizedAccessException>(ReadError(store));
            File.SetUnixFileMode(SettingsPath, mode);
            Assert.Equal("Private", store.Load().SortBy);
            Assert.False(ReadOnly(store));
        }
        finally { File.SetUnixFileMode(SettingsPath, mode); }
        Assert.Equal("{\"SortBy\":\"Private\"}", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Write_denied_preserves_last_good_and_retry_reopens()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new FileSettingsStore(SettingsPath);
        store.Save(new AppSettings { SortBy = "Original" });
        var bytes = File.ReadAllBytes(SettingsPath);
        var mode = File.GetUnixFileMode(root);
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(root, "control"), "denied"));
            Assert.NotNull(Record.Exception(() => store.Save(new AppSettings { SortBy = "Replacement" })));
            Assert.Equal(bytes, File.ReadAllBytes(SettingsPath));
            Assert.Equal("Original", new FileSettingsStore(SettingsPath).Load().SortBy);
        }
        finally { File.SetUnixFileMode(root, mode); }
        store.Save(new AppSettings { SortBy = "Retried" });
        Assert.Equal("Retried", new FileSettingsStore(SettingsPath).Load().SortBy);
    }

    [Fact]
    public void Concurrent_store_instances_and_relative_path_aliases_leave_a_valid_complete_document()
    {
        Parallel.For(0, 12, i =>
        {
            var path = i % 2 == 0 ? SettingsPath : Path.Combine(root, ".", "settings.json");
            var store = new FileSettingsStore(path);
            store.Save(new AppSettings { SortBy = "Writer-" + i });
        });
        Assert.StartsWith("Writer-", new FileSettingsStore(SettingsPath).Load().SortBy);
        using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
    }

    [Fact]
    public void Settings_model_exposes_recoverable_error_and_never_emits_save_success()
    {
        File.WriteAllText(SettingsPath, "null");
        var model = new SettingsViewModel(new FileSettingsStore(SettingsPath));
        Assert.True((bool?)model.GetType().GetProperty("IsReadOnly")?.GetValue(model) ?? false);
        var message = model.GetType().GetProperty("RecoveryMessage")?.GetValue(model) as string;
        Assert.Contains("recover", message!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(Record.Exception(model.SaveCurrent));
        Assert.Equal("null", File.ReadAllText(SettingsPath));
    }
}
