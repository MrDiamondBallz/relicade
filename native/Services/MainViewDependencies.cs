namespace Relicade.Services;

/// <summary>Host composition options. Injected services remain owned by their caller.</summary>
public sealed class MainViewDependencies
{
    public ISettingsStore SettingsStore { get; init; } = SettingsStoreProvider.Default;
    public GameManager? GameManager { get; init; }
    /// <summary>Optional host URL dispatch boundary; null uses the normal operating-system launcher.</summary>
    public Action<string>? OpenExternalUrl { get; init; }
    public bool EnableInput { get; init; } = true;
    public bool EnableMusic { get; init; } = true;
    public bool InitializeOnOpen { get; init; } = true;
}
