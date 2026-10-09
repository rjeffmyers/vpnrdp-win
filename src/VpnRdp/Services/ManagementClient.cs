using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnRdp.Services;

/// <summary>Line-oriented client for the OpenVPN management interface (127.0.0.1, password protected).</summary>
public sealed class ManagementClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private volatile TaskCompletionSource<string>? _pendingReply;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public event Action<ManagementEvent>? EventReceived;
    public event Action<string>? ReplyReceived;
    public event Action? Closed;

    /// <summary>Picks a free loopback port for openvpn's --management.</summary>
    public static int FindFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Connects (retrying while openvpn starts up) and authenticates with the management password.</summary>
    public async Task ConnectAsync(int port, string password, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _tcp = new TcpClient();
            try
            {
                await _tcp.ConnectAsync(IPAddress.Loopback, port, ct);
                break;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                _tcp.Dispose();
                await Task.Delay(250, ct);
            }
            catch (SocketException)
            {
                throw new OpenVpnException("OpenVPN started but its management interface never came up. See the log for details.");
            }
        }

        _stream = _tcp.GetStream();
        // openvpn sends "ENTER PASSWORD:" with no newline, so read raw until we see it.
        await WaitForPromptAsync("ENTER PASSWORD:", TimeSpan.FromSeconds(10), ct);
        await WriteRawAsync(password + "\n", ct);
        _reader = new StreamReader(_stream, Encoding.UTF8);
        _ = Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// Sends one command and waits for its "SUCCESS:"/"ERROR:" reply. OpenVPN only reliably handles
    /// one command per read, so commands must not be pipelined (OpenVPN-GUI does the same).
    /// </summary>
    public async Task<string> SendAsync(string command, CancellationToken ct = default)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingReply = reply;
            await WriteRawAsync(command + "\n", ct);
            var done = await Task.WhenAny(reply.Task, Task.Delay(CommandTimeout, ct));
            if (done != reply.Task)
                throw new OpenVpnException($"OpenVPN did not answer the management command \"{command.Split(' ')[0]}\".");
            return await reply.Task;
        }
        finally
        {
            _pendingReply = null;
            _commandLock.Release();
        }
    }

    /// <summary>Quotes a value for username/password commands ("..." with \ and " escaped).</summary>
    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public Task<string> SendUsernameAsync(string tag, string user) => SendAsync($"username {Quote(tag)} {Quote(user)}");
    public Task<string> SendPasswordAsync(string tag, string pass) => SendAsync($"password {Quote(tag)} {Quote(pass)}");

    private async Task WaitForPromptAsync(string prompt, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sb = new StringBuilder();
        var buf = new byte[256];
        while (!sb.ToString().Contains(prompt, StringComparison.Ordinal))
        {
            int n;
            try
            {
                n = await _stream!.ReadAsync(buf, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new OpenVpnException("Timed out waiting for the OpenVPN management password prompt.");
            }
            if (n == 0)
                throw new OpenVpnException("OpenVPN closed the management connection.");
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
        }
    }

    private async Task WriteRawAsync(string text, CancellationToken ct)
    {
        if (_stream is null)
            return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(bytes, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_reader is not null)
            {
                var line = await _reader.ReadLineAsync();
                if (line is null)
                    break;
                if (line.StartsWith('>'))
                {
                    var ev = ManagementEvent.Parse(line);
                    if (ev is not null)
                        EventReceived?.Invoke(ev);
                }
                else
                {
                    ReplyReceived?.Invoke(line);
                    if (line.StartsWith("SUCCESS:", StringComparison.Ordinal) || line.StartsWith("ERROR:", StringComparison.Ordinal))
                        _pendingReply?.TrySetResult(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // Connection dropped.
        }
        _pendingReply?.TrySetException(new IOException("Management connection closed"));
        Closed?.Invoke();
    }

    public void Dispose()
    {
        _reader = null;
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }
}
