param([string]$Dotnet = '', [switch]$NativeChecks, [switch]$Install, [switch]$Launch, [switch]$DesktopShortcut)

$ErrorActionPreference = 'Stop'
$packageRepo = Split-Path $PSScriptRoot -Parent
Push-Location $packageRepo
try {
    if (-not $Dotnet) {
        $localSdk = Join-Path $packageRepo 'artifacts/dotnet/dotnet.exe'
        $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
    }
    $env:DOTNET_CLI_HOME = Join-Path $packageRepo 'artifacts/dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if ((& $Dotnet --version) -notmatch '^10\.') { throw '.NET 10 SDK is required.' }
    if ($DesktopShortcut -and -not $Install) { throw '-DesktopShortcut requires -Install.' }
    $versionSource = Get-Content -LiteralPath 'src/Widgates/BuildInfo.cs' -Raw
    if ($versionSource -notmatch 'Version = "([0-9.]+)-preview"') { throw 'Cannot read product version.' }
    $version = $Matches[1]
    [xml]$project = Get-Content -LiteralPath 'src/Widgates/Widgates.csproj'
    if ($project.Project.PropertyGroup.Version -ne "$version-preview") { throw 'SDK and inbox versions differ.' }
    $results = Join-Path $packageRepo 'artifacts/lifecycle-checks'
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    function Invoke-MorfCheck([string]$Label, [scriptblock]$Run) {
        & $Run 2>&1 | Tee-Object -FilePath (Join-Path $results ($Label + '.log'))
        if ($LASTEXITCODE -ne 0) { throw "$Label failed ($LASTEXITCODE)." }
    }
    Invoke-MorfCheck 'net-build' { & $Dotnet build src/Widgates/Widgates.csproj -c Release -o artifacts/refined-release }
    $netOutput = Join-Path $results 'net'
    New-Item -ItemType Directory -Force -Path (Join-Path $netOutput 'junction-target') | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $netOutput 'junction-fixture'))) {
        New-Item -ItemType Junction -Path (Join-Path $netOutput 'junction-fixture') -Target (Join-Path $netOutput 'junction-target') | Out-Null
    }
    Invoke-MorfCheck 'net-standard' { & $Dotnet run --project tests/Widgates.Checks -c Release -- $netOutput }
    Invoke-MorfCheck 'inbox-standard' { & (Join-Path $PSScriptRoot 'Check-InboxPreview.ps1') -ChecksDirectory 'artifacts/lifecycle-checks/inbox' -PreviewDirectory 'artifacts/inbox-refined-preview' }
    if ($NativeChecks) {
        foreach ($scenario in @('layout', 'settings', 'lifecycle', 'notifications', 'expansion', 'floating', 'drawer', 'clock', 'display', 'refresh-volume', 'windows-notifications', 'product-pack')) {
            Invoke-MorfCheck ("net-native-" + $scenario) { & $Dotnet run --project tests/Widgates.Checks -c Release --no-build -- ("--native-" + $scenario) (Join-Path $results ("native-net-" + $scenario)) }
            Invoke-MorfCheck ("inbox-native-" + $scenario) { & (Join-Path $results 'inbox/Checks.exe') ("--native-" + $scenario) (Join-Path $results ("native-inbox-" + $scenario)) }
        }
    }
    $exe = Join-Path $packageRepo 'artifacts/inbox-refined-preview/WindowsWidgates.exe'
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Built EXE has an unexpected version.' }
    $share = Join-Path $packageRepo "artifacts/share/Morf-$version-preview"
    New-Item -ItemType Directory -Force -Path $share | Out-Null
    # Exact allowlist: no source code, settings, history, build reports or SDK files enter the archive.
    Copy-Item -LiteralPath $exe -Destination (Join-Path $share 'WindowsWidgates.exe') -Force
    Copy-Item -LiteralPath (Join-Path $packageRepo 'LICENSE') -Destination (Join-Path $share 'LISANS.txt') -Force
    @"
Morf $version-preview

Windows API tabanı: Windows 10 1809 (17763). .NET Framework 4.8 gerekir.
Bu önizlemenin tüm Windows sürümlerinde uyumluluğu doğrulanmamıştır.

ZIP'i bir klasöre çıkarın ve WindowsWidgates.exe dosyasını açın.
Ctrl+Alt+M paneli açar/kapatır. Dişli simgesi veya sağ tık ayarları açar.
Uygulamadan ayarlar ya da bildirim alanı menüsüyle çıkabilirsiniz.
Windows ile başlama varsayılan kapalıdır; Sistem ayarlarından açılır.

Raf geçicidir ve çıkışta temizlenir. Dosyaların asılları yerinde kalır.
Canlı Windows bildirimleri bu taşınabilir sürümde kullanılamaz;
ayrı paket kimliği, güvenilir imza ve Windows izni gerekir.
Çoklu ses çıkışı deneyseldir; cihazlar arasında gecikme olabilir.

Bu taşınabilir önizleme imzasızdır. Önceki Morf dosyaları Kaspersky
tarafından işaretlendi; inceleme sonuçlanmadı. Çalıştırmak için antivirüs
korumasını kapatmayın. SHA256.txt, EXE dosyasının doğrulama özetini içerir.
"@ | Set-Content -LiteralPath (Join-Path $share 'ONCE-OKU.txt') -Encoding UTF8
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    ($hash + '  WindowsWidgates.exe') | Set-Content -LiteralPath (Join-Path $share 'SHA256.txt') -Encoding ASCII
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $packageRepo "artifacts/share/Morf-$version-preview-Windows.zip"
    $temporaryZip = $zipPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
    try {
        $zip = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
        try { foreach ($file in @('WindowsWidgates.exe', 'ONCE-OKU.txt', 'LISANS.txt', 'SHA256.txt')) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $share $file), $file, [IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
        finally { $zip.Dispose() }
        Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
    } finally { if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip } }
    $verify = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ((($verify.Entries.FullName | Sort-Object) -join ',') -ne 'LISANS.txt,ONCE-OKU.txt,SHA256.txt,WindowsWidgates.exe') { throw 'Unexpected archive contents.' }
        $stream = $verify.GetEntry('WindowsWidgates.exe').Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $archivedHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
        if ($archivedHash -ne $hash) { throw 'Archive hash mismatch.' }
    } finally { $verify.Dispose() }
    Write-Output "Verified source-free package: $zipPath; EXE SHA256 $hash"
    if ($Install) { & (Join-Path $PSScriptRoot 'Install-UserPreview.ps1') -DesktopShortcut:$DesktopShortcut }
    if ($Launch) {
        if (-not $Install) { throw '-Launch requires -Install so the tested binary is used.' }
        & (Join-Path $PSScriptRoot 'Run.ps1')
    }
} finally { Pop-Location }
