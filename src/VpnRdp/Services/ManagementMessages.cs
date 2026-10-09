using System.Text;

namespace VpnRdp.Services;

/// <summary>Real-time notifications from the OpenVPN management interface (lines starting with '&gt;').</summary>
public abstract record ManagementEvent
{
    /// <summary>Parses one line. Returns null for non-notification lines (command replies etc.).</summary>
    public static ManagementEvent? Parse(string line)
    {
        if (line.Length < 2 || line[0] != '>')
            return null;
        var colon = line.IndexOf(':');
        if (colon < 0)
            return null;
        var kind = line[1..colon];
        var body = line[(colon + 1)..];

        switch (kind)
        {
            case "STATE":
            {
                // time,STATE,description,local_ip,remote_ip,remote_port,local_addr,local_port,local_ipv6
                var f = body.Split(',');
                return new StateEvent(
                    f.ElementAtOrDefault(1) ?? "",
                    f.ElementAtOrDefault(2) ?? "",
                    f.ElementAtOrDefault(3) ?? "",
                    f.ElementAtOrDefault(4) ?? "");
            }
            case "BYTECOUNT":
            {
                var f = body.Split(',');
                return f.Length >= 2 && long.TryParse(f[0], out var inB) && long.TryParse(f[1], out var outB)
                    ? new ByteCountEvent(inB, outB)
                    : null;
            }
            case "LOG":
            {
                // time,flags,message — message may itself contain commas
                var f = body.Split(',', 3);
                return new LogEvent(f.ElementAtOrDefault(1) ?? "", f.ElementAtOrDefault(2) ?? body);
            }
            case "HOLD":
                return new HoldEvent(body);
            case "FATAL":
                return new FatalEvent(body);
            case "INFOMSG" or "INFO":
                if (body.StartsWith("WEB_AUTH:", StringComparison.Ordinal) ||
                    body.StartsWith("OPEN_URL:", StringComparison.Ordinal))
                {
                    var url = body[(body.IndexOf(':') + 1)..];
                    // WEB_AUTH:flags:url
                    var second = url.IndexOf(':');
                    if (body.StartsWith("WEB_AUTH:", StringComparison.Ordinal) && second >= 0 &&
                        !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        url = url[(second + 1)..];
                    return new WebAuthEvent(url);
                }
                if (body.StartsWith("CR_TEXT:", StringComparison.Ordinal))
                {
                    // CR_TEXT:flags:challenge text
                    var f = body.Split(':', 3);
                    return new CrTextEvent(f.ElementAtOrDefault(1) ?? "", f.ElementAtOrDefault(2) ?? "");
                }
                return new InfoEvent(body);
            case "PASSWORD":
                return ParsePassword(body);
            default:
                return new InfoEvent(line[1..]);
        }
    }

    private static ManagementEvent ParsePassword(string body)
    {
        // Need 'Auth' username/password
        // Need 'Auth' username/password SC:1,Enter your OTP
        // Need 'Private Key' password
        // Verification Failed: 'Auth'
        // Verification Failed: 'Auth' ['CRV1:R,E:state:dXNlcg==:Enter PIN']
        // Auth-Token:xxxx
        if (body.StartsWith("Verification Failed", StringComparison.Ordinal))
        {
            var crv = body.IndexOf("['CRV1:", StringComparison.Ordinal);
            if (crv >= 0)
            {
                var end = body.IndexOf("']", crv, StringComparison.Ordinal);
                var crv1 = body[(crv + 2)..(end < 0 ? body.Length : end)];
                var c = DynamicChallenge.TryParse(crv1);
                if (c is not null)
                    return new DynamicChallengeEvent(c);
            }
            return new AuthFailedEvent(ExtractTag(body));
        }

        if (body.StartsWith("Need ", StringComparison.Ordinal))
        {
            var tag = ExtractTag(body);
            var needsUser = body.Contains("username/password", StringComparison.Ordinal);
            StaticChallenge? sc = null;
            var scIdx = body.IndexOf(" SC:", StringComparison.Ordinal);
            if (scIdx >= 0)
            {
                var spec = body[(scIdx + 4)..];
                var comma = spec.IndexOf(',');
                if (comma > 0 && int.TryParse(spec[..comma], out var flags))
                    sc = new StaticChallenge((flags & 1) != 0, (flags & 2) != 0, spec[(comma + 1)..]);
            }
            return new PasswordRequestEvent(tag, needsUser, sc);
        }

        return new InfoEvent("PASSWORD:" + body);
    }

    private static string ExtractTag(string s)
    {
        var a = s.IndexOf('\'');
        var b = a >= 0 ? s.IndexOf('\'', a + 1) : -1;
        return a >= 0 && b > a ? s[(a + 1)..b] : "";
    }
}

public sealed record StateEvent(string State, string Description, string LocalIp, string RemoteIp) : ManagementEvent;
public sealed record ByteCountEvent(long BytesIn, long BytesOut) : ManagementEvent;
public sealed record LogEvent(string Flags, string Message) : ManagementEvent;
public sealed record HoldEvent(string Text) : ManagementEvent;
public sealed record FatalEvent(string Text) : ManagementEvent;
public sealed record InfoEvent(string Text) : ManagementEvent;
public sealed record WebAuthEvent(string Url) : ManagementEvent;
public sealed record CrTextEvent(string Flags, string Challenge) : ManagementEvent;
public sealed record AuthFailedEvent(string Tag) : ManagementEvent;
public sealed record PasswordRequestEvent(string Tag, bool NeedsUsername, StaticChallenge? Challenge) : ManagementEvent;
public sealed record DynamicChallengeEvent(DynamicChallenge Challenge) : ManagementEvent;

/// <summary>"static-challenge" in the config: prompt for an OTP alongside the password.</summary>
public sealed record StaticChallenge(bool Echo, bool Concat, string Text)
{
    public string BuildPassword(string password, string response) => Concat
        ? password + response
        : $"SCRV1:{B64(password)}:{B64(response)}";

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
}

/// <summary>Server-issued challenge (CRV1) after the first auth attempt, e.g. an MFA PIN.</summary>
public sealed record DynamicChallenge(bool Echo, bool ResponseRequired, string StateId, string Username, string Text)
{
    public static DynamicChallenge? TryParse(string crv1)
    {
        // CRV1:flags:state_id:base64_username:challenge_text
        var f = crv1.Split(':', 5);
        if (f.Length < 5 || f[0] != "CRV1")
            return null;
        string user;
        try
        {
            user = Encoding.UTF8.GetString(Convert.FromBase64String(f[3]));
        }
        catch (FormatException)
        {
            user = "";
        }
        var flags = f[1].Split(',');
        return new DynamicChallenge(flags.Contains("E"), flags.Contains("R"), f[2], user, f[4]);
    }

    public string BuildPassword(string response) => $"CRV1::{StateId}::{response}";
}
