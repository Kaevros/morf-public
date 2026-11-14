param([string]$SourceExe = 'artifacts/inbox-refined-preview/WindowsWidgates.exe', [switch]$DesktopShortcut)

$ErrorActionPreference = 'Stop'
$installRepo = Split-Path $PSScriptRoot -Parent
$installSource = (Resolve-Path -LiteralPath (Join-Path $installRepo $SourceExe)).Path
$installRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Morf/app'))
$installTarget = Join-Path $installRoot 'WindowsWidgates.exe'
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Morf')) + [IO.Path]::DirectorySeparatorChar
if (-not $installRoot.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected installation target.' }
foreach ($installProcess in @(Get-Process -Name WindowsWidgates -ErrorAction SilentlyContinue)) {
    if ($installProcess.Path -eq $installTarget) { throw 'Installed Morf is running. Exit it normally before updating; the temporary shelf is held in that process.' }
}
$installAncestor = $installRoot
while ($installAncestor) {
    if ((Test-Path -LiteralPath $installAncestor) -and ((Get-Item -LiteralPath $installAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation path must not traverse a reparse point.' }
    $installAncestor = Split-Path -Path $installAncestor -Parent
}
if ((Test-Path -LiteralPath $installTarget) -and ((Get-Item -LiteralPath $installTarget -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation target must not be a reparse point.' }
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Copy-Item -LiteralPath $installSource -Destination $installTarget -Force
if ((Get-FileHash -LiteralPath $installSource -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $installTarget -Algorithm SHA256).Hash) { throw 'Installed binary verification failed.' }
. (Join-Path $PSScriptRoot 'Morf-Shortcut.ps1')
$shortcutDestinations = @(@{ Name = 'Start menu'; Folder = [Environment]::GetFolderPath('Programs') })
if ($DesktopShortcut) { $shortcutDestinations += @{ Name = 'Desktop'; Folder = [Environment]::GetFolderPath('DesktopDirectory') } }
foreach ($destination in $shortcutDestinations) {
    try {
        if (-not $destination.Folder) { throw 'Windows did not provide a shortcut folder.' }
        $shortcut = Join-Path $destination.Folder 'Morf.lnk'
        $status = New-MorfShortcut -ShortcutPath $shortcut -TargetPath $installTarget
        if ($status -eq 'conflict') { Write-Warning ($destination.Name + ': an unrelated Morf.lnk already exists and was not replaced.') }
        else { Write-Output ($destination.Name + ' shortcut: ' + $status) }
    } catch { Write-Warning ($destination.Name + ' shortcut could not be created: ' + $_.Exception.Message) }
}
Write-Output ('Installed current-user preview: ' + $installTarget)
Write-Output 'Verified binary. Startup registry and saved preferences were not changed. Existing widget processes were preserved.'
