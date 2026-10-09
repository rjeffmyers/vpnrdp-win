# VPN+RDP Manager for Windows

A small Windows app that connects an **OpenVPN Community** profile and then opens a **Remote Desktop** session. You can do both with one click or keep them separate. It is the Windows counterpart of [vpnrdp](https://github.com/rjeffmyers/vpnrdp), the Linux GTK version.

It was built for Windows 11 **Home** machines. The usual OpenVPN client has DNS problems on Home, while OpenVPN Community works well. This app provides an OpenVPN Connect–style interface on top of OpenVPN Community.

## Features

- **One-time setup**:
  - Installs OpenVPN Community via `winget` (or from a signed `.msi`).
  - Adds the user to the `OpenVPN Administrators` group, so day-to-day connections don't need admin rights.
- **Import `.ovpn` files**:
  - Copies the config and any referenced cert/key files into the app's own folder.
  - Shows the server and whether the profile asks for a username and password.
- **Profiles** with a VPN username and password. Passwords are stored in **Windows Credential Manager** or requested every time.
- **Sign-in prompts** for static challenge/OTP (`static-challenge`), dynamic challenge (CRV1), private-key passphrases, and web/SSO sign-in (opens the browser).
- **Live status, traffic graph and log**, read from the OpenVPN management interface.
- **Connect + RDP**: launches `mstsc` when the tunnel comes up. It can optionally drop the VPN when the RDP window is closed.
- **RDP settings**:
  - Full screen or windowed, resolution, scaling, dynamic resolution, smart sizing.
  - **Multi-monitor**: all monitors, selected monitors (visual picker), or span.
  - Audio and microphone, clipboard, drives, printers, smart cards, keyboard hook.
  - Experience settings, admin session, RD Gateway, plus free-form extra `.rdp` lines.
- The RDP password is saved as `TERMSRV/<host>`, where mstsc reads it natively. It never appears on a command line.
- **System tray**: connect and disconnect from the tray. The app keeps running in the tray while connected and can start with Windows.

## Requirements

- Windows 10 1809+ or Windows 11. Home and Pro both work; nothing here depends on Pro-only features.
- An administrator **once**, for setup. After that, standard users can connect.
- The self-contained exe includes .NET, so nothing else needs to be installed.

## Installing

Download `VpnRdp-Setup-x.y.z.exe` (per-user install, no admin needed) or the portable `VpnRdp.exe` from Releases. Then:

1. Run it. If OpenVPN isn't ready, a banner appears. Click **Setup...**, choose **Install / update OpenVPN and set up my account**, and approve the UAC prompt.
2. Click **Import .ovpn**, choose the file from your VPN admin, and fill in the VPN username and the Remote Desktop computer.
3. Click **Connect + RDP**.

## How it works

```
VpnRdp.exe ──named pipe──▶ OpenVPNServiceInteractive (SYSTEM) ──starts──▶ openvpn.exe (as you)
     │                                                                    │
     └──────── TCP 127.0.0.1 management interface (password) ─────────────┘
     │
     └── writes %LOCALAPPDATA%\VpnRdp\rdp\<profile>.rdp ──▶ mstsc.exe
```

- openvpn is started through the **OpenVPN Interactive Service** pipe (`\\.\pipe\openvpn\service`), the same way OpenVPN-GUI does it. The service creates the adapter and sets routes and DNS, so the app itself never runs elevated.
- The service runs configs outside `C:\Program Files\OpenVPN\config` only for members of **Administrators** or **OpenVPN Administrators**. That is why setup adds you to that group. Some OpenVPN 2.7 installs don't create the group, so setup creates it if it's missing.
- Each connection gets a random management port and password. Username, password and challenge answers go over the management interface (`--management-query-passwords`), never through files.

| What | Where |
| --- | --- |
| Profiles (no secrets) | `%APPDATA%\VpnRdp\profiles.json` |
| Imported configs | `%LOCALAPPDATA%\VpnRdp\configs\<profile-id>\` |
| Generated .rdp files | `%LOCALAPPDATA%\VpnRdp\rdp\` |
| Logs | `%LOCALAPPDATA%\VpnRdp\logs\` (Tools ▸ Open log folder) |
| VPN password | Credential Manager, `VpnRdp:<profile-id>:vpn` |
| RDP password | Credential Manager, `TERMSRV/<host>` |

## Notes

- **Monitor IDs.** mstsc numbers monitors by their Windows display number minus one, so the IDs can have gaps (for example 0, 1, 3). The picker shows both numbers. **Show mstsc IDs** opens `mstsc /l` so you can compare, and **Identify** flashes the numbers on each screen.
- **"Your credentials did not work" / saved RDP password ignored.** Some machines have a policy that blocks saved credentials for servers that only use NTLM. Choose *Ask every time* for the RDP password and let mstsc prompt instead.
- **Already using OpenVPN-GUI?** It can run at the same time; they don't share profiles. Don't connect the same profile in both.

## Building

```powershell
dotnet test VpnRdp.sln
dotnet publish src/VpnRdp/VpnRdp.csproj -p:PublishProfile=win-x64      # -> artifacts\publish\VpnRdp.exe
iscc /DAppVersion=0.1.0 installer\VpnRdp.iss                           # -> artifacts\VpnRdp-Setup-0.1.0.exe (needs Inno Setup 6)
```

Pushing a `v*` tag runs `.github/workflows/release.yml`. It tests, publishes, builds the installer, and attaches both files to a GitHub release.

## Project layout

```
src/VpnRdp/
  Models/        Profile, RdpSettings
  Services/      InteractiveServiceClient, ManagementClient, ManagementMessages, VpnSession,
                 OpenVpnLocator, OpenVpnSetup (elevated helper), OvpnImporter,
                 RdpFileBuilder, RdpLauncher, MonitorEnumerator, CredentialStore, ProfileStore
  ViewModels/    MainViewModel, ProfileItemViewModel
  Views/         MainWindow, ProfileEditorWindow, SetupWindow, PromptWindow
tests/VpnRdp.Tests/   xUnit tests (management protocol parsing, .rdp generation, .ovpn import, credentials)
installer/            Inno Setup script
```
