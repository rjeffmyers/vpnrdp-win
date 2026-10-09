using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace VpnRdp.Services;

/// <summary>
/// One-time admin setup: install OpenVPN Community and add the user to "OpenVPN Administrators"
/// so the Interactive Service will run their configs without admin rights afterwards.
///
/// The non-elevated app re-launches its own exe with "--setup ..." via the "runas" verb, so the
/// UAC prompt can be answered by the user or by someone else's admin account. The original
/// user's SID is passed on the command line because the elevated process may be a different account.
/// </summary>
public static class OpenVpnSetup
{
    public const string WingetId = "OpenVPNTechnologies.OpenVPN";
    public const string DownloadPage = "https://openvpn.net/community/";

    public const string SetupArg = "--setup";
    private const string MsiArg = "--msi";
    private const string WingetArg = "--winget";
    private const string SidArg = "--user-sid";

    // Exit codes from the elevated helper.
    public const int ExitOk = 0;
    public const int ExitInstallFailed = 10;
    public const int ExitGroupFailed = 11;
    public const int ExitBadArgs = 12;

    public enum InstallSource { None, Winget, Msi }

    /// <summary>Runs the elevated helper and waits for it. Returns its exit code. Throws if UAC was cancelled.</summary>
    public static async Task<int> RunElevatedAsync(InstallSource source, string? msiPath)
    {
        var userSid = WindowsIdentity.GetCurrent().User!.Value;
        var args = new List<string> { SetupArg, SidArg, userSid };
        if (source == InstallSource.Winget)
            args.Add(WingetArg);
        else if (source == InstallSource.Msi)
        {
            args.Add(MsiArg);
            args.Add(msiPath!);
        }

        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', args.Select(Quote)),
        };
        Log.Info($"Starting elevated setup: {psi.Arguments}");
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start elevated setup");
        await proc.WaitForExitAsync();
        Log.Info($"Elevated setup exited with {proc.ExitCode}");
        return proc.ExitCode;
    }

    /// <summary>Entry point inside the elevated process. Returns the process exit code.</summary>
    public static int RunElevatedHelper(string[] args)
    {
        string? sid = null, msi = null;
        var winget = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case SidArg when i + 1 < args.Length: sid = args[++i]; break;
                case MsiArg when i + 1 < args.Length: msi = args[++i]; break;
                case WingetArg: winget = true; break;
            }
        }
        if (sid is null)
            return ExitBadArgs;

        try
        {
            if (winget && !InstallWithWinget())
                return ExitInstallFailed;
            if (msi is not null && !InstallMsi(msi))
                return ExitInstallFailed;
        }
        catch (Exception ex)
        {
            Log.Error("OpenVPN install failed", ex);
            return ExitInstallFailed;
        }

        var install = OpenVpnLocator.Find();
        if (install is null)
        {
            Log.Error("OpenVPN still not found after install");
            return ExitInstallFailed;
        }

        try
        {
            AddToLocalGroup(install.AdminGroup, new SecurityIdentifier(sid));
            OpenVpnLocator.TryStartInteractiveService();
        }
        catch (Exception ex)
        {
            Log.Error("Adding user to OpenVPN admin group failed", ex);
            return ExitGroupFailed;
        }
        return ExitOk;
    }

    public static bool IsWingetAvailable()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\winget.exe");
        return File.Exists(path) || FindOnPath("winget.exe") is not null;
    }

    private static bool InstallWithWinget()
    {
        // winget downloads from the publisher and verifies the package hash from the community manifest.
        var exitCode = RunAndWait("winget",
            $"install --id {WingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
        // 0 = installed; 0x8A15002B = already installed / no applicable upgrade.
        return exitCode == 0 || exitCode == unchecked((int)0x8A15002B);
    }

    private static bool InstallMsi(string msiPath)
    {
        VerifyPublisher(msiPath);
        var log = Path.Combine(AppPaths.LogDir, "openvpn-msi-install.log");
        var exitCode = RunAndWait("msiexec.exe", $"/i {Quote(msiPath)} /qn /norestart /l*v {Quote(log)}");
        // 3010 = success, reboot required.
        return exitCode == 0 || exitCode == 3010;
    }

    /// <summary>Refuse to run an MSI that isn't Authenticode-signed by OpenVPN Inc.</summary>
    public static void VerifyPublisher(string path)
    {
        X509Certificate2 cert;
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is the simplest way to read the Authenticode signer.
            cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("The installer is not digitally signed.", ex);
        }

        if (!cert.Subject.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The installer is signed by \"{cert.Subject}\", not OpenVPN Inc.");

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        if (!chain.Build(cert))
        {
            var status = string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
            throw new InvalidOperationException($"The installer's signature could not be verified: {status}");
        }
    }

    private static void AddToLocalGroup(string group, SecurityIdentifier member)
    {
        EnsureLocalGroup(group);
        var sidBytes = new byte[member.BinaryLength];
        member.GetBinaryForm(sidBytes, 0);
        var sidPtr = Marshal.AllocHGlobal(sidBytes.Length);
        try
        {
            Marshal.Copy(sidBytes, 0, sidPtr, sidBytes.Length);
            var info = new LOCALGROUP_MEMBERS_INFO_0 { Sid = sidPtr };
            var rc = NetLocalGroupAddMembers(null, group, 0, ref info, 1);
            const int ERROR_MEMBER_IN_ALIAS = 1378;
            if (rc != 0 && rc != ERROR_MEMBER_IN_ALIAS)
                throw new Win32Exception(rc, $"NetLocalGroupAddMembers({group})");
            Log.Info($"User {member} is a member of \"{group}\"");
        }
        finally
        {
            Marshal.FreeHGlobal(sidPtr);
        }
    }

    /// <summary>Some OpenVPN installs (seen with 2.7.x) don't create the admin group; the service still honors it by name.</summary>
    private static void EnsureLocalGroup(string group)
    {
        var info = new LOCALGROUP_INFO_1 { Name = group, Comment = "Members may run any OpenVPN config via the OpenVPN Interactive Service" };
        var rc = NetLocalGroupAdd(null, 1, ref info, out _);
        const int ERROR_ALIAS_EXISTS = 1379, NERR_GroupExists = 2223;
        if (rc == 0)
            Log.Info($"Created local group \"{group}\"");
        else if (rc != ERROR_ALIAS_EXISTS && rc != NERR_GroupExists)
            throw new Win32Exception(rc, $"NetLocalGroupAdd({group})");
    }

    private static int RunAndWait(string file, string arguments)
    {
        Log.Info($"Running: {file} {arguments}");
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Log.Info($"{file} exited with {p.ExitCode} (0x{p.ExitCode:X8})");
        return p.ExitCode;
    }

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(d => Path.Combine(d, exe))
        .FirstOrDefault(File.Exists);

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    [StructLayout(LayoutKind.Sequential)]
    private struct LOCALGROUP_MEMBERS_INFO_0
    {
        public IntPtr Sid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOCALGROUP_INFO_1
    {
        public string Name;
        public string Comment;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupAdd(string? server, int level, ref LOCALGROUP_INFO_1 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupAddMembers(string? server, string group, int level,
        ref LOCALGROUP_MEMBERS_INFO_0 buf, int totalEntries);
}
