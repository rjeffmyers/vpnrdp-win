using System.Diagnostics;
using System.Security.Cryptography;
using VpnRdp.Models;

namespace VpnRdp.Services;

public enum VpnState
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting,
    Disconnecting,
    Failed,
}

public sealed record CredentialAnswer(string User, string Password, bool Save);

/// <summary>UI hooks the session calls when OpenVPN needs something from the user. Return null to cancel.</summary>
public interface IVpnPrompter
{
    Task<CredentialAnswer?> AskCredentialsAsync(Profile profile, string defaultUser, string? error);
    Task<string?> AskChallengeAsync(Profile profile, string challengeText, bool echo);
    Task<string?> AskPrivateKeyPasswordAsync(Profile profile);
    void OpenUrl(string url);
}

/// <summary>One VPN connection: openvpn via the Interactive Service, driven over the management interface.</summary>
public sealed class VpnSession : IDisposable
{
    private readonly Profile _profile;
    private readonly IVpnPrompter _prompter;
    private InteractiveServiceClient? _service;
    private ManagementClient? _mgmt;
    private CredentialAnswer? _pendingSave;
    private CredentialAnswer? _pendingChallengeAnswer;
    private bool _authFailed;
    private bool _userCancelled;
    private string? _lastError;
    private TaskCompletionSource? _closed;

    public VpnSession(Profile profile, IVpnPrompter prompter)
    {
        _profile = profile;
        _prompter = prompter;
    }

    public VpnState State { get; private set; } = VpnState.Disconnected;
    public string StateDetail { get; private set; } = "";
    public string? TunnelIp { get; private set; }
    public long BytesIn { get; private set; }
    public long BytesOut { get; private set; }

