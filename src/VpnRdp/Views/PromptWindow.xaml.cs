using System.Windows;

namespace VpnRdp.Views;

/// <summary>Reusable prompt for VPN username/password, OTP challenges and key passphrases.</summary>
public partial class PromptWindow : Window
{
    private readonly bool _echo;

    public PromptWindow(string title, string message, string? error, bool askUser, string defaultUser,
        string secretLabel, bool echoSecret, bool offerSave)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        if (!string.IsNullOrEmpty(error))
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
        }
        UserPanel.Visibility = askUser ? Visibility.Visible : Visibility.Collapsed;
        UserBox.Text = defaultUser;
        SecretLabel.Text = secretLabel;
        _echo = echoSecret;
        SecretBox.Visibility = echoSecret ? Visibility.Collapsed : Visibility.Visible;
        SecretPlainBox.Visibility = echoSecret ? Visibility.Visible : Visibility.Collapsed;
        SaveBox.Visibility = offerSave ? Visibility.Visible : Visibility.Collapsed;
        SaveBox.IsChecked = offerSave;

        Loaded += (_, _) =>
        {
            if (askUser && string.IsNullOrEmpty(defaultUser)) UserBox.Focus();
            else if (echoSecret) SecretPlainBox.Focus();
            else SecretBox.Focus();
        };
    }

    public string User => UserBox.Text.Trim();
    public string Secret => _echo ? SecretPlainBox.Text : SecretBox.Password;
    public bool Save => SaveBox.Visibility == Visibility.Visible && SaveBox.IsChecked == true;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
