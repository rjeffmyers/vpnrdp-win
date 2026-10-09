; Inno Setup script — builds VpnRdp-Setup-<version>.exe from the single-file publish output.
; Build: iscc /DAppVersion=0.1.0 installer\VpnRdp.iss
; Installs per-user (no admin needed). OpenVPN itself is installed from inside the app (Tools > Setup OpenVPN).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6C0B6E42-3F0E-4C2B-9E2B-6A51A3D0B7F1}
AppName=VPN+RDP Manager
AppVersion={#AppVersion}
AppPublisher=rjeffmyers
AppPublisherURL=https://github.com/rjeffmyers/vpnrdp-win
DefaultDirName={localappdata}\Programs\VpnRdp
DefaultGroupName=VPN+RDP Manager
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=VpnRdp-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\VpnRdp.exe
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\artifacts\publish\VpnRdp.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\VPN+RDP Manager"; Filename: "{app}\VpnRdp.exe"
Name: "{autodesktop}\VPN+RDP Manager"; Filename: "{app}\VpnRdp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\VpnRdp.exe"; Description: "Launch VPN+RDP Manager"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop a running instance so its files can be removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM VpnRdp.exe /F"; Flags: runhidden; RunOnceId: "KillVpnRdp"

[Registry]
; Remove the "Start with Windows" entry (set from the app's Tools menu) on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "VpnRdp"; Flags: uninsdeletevalue
