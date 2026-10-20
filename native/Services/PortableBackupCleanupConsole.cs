using System.Runtime.InteropServices;

namespace Relicade.Services;

/// <summary>Console attachment for the early, original-argument backup-cleanup route only.</summary>
public static class PortableBackupCleanupConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static int Run(Func<int> execute) => Run(execute, OperatingSystem.IsWindows(),
        () => AttachConsole(-1),
        () =>
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }, () => { FreeConsole(); });

    // The OS boundary is injected only for focused lifecycle tests. Failed attach
    // leaves inherited/redirected streams alone, as in the ordinary Desktop CLI.
    internal static int Run(Func<int> execute, bool windows, Func<bool> attach, Action resetStreams, Action detach)
    {
        if (!windows) return execute();
        try
        {
            if (attach()) resetStreams();
            return execute();
        }
        finally { detach(); }
    }
}
