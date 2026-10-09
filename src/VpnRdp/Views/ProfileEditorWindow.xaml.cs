using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using VpnRdp.Models;
using VpnRdp.Services;
using WinForms = System.Windows.Forms;

namespace VpnRdp.Views;

public partial class ProfileEditorWindow : Window
{
    private readonly Profile _profile;
    private readonly List<MonitorInfo> _monitors = MonitorEnumerator.GetMonitors();
    private readonly bool _hasSavedVpnPassword;
    private readonly bool _hasSavedRdpPassword;

    public ProfileEditorWindow(Profile profile)
    {
        InitializeComponent();
        _profile = profile;
        DataContext = profile;
        Title = $"Connection profile — {profile.Name}";

        _hasSavedVpnPassword = CredentialStore.Read(CredentialStore.VpnTarget(profile.Id)) is not null;
        VpnPasswordHint.Text = _hasSavedVpnPassword
            ? "A password is saved. Leave blank to keep it."
            : "Leave blank to be asked on first connect (you can save it then).";

        _hasSavedRdpPassword = !string.IsNullOrWhiteSpace(profile.Rdp.Host) &&
                               CredentialStore.Read(CredentialStore.RdpTarget(profile.Rdp.Host)) is not null;
        RdpPasswordHint.Text = _hasSavedRdpPassword
            ? "A password is saved for this host. Leave blank to keep it."
            : "Leave blank to let Remote Desktop ask (it can remember it too).";

        ShowOvpnInfo();
        UpdateMonitorHelp();
    }

    public Profile Result => _profile;

    // ---------- VPN ----------

    private void BrowseOvpn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose OpenVPN profile",
            Filter = "OpenVPN profiles (*.ovpn;*.conf)|*.ovpn;*.conf|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            var info = OvpnImporter.Import(dlg.FileName, _profile.Id);
            _profile.OvpnPath = info.ImportedPath;
            OvpnBox.Text = info.ImportedPath;
            if (string.IsNullOrWhiteSpace(_profile.Name) || _profile.Name == "New connection")
            {
                _profile.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
                // Rebind so the name box shows it.
                DataContext = null;
                DataContext = _profile;
            }
            ShowOvpnInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowOvpnInfo()
    {
        if (!File.Exists(_profile.OvpnPath))
        {
            OvpnInfoText.Text = "Choose the .ovpn file you received from your VPN administrator.";
            return;
        }
        var info = OvpnImporter.Inspect(_profile.OvpnPath);
        OvpnInfoText.Text = $"Server: {info.Remote ?? "(not found)"}" +
                            (info.NeedsUserPass ? "  •  asks for username/password" : "  •  certificate only");
    }

    // ---------- Monitors ----------

    private MonitorMode CurrentMonitorMode =>
        MonitorModeBox.SelectedValue is MonitorMode m ? m : _profile.Rdp.MonitorMode;

