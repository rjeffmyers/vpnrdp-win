using System.Windows;
using VpnRdp.Services;
using VpnRdp.Views;

namespace VpnRdp;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Elevated helper mode: install OpenVPN / fix group membership, then exit. No UI.
        if (e.Args.Length > 0 && e.Args[0] == OpenVpnSetup.SetupArg)
        {
            Shutdown(OpenVpnSetup.RunElevatedHelper(e.Args[1..]));
            return;
        }

        _singleInstance = new Mutex(true, @"Local\VpnRdp.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show("VPN+RDP Manager is already running (check the system tray).", "VPN+RDP Manager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "VPN+RDP Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Log.Info($"Starting VpnRdp {typeof(App).Assembly.GetName().Version} on {Environment.OSVersion}");
        var main = new MainWindow();
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
