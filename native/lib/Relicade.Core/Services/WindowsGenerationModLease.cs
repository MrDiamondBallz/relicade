using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Relicade.Core.Services;

// A Windows directory cannot be renamed with an open descendant handle. Open an
// outside hard-link NAME of the same reserved lock FILE instead: FileShare.None
// still excludes legacy writers opening the original in-generation name.
internal static class WindowsGenerationModLease
{
    internal const string AliasDirectoryName = ".relicade-mod-leases-v1";

    internal static FileStream Open(string path, FileMode mode, string installParent, IReadOnlyList<string> protectedTrees)
    {
        if (!OperatingSystem.IsWindows())
            return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None);

        PortableInstallationTransaction.CheckLinks(path);
        Identity expected;
        // Detect an already-active legacy writer before provisioning anything.
        // The stable outside writer remains held by the caller through this gap.
        using (var initial = new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None))
            expected = FileIdentity(initial.SafeFileHandle, directory: false);

        // Only the initial open may report an originally absent optional lock.
        // Once observed, any missing identity is uncertainty, never permission
        // for removal callers to proceed without mod exclusion.
        try { return OpenObserved(path, installParent, protectedTrees, expected); }
        catch (FileNotFoundException error) { throw new IOException("Observed mod coordination disappeared; mutation refused.", error); }
        catch (DirectoryNotFoundException error) { throw new IOException("Observed mod coordination directory disappeared; mutation refused.", error); }
    }

    static FileStream OpenObserved(string path, string installParent, IReadOnlyList<string> protectedTrees, Identity expected)
    {

        var basePath = ChooseBase(expected.Volume, installParent, protectedTrees);
        var directory = Path.Combine(basePath, AliasDirectoryName);
        if (protectedTrees.Any(tree => Within(directory, tree)))
            throw new IOException("Mod coordination must remain outside every installation and transaction tree.");
        PortableInstallationTransaction.CheckLinks(directory);
        Directory.CreateDirectory(directory);
        PortableInstallationTransaction.CheckLinks(directory);
        if (PathIdentity(directory, directory: true).Volume != expected.Volume)
            throw new IOException("Mod coordination must stay on the installation's own filesystem.");
        var alias = Path.Combine(directory, $"{expected.Volume:x16}-{expected.High:x16}{expected.Low:x16}.lock");
        PortableInstallationTransaction.CheckLinks(alias);
        if (!CreateHardLink(alias, path, IntPtr.Zero))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is not (80 or 183))
                throw new IOException("Windows could not establish an exclusive same-file mod lease. No weaker lock or alternate permission path was used.", new Win32Exception(error));
        }
        // Never replace an alias, even when its name looks like the expected ID.
        if (PathIdentity(alias, directory: false) != expected)
            throw new IOException("Existing Windows mod coordination has a different file identity; evidence retained for repair.");

        var held = new FileStream(alias, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (FileIdentity(held.SafeFileHandle, directory: false) != expected ||
                PathIdentity(path, directory: false) != expected)
                throw new IOException("The generation's mod lock changed during lease acquisition; no payload mutation is authorized.");
            return held;
        }
        catch { held.Dispose(); throw; }
    }

    static string ChooseBase(ulong volume, string installParent, IReadOnlyList<string> protectedTrees)
    {
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!protectedTrees.Any(tree => Within(temporary, tree)))
        {
            try
            {
                if (PathIdentity(temporary, directory: true).Volume == volume) return temporary;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            // Access denial and malformed paths propagate. Only known absence or
            // a different volume permits selecting the already-known parent.
        }
        installParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installParent));
        if (protectedTrees.Any(tree => Within(installParent, tree)) || PathIdentity(installParent, directory: true).Volume != volume)
            throw new IOException("No same-filesystem mod coordination location exists outside the protected trees.");
        return installParent;
    }

    static bool Within(string path, string tree)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        tree = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tree));
        var prefix = Path.EndsInDirectorySeparator(tree) ? tree : tree + Path.DirectorySeparatorChar;
        return string.Equals(path, tree, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    readonly record struct Identity(ulong Volume, ulong Low, ulong High);

    static Identity PathIdentity(string path, bool directory)
    {
        PortableInstallationTransaction.CheckLinks(path);
        // Metadata-only access works alongside our exclusive RW lease. Close this
        // descendant handle before any generation rename, and do not follow reparse points.
        using var file = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x00200000 | (directory ? 0x02000000u : 0u), IntPtr.Zero);
        if (file.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 2) throw new FileNotFoundException("Mod coordination path is missing.", path);
            if (error == 3) throw new DirectoryNotFoundException("Mod coordination directory is missing.");
            throw new IOException("Windows could not inspect mod coordination identity.", new Win32Exception(error));
        }
        return FileIdentity(file, directory);
    }

    static Identity FileIdentity(SafeFileHandle file, bool directory)
    {
        if (!GetFileInformation(file, out var info))
            throw new IOException("Windows could not inspect mod coordination identity.", new Win32Exception(Marshal.GetLastPInvokeError()));
        var attributes = (FileAttributes)info.Attributes;
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 ||
            ((attributes & FileAttributes.Directory) != 0) != directory ||
            !directory && (info.SizeHigh != 0 || info.SizeLow != 0))
            throw new IOException("Mod coordination requires a regular empty lock file and unlinked directories.");
        // FILE_ID_INFO carries the full 128-bit ID (legacy 64-bit file indexes
        // are not guaranteed unique on ReFS). Unsupported identity queries refuse;
        // there is no truncated-ID or path-name fallback.
        if (!GetFileId(file, 18, out var identity, (uint)Marshal.SizeOf<FileIdInformation>()))
            throw new IOException("Windows could not establish the full mod coordination identity.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return new(identity.Volume, identity.Low, identity.High);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Information
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileIdInformation { public ulong Volume, Low, High; }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLink(string alias, string existing, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformation(SafeFileHandle file, out Information information);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileId(SafeFileHandle file, int informationClass, out FileIdInformation information, uint size);
}
