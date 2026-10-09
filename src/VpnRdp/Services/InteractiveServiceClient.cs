using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace VpnRdp.Services;

/// <summary>
/// Starts openvpn.exe through OpenVPN's Interactive Service (the same mechanism OpenVPN-GUI uses),
/// so the user never needs admin rights to connect. The service creates the tunnel adapter, sets
/// routes and DNS on our behalf.
///
/// Protocol (openvpnserv/interactive.c):
///   client -> service: UTF-16 "workdir\0options\0stdin\0" as one message
///   service -> client: UTF-16 "0x%08x\n...\n..."   error == 0  => second line is "0x&lt;pid&gt;"
///                                                     error != 0 => function name, then message
/// The pipe must stay open while openvpn runs; the service reports later errors on it.
/// </summary>
public sealed class InteractiveServiceClient : IDisposable
{
    public const string PipeName = @"openvpn\service";

    private NamedPipeClientStream? _pipe;

    public event Action<string>? ServiceMessage;

    public int ProcessId { get; private set; }

    /// <param name="workDir">Working directory for openvpn (relative paths in the config resolve here).</param>
    /// <param name="options">openvpn command line, without the leading "openvpn".</param>
    /// <param name="stdinInput">Text written to openvpn's stdin (we use it for the management password).</param>
    public async Task<int> StartAsync(string workDir, string options, string stdinInput, CancellationToken ct)
    {
        _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await _pipe.ConnectAsync(5000, ct);
        }
        catch (TimeoutException)
        {
            throw new OpenVpnException(
                "Could not reach the OpenVPN Interactive Service. Make sure OpenVPN Community is installed " +
                "and the \"OpenVPNServiceInteractive\" service is running.");
        }
        _pipe.ReadMode = PipeTransmissionMode.Message;

        var payload = Encoding.Unicode.GetBytes(workDir + "\0" + options + "\0" + stdinInput + "\0");
        await _pipe.WriteAsync(payload, ct);
        await _pipe.FlushAsync(ct);

        var reply = await ReadMessageAsync(ct) ?? throw new OpenVpnException("The OpenVPN service closed the connection.");
        var (error, line2, line3) = ParseReply(reply);
        if (error != 0)
            throw new OpenVpnException(DescribeError(error, line2, line3));

        ProcessId = ParseHex(line2);
        Log.Info($"Interactive service started openvpn, pid {ProcessId}");
        _ = Task.Run(() => ListenAsync(CancellationToken.None));
        return ProcessId;
    }

    internal static (uint Error, string Line2, string Line3) ParseReply(string reply)
    {
        var parts = reply.TrimEnd('\0').Split('\n', 3);
        var error = (uint)ParseHex(parts[0]);
        return (error, parts.Length > 1 ? parts[1] : "", parts.Length > 2 ? parts[2] : "");
    }

    internal static int ParseHex(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : -1;
    }

    private static string DescribeError(uint error, string func, string message)
    {
        // ERROR_STARTUP_DATA / option validation failures come back with a human-readable message.
        var text = string.IsNullOrWhiteSpace(message) ? func : message;
        if (text.Contains("OpenVPN Administrators", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not authorized", StringComparison.OrdinalIgnoreCase))
        {
            text += "\n\nRun Setup from the Tools menu to add your account to the \"OpenVPN Administrators\" group.";
        }
        return $"OpenVPN service error 0x{error:X8} ({func}): {text}";
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            while (_pipe is { IsConnected: true })
            {
                var msg = await ReadMessageAsync(ct);
                if (msg is null)
                    break;
                Log.Info($"Interactive service: {msg.Replace('\n', ' ')}");
                ServiceMessage?.Invoke(msg);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Pipe closed — openvpn exited or we disposed.
        }
    }

    private async Task<string?> ReadMessageAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        do
        {
            var n = await _pipe!.ReadAsync(buffer, ct);
            if (n == 0)
                return ms.Length == 0 ? null : Encoding.Unicode.GetString(ms.ToArray());
            ms.Write(buffer, 0, n);
        } while (!_pipe.IsMessageComplete);
        return Encoding.Unicode.GetString(ms.ToArray());
    }

    public void Dispose()
    {
        _pipe?.Dispose();
        _pipe = null;
    }
}

public sealed class OpenVpnException(string message) : Exception(message);
