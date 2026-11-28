param([string]$SdkToolsDirectory = '', [string]$CertificateThumbprint = '', [string]$OutputDirectory = 'artifacts/notifications-package', [ValidateRange(0,65535)][int]$PackageRevision = 0)

$ErrorActionPreference = 'Stop'
$notificationRepo = Split-Path $PSScriptRoot -Parent
Push-Location $notificationRepo
try {
    if (-not $SdkToolsDirectory) {
        $notificationToolCandidates = @(
            (Join-Path $notificationRepo 'artifacts/notification-tools/sdk/bin/10.0.26100.0/x64'),
            (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin/10.0.26100.0/x64')
        )
        $SdkToolsDirectory = $notificationToolCandidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'makeappx.exe') } | Select-Object -First 1
    }
    if (-not $SdkToolsDirectory) { throw 'Windows SDK MakeAppx is required. Supply -SdkToolsDirectory; see docs/BUILDING.md.' }
    $notificationMakeAppx = Join-Path $SdkToolsDirectory 'makeappx.exe'
    $notificationSignature = Get-AuthenticodeSignature -LiteralPath $notificationMakeAppx
    if ($notificationSignature.Status -ne 'Valid' -or $notificationSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'MakeAppx must have a valid Microsoft signature.' }
    $notificationOutput = [IO.Path]::GetFullPath((Join-Path $notificationRepo $OutputDirectory))
    if (-not $notificationOutput.StartsWith($notificationRepo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must be inside this repository.' }
    $notificationStage = Join-Path $notificationOutput 'layout'
    New-Item -ItemType Directory -Force -Path (Join-Path $notificationStage 'Assets') | Out-Null
    & (Join-Path $PSScriptRoot 'Build-InboxPreview.ps1') -OutputDirectory 'artifacts/inbox-notifications-preview'
    if ($LASTEXITCODE -ne 0) { throw 'Inbox build failed.' }
    Copy-Item -LiteralPath (Join-Path $notificationRepo 'artifacts/inbox-notifications-preview/WindowsWidgates.exe') -Destination $notificationStage -Force
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    $notificationLogo = [System.Windows.Media.Imaging.BitmapFrame]::Create([Uri](Join-Path $notificationRepo 'src/Widgates/Assets/Brand/morf-256.png'))
    foreach ($notificationSize in @(44, 50, 150)) {
        $notificationScale = [double]$notificationSize / [double]$notificationLogo.PixelWidth
        $notificationTransform = [System.Windows.Media.ScaleTransform]::new($notificationScale, $notificationScale)
        $notificationBitmap = [System.Windows.Media.Imaging.TransformedBitmap]::new($notificationLogo, $notificationTransform)
        $notificationEncoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $notificationEncoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($notificationBitmap))
        $notificationStream = [IO.File]::Create((Join-Path $notificationStage "Assets/logo-$notificationSize.png"))
        try { $notificationEncoder.Save($notificationStream) } finally { $notificationStream.Dispose() }
    }
    $notificationPublisher = 'CN=Morf Preview'
    if ($CertificateThumbprint) {
        $notificationCertificate = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $CertificateThumbprint)
        if (-not $notificationCertificate.HasPrivateKey) { throw 'The selected signing certificate has no private key.' }
        $notificationPublisher = $notificationCertificate.Subject
    }
    $notificationPublisher = [Security.SecurityElement]::Escape($notificationPublisher)
    $notificationVersionSource = Get-Content -LiteralPath 'src/Widgates/BuildInfo.cs' -Raw
    if ($notificationVersionSource -notmatch 'Version = "([0-9.]+)-preview"') { throw 'Cannot read product version.' }
    $notificationVersion = $Matches[1]
    @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
 xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 xmlns:virtualization="http://schemas.microsoft.com/appx/manifest/virtualization/windows10"
 IgnorableNamespaces="uap uap3 uap5 rescap virtualization">
 <Identity Name="Morf.NotificationsPreview" Publisher="$notificationPublisher" Version="$notificationVersion.$PackageRevision" ProcessorArchitecture="x64" />
 <Properties><DisplayName>Morf</DisplayName><PublisherDisplayName>Morf</PublisherDisplayName><Logo>Assets/logo-50.png</Logo>
  <virtualization:RegistryWriteVirtualization><virtualization:ExcludedKeys>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Morf\NotificationRouting</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Google\Chrome\NativeMessagingHosts\com.morf.pip</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Microsoft\Edge\NativeMessagingHosts\com.morf.pip</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Chromium\NativeMessagingHosts\com.morf.pip</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.morf.pip</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Vivaldi\NativeMessagingHosts\com.morf.pip</virtualization:ExcludedKey>
   <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Opera Software\NativeMessagingHosts\com.morf.pip</virtualization:ExcludedKey>
  </virtualization:ExcludedKeys></virtualization:RegistryWriteVirtualization>
 </Properties>
 <Resources><Resource Language="tr-tr" /><Resource Language="en-us" /></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26300.0" /></Dependencies>
 <Applications><Application Id="App" Executable="WindowsWidgates.exe" EntryPoint="Windows.FullTrustApplication">
  <uap:VisualElements DisplayName="Morf" Description="Medya ve süreli uygulama bildirimleri" BackgroundColor="transparent" Square150x150Logo="Assets/logo-150.png" Square44x44Logo="Assets/logo-44.png" />
  <Extensions><uap5:Extension Category="windows.startupTask" Executable="WindowsWidgates.exe" EntryPoint="Windows.FullTrustApplication">
   <uap5:StartupTask TaskId="MorfStartup" Enabled="false" DisplayName="Morf" />
  </uap5:Extension></Extensions>
 </Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /><rescap:Capability Name="unvirtualizedResources" /><uap3:Capability Name="userNotificationListener" /></Capabilities>
</Package>
"@ | Set-Content -LiteralPath (Join-Path $notificationStage 'AppxManifest.xml') -Encoding UTF8
    $notificationFiles = @(Get-ChildItem -LiteralPath $notificationStage -File -Recurse | ForEach-Object { $_.FullName.Substring($notificationStage.Length + 1).Replace('\','/') } | Sort-Object)
    if (($notificationFiles -join ',') -ne 'AppxManifest.xml,Assets/logo-150.png,Assets/logo-44.png,Assets/logo-50.png,WindowsWidgates.exe') { throw 'Unexpected files in notification package layout.' }
    $notificationPackage = Join-Path $notificationOutput "Morf-Notifications-$notificationVersion-preview-x64.msix"
    & $notificationMakeAppx pack /o /d $notificationStage /p $notificationPackage
    if ($LASTEXITCODE -ne 0) { throw 'MSIX validation or packing failed.' }
    if ($CertificateThumbprint) {
        $notificationSignTool = Join-Path $SdkToolsDirectory 'signtool.exe'
        if ((Get-AuthenticodeSignature -LiteralPath $notificationSignTool).Status -ne 'Valid') { throw 'SignTool signature is invalid.' }
        & $notificationSignTool sign /fd SHA256 /sha1 $CertificateThumbprint $notificationPackage
        if ($LASTEXITCODE -ne 0) { throw 'Package signing failed.' }
        & $notificationSignTool verify /pa $notificationPackage
        if ($LASTEXITCODE -ne 0) { throw 'Package signature verification failed.' }
    }
    (Get-FileHash -LiteralPath $notificationPackage -Algorithm SHA256).Hash | Set-Content -LiteralPath ($notificationPackage + '.sha256') -Encoding ASCII
    Write-Output "Validated package: $notificationPackage; signed=$([bool]$CertificateThumbprint). No installation, certificate trust change or publication performed."
} finally { Pop-Location }
