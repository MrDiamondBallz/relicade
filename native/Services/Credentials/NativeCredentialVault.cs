using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Relicade.Services.Credentials;

/// <summary>Injectable OS-call boundary. Tests must supply a controlled implementation, not invoke the host vault.</summary>
public interface INativeCredentialApi
{
    string? Read(string service, string account);
    void Store(string service, string account, string value);
    void Delete(string service, string account);
}

public sealed class NativeCredentialVault(INativeCredentialApi api) : ICredentialVault
{
    private Task<T> Run<T>(CredentialProvider provider, Func<string, T> operation, CancellationToken ct)
    {
        var account = CredentialIdentity.Account(provider);
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try { return operation(account); }
            catch (Exception ex) { throw CredentialSession.Sanitize(ex); }
        }, ct);
    }
    public Task<string?> ReadAsync(CredentialProvider provider, CancellationToken cancellationToken) =>
        Run(provider, a => api.Read(CredentialIdentity.Service, a), cancellationToken);
    public Task StoreAsync(CredentialProvider provider, string value, CancellationToken cancellationToken)
    {
        CredentialIdentity.ValidateToken(value);
        return Run(provider, a => { api.Store(CredentialIdentity.Service, a, value); return true; }, cancellationToken);
    }
    public Task DeleteAsync(CredentialProvider provider, CancellationToken cancellationToken) =>
        Run(provider, a => { api.Delete(CredentialIdentity.Service, a); return true; }, cancellationToken);
}

internal sealed class WindowsCredentialApi : INativeCredentialApi
{
    private const uint Generic = 1, LocalMachine = 2;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr pointer);

    public string? Read(string service, string account)
    {
        if (!CredRead(service + "/" + account, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null; // ERROR_NOT_FOUND only, not logon/permission failure.
            throw Failure(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize > 4096 || credential.CredentialBlobSize % 2 != 0) throw new CredentialVaultUnavailableException();
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? "";
        }
        finally { CredFree(pointer); }
    }
    public void Store(string service, string account, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new Credential { Type = Generic, TargetName = service + "/" + account,
                UserName = account, Persist = LocalMachine, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = pointer };
            if (!CredWrite(ref credential, 0)) throw Failure(Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.FreeHGlobal(pointer);
        }
    }
    public void Delete(string service, string account)
    {
        if (!CredDelete(service + "/" + account, Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw Failure(error);
        }
    }
    private static CredentialVaultUnavailableException Failure(int error) => new(error switch
    {
        5 => CredentialVaultFailure.Denied, 1312 => CredentialVaultFailure.Locked, _ => CredentialVaultFailure.Unavailable
    });
}

/// <summary>Generic-password Keychain API; credentials never become command-line arguments.</summary>
internal sealed class MacKeychainCredentialApi : INativeCredentialApi
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, out uint length, out IntPtr data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, uint length, byte[] data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr item);

    private static int Find(string service, string account, out uint length, out IntPtr data, out IntPtr item)
    {
        var s = Encoding.UTF8.GetBytes(service); var a = Encoding.UTF8.GetBytes(account);
        return SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out length, out data, out item);
    }
    public string? Read(string service, string account)
    {
        var status = Find(service, account, out var length, out var data, out var item);
        if (status == -25300) return null;
        if (status != 0) throw Failure(status);
        byte[] bytes = new byte[length <= 2048 ? length : 0];
        try
        {
            if (length > 2048) throw new CredentialVaultUnavailableException();
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    public void Store(string service, string account, string value)
    {
        var status = Find(service, account, out _, out var oldData, out var item);
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            if (status == 0)
                status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes);
            else if (status == -25300)
            {
                var s = Encoding.UTF8.GetBytes(service); var a = Encoding.UTF8.GetBytes(account);
                status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, (uint)bytes.Length, bytes, out item);
            }
            if (status != 0) throw Failure(status);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); if (oldData != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, oldData); if (item != IntPtr.Zero) CFRelease(item); }
    }
    public void Delete(string service, string account)
    {
        var status = Find(service, account, out _, out var data, out var item);
        if (status == -25300) return;
        if (status != 0) throw Failure(status);
        try { status = SecKeychainItemDelete(item); if (status != 0) throw Failure(status); }
        finally { SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    private static CredentialVaultUnavailableException Failure(int status) => new(status switch
    {
        -25308 => CredentialVaultFailure.Locked,
        -25293 or -128 => CredentialVaultFailure.Denied,
        _ => CredentialVaultFailure.Unavailable
    });
}
