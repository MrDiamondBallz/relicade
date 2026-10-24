using System.Runtime.InteropServices;

namespace Relicade.Tests;

internal static class OwnedDataFixturePlatform
{
    internal static void RequireLinuxX64() => Assert.SkipUnless(
        OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
        "This fixture exercises the Linux x64 owned-data adapter with synthetic ELF bytes and POSIX file rules; it does not validate Windows game-data setup.");
}
