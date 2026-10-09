using System.Diagnostics;
using VpnRdp.Models;

namespace VpnRdp.Services;

/// <summary>Writes the .rdp file, stores the RDP password for mstsc, and starts mstsc.exe.</summary>
public static class RdpLauncher
{
    public static string MstscPath => Path.Combine(Environment.SystemDirectory, "mstsc.exe");

    /// <summary>Starts mstsc for the profile. The returned process raises Exited when the session window closes.</summary>
    public static Process Launch(Profile profile)
    {
        var s = profile.Rdp;
        if (string.IsNullOrWhiteSpace(s.Host))
            throw new InvalidOperationException("This profile has no RDP host configured.");

        var rdpPath = Path.Combine(AppPaths.RdpDir, profile.Id + ".rdp");
        File.WriteAllText(rdpPath, RdpFileBuilder.Build(s), System.Text.Encoding.Unicode);

        var psi = new ProcessStartInfo(MstscPath, $"\"{rdpPath}\"") { UseShellExecute = false };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("mstsc.exe did not start.");
        proc.EnableRaisingEvents = true;
        Log.Info($"[{profile.Name}] launched mstsc (pid {proc.Id}) -> {s.Host}");
        return proc;
    }

    /// <summary>
    /// Saves (or removes) the RDP password under TERMSRV/&lt;host&gt;, where mstsc picks it up automatically.
    /// "Ask every time" deletes any stored copy so mstsc shows its own prompt.
    /// </summary>
    public static void StoreCredentials(RdpSettings s, string? password)
    {
        if (string.IsNullOrWhiteSpace(s.Host))
            return;
        var target = CredentialStore.RdpTarget(s.Host);
        if (s.PasswordMode == SecretMode.AskEveryTime)
        {
            CredentialStore.Delete(target);
            return;
        }
        if (password is null)
            return; // unchanged
        var user = string.IsNullOrWhiteSpace(s.Domain) || s.Username.Contains('\\') || s.Username.Contains('@')
            ? s.Username
            : $"{s.Domain}\\{s.Username}";
        CredentialStore.Write(target, user, password);
    }

    /// <summary>Opens mstsc's own dialog listing monitor IDs, to double-check the picker numbering.</summary>
    public static void ShowMstscMonitorIds() =>
        Process.Start(new ProcessStartInfo(MstscPath, "/l") { UseShellExecute = false });
}
