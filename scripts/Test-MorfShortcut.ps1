$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Morf-Shortcut.ps1')
$testRoot = Join-Path $repo ('artifacts/shortcut-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$target = Join-Path $testRoot 'example-target.txt'
[IO.File]::WriteAllText($target, 'shortcut fixture')
$shortcut = Join-Path $testRoot 'Morf.lnk'
if ((New-MorfShortcut -ShortcutPath $shortcut -TargetPath $target) -ne 'created') { throw 'Shortcut was not created.' }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
if ($link.TargetPath -ne $target -or $link.WorkingDirectory -ne $testRoot -or $link.IconLocation -ne ($target + ',0')) { throw 'Shortcut metadata is incorrect.' }
Write-Output 'PASS Shortcut uses the chosen target, working directory and icon.'
$before = (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash
if ((New-MorfShortcut -ShortcutPath $shortcut -TargetPath $target) -ne 'exists' -or (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash -ne $before) { throw 'Existing shortcut was changed.' }
Write-Output 'PASS Existing matching shortcut is preserved.'
$other = Join-Path $testRoot 'other-target.txt'
[IO.File]::WriteAllText($other, 'other fixture')
if ((New-MorfShortcut -ShortcutPath $shortcut -TargetPath $other) -ne 'conflict' -or (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash -ne $before) { throw 'Unrelated shortcut was replaced.' }
Write-Output 'PASS Conflicting shortcut is never overwritten.'
$link.Arguments = '--custom'; $link.Save()
$custom = (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash
if ((New-MorfShortcut -ShortcutPath $shortcut -TargetPath $target) -ne 'conflict' -or (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash -ne $custom) { throw 'A customized shortcut was replaced.' }
Write-Output 'PASS A link with custom arguments is preserved.'
if (@(Get-ChildItem -LiteralPath $testRoot -Filter '*.tmp.lnk').Count -ne 0) { throw 'A temporary shortcut remained.' }
Write-Output 'PASS Temporary shortcut is removed after creation.'
