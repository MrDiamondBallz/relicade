namespace Relicade.Services;

public static class SettingsStoreProvider
{
    private static ISettingsStore? _default;

    public static ISettingsStore Default
    {
        get => _default ??= new FileSettingsStore(RelicadePaths.SettingsJsonPath);
        set => _default = value;
    }
}
