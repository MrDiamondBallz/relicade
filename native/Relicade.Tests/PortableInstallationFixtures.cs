namespace Relicade.Tests;

// Deliberately non-runnable synthetic ELF-shaped bytes. Only header recognition is
// exercised; tests never execute this payload or any downloaded game/installer.
internal static class PortableInstallationFixtures
{
    internal static byte[] NativePayload(string text)
    {
        var bytes = new byte[64 + System.Text.Encoding.UTF8.GetByteCount(text)];
        bytes[0] = 0x7f; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2; bytes[5] = 1; bytes[6] = 1; bytes[16] = 2; bytes[24] = 1;
        System.Text.Encoding.UTF8.GetBytes(text).CopyTo(bytes, 64); return bytes;
    }
}