    public event Action<VpnSession>? StateChanged;
    public event Action<long, long>? Traffic;
    public event Action<string>? LogLine;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State is not (VpnState.Disconnected or VpnState.Failed))
            return;

        _authFailed = false;
        _userCancelled = false;
        _lastError = null;
        _pendingSave = null;
        _pendingChallengeAnswer = null;
        _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetState(VpnState.Connecting, "Starting OpenVPN");

        try
        {
            var install = OpenVpnLocator.Find()
                ?? throw new OpenVpnException("OpenVPN Community is not installed. Use Tools > Setup OpenVPN.");
            if (!File.Exists(_profile.OvpnPath))
                throw new OpenVpnException($"Config file not found: {_profile.OvpnPath}");
            if (!OpenVpnLocator.IsCurrentUserAuthorized(install) && !IsUnder(_profile.OvpnPath, install.ConfigDir))
            {
                throw new OpenVpnException(
                    $"Your account is not in the \"{install.AdminGroup}\" group, so the OpenVPN service will refuse " +
                    "to start this config.\n\nUse Tools > Setup OpenVPN (requires an administrator once).");
            }

            var port = ManagementClient.FindFreePort();
            var mgmtPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var options = string.Join(' ',
                "--config", Quote(_profile.OvpnPath),
                "--management", "127.0.0.1", port.ToString(), "stdin",
                "--management-query-passwords",
                "--management-hold",
                "--auth-retry", "interact",
                "--setenv", "IV_GUI_VER", Quote("VpnRdp-Windows"));

            _service = new InteractiveServiceClient();
            _service.ServiceMessage += msg => Emit($"[service] {msg.Replace('\n', ' ')}");
            await _service.StartAsync(Path.GetDirectoryName(_profile.OvpnPath)!, options, mgmtPassword + "\n", ct);

            _mgmt = new ManagementClient();
            _mgmt.EventReceived += OnEvent;
            _mgmt.ReplyReceived += line =>
            {
                if (line.StartsWith("ERROR", StringComparison.Ordinal))
                    Emit($"[mgmt] {line}");
                else
                    Log.Info($"[{_profile.Name}] [mgmt] {line}");
            };
            _mgmt.Closed += OnManagementClosed;
            await _mgmt.ConnectAsync(port, mgmtPassword, TimeSpan.FromSeconds(20), ct);

            await _mgmt.SendAsync("state on", ct);
            await _mgmt.SendAsync("bytecount 2", ct);
            await _mgmt.SendAsync("log on all", ct);
            await _mgmt.SendAsync("hold release", ct);
        }
        catch (Exception ex) when (ex is OpenVpnException or IOException or OperationCanceledException)
        {
            Log.Error($"[{_profile.Name}] connect failed", ex);
            TryKill(_service?.ProcessId ?? 0); // don't leave a half-started openvpn holding the tunnel
            Cleanup();
            Fail(ex.Message);
        }
    }

    public async Task DisconnectAsync()
    {
        if (State is VpnState.Disconnected or VpnState.Failed)
            return;
        _userCancelled = true;
        SetState(VpnState.Disconnecting, "Disconnecting");
        try
        {
            if (_mgmt is not null)
                await _mgmt.SendAsync("signal SIGTERM");
        }
        catch (Exception ex) when (ex is IOException or OpenVpnException or ObjectDisposedException)
        {
            // Already gone or not answering — fall through to the kill fallback.
        }

        var closed = _closed?.Task ?? Task.CompletedTask;
        if (await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(8))) != closed)
        {
            Log.Warn($"[{_profile.Name}] openvpn did not exit on SIGTERM, killing pid {_service?.ProcessId}");
            TryKill(_service?.ProcessId ?? 0);
        }
        Cleanup();
        SetState(VpnState.Disconnected, "Disconnected");
    }

    private async void OnEvent(ManagementEvent ev)
    {
        try
        {
            switch (ev)
            {
                case StateEvent s:
                    OnState(s);
                    break;
                case ByteCountEvent b:
                    BytesIn = b.BytesIn;
                    BytesOut = b.BytesOut;
                    Traffic?.Invoke(b.BytesIn, b.BytesOut);
                    break;
                case LogEvent l:
                    Emit(l.Message);
                    break;
                case FatalEvent f:
                    _lastError = f.Text;
                    Emit("FATAL: " + f.Text);
                    break;
                case PasswordRequestEvent p:
                    await OnPasswordRequest(p);
                    break;
                case AuthFailedEvent:
                    _authFailed = true;
                    _pendingSave = null;
                    Emit("Authentication failed");
                    SetState(VpnState.Authenticating, "Authentication failed");
                    break;
                case DynamicChallengeEvent dc:
                    await OnDynamicChallenge(dc.Challenge);
                    break;
                case WebAuthEvent w:
                    Emit("Opening browser for sign-in: " + w.Url);
                    _prompter.OpenUrl(w.Url);
                    break;
                case CrTextEvent cr:
                {
                    var answer = await _prompter.AskChallengeAsync(_profile, cr.Challenge, cr.Flags.Contains('E'));
                    if (answer is null)
                        await CancelAsync();
                    else
                        await _mgmt!.SendAsync("cr-response " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(answer)));
                    break;
                }
                case InfoEvent i:
                    Emit(i.Text);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[{_profile.Name}] error handling management event {ev}", ex);
        }
    }

    private void OnState(StateEvent s)
    {
        Emit($"State: {s.State} {s.Description}");
        switch (s.State)
        {
            case "CONNECTED":
                TunnelIp = s.LocalIp;
                if (s.Description.StartsWith("SUCCESS", StringComparison.Ordinal))
                {
                    SavePendingCredentials();
                    SetState(VpnState.Connected, $"Connected — {s.LocalIp}");
                }
                else
                {
                    // CONNECTED,ERROR = connected but some routes/DNS failed to apply.
                    SetState(VpnState.Connected, $"Connected with errors — {s.LocalIp} (see log)");
                }
                break;
            case "RECONNECTING":
                SetState(VpnState.Reconnecting, $"Reconnecting ({s.Description})");
                break;
            case "EXITING":
                if (!_userCancelled && !string.IsNullOrEmpty(s.Description) && s.Description != "exit-with-notification")
                    _lastError ??= s.Description;
                break;
            case "AUTH" or "GET_CONFIG" or "AUTH_PENDING":
                SetState(VpnState.Authenticating, Pretty(s.State));
                break;
            default:
                if (State != VpnState.Disconnecting)
                    SetState(State == VpnState.Reconnecting ? VpnState.Reconnecting : VpnState.Connecting, Pretty(s.State));
                break;
        }
    }

    private async Task OnPasswordRequest(PasswordRequestEvent p)
    {
        if (p.Tag == "Private Key")
        {
            var pk = await _prompter.AskPrivateKeyPasswordAsync(_profile);
            if (pk is null)
                await CancelAsync();
            else
                await _mgmt!.SendPasswordAsync("Private Key", pk);
            return;
        }

        if (p.Tag != "Auth")
        {
            Emit($"Unsupported password request '{p.Tag}'");
            return;
        }

        if (_pendingChallengeAnswer is { } crv1)
        {
            _pendingChallengeAnswer = null;
            await _mgmt!.SendUsernameAsync("Auth", crv1.User);
            await _mgmt!.SendPasswordAsync("Auth", crv1.Password);
            return;
        }

        var answer = await GetCredentialsAsync();
        if (answer is null)
        {
            await CancelAsync();
            return;
        }

        var password = answer.Password;
        if (p.Challenge is { } sc)
        {
            var response = await _prompter.AskChallengeAsync(_profile, sc.Text, sc.Echo);
            if (response is null)
            {
                await CancelAsync();
                return;
            }
            password = sc.BuildPassword(answer.Password, response);
        }

        if (p.NeedsUsername)
            await _mgmt!.SendUsernameAsync("Auth", answer.User);
        await _mgmt!.SendPasswordAsync("Auth", password);
    }

    private async Task<CredentialAnswer?> GetCredentialsAsync()
    {
        var target = CredentialStore.VpnTarget(_profile.Id);
        if (!_authFailed && _profile.VpnPasswordMode == SecretMode.Save)
        {
            var stored = CredentialStore.Read(target);
            if (stored is { } s && !string.IsNullOrEmpty(s.Secret))
                return new CredentialAnswer(string.IsNullOrEmpty(s.User) ? _profile.VpnUsername : s.User, s.Secret, false);
        }

        var answer = await _prompter.AskCredentialsAsync(_profile, _profile.VpnUsername,
            _authFailed ? "Authentication failed — check your username and password." : null);
        if (answer is { Save: true })
            _pendingSave = answer; // persisted only once the server accepts it
        return answer;
    }

    private async Task OnDynamicChallenge(DynamicChallenge c)
    {
        var response = await _prompter.AskChallengeAsync(_profile, c.Text, c.Echo);
        if (response is null)
        {
            await CancelAsync();
            return;
        }
        // openvpn re-asks for Auth next; OnPasswordRequest answers that one with the CRV1 response.
        _authFailed = false;
        _pendingChallengeAnswer = new CredentialAnswer(c.Username, c.BuildPassword(response), false);
    }

    private void SavePendingCredentials()
    {
        if (_pendingSave is null)
            return;
        try
        {
            CredentialStore.Write(CredentialStore.VpnTarget(_profile.Id), _pendingSave.User, _pendingSave.Password);
            Log.Info($"[{_profile.Name}] saved VPN credentials");
        }
        catch (Exception ex)
        {
            Log.Error($"[{_profile.Name}] could not save VPN credentials", ex);
        }
        _pendingSave = null;
    }

    private async Task CancelAsync()
    {
        Emit("Cancelled by user");
        await DisconnectAsync();
    }

    private void OnManagementClosed()
    {
        _closed?.TrySetResult();
        if (State == VpnState.Disconnecting || _userCancelled)
            return;
        Cleanup();
        if (_lastError is not null)
            Fail(_lastError);
        else
            SetState(VpnState.Disconnected, "OpenVPN exited — see log");
    }

    private void Fail(string message)
    {
        _lastError = message;
        SetState(VpnState.Failed, message);
    }

    private void Cleanup()
    {
        if (_mgmt is not null)
        {
            _mgmt.EventReceived -= OnEvent;
            _mgmt.Closed -= OnManagementClosed;
            _closed?.TrySetResult();
        }
        _mgmt?.Dispose();
        _mgmt = null;
        _service?.Dispose();
        _service = null;
        TunnelIp = null;
    }

    private void SetState(VpnState state, string detail)
    {
        State = state;
        StateDetail = detail;
        Log.Info($"[{_profile.Name}] {state}: {detail}");
        StateChanged?.Invoke(this);
    }

    private void Emit(string line)
    {
        Log.Info($"[{_profile.Name}] {line}");
        LogLine?.Invoke(line);
    }

    private static void TryKill(int pid)
    {
        if (pid <= 0)
            return;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.ProcessName.Equals("openvpn", StringComparison.OrdinalIgnoreCase))
                p.Kill();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not kill openvpn pid {pid}: {ex.Message}");
        }
    }

    private static string Pretty(string state) => state switch
    {
        "CONNECTING" => "Connecting to server",
        "WAIT" => "Waiting for server",
        "AUTH" => "Authenticating",
        "AUTH_PENDING" => "Waiting for sign-in",
        "GET_CONFIG" => "Downloading configuration",
        "ASSIGN_IP" => "Assigning IP address",
        "ADD_ROUTES" => "Adding routes",
        "TCP_CONNECT" => "Connecting (TCP)",
        "RESOLVE" => "Resolving server address",
        _ => state,
    };

    private static string Quote(string s) => $"\"{s}\"";

    private static bool IsUnder(string path, string dir)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Cleanup();
}
