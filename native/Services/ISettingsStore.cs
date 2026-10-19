namespace Relicade.Services;

public interface ISettingsStore
{
    AppSettings Current { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}
