# Building and packaging

Run commands from the repository root on Windows. Morf has two build paths that share the same application sources.

| Build | Toolchain | Purpose |
| --- | --- | --- |
| SDK | .NET 10 SDK with Windows desktop support | Main development build |
| Inbox preview | .NET Framework 4.8 compiler and Windows WinRT metadata | Single-EXE portable preview and MSIX payload |

## SDK build

```powershell
dotnet build src/Widgates/Widgates.csproj -c Release
dotnet run --project src/Widgates/Widgates.csproj -c Release --no-build
```

The project targets `net10.0-windows10.0.17763.0`. The API target does not override the runtime's own Windows support requirements. No explicit third-party NuGet packages are used by the application project.

## Portable preview

Use 64-bit PowerShell with .NET Framework 4.8 installed:

```powershell
.\scripts\Build-InboxPreview.ps1
.\artifacts\inbox-preview\WindowsWidgates.exe
```

The script uses Windows' compiler, WPF assemblies and `%WINDIR%\System32\WinMetadata`. The EXE embeds the manifest and branding assets. It has no package identity and cannot listen to live Windows notifications.

## Checks

```powershell
dotnet run --project tests/Widgates.Checks -c Release -- artifacts/checks
.\scripts\Check-InboxPreview.ps1
.\scripts\Test-MorfShortcut.ps1
```

The check project compiles the application sources with a separate entry point. Standard checks cover state, parsing, layout, file handling and synthetic integration cases. They do not establish physical-device compatibility.

To open WPF player/mixer/settings previews using demonstration data:

```powershell
dotnet run --project tests/Widgates.Checks -c Release -- --native-mixer-visual artifacts/screenshots
```

This mode does not start audio capture or write real audio levels. It opens temporary windows and produces PNGs. It also supplies the [README screenshots](images/README.md).

Native integration modes are opt-in. They may open windows, access Windows integrations or exercise a test audio session. In particular, `--native-product-pack` includes a loopback interface probe. The full `-NativeChecks` sequence has known Inbox settings-window shutdown failures in the current development environment; run scenarios separately when diagnosing it. See [release readiness](RELEASING.md).

## Portable package

```powershell
.\scripts\Build-Test-Package.ps1
```

The script builds both runtimes, runs standard checks, checks version alignment and verifies a four-file ZIP containing the EXE, instructions, license and SHA256. It looks for a local SDK at `artifacts/dotnet/dotnet.exe`, then falls back to `dotnet`; `-Dotnet` can select an SDK explicitly.

Output: `artifacts/share/Morf-0.3.2-preview-Windows.zip` for the current version. Generated SDK output, logs, test fixtures and local archives stay under `artifacts/` and are excluded from Git.

For optional current-user installation:

```powershell
.\scripts\Build-Test-Package.ps1 -Install -Launch
```

Installation copies the portable EXE to `%LOCALAPPDATA%\Morf\app` and creates a Start menu shortcut. `-DesktopShortcut` requests an additional desktop shortcut. Exit the running app normally before updating; the installer does not terminate it. Preferences and the startup setting are preserved.

## Notification-enabled MSIX

Prerequisites: the inbox toolchain and Windows SDK x64 `MakeAppx.exe` and `SignTool.exe`. The builder validates the SDK tool signatures. It searches a local development SDK path and Windows SDK 10.0.26100.0; use `-SdkToolsDirectory` for another installation.

```powershell
.\scripts\Build-NotificationPackage.ps1 `
  -SdkToolsDirectory 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64' `
  -CertificateThumbprint '<approved CurrentUser\My signing certificate>' `
  -PackageRevision 1
```

The certificate must have an accessible private key and a publisher matching the package identity. The script can create an unsigned package without `-CertificateThumbprint`, but that output is not ready for normal installation. It never creates certificates, changes trust or enables developer mode.

After validating the signed package, installation is a separate owner-controlled action:

```powershell
Add-AppxPackage -Path '.\artifacts\notifications-package\Morf-Notifications-0.3.2-preview-x64.msix'
```

Close Morf normally before an update. Package identity, Windows notification permission and banner-routing recovery require their own validation. Keep signing keys and certificate exports outside the repository.

## Friend-test setup ZIP

Use an existing owner-selected signing certificate and a fresh output directory:

```powershell
.\scripts\Build-FriendSetup.ps1 -CertificateThumbprint '<existing signing certificate>' -OutputDirectory 'artifacts/friend-setup-new'
```

The archive contains `Kurulum.exe`, the signed notification-capable MSIX, public-only `Morf.cer`, `KURULUM.txt` and `SHA256.txt`. The builder signs and verifies the setup executable and MSIX package, verifies the setup's embedded payload hashes and every ZIP entry, and never installs the app or changes certificate trust. The setup runs as the user and opens the Windows certificate and MSIX installers only when their buttons are clicked; first-device certificate trust and notification permission remain explicit user actions. It does not include the private signing key.

`Kurulum.exe --verify` checks payload integrity without opening installers. `--render` writes a setup preview PNG without changing trust or installing the app. These modes support local packaging validation; they do not replace a clean-device installation test.
