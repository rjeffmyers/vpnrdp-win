using System.Text;
using VpnRdp.Services;

namespace VpnRdp.Tests;

public class ManagementParsingTests
{
    [Fact]
    public void ParsesConnectedState()
    {
        var ev = ManagementEvent.Parse(">STATE:1728470000,CONNECTED,SUCCESS,10.8.0.6,203.0.113.5,1194,,");
        var s = Assert.IsType<StateEvent>(ev);
        Assert.Equal("CONNECTED", s.State);
        Assert.Equal("SUCCESS", s.Description);
        Assert.Equal("10.8.0.6", s.LocalIp);
        Assert.Equal("203.0.113.5", s.RemoteIp);
    }

    [Fact]
    public void ParsesByteCount()
    {
        var b = Assert.IsType<ByteCountEvent>(ManagementEvent.Parse(">BYTECOUNT:12345,678"));
        Assert.Equal(12345, b.BytesIn);
        Assert.Equal(678, b.BytesOut);
    }

    [Fact]
    public void ParsesLogWithCommasInMessage()
    {
        var l = Assert.IsType<LogEvent>(ManagementEvent.Parse(">LOG:1728470000,I,Peer Connection Initiated with [AF_INET]1.2.3.4:1194, ok"));
        Assert.Equal("I", l.Flags);
        Assert.Equal("Peer Connection Initiated with [AF_INET]1.2.3.4:1194, ok", l.Message);
    }

    [Fact]
    public void ParsesAuthRequest()
    {
        var p = Assert.IsType<PasswordRequestEvent>(ManagementEvent.Parse(">PASSWORD:Need 'Auth' username/password"));
        Assert.Equal("Auth", p.Tag);
        Assert.True(p.NeedsUsername);
        Assert.Null(p.Challenge);
    }

    [Fact]
    public void ParsesPrivateKeyRequest()
    {
        var p = Assert.IsType<PasswordRequestEvent>(ManagementEvent.Parse(">PASSWORD:Need 'Private Key' password"));
        Assert.Equal("Private Key", p.Tag);
        Assert.False(p.NeedsUsername);
    }

    [Fact]
    public void ParsesStaticChallenge()
    {
        var p = Assert.IsType<PasswordRequestEvent>(ManagementEvent.Parse(">PASSWORD:Need 'Auth' username/password SC:1,Enter your OTP"));
        Assert.NotNull(p.Challenge);
        Assert.True(p.Challenge!.Echo);
        Assert.False(p.Challenge.Concat);
        Assert.Equal("Enter your OTP", p.Challenge.Text);

        var pw = p.Challenge.BuildPassword("secret", "123456");
        Assert.Equal($"SCRV1:{B64("secret")}:{B64("123456")}", pw);
    }

    [Fact]
    public void StaticChallengeConcatMode()
    {
        var p = Assert.IsType<PasswordRequestEvent>(ManagementEvent.Parse(">PASSWORD:Need 'Auth' username/password SC:2,OTP"));
        Assert.True(p.Challenge!.Concat);
        Assert.False(p.Challenge.Echo);
        Assert.Equal("secret123456", p.Challenge.BuildPassword("secret", "123456"));
    }

    [Fact]
    public void ParsesAuthFailed()
    {
        var f = Assert.IsType<AuthFailedEvent>(ManagementEvent.Parse(">PASSWORD:Verification Failed: 'Auth'"));
        Assert.Equal("Auth", f.Tag);
    }

    [Fact]
    public void ParsesDynamicChallenge()
    {
        var line = $">PASSWORD:Verification Failed: 'Auth' ['CRV1:R,E:Om01u7Fh4LrGBS7uh0SWmzwabUiGiW6l:{B64("jeff")}:Enter PIN: now']";
        var dc = Assert.IsType<DynamicChallengeEvent>(ManagementEvent.Parse(line)).Challenge;
        Assert.True(dc.Echo);
        Assert.True(dc.ResponseRequired);
        Assert.Equal("Om01u7Fh4LrGBS7uh0SWmzwabUiGiW6l", dc.StateId);
        Assert.Equal("jeff", dc.Username);
        Assert.Equal("Enter PIN: now", dc.Text);
        Assert.Equal("CRV1::Om01u7Fh4LrGBS7uh0SWmzwabUiGiW6l::4321", dc.BuildPassword("4321"));
    }

    [Fact]
    public void ParsesWebAuth()
    {
        var w = Assert.IsType<WebAuthEvent>(ManagementEvent.Parse(">INFOMSG:WEB_AUTH::https://sso.example.com/login?x=1"));
        Assert.Equal("https://sso.example.com/login?x=1", w.Url);
    }

    [Fact]
    public void IgnoresCommandReplies()
    {
        Assert.Null(ManagementEvent.Parse("SUCCESS: hold release succeeded"));
        Assert.Null(ManagementEvent.Parse(""));
    }

    [Fact]
    public void QuotesManagementValues()
    {
        Assert.Equal("\"pa\\\"ss\\\\word\"", ManagementClient.Quote("pa\"ss\\word"));
    }

    [Theory]
    [InlineData("0x00000000\n0x00001a2b\nProcess ID", 0u, "0x00001a2b")]
    [InlineData("0x20000000\nCheckOption\nYou have specified a config file location", 0x20000000u, "CheckOption")]
    public void ParsesInteractiveServiceReply(string reply, uint error, string line2)
    {
        var (e, l2, _) = InteractiveServiceClient.ParseReply(reply);
        Assert.Equal(error, e);
        Assert.Equal(line2, l2);
        if (error == 0)
            Assert.Equal(0x1a2b, InteractiveServiceClient.ParseHex(l2));
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
}
