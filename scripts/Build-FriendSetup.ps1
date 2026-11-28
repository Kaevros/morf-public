param([Parameter(Mandatory=$true)][string]$CertificateThumbprint, [string]$OutputDirectory = 'artifacts/friend-setup', [string]$SdkToolsDirectory = '', [ValidateRange(0,65535)][int]$PackageRevision = 0)

$ErrorActionPreference = 'Stop'
$friendRepo = Split-Path $PSScriptRoot -Parent
Push-Location $friendRepo
try {
    $friendOutput = [IO.Path]::GetFullPath((Join-Path $friendRepo $OutputDirectory))
    if (-not $friendOutput.StartsWith($friendRepo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain inside this repository.' }
    New-Item -ItemType Directory -Force -Path $friendOutput | Out-Null
    $friendBuildDirectory = Join-Path $OutputDirectory 'msix-build'
    & (Join-Path $PSScriptRoot 'Build-NotificationPackage.ps1') -CertificateThumbprint $CertificateThumbprint -OutputDirectory $friendBuildDirectory -SdkToolsDirectory $SdkToolsDirectory -PackageRevision $PackageRevision
    $friendVersionSource = Get-Content src/Widgates/BuildInfo.cs -Raw
    if ($friendVersionSource -notmatch 'Version = "([0-9.]+)-preview"') { throw 'Cannot read product version.' }
    $friendVersion = $Matches[1]
    $friendVersionLabel = if ($PackageRevision -eq 0) { $friendVersion } else { "$friendVersion.$PackageRevision" }
    $friendLayout = Join-Path $friendOutput 'share'
    if (Test-Path -LiteralPath $friendLayout) { throw 'Choose a fresh output directory; existing share files are preserved.' }
    New-Item -ItemType Directory -Path $friendLayout | Out-Null
    $friendPackageName = "Morf-$friendVersionLabel-x64.msix"
    Copy-Item -LiteralPath (Join-Path $friendOutput "msix-build/Morf-Notifications-$friendVersion-preview-x64.msix") -Destination (Join-Path $friendLayout $friendPackageName)
    $friendCertificate = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $CertificateThumbprint)
    Export-Certificate -Cert $friendCertificate -FilePath (Join-Path $friendLayout 'Morf.cer') | Out-Null
    $friendPublicCertificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $friendLayout 'Morf.cer'))
    if ($friendPublicCertificate.HasPrivateKey) { throw 'The share certificate must be public-only.' }
    $friendPackageHash = (Get-FileHash -LiteralPath (Join-Path $friendLayout $friendPackageName) -Algorithm SHA256).Hash
    $friendCertificateHash = (Get-FileHash -LiteralPath (Join-Path $friendLayout 'Morf.cer') -Algorithm SHA256).Hash
    @"
