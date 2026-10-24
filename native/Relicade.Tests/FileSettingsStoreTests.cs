using FluentAssertions;
using Relicade;
using Relicade.Services;

namespace Relicade.Tests;

public class FileSettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsPath;
    private readonly CredentialVaultTestScope _vault = new();

    public FileSettingsStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Relicade.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsPath = Path.Combine(_tempDirectory, "settings.json");
    }

    public void Dispose()
    {
        _vault.Dispose();
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new FileSettingsStore(_settingsPath);

        store.Current.FirstStartup.Should().BeTrue();
        store.Current.TruncateLibraryCardTitles.Should().BeTrue();
        store.Current.GridCompactCards.Should().BeFalse();
        store.Current.IconFill.Should().BeFalse();
        store.Current.SlotSize.Should().Be(180);
        store.Current.IconSize.Should().Be(124);
        store.Current.ActionButtonSize.Should().Be(36);
        store.Current.LibraryCardTagMaxLines.Should().Be(1);
        store.Current.LibraryCardTagZeroMeansHidden.Should().BeTrue();
        store.Current.IconMargin.Should().Be(0);
        store.Current.SlotTextMargin.Should().Be(0);
        store.Current.HiddenApps.Should().NotBeNull();
        store.Current.GitHubTokenBannerPermanentlyDismissed.Should().BeFalse();
        store.Current.GitHubTokenBannerSnoozedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void Save_and_Load_round_trip_settings()
    {
        var store = new FileSettingsStore(_settingsPath);
        var settings = store.Load();
        settings.GitHubApiToken = "test-token";
        settings.GitLabApiToken = "gitlab-token";
        settings.CodebergApiToken = "codeberg-token";
        settings.SortBy = "Name";
        settings.MouseWheelScrollSpeed = 3;
        settings.TruncateLibraryCardTitles = false;
        settings.ListScope = AppListScope.InstalledOnly;
        settings.ManuallyHiddenApps.Add("folder:TestGame");
        settings.GitHubTokenBannerPermanentlyDismissed = true;
        settings.GitHubTokenBannerSnoozedUntilUtc = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

        store.Save(settings);

        // A fresh runtime without a vault must not recover credentials from general settings.
        var reloaded = new FileSettingsStore(_settingsPath, new Relicade.Services.Credentials.CredentialSession(
            new Relicade.Services.Credentials.UnavailableCredentialVault())).Load();
        reloaded.GitHubApiToken.Should().BeEmpty();
        reloaded.GitLabApiToken.Should().BeEmpty();
        reloaded.CodebergApiToken.Should().BeEmpty();
        File.ReadAllText(_settingsPath).Should().NotContain("test-token").And.NotContain("gitlab-token").And.NotContain("codeberg-token");
        reloaded.SortBy.Should().Be("Name");
        reloaded.MouseWheelScrollSpeed.Should().Be(3);
        reloaded.TruncateLibraryCardTitles.Should().BeFalse();
        reloaded.ListScope.Should().Be(AppListScope.InstalledOnly);
        reloaded.ManuallyHiddenApps.Should().Contain("folder:TestGame");
        reloaded.HiddenApps.Should().BeEmpty();
        reloaded.GitHubTokenBannerPermanentlyDismissed.Should().BeTrue();
        reloaded.GitHubTokenBannerSnoozedUntilUtc.Should().Be(
            new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atomic_save_creates_or_replaces_settings_on_the_current_platform(bool existing)
    {
        if (existing)
            File.WriteAllText(_settingsPath, "{\"FirstStartup\":false,\"SortBy\":\"LastPlayed\"}");
        var store = new FileSettingsStore(_settingsPath);
        store.Current.SortBy = "Name";

        store.Save(store.Current);

        new FileSettingsStore(_settingsPath).Current.SortBy.Should().Be("Name");
        Directory.GetFiles(_tempDirectory, "*.tmp").Should().BeEmpty();
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(_settingsPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void AppSettings_static_methods_use_default_store()
    {
        var original = SettingsStoreProvider.Default;
        try
        {
            SettingsStoreProvider.Default = new FileSettingsStore(_settingsPath);
            var settings = AppSettings.Load();
            settings.AppsPath = "D:\\Apps";
            AppSettings.Save(settings);

            AppSettings.Load().AppsPath.Should().Be("D:\\Apps");
        }
        finally
        {
            SettingsStoreProvider.Default = original;
        }
    }

    [Fact]
    public void Save_handles_concurrent_writes()
    {
        var store = new FileSettingsStore(_settingsPath);
        var settings = store.Load();
        settings.SortBy = "Name";

        Parallel.For(0, 8, i =>
        {
            var copy = store.Load();
            copy.SortBy = $"Thread-{i}";
            store.Save(copy);
        });

        File.Exists(_settingsPath).Should().BeTrue();
        store.Load().SortBy.Should().StartWith("Thread-");
    }
}
