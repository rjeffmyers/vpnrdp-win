using System.Diagnostics;
using System.Windows;
using VpnRdp.Models;
using VpnRdp.Services;

namespace VpnRdp.Views;

/// <summary>Shows prompt dialogs on the UI thread on behalf of a VPN session.</summary>
public sealed class DialogPrompter : IVpnPrompter
{
    public Task<CredentialAnswer?> AskCredentialsAsync(Profile profile, string defaultUser, string? error) =>
        OnUi(() =>
        {
            var w = new PromptWindow($"Sign in — {profile.Name}", $"Enter your VPN credentials for \"{profile.Name}\".",
                error, askUser: true, defaultUser, "Password", echoSecret: false,
                offerSave: profile.VpnPasswordMode == SecretMode.Save) { Owner = MainOwner() };
            return w.ShowDialog() == true ? new CredentialAnswer(w.User, w.Secret, w.Save) : null;
        });

    public Task<string?> AskChallengeAsync(Profile profile, string challengeText, bool echo) =>
        OnUi(() =>
        {
            var w = new PromptWindow($"Verification — {profile.Name}", challengeText, null, askUser: false, "",
                "Response", echo, offerSave: false) { Owner = MainOwner() };
            return w.ShowDialog() == true ? w.Secret : null;
        });

    public Task<string?> AskPrivateKeyPasswordAsync(Profile profile) =>
        OnUi(() =>
        {
            var w = new PromptWindow($"Private key — {profile.Name}", "Enter the passphrase for the private key.",
                null, askUser: false, "", "Passphrase", echoSecret: false, offerSave: false) { Owner = MainOwner() };
            return w.ShowDialog() == true ? w.Secret : null;
        });

    public void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static Window? MainOwner()
    {
        var w = Application.Current.MainWindow;
        return w is { IsVisible: true } ? w : null;
    }

    private static Task<T> OnUi<T>(Func<T> f) => Application.Current.Dispatcher.InvokeAsync(f).Task;
}