using System.Reflection;
[assembly: AssemblyVersion("$friendVersion.$PackageRevision")]
[assembly: AssemblyFileVersion("$friendVersion.$PackageRevision")]
internal static class SetupPayload {
 internal const string Version = "$friendVersionLabel-preview";
 internal const string Package = "$friendPackageName";
 internal const string PackageHash = "$friendPackageHash";
 internal const string CertificateHash = "$friendCertificateHash";
}
"@ | Set-Content -LiteralPath (Join-Path $friendOutput 'SetupPayload.cs') -Encoding UTF8
    $friendFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $friendReferences = @('WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $friendFramework $_) }
    $friendSetupSource = (Get-Item -LiteralPath 'packaging/FriendSetup.cs').FullName
    & (Join-Path $friendFramework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror /codepage:65001 /utf8output "/out:$friendLayout/Kurulum.exe" "/win32manifest:$friendRepo/packaging/Setup.manifest" "/win32icon:$friendRepo/src/Widgates/Assets/Brand/morf.ico" "/resource:$friendRepo/src/Widgates/Assets/Brand/morf-256.png,MorfSetupLogo" @friendReferences $friendSetupSource (Join-Path $friendOutput 'SetupPayload.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    if (-not $SdkToolsDirectory) {
        $SdkToolsDirectory = @((Join-Path $friendRepo 'artifacts/notification-tools/sdk/bin/10.0.26100.0/x64'),(Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin/10.0.26100.0/x64')) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'signtool.exe') } | Select-Object -First 1
    }
    $friendSignTool = Join-Path $SdkToolsDirectory 'signtool.exe'
    if ((Get-AuthenticodeSignature -LiteralPath $friendSignTool).Status -ne 'Valid') { throw 'SignTool signature is not valid.' }
    & $friendSignTool sign /fd SHA256 /sha1 $CertificateThumbprint (Join-Path $friendLayout 'Kurulum.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Setup signing failed.' }
    & $friendSignTool verify /pa (Join-Path $friendLayout 'Kurulum.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Setup signature verification failed.' }
    $friendVerifyProcess = Start-Process -FilePath (Join-Path $friendLayout 'Kurulum.exe') -ArgumentList '--verify' -WindowStyle Hidden -PassThru -Wait
    if ($friendVerifyProcess.ExitCode -ne 0) { throw 'Setup payload verification failed.' }
    @"
Morf $friendVersionLabel-preview — arkadaş testi / Windows x64

ZIP'in tamamını bir klasöre çıkarın ve Kurulum.exe dosyasını açın.
Kurulum penceresi iki açık adım sunar: sertifika ve Windows MSIX kurulumu.
İlk kurulumda Morf.cer > Sertifikayı Yükle > Yerel Makine > Güvenilen Kişiler.
Bu yerel test sertifikasıdır; yönetici onayı gerekebilir. Özel anahtar içermez.
Ardından Morf'u yükle / güncelle düğmesini kullanın. Windows Uygulama
Yükleyicisi gereklidir; kurulumun tamamlandığını Windows penceresinden kontrol edin.
Morf açıksa kendi Çıkış menüsüyle kapatın; geçici raf bellekte tutulur.
Mevcut Morf ayarları güncellemede korunur. Morf'u Başlat menüsünden açın.

Bildirimler için Morf > Ayarlar > Bildirimler bölümünden Windows iznini verin.
WhatsApp bildirimi uygulamayı açar; sohbeti WhatsApp içinden seçebilirsiniz.
Küçük video penceresi için Ayarlar > Medya ve ses > Pencere içinde video · PiP
seçeneğini açın ve tarayıcı eklentisinin kurulum adımlarını izleyin.
Eklenti yerel geliştirme önizlemesidir; mağaza kurulumu veya tüm tarayıcılarda
uyumluluk iddiası değildir. Standart görünüm başlıksız tarayıcı video penceresi,
gelişmiş görünüm ise Morf denetimleri ve tarayıcı başlığı kullanır.
Video geçişlerinde açık kalma, sonraki video ve ek ayarlar için Gelişmiş
görünümü seçin. PiP ayarları hız, ses, tekrar ve bitince sonraki videoyu sunar.
Güncellemeden sonra tarayıcının uzantılar sayfasındaki Morf eklentisini
yenileyin, ardından açık video sekmelerini bir kez yenileyin.
Morf standart kullanıcı olarak çalışır. Çoklu ses çıkışı deneyseldir.
Windows 10 1809+ API tabanı ve .NET Framework 4.8 gerekir; tüm cihazlarda
uyumluluk ve başka bilgisayarda kurulum henüz doğrulanmadı.
Önceki Kaspersky bulguları sonuçlanmadı; antivirüsü kapatmayın.
Kaldırma: Windows Ayarları > Uygulamalar > Morf. Test sertifikasını kaldırırken
yalnızca bu pakete ait Morf Preview sertifikasını seçin.
"@ | Set-Content -LiteralPath (Join-Path $friendLayout 'KURULUM.txt') -Encoding UTF8
    $friendAllowedFiles = @('Kurulum.exe',$friendPackageName,'Morf.cer','KURULUM.txt')
    $friendAllowedFiles | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $friendLayout $_) -Algorithm SHA256).Hash + '  ' + $_ } | Set-Content -LiteralPath (Join-Path $friendLayout 'SHA256.txt') -Encoding ASCII
    $friendZip = Join-Path $friendOutput "Morf-$friendVersionLabel-Kurulum-x64.zip"
    Compress-Archive -LiteralPath @($friendAllowedFiles + 'SHA256.txt' | ForEach-Object { Join-Path $friendLayout $_ }) -DestinationPath $friendZip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $friendArchive = [IO.Compression.ZipFile]::OpenRead($friendZip)
    try {
        if ((@($friendArchive.Entries.FullName | Sort-Object) -join ',') -ne (@($friendAllowedFiles + 'SHA256.txt' | Sort-Object) -join ',')) { throw 'Unexpected ZIP entries.' }
        foreach ($friendName in $friendAllowedFiles + 'SHA256.txt') {
            $friendStream = $friendArchive.GetEntry($friendName).Open(); $friendHasher = [Security.Cryptography.SHA256]::Create()
            try { $friendArchivedHash = [BitConverter]::ToString($friendHasher.ComputeHash($friendStream)).Replace('-', '') } finally { $friendStream.Dispose(); $friendHasher.Dispose() }
            if ($friendArchivedHash -ne (Get-FileHash -LiteralPath (Join-Path $friendLayout $friendName) -Algorithm SHA256).Hash) { throw 'Archived file hash mismatch.' }
        }
    } finally { $friendArchive.Dispose() }
    Write-Output "Verified friend setup: $friendZip; no installation or trust changes performed."
} finally { Pop-Location }
