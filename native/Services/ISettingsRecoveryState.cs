namespace Relicade.Services;

/// <summary>Existing settings could not be read. Repair the document externally, then retry Load.</summary>
public interface ISettingsRecoveryState
{
    bool IsReadOnly { get; }
    Exception? ReadError { get; }
}

public sealed class SettingsRecoveryRequiredException : IOException
{
    public SettingsRecoveryRequiredException(Exception cause)
        : base("Settings require recovery. Existing settings have not been replaced. Repair or restore the settings file, then retry loading it before saving.", cause) { }
}
