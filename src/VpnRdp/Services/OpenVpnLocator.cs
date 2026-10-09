using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace VpnRdp.Services;

public sealed record OpenVpnInstall(
    string ExePath,
    string ConfigDir,
    string AdminGroup,
    string? Version,
    bool InteractiveServiceRunning);

/// <summary>Finds the OpenVPN Community install and checks what the Interactive Service will allow us to do.</summary>
public static class OpenVpnLocator
{
    public const string InteractiveServiceName = "OpenVPNServiceInteractive";
    public const string DefaultAdminGroup = "OpenVPN Administrators";

    public static OpenVpnInstall? Find()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OpenVPN");
        var exe = key?.GetValue("exe_path") as string;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return null;

        var configDir = key!.GetValue("config_dir") as string
                        ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(exe)!)!, "config");
        var group = key.GetValue("ovpn_admin_group") as string;
        if (string.IsNullOrWhiteSpace(group))
            group = DefaultAdminGroup;

        string? version = null;
        try
        {
            version = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
        }
        catch
        {
            // Version is informational only.
        }

        return new OpenVpnInstall(exe, configDir, group, version, IsInteractiveServiceRunning());
    }

    public static bool IsInteractiveServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(InteractiveServiceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false; // not installed
        }
    }

    /// <summary>Starts the Interactive Service if it is installed but stopped (needs admin unless already running).</summary>
    public static void TryStartInteractiveService()
    {
        using var sc = new ServiceController(InteractiveServiceName);
        if (sc.Status != ServiceControllerStatus.Running)
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// The Interactive Service only lets "authorized" users run configs outside the global config dir.
    /// Authorized = member of the built-in Administrators group or of the OpenVPN admin group.
    /// The service checks both the token and the local group database, so we do the same.
    /// </summary>
    public static bool IsCurrentUserAuthorized(OpenVpnInstall install)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User;
        if (userSid is null)
            return false;

        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        SecurityIdentifier? ovpnSid = null;
        try
        {
            ovpnSid = (SecurityIdentifier)new NTAccount(install.AdminGroup).Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            // Group does not exist (very old install) — only admins are authorized then.
        }

        // Token groups (includes deny-only admin SID for UAC-filtered admins).
        foreach (var g in identity.Groups ?? new IdentityReferenceCollection())
        {
            if (g.Equals(adminsSid) || (ovpnSid is not null && g.Equals(ovpnSid)))
                return true;
        }

        // Direct membership in the local group database (catches changes not yet in the token).
        return IsDirectMember(adminsSid, userSid) || (ovpnSid is not null && IsDirectMember(ovpnSid, userSid));
    }

    private static bool IsDirectMember(SecurityIdentifier groupSid, SecurityIdentifier userSid)
    {
        string groupName;
        try
        {
            groupName = ((NTAccount)groupSid.Translate(typeof(NTAccount))).Value;
            var slash = groupName.IndexOf('\\');
            if (slash >= 0)
                groupName = groupName[(slash + 1)..];
        }
        catch
        {
            return false;
        }

        IntPtr buf = IntPtr.Zero;
        try
        {
            var rc = NetLocalGroupGetMembers(null, groupName, 0, out buf, -1, out var read, out _, IntPtr.Zero);
            if (rc != 0 || buf == IntPtr.Zero)
                return false;
            var size = Marshal.SizeOf<IntPtr>();
            for (var i = 0; i < read; i++)
            {
                var sidPtr = Marshal.ReadIntPtr(buf, i * size);
                if (new SecurityIdentifier(sidPtr).Equals(userSid))
                    return true;
            }
            return false;
        }
        finally
        {
            if (buf != IntPtr.Zero)
                NetApiBufferFree(buf);
        }
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(string? server, string group, int level, out IntPtr buf,
        int prefMaxLen, out int entriesRead, out int totalEntries, IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