    private void MonitorMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentMonitorMode == MonitorMode.Single && _profile.Rdp.SelectedMonitors.Count > 1)
            _profile.Rdp.SelectedMonitors.RemoveRange(1, _profile.Rdp.SelectedMonitors.Count - 1);
        UpdateMonitorHelp();
        DrawMonitors();
    }

    private void UpdateMonitorHelp()
    {
        if (MonitorHelp is null)
            return;
        MonitorHelp.Text = CurrentMonitorMode switch
        {
            MonitorMode.Single => "Click a monitor to use it for the session (none selected = primary monitor).",
            MonitorMode.SelectedMonitors => "Click the monitors to use. Selected monitors must be next to each other.",
            MonitorMode.AllMonitors => "The remote session will use every monitor.",
            MonitorMode.Span => "One large remote desktop is stretched across all monitors (they must form a rectangle).",
            _ => "",
        };
    }

    private void MonitorCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMonitors();

    private void DrawMonitors()
    {
        if (MonitorCanvas is null)
            return;
        MonitorCanvas.Children.Clear();
        if (_monitors.Count == 0 || MonitorCanvas.ActualWidth < 10)
            return;

        var minX = _monitors.Min(m => m.Left);
        var minY = _monitors.Min(m => m.Top);
        var maxX = _monitors.Max(m => m.Left + m.Width);
        var maxY = _monitors.Max(m => m.Top + m.Height);
        var scale = Math.Min(MonitorCanvas.ActualWidth / (maxX - minX), MonitorCanvas.ActualHeight / (maxY - minY));
        var offsetX = (MonitorCanvas.ActualWidth - (maxX - minX) * scale) / 2;

        var mode = CurrentMonitorMode;
        var selectable = mode is MonitorMode.Single or MonitorMode.SelectedMonitors;

        var accent = new SolidColorBrush(Color.FromRgb(0x1E, 0x64, 0xB4));
        foreach (var m in _monitors)
        {
            var selected = mode is MonitorMode.AllMonitors or MonitorMode.Span ||
                           _profile.Rdp.SelectedMonitors.Contains(m.MstscId) ||
                           (mode == MonitorMode.Single && _profile.Rdp.SelectedMonitors.Count == 0 && m.IsPrimary);
            var tile = new Border
            {
                Width = Math.Max(20, m.Width * scale - 4),
                Height = Math.Max(20, m.Height * scale - 4),
                Background = selected ? accent : Brushes.White,
                BorderBrush = selected ? accent : Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Cursor = selectable ? System.Windows.Input.Cursors.Hand : null,
                Child = new TextBlock
                {
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = selected ? Brushes.White : Brushes.Black,
                    Text = $"Display {m.DisplayNumber}\n{m.Width}×{m.Height}" + (m.IsPrimary ? "\nprimary" : "") + $"\nmstsc ID {m.MstscId}",
                    FontSize = 11,
                },
                Tag = m,
                ToolTip = m.DeviceName,
            };
            if (selectable)
                tile.MouseLeftButtonUp += Monitor_Click;
            Canvas.SetLeft(tile, offsetX + (m.Left - minX) * scale + 2);
            Canvas.SetTop(tile, (m.Top - minY) * scale + 2);
            MonitorCanvas.Children.Add(tile);
        }
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        var m = (MonitorInfo)((FrameworkElement)sender).Tag;
        var sel = _profile.Rdp.SelectedMonitors;
        if (CurrentMonitorMode == MonitorMode.Single)
        {
            sel.Clear();
            sel.Add(m.MstscId);
        }
        else if (!sel.Remove(m.MstscId))
        {
            sel.Add(m.MstscId);
            sel.Sort();
        }
        DrawMonitors();
    }

    private void Identify_Click(object sender, RoutedEventArgs e)
    {
        // WinForms forms are positioned in physical pixels, which matches EnumDisplayMonitors.
        var forms = _monitors.Select(m =>
        {
            var f = new WinForms.Form
            {
                FormBorderStyle = WinForms.FormBorderStyle.None,
                StartPosition = WinForms.FormStartPosition.Manual,
                TopMost = true,
                ShowInTaskbar = false,
                BackColor = System.Drawing.Color.FromArgb(45, 125, 210),
                Size = new System.Drawing.Size(260, 160),
                Location = new System.Drawing.Point(m.Left + 40, m.Top + 40),
            };
            f.Controls.Add(new WinForms.Label
            {
                Dock = WinForms.DockStyle.Fill,
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 28, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Text = $"Display {m.DisplayNumber}\nmstsc ID {m.MstscId}",
            });
            f.Show();
            return f;
        }).ToList();

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            forms.ForEach(f => f.Close());
        };
        timer.Start();
    }

    private void MstscIds_Click(object sender, RoutedEventArgs e) => RdpLauncher.ShowMstscMonitorIds();

    // ---------- Advanced ----------

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var w = new Window
        {
            Title = "Generated .rdp file",
            Owner = this,
            Width = 520,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = RdpFileBuilder.Build(_profile.Rdp),
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(8),
            },
        };
        w.ShowDialog();
    }

    // ---------- Save ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _profile.Name = _profile.Name.Trim();
        if (string.IsNullOrWhiteSpace(_profile.Name))
        {
            MessageBox.Show(this, "Enter a profile name.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!File.Exists(_profile.OvpnPath))
        {
            MessageBox.Show(this, "Choose an OpenVPN config (.ovpn) file.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _profile.Rdp.Host = _profile.Rdp.Host.Trim();
        if (_profile.RdpEnabled && string.IsNullOrWhiteSpace(_profile.Rdp.Host))
        {
            MessageBox.Show(this, "Enter the Remote Desktop computer name, or untick \"This profile has a Remote Desktop target\".",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_profile.Rdp.MonitorMode == MonitorMode.SelectedMonitors && _profile.Rdp.SelectedMonitors.Count == 0)
        {
            MessageBox.Show(this, "Select at least one monitor on the Display tab.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_profile.Rdp.MonitorMode is MonitorMode.AllMonitors or MonitorMode.Span)
            _profile.Rdp.SelectedMonitors.Clear();

        try
        {
            var vpnTarget = CredentialStore.VpnTarget(_profile.Id);
            if (_profile.VpnPasswordMode == SecretMode.AskEveryTime)
                CredentialStore.Delete(vpnTarget);
            else if (VpnPasswordBox.Password.Length > 0)
                CredentialStore.Write(vpnTarget, _profile.VpnUsername, VpnPasswordBox.Password);

            if (_profile.RdpEnabled)
                RdpLauncher.StoreCredentials(_profile.Rdp, RdpPasswordBox.Password.Length > 0 ? RdpPasswordBox.Password : null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save the password: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
    }
}
