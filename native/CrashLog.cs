using System.Diagnostics;
using System.Text;
using Relicade.Services;

namespace Relicade;

public static class CrashLog
{
    public static void LogFromUiThread(string source, Exception ex) => Log(source, ex);

    public static void Log(string source, Exception ex)
    {
        var message = new StringBuilder();
        message.AppendLine($"[{DateTime.UtcNow:O}] {source}");
        message.AppendLine(ex.ToString());

        try
        {
            RelicadePaths.EnsureUserDataRootExists();
            File.AppendAllText(RelicadePaths.CrashLogPath, message.ToString() + Environment.NewLine);
        }
        catch
        {
            // Best-effort logging only.
        }

        Debug.WriteLine(message.ToString());
        Trace.WriteLine(message.ToString());

        try
        {
            Console.Error.WriteLine(message.ToString());
        }
        catch
        {
            // WinExe / Android may have no console attached.
        }
    }
}
