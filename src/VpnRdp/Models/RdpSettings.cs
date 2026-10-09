namespace VpnRdp.Models;

public enum DisplayMode
{
    Fullscreen,
    Windowed,
}

public enum MonitorMode
{
    /// <summary>Use only the primary (or the single selected) monitor.</summary>
    Single,
    /// <summary>Use every monitor (use multimon:i:1, no selection).</summary>
    AllMonitors,
    /// <summary>Use the monitors listed in <see cref="RdpSettings.SelectedMonitors"/>.</summary>
    SelectedMonitors,
    /// <summary>Span one large desktop across all monitors (span monitors:i:1).</summary>
    Span,
}

public enum AudioMode
{
    PlayLocal = 0,
    PlayRemote = 1,
    DoNotPlay = 2,
}

public enum ConnectionSpeed
{
    AutoDetect = 7,
    Lan = 6,
    Wan = 5,
    BroadbandHigh = 4,
    Satellite = 3,
    BroadbandLow = 2,
    Modem = 1,
}

/// <summary>Everything that ends up in the generated .rdp file.</summary>
public sealed class RdpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public SecretMode PasswordMode { get; set; } = SecretMode.Save;

    // ---- Display ----
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Fullscreen;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Single;
    /// <summary>mstsc monitor IDs (as shown by "mstsc /l").</summary>
    public List<int> SelectedMonitors { get; set; } = new();
    public bool DynamicResolution { get; set; } = true;
    public bool SmartSizing { get; set; }
    /// <summary>0 = don't set; otherwise 100/125/150/175/200...</summary>
    public int DesktopScaleFactor { get; set; }
    public int ColorDepth { get; set; } = 32;
    public bool ShowConnectionBar { get; set; } = true;

    // ---- Local resources ----
    public AudioMode Audio { get; set; } = AudioMode.PlayLocal;
    public bool Microphone { get; set; }
    public bool Clipboard { get; set; } = true;
    public bool Printers { get; set; }
    public bool Drives { get; set; }
    public bool SmartCards { get; set; }
    /// <summary>Apply Windows key combos (Alt+Tab etc.) to: 0 local, 1 remote, 2 remote only when fullscreen.</summary>
    public int KeyboardHook { get; set; } = 2;

    // ---- Experience ----
    public ConnectionSpeed Speed { get; set; } = ConnectionSpeed.AutoDetect;
    public bool Wallpaper { get; set; }
    public bool FontSmoothing { get; set; } = true;
    public bool DesktopComposition { get; set; } = true;
    public bool BitmapCaching { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;

    // ---- Advanced ----
    public bool AdminSession { get; set; }
    /// <summary>0 = connect without warning, 1 = don't connect, 2 = warn (mstsc default).</summary>
    public int AuthenticationLevel { get; set; } = 2;
    public string GatewayHost { get; set; } = "";
    /// <summary>Raw "key:type:value" lines appended verbatim; later keys override earlier ones.</summary>
    public string ExtraLines { get; set; } = "";
}
