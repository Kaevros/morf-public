param([string]$NativeExecutable = 'artifacts/brave-fix-inbox/WindowsWidgates.exe',
    [string]$BrowserExecutable = 'C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe')
$ErrorActionPreference = 'Stop'
$testRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testNative = [IO.Path]::GetFullPath((Join-Path $testRepo $NativeExecutable))
if (!$testNative.StartsWith($testRepo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $testNative)) { throw 'A built repository executable is required.' }
$manifestPath = Join-Path $env:LOCALAPPDATA 'Morf/PiP/chromium-host.json'
$originalManifest = [IO.File]::ReadAllText($manifestPath)
$manifest = $originalManifest | ConvertFrom-Json
if ($manifest.name -ne 'com.morf.pip') { throw 'Unexpected host registration.' }
$manifest.path = $testNative
$temporaryManifest = $manifest | ConvertTo-Json -Depth 4
$encoding = New-Object Text.UTF8Encoding($false)
try {
    # Reuse the already registered per-user manifest only for this bounded test.
    # The finally block restores the exact original unless the owner/app changed it.
    [IO.File]::WriteAllText($manifestPath, $temporaryManifest, $encoding)
    $env:MORF_CHROMIUM_EXECUTABLE = $BrowserExecutable
    Push-Location $testRepo
    try { & node (Join-Path $PSScriptRoot 'real-native-browser-check.cjs'); $testExitCode = $LASTEXITCODE }
    finally { Pop-Location }
} finally {
    $currentManifest = [IO.File]::ReadAllText($manifestPath)
    if ($currentManifest -eq $temporaryManifest) { [IO.File]::WriteAllText($manifestPath, $originalManifest, $encoding) }
    else { Write-Output 'Host registration changed independently during the test; the newer content was retained.' }
}
exit $testExitCode
