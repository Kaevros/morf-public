$ErrorActionPreference = 'Stop'
$runExe = Join-Path $env:LOCALAPPDATA 'Morf/app/WindowsWidgates.exe'
if (-not (Test-Path -LiteralPath $runExe -PathType Leaf)) {
    throw 'Installed Morf was not found. Build and run scripts/Install-UserPreview.ps1 first.'
}
$runPreview = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/inbox-refined-preview/WindowsWidgates.exe'
if ((Test-Path -LiteralPath $runPreview) -and
    ((Get-FileHash -LiteralPath $runPreview -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $runExe -Algorithm SHA256).Hash)) {
    throw 'The installed Morf differs from the latest local preview. Run scripts/Install-UserPreview.ps1 to update it.'
}
$runExisting = @(Get-Process -Name WindowsWidgates -ErrorAction SilentlyContinue)
if (@($runExisting | Where-Object { $_.Path -ne $runExe }).Count -gt 0) { throw 'A different Morf copy is running. Exit it normally before switching versions.' }
Start-Process -FilePath $runExe -WindowStyle Hidden
if ($runExisting.Count -gt 0) { Write-Output 'Requested the existing Morf window. Game hiding rules remain active.' }
else { Write-Output 'Started the installed Morf. Hover the docked edge or press Ctrl+Alt+M to open it.' }

