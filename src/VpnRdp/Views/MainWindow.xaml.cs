using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VpnRdp.Models;
using VpnRdp.Services;
using VpnRdp.ViewModels;
using WinForms = System.Windows.Forms;

namespace VpnRdp.Views;

public partial class MainWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "VpnRdp";

    private readonly MainViewModel _vm;
    private readonly WinForms.NotifyIcon _tray;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new DialogPrompter());
        DataContext = _vm;
        Application.Current.MainWindow = this;

        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "VPN+RDP Manager",
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.ContextMenuStrip = new WinForms.ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu();

        foreach (var p in _vm.Profiles)
            p.PropertyChanged += Profile_PropertyChanged;
        _vm.Profiles.CollectionChanged += (_, e) =>
        {
            foreach (ProfileItemViewModel p in e.NewItems ?? Array.Empty<ProfileItemViewModel>())
                p.PropertyChanged += Profile_PropertyChanged;
        };

        // Keep the log scrolled to the newest line.
        ((System.Collections.Specialized.INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, _) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };

        StartWithWindowsItem.IsChecked = Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunValue) is not null;
        Activated += (_, _) => _vm.RefreshOpenVpnStatus();

        if (Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            Loaded += (_, _) => Hide();
        }
    }

    private ProfileItemViewModel? Selected => _vm.Selected;

    // ---------- profile actions ----------

    private async void ConnectRdp_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p)
            await p.ConnectAndRdpAsync();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p)
            await p.ConnectAsync();
    }

    private void Rdp_Click(object sender, RoutedEventArgs e) => Selected?.LaunchRdp();

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p)
            await p.DisconnectAsync();
    }

    private async void ProfileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not { } p)
            return;
        if (p.IsActive) return;
        if (p.Profile.RdpEnabled && p.Profile.AutoRdpAfterConnect) await p.ConnectAndRdpAsync();
        else await p.ConnectAsync();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import OpenVPN profile",
            Filter = "OpenVPN profiles (*.ovpn;*.conf)|*.ovpn;*.conf|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var profile = new Profile { Name = Path.GetFileNameWithoutExtension(dlg.FileName) };
        try
        {
            var info = OvpnImporter.Import(dlg.FileName, profile.Id);
            profile.OvpnPath = info.ImportedPath;
            if (info.MissingFiles.Count > 0)
            {
                MessageBox.Show(this,
                    "These files referenced by the profile were not found next to it:\n\n" +
                    string.Join("\n", info.MissingFiles) + "\n\nThe connection may fail until they are added to:\n" +
                    Path.GetDirectoryName(info.ImportedPath),
                    "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        OpenEditor(profile, isNew: true);
    }

    private void New_Click(object sender, RoutedEventArgs e) => OpenEditor(new Profile { Name = "New connection" }, isNew: true);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p)
            OpenEditor(p.Profile.Clone(), isNew: false, p);
    }

    private void OpenEditor(Profile profile, bool isNew, ProfileItemViewModel? existing = null)
    {
        var editor = new ProfileEditorWindow(profile) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            if (isNew)
            {
                // Discard the config folder an import may have created.
                var dir = Path.Combine(AppPaths.OvpnDir, profile.Id);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
            }
            return;
        }
        if (existing is null) _vm.Add(editor.Result);
        else _vm.Replace(existing, editor.Result);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p)
            return;
        if (MessageBox.Show(this, $"Delete \"{p.Name}\" and its saved VPN password?", "Delete profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await p.DisconnectAsync();
        _vm.Remove(p);
    }

    // ---------- tools ----------

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        new SetupWindow { Owner = this }.ShowDialog();
        _vm.RefreshOpenVpnStatus();
        await Task.CompletedTask;
    }

    private void MonitorIds_Click(object sender, RoutedEventArgs e) => RdpLauncher.ShowMstscMonitorIds();

    private void Logs_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.LogDir) { UseShellExecute = true });

    private void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartWithWindowsItem.IsChecked)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            $"VPN+RDP Manager {typeof(App).Assembly.GetName().Version?.ToString(3)}\n\n" +
            "Connects OpenVPN Community profiles and opens Remote Desktop sessions.\n" +
            "Windows port of github.com/rjeffmyers/vpnrdp",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();

    // ---------- tray / lifetime ----------

    private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ProfileItemViewModel p || e.PropertyName != nameof(ProfileItemViewModel.State))
            return;
        if (p.State is VpnState.Connected or VpnState.Failed)
        {
            _tray.ShowBalloonTip(3000, p.Name, p.StatusText,
                p.State == VpnState.Failed ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
        }
        var active = _vm.Profiles.Where(x => x.State == VpnState.Connected).Select(x => x.Name).ToList();
        var text = active.Count == 0 ? "VPN+RDP Manager" : "Connected: " + string.Join(", ", active);
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private void BuildTrayMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();
        foreach (var p in _vm.Profiles)
        {
            var item = new WinForms.ToolStripMenuItem(p.Name) { Checked = p.State == VpnState.Connected };
            if (p.IsActive)
            {
                item.DropDownItems.Add("Open RDP", null, (_, _) => p.LaunchRdp());
                item.DropDownItems.Add("Disconnect", null, async (_, _) => await p.DisconnectAsync());
            }
            else
            {
                if (p.Profile.RdpEnabled)
                    item.DropDownItems.Add("Connect + RDP", null, async (_, _) => await p.ConnectAndRdpAsync());
                item.DropDownItems.Add("Connect VPN", null, async (_, _) => await p.ConnectAsync());
            }
            menu.Items.Add(item);
        }
        if (_vm.Profiles.Count > 0)
            menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _vm.AnyActive)
        {
            // Keep tunnels up; live in the tray.
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, "VPN+RDP Manager", "Still connected — running in the system tray.", WinForms.ToolTipIcon.Info);
            return;
        }
        if (!_exiting)
        {
            e.Cancel = true;
            ExitApp();
            return;
        }
        base.OnClosing(e);
    }

    private async void ExitApp()
    {
        if (_exiting)
            return;
        if (_vm.AnyActive && MessageBox.Show(this, "Disconnect all VPN connections and exit?", "Exit",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _exiting = true;
        IsEnabled = false;
        await Task.WhenAll(_vm.Profiles.Where(p => p.IsActive).Select(p => p.DisconnectAsync()));
        _tray.Visible = false;
        _tray.Dispose();
        Close();
        Application.Current.Shutdown();
    }
}
