using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using VpnRdp.Services;

namespace VpnRdp.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var install = OpenVpnLocator.Find();
        InstalledText.Text = install is null ? "Not installed" : $"Installed — version {install.Version}";
        ServiceText.Text = install is null ? "—" : install.InteractiveServiceRunning ? "Running" : "Not running";
        AuthText.Text = install is null
            ? "—"
            : OpenVpnLocator.IsCurrentUserAuthorized(install)
                ? $"OK (Administrators or \"{install.AdminGroup}\")"
                : $"Not yet in \"{install.AdminGroup}\"";
        WingetButton.IsEnabled = OpenVpnSetup.IsWingetAvailable();
        if (!WingetButton.IsEnabled)
            WingetButton.ToolTip = "winget (App Installer) is not available on this PC — use the .msi option.";
        FixButton.IsEnabled = install is not null;
    }

    private async void Winget_Click(object sender, RoutedEventArgs e) => await Run(OpenVpnSetup.InstallSource.Winget, null);

    private async void Msi_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose the OpenVPN installer", Filter = "Windows Installer (*.msi)|*.msi" };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            // Check before elevating so the user gets a clear message; the elevated helper checks again.
            OpenVpnSetup.VerifyPublisher(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Installer rejected", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        await Run(OpenVpnSetup.InstallSource.Msi, dlg.FileName);
    }

    private async void Fix_Click(object sender, RoutedEventArgs e) => await Run(OpenVpnSetup.InstallSource.None, null);

    private void DownloadPage_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(OpenVpnSetup.DownloadPage) { UseShellExecute = true });

    private async Task Run(OpenVpnSetup.InstallSource source, string? msi)
    {
        IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        ResultText.Foreground = System.Windows.Media.Brushes.Black;
        ResultText.Text = source == OpenVpnSetup.InstallSource.None
            ? "Waiting for administrator approval..."
            : "Installing OpenVPN — this can take a minute or two...";
        try
        {
            var code = await OpenVpnSetup.RunElevatedAsync(source, msi);
            ResultText.Text = code switch
            {
                OpenVpnSetup.ExitOk =>
                    "Setup complete. You can now connect without administrator rights.",
                OpenVpnSetup.ExitInstallFailed =>
                    "OpenVPN installation failed. Check the log folder (Tools > Open log folder) or try the .msi option.",
                OpenVpnSetup.ExitGroupFailed =>
                    "OpenVPN is installed, but adding your account to the OpenVPN Administrators group failed. See the log.",
                _ => $"Setup exited with code {code}.",
            };
            ResultText.Foreground = code == OpenVpnSetup.ExitOk
                ? System.Windows.Media.Brushes.SeaGreen
                : System.Windows.Media.Brushes.Firebrick;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ResultText.Text = "Cancelled — administrator approval is required for setup.";
        }
        catch (Exception ex)
        {
            ResultText.Text = "Setup failed: " + ex.Message;
            ResultText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            IsEnabled = true;
            RefreshStatus();
        }
    }
}
