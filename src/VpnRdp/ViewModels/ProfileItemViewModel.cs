using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VpnRdp.Models;
using VpnRdp.Services;

namespace VpnRdp.ViewModels;

/// <summary>A profile row in the main list plus its live VPN session and RDP process.</summary>
public sealed partial class ProfileItemViewModel : ObservableObject
{
    private const int TrafficSamples = 60;
    private readonly IVpnPrompter _prompter;
    private VpnSession? _session;
    private Process? _rdp;
    private long _lastIn, _lastOut;
    private DateTime _lastSample;
    private readonly Queue<double> _inRates = new();
    private readonly Queue<double> _outRates = new();
    private bool _rdpPending;

    public ProfileItemViewModel(Profile profile, IVpnPrompter prompter)
    {
        Profile = profile;
        _prompter = prompter;
    }

    public Profile Profile { get; private set; }

    public string Name => Profile.Name;
    public string Subtitle => Profile.RdpEnabled && !string.IsNullOrWhiteSpace(Profile.Rdp.Host)
        ? $"VPN → RDP {Profile.Rdp.Host}"
        : "VPN only";

    [ObservableProperty] private VpnState _state = VpnState.Disconnected;
    [ObservableProperty] private string _statusText = "Disconnected";
    [ObservableProperty] private string _trafficText = "";
    [ObservableProperty] private PointCollection _inSparkline = new();
    [ObservableProperty] private PointCollection _outSparkline = new();
    [ObservableProperty] private bool _rdpRunning;

    public ObservableCollection<string> LogLines { get; } = new();

    public Brush StatusBrush => State switch
    {
        VpnState.Connected => Brushes.SeaGreen,
        VpnState.Failed => Brushes.Firebrick,
        VpnState.Disconnected => Brushes.Gray,
        _ => Brushes.Orange,
    };

    public bool IsBusy => State is VpnState.Connecting or VpnState.Authenticating or VpnState.Reconnecting or VpnState.Disconnecting;
    public bool IsActive => State is not (VpnState.Disconnected or VpnState.Failed);

    partial void OnStateChanged(VpnState value)
    {
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsActive));
    }

    public void Update(Profile profile)
    {
        Profile = profile;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Subtitle));
    }

    public Task ConnectAsync() => StartVpnAsync(launchRdp: false);

    public Task ConnectAndRdpAsync() => StartVpnAsync(launchRdp: true);

    private async Task StartVpnAsync(bool launchRdp)
    {
        if (IsActive)
        {
            if (launchRdp && State == VpnState.Connected)
                LaunchRdp();
            return;
        }
        _rdpPending = launchRdp && Profile.RdpEnabled;
        ResetTraffic();
        LogLines.Clear();

        _session?.Dispose();
        _session = new VpnSession(Profile, _prompter);
        _session.StateChanged += s => OnUi(() => ApplySessionState(s));
        _session.Traffic += (i, o) => OnUi(() => AddTrafficSample(i, o));
        _session.LogLine += line => OnUi(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });
        await _session.ConnectAsync();
    }

    public async Task DisconnectAsync()
    {
        _rdpPending = false;
        if (_session is not null)
            await _session.DisconnectAsync();
    }

    public void LaunchRdp()
    {
        if (_rdp is { HasExited: false })
        {
            Log.Info($"[{Name}] RDP already running");
            return;
        }
        try
        {
            _rdp = RdpLauncher.Launch(Profile);
            RdpRunning = true;
            _rdp.Exited += (_, _) => OnUi(OnRdpExited);
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] RDP launch failed", ex);
            MessageBox.Show(ex.Message, "Could not start Remote Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRdpExited()
    {
        RdpRunning = false;
        _rdp?.Dispose();
        _rdp = null;
        Log.Info($"[{Name}] RDP session closed");
        if (Profile.DisconnectVpnWhenRdpCloses && IsActive)
            await DisconnectAsync();
    }

    private void ApplySessionState(VpnSession s)
    {
        State = s.State;
        StatusText = s.StateDetail;
        if (s.State == VpnState.Connected && _rdpPending)
        {
            _rdpPending = false;
            LaunchRdp();
        }
        if (s.State is VpnState.Disconnected or VpnState.Failed)
        {
            _rdpPending = false;
            TrafficText = "";
        }
    }

    private void ResetTraffic()
    {
        _inRates.Clear();
        _outRates.Clear();
        _lastIn = _lastOut = 0;
        _lastSample = DateTime.MinValue;
        InSparkline = new PointCollection();
        OutSparkline = new PointCollection();
        TrafficText = "";
    }

    private void AddTrafficSample(long totalIn, long totalOut)
    {
        var now = DateTime.UtcNow;
        if (_lastSample != DateTime.MinValue)
        {
            var secs = Math.Max(0.5, (now - _lastSample).TotalSeconds);
            Push(_inRates, Math.Max(0, totalIn - _lastIn) / secs);
            Push(_outRates, Math.Max(0, totalOut - _lastOut) / secs);
            var max = Math.Max(1024, _inRates.Concat(_outRates).DefaultIfEmpty(0).Max());
            InSparkline = ToPoints(_inRates, max);
            OutSparkline = ToPoints(_outRates, max);
            TrafficText = $"↓ {FormatRate(_inRates.Last())}   ↑ {FormatRate(_outRates.Last())}   " +
                          $"(total ↓ {FormatBytes(totalIn)} ↑ {FormatBytes(totalOut)})";
        }
        _lastIn = totalIn;
        _lastOut = totalOut;
        _lastSample = now;
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > TrafficSamples)
            q.Dequeue();
    }

    /// <summary>Points for a 300x50 canvas, newest sample on the right.</summary>
    private static PointCollection ToPoints(IEnumerable<double> rates, double max)
    {
        const double w = 300, h = 50;
        var arr = rates.ToArray();
        var pts = new PointCollection();
        var offset = TrafficSamples - arr.Length;
        for (var i = 0; i < arr.Length; i++)
            pts.Add(new Point((offset + i) * w / (TrafficSamples - 1), h - arr[i] / max * h));
        pts.Freeze();
        return pts;
    }

    internal static string FormatRate(double bytesPerSec) => FormatBytes((long)bytesPerSec) + "/s";

    internal static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024:0.0} MB",
        _ => $"{b / 1024.0 / 1024 / 1024:0.00} GB",
    };

    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) a();
        else d.BeginInvoke(a);
    }
}
