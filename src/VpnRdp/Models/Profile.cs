namespace VpnRdp.Models;

public enum SecretMode
{
    /// <summary>Stored in Windows Credential Manager.</summary>
    Save,
    /// <summary>Prompted for on every connect.</summary>
    AskEveryTime,
}

/// <summary>A named connection: an OpenVPN config plus optional RDP target.</summary>
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    // ---- VPN ----
    /// <summary>Imported .ovpn path (inside the app's config folder).</summary>
    public string OvpnPath { get; set; } = "";
    public string VpnUsername { get; set; } = "";
    public SecretMode VpnPasswordMode { get; set; } = SecretMode.Save;

    // ---- RDP ----
    public bool RdpEnabled { get; set; } = true;
    /// <summary>Launch RDP automatically once the VPN reports CONNECTED.</summary>
    public bool AutoRdpAfterConnect { get; set; } = true;
    /// <summary>Drop the VPN when the RDP window is closed.</summary>
    public bool DisconnectVpnWhenRdpCloses { get; set; } = true;
    public RdpSettings Rdp { get; set; } = new();

    public Profile Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<Profile>(json)!;
    }
}
