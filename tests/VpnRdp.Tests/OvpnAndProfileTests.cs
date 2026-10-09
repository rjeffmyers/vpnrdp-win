using VpnRdp.Models;
using VpnRdp.Services;

namespace VpnRdp.Tests;

public class OvpnAndProfileTests
{
    [Fact]
    public void InspectFindsRemoteAuthAndFiles()
    {
        var info = OvpnImporter.InspectText(new[]
        {
            "client",
            "dev tun",
            "remote vpn.example.com 1194",
            "remote backup.example.com 443",
            "auth-user-pass",
            "ca ca.crt",
            "cert \"my cert.crt\"",
            "key keys/client.key",
            "# tls-auth ignored.key 1",
            "<tls-crypt>",
            "-----BEGIN OpenVPN Static key V1-----",
            "key not-a-directive",
            "</tls-crypt>",
        });
        Assert.True(info.NeedsUserPass);
        Assert.Equal("vpn.example.com:1194", info.Remote);
        Assert.Equal(new[] { "ca.crt", "my cert.crt", "keys\\client.key" }, info.ReferencedFiles);
    }

    [Fact]
    public void AuthUserPassWithFileDoesNotPrompt()
    {
        var info = OvpnImporter.InspectText(new[] { "auth-user-pass creds.txt", "remote h" });
        Assert.False(info.NeedsUserPass);
        Assert.Contains("creds.txt", info.ReferencedFiles);
    }

    [Fact]
    public void TokenizeHandlesQuotes()
    {
        Assert.Equal(new[] { "cert", "a b.crt" }, OvpnImporter.Tokenize("cert \"a b.crt\""));
        Assert.Equal(new[] { "cert", "a b.crt" }, OvpnImporter.Tokenize("cert 'a b.crt'"));
        Assert.Equal(new[] { "remote", "h", "1194" }, OvpnImporter.Tokenize("  remote   h\t1194 "));
    }

    [Fact]
    public void ProfileStoreRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vpnrdp-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ProfileStore(path);
            var p = new Profile
            {
                Name = "Office",
                OvpnPath = @"C:\x\office.ovpn",
                VpnUsername = "jeff",
                Rdp = { Host = "vm01", MonitorMode = MonitorMode.SelectedMonitors, SelectedMonitors = { 0, 1 } },
            };
            store.Save(new[] { p });
            var loaded = Assert.Single(store.Load());
            Assert.Equal(p.Id, loaded.Id);
            Assert.Equal("Office", loaded.Name);
            Assert.Equal(MonitorMode.SelectedMonitors, loaded.Rdp.MonitorMode);
            Assert.Equal(new[] { 0, 1 }, loaded.Rdp.SelectedMonitors);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
