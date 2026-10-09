namespace VpnRdp.Services;

public static class AppPaths
{
    /// <summary>%APPDATA%\VpnRdp — profiles.json lives here (roams with the user).</summary>
    public static string ConfigDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VpnRdp"));

    /// <summary>%LOCALAPPDATA%\VpnRdp — logs, generated .rdp files, imported .ovpn configs.</summary>
    public static string LocalDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnRdp"));

    public static string ProfilesFile => Path.Combine(ConfigDir, "profiles.json");
    public static string LogDir => Ensure(Path.Combine(LocalDir, "logs"));
    public static string RdpDir => Ensure(Path.Combine(LocalDir, "rdp"));
    public static string OvpnDir => Ensure(Path.Combine(LocalDir, "configs"));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
