using VpnRdp.Models;
using VpnRdp.Services;

namespace VpnRdp.Tests;

public class RdpFileBuilderTests
{
    private static Dictionary<string, string> Parse(string rdp) =>
        rdp.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(l =>
            {
                Assert.True(RdpFileBuilder.TryParseLine(l, out var n, out var t, out var v), $"bad line: {l}");
                return (n, v: $"{t}:{v}");
            })
            .ToDictionary(x => x.n, x => x.v, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void DefaultsProduceFullscreenSingleMonitor()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "vm01.corp.local" }));
        Assert.Equal("s:vm01.corp.local", d["full address"]);
        Assert.Equal("i:2", d["screen mode id"]);
        Assert.Equal("i:0", d["use multimon"]);
        Assert.False(d.ContainsKey("selectedmonitors"));
        Assert.Equal("i:0", d["prompt for credentials"]);
    }

    [Fact]
    public void NonDefaultPortAndDomainUser()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "10.0.0.5", Port = 3390, Username = "jeff", Domain = "CORP" }));
        Assert.Equal("s:10.0.0.5:3390", d["full address"]);
        Assert.Equal("s:CORP\\jeff", d["username"]);
    }

    [Fact]
    public void UpnUsernameIgnoresDomain()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "h", Username = "jeff@corp.com", Domain = "CORP" }));
        Assert.Equal("s:jeff@corp.com", d["username"]);
    }

    [Fact]
    public void SelectedMonitors()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings
        {
            Host = "h",
            MonitorMode = MonitorMode.SelectedMonitors,
            SelectedMonitors = { 0, 2 },
        }));
        Assert.Equal("i:1", d["use multimon"]);
        Assert.Equal("s:0,2", d["selectedmonitors"]);
    }

    [Fact]
    public void AllMonitorsHasNoSelection()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "h", MonitorMode = MonitorMode.AllMonitors, SelectedMonitors = { 1 } }));
        Assert.Equal("i:1", d["use multimon"]);
        Assert.False(d.ContainsKey("selectedmonitors"));
    }

    [Fact]
    public void SingleSelectedMonitor()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "h", MonitorMode = MonitorMode.Single, SelectedMonitors = { 3 } }));
        Assert.Equal("i:1", d["use multimon"]);
        Assert.Equal("s:3", d["selectedmonitors"]);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 1)]
    [InlineData(@"\\.\DISPLAY12", 12)]
    [InlineData("garbage", null)]
    public void ParsesDisplayNumber(string device, int? expected) =>
        Assert.Equal(expected, MonitorEnumerator.ParseDisplayNumber(device));

    [Fact]
    public void Span()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "h", MonitorMode = MonitorMode.Span }));
        Assert.Equal("i:1", d["span monitors"]);
        Assert.Equal("i:0", d["use multimon"]);
    }

    [Fact]
    public void WindowedSetsSize()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings { Host = "h", DisplayMode = DisplayMode.Windowed, Width = 1600, Height = 900 }));
        Assert.Equal("i:1", d["screen mode id"]);
        Assert.Equal("i:1600", d["desktopwidth"]);
        Assert.Equal("i:900", d["desktopheight"]);
    }

    [Fact]
    public void ResourcesAndGateway()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings
        {
            Host = "h",
            Drives = true,
            Clipboard = false,
            Audio = AudioMode.DoNotPlay,
            GatewayHost = "gw.corp.com",
            AdminSession = true,
        }));
        Assert.Equal("s:*", d["drivestoredirect"]);
        Assert.Equal("i:0", d["redirectclipboard"]);
        Assert.Equal("i:2", d["audiomode"]);
        Assert.Equal("s:gw.corp.com", d["gatewayhostname"]);
        Assert.Equal("i:1", d["gatewayusagemethod"]);
        Assert.Equal("i:1", d["administrative session"]);
    }

    [Fact]
    public void ExtraLinesOverrideAndAppend()
    {
        var d = Parse(RdpFileBuilder.Build(new RdpSettings
        {
            Host = "h",
            ExtraLines = "screen mode id:i:1\r\ncamerastoredirect:s:*\r\ngarbage line\r\nalternate shell:s:C:\\tools\\app.exe",
        }));
        Assert.Equal("i:1", d["screen mode id"]);
        Assert.Equal("s:*", d["camerastoredirect"]);
        Assert.Equal("s:C:\\tools\\app.exe", d["alternate shell"]);
        Assert.False(d.ContainsKey("garbage line"));
    }
}
