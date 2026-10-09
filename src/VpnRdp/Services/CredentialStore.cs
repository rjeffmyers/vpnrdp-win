using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace VpnRdp.Services;

/// <summary>
/// Windows Credential Manager wrapper (the same store "cmdkey" and mstsc use).
/// Secrets are protected by DPAPI under the user's logon and never touch profiles.json.
/// </summary>
public static class CredentialStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    public static string VpnTarget(string profileId) => $"VpnRdp:{profileId}:vpn";

    /// <summary>mstsc looks up saved credentials under TERMSRV/&lt;host&gt; (same as "cmdkey /generic:TERMSRV/host").</summary>
    public static string RdpTarget(string host) => $"TERMSRV/{host}";

    public static void Write(string target, string userName, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = userName,
                CredentialBlob = blobPtr,
                CredentialBlobSize = blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredWrite failed for {target}");
        }
        finally
        {
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <summary>Returns (user, secret) or null when nothing is stored.</summary>
    public static (string User, string Secret)? Read(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND)
                return null;
            throw new Win32Exception(err, $"CredRead failed for {target}");
        }
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            var secret = cred.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2)
                : "";
            return (cred.UserName ?? "", secret);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static void Delete(string target)
    {
        if (!CredDelete(target, CRED_TYPE_GENERIC, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ERROR_NOT_FOUND)
                throw new Win32Exception(err, $"CredDelete failed for {target}");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
