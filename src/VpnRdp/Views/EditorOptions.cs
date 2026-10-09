using VpnRdp.Models;

namespace VpnRdp.Views;

/// <summary>Friendly names for enum-backed combo boxes (SelectedValuePath="Key", DisplayMemberPath="Value").</summary>
public static class EditorOptions
{
    public static KeyValuePair<SecretMode, string>[] SecretModes { get; } =
    {
        new(SecretMode.Save, "Save in Windows Credential Manager"),
        new(SecretMode.AskEveryTime, "Ask every time"),
    };

    public static KeyValuePair<DisplayMode, string>[] DisplayModes { get; } =
    {
        new(DisplayMode.Fullscreen, "Full screen"),
        new(DisplayMode.Windowed, "Window"),
    };

    public static KeyValuePair<MonitorMode, string>[] MonitorModes { get; } =
    {
        new(MonitorMode.Single, "Single monitor"),
        new(MonitorMode.AllMonitors, "All monitors"),
        new(MonitorMode.SelectedMonitors, "Selected monitors (pick below)"),
        new(MonitorMode.Span, "Span one desktop across all monitors"),
    };

    public static KeyValuePair<AudioMode, string>[] AudioModes { get; } =
    {
        new(AudioMode.PlayLocal, "Play on this computer"),
        new(AudioMode.PlayRemote, "Play on remote computer"),
        new(AudioMode.DoNotPlay, "Do not play"),
    };

    public static KeyValuePair<ConnectionSpeed, string>[] Speeds { get; } =
    {
        new(ConnectionSpeed.AutoDetect, "Detect connection quality automatically"),
        new(ConnectionSpeed.Lan, "LAN (10 Mbps or higher)"),
        new(ConnectionSpeed.Wan, "WAN (10 Mbps or higher with high latency)"),
        new(ConnectionSpeed.BroadbandHigh, "High-speed broadband (2 – 10 Mbps)"),
        new(ConnectionSpeed.Satellite, "Satellite (2 – 16 Mbps, high latency)"),
        new(ConnectionSpeed.BroadbandLow, "Low-speed broadband (256 Kbps – 2 Mbps)"),
        new(ConnectionSpeed.Modem, "Modem (56 Kbps)"),
    };

    public static KeyValuePair<int, string>[] KeyboardHooks { get; } =
    {
        new(0, "On this computer"),
        new(1, "On the remote computer"),
        new(2, "Only when using full screen"),
    };

    public static KeyValuePair<int, string>[] AuthLevels { get; } =
    {
        new(2, "Warn me (default)"),
        new(0, "Connect and don't warn me"),
        new(1, "Do not connect"),
    };

    public static KeyValuePair<int, string>[] ScaleFactors { get; } =
    {
        new(0, "Same as this computer"),
        new(100, "100%"),
        new(125, "125%"),
        new(150, "150%"),
        new(175, "175%"),
        new(200, "200%"),
    };

    public static int[] ColorDepths { get; } = { 32, 24, 16, 15 };
}
