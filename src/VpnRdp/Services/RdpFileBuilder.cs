using System.Text;
using VpnRdp.Models;

namespace VpnRdp.Services;

/// <summary>Turns <see cref="RdpSettings"/> into the contents of a .rdp file for mstsc.</summary>
public static class RdpFileBuilder
{
    public static string Build(RdpSettings s)
    {
        // Ordered, case-insensitive on the setting name so ExtraLines can override anything.
        var entries = new List<(string Name, string Type, string Value)>();
        void Set(string name, string type, object value)
        {
            var idx = entries.FindIndex(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var entry = (name, type, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
            if (idx >= 0) entries[idx] = entry;
            else entries.Add(entry);
        }
        void Flag(string name, bool on) => Set(name, "i", on ? 1 : 0);

        var address = s.Port is > 0 and not 3389 ? $"{s.Host}:{s.Port}" : s.Host;
        Set("full address", "s", address);
        if (!string.IsNullOrWhiteSpace(s.Username))
        {
            var user = string.IsNullOrWhiteSpace(s.Domain) || s.Username.Contains('\\') || s.Username.Contains('@')
                ? s.Username
                : $"{s.Domain}\\{s.Username}";
            Set("username", "s", user);
        }
        Flag("prompt for credentials", false);

        // ---- Display ----
        Set("screen mode id", "i", s.DisplayMode == DisplayMode.Fullscreen ? 2 : 1);
        if (s.DisplayMode == DisplayMode.Windowed || s.MonitorMode == MonitorMode.Single)
        {
            Set("desktopwidth", "i", s.Width);
            Set("desktopheight", "i", s.Height);
        }
        switch (s.MonitorMode)
        {
            case MonitorMode.Single:
                // mstsc only honors selectedmonitors with multimon on; one entry = full screen on that monitor.
                if (s.SelectedMonitors.Count == 1 && s.DisplayMode == DisplayMode.Fullscreen)
                {
                    Flag("use multimon", true);
                    Set("selectedmonitors", "s", s.SelectedMonitors[0]);
                }
                else
                {
                    Flag("use multimon", false);
                }
                break;
            case MonitorMode.AllMonitors:
                Flag("use multimon", true);
                break;
            case MonitorMode.SelectedMonitors:
                Flag("use multimon", true);
                if (s.SelectedMonitors.Count > 0)
                    Set("selectedmonitors", "s", string.Join(',', s.SelectedMonitors));
                break;
            case MonitorMode.Span:
                Flag("use multimon", false);
                Flag("span monitors", true);
                break;
        }
        Flag("dynamic resolution", s.DynamicResolution);
        Flag("smart sizing", s.SmartSizing);
        if (s.DesktopScaleFactor > 0)
            Set("desktopscalefactor", "i", s.DesktopScaleFactor);
        Set("session bpp", "i", s.ColorDepth);
        Flag("displayconnectionbar", s.ShowConnectionBar);

        // ---- Local resources ----
        Set("audiomode", "i", (int)s.Audio);
        Flag("audiocapturemode", s.Microphone);
        Flag("redirectclipboard", s.Clipboard);
        Flag("redirectprinters", s.Printers);
        Set("drivestoredirect", "s", s.Drives ? "*" : "");
        Flag("redirectsmartcards", s.SmartCards);
        Set("keyboardhook", "i", s.KeyboardHook);

        // ---- Experience ----
        Set("connection type", "i", (int)s.Speed);
        Flag("networkautodetect", s.Speed == ConnectionSpeed.AutoDetect);
        Flag("bandwidthautodetect", true);
        Flag("disable wallpaper", !s.Wallpaper);
        Flag("allow font smoothing", s.FontSmoothing);
        Flag("allow desktop composition", s.DesktopComposition);
        Flag("bitmapcachepersistenable", s.BitmapCaching);
        Flag("autoreconnection enabled", s.AutoReconnect);

        // ---- Advanced ----
        Flag("administrative session", s.AdminSession);
        Set("authentication level", "i", s.AuthenticationLevel);
        if (!string.IsNullOrWhiteSpace(s.GatewayHost))
        {
            Set("gatewayhostname", "s", s.GatewayHost);
            Set("gatewayusagemethod", "i", 1);
            Set("gatewayprofileusagemethod", "i", 1);
            Flag("promptcredentialonce", true);
        }
        else
        {
            Set("gatewayusagemethod", "i", 0);
        }

        foreach (var raw in (s.ExtraLines ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (TryParseLine(line, out var name, out var type, out var value))
                Set(name, type, value);
        }

        var sb = new StringBuilder();
        foreach (var (name, type, value) in entries)
            sb.Append(name).Append(':').Append(type).Append(':').Append(value).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Parses "name:type:value" (value may contain colons).</summary>
    internal static bool TryParseLine(string line, out string name, out string type, out string value)
    {
        name = type = value = "";
        var first = line.IndexOf(':');
        if (first <= 0)
            return false;
        var second = line.IndexOf(':', first + 1);
        if (second < 0)
            return false;
        name = line[..first].Trim();
        type = line[(first + 1)..second].Trim();
        value = line[(second + 1)..];
        return type is "i" or "s" or "b";
    }
}
