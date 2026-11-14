function New-MorfShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $shortcutPath = [IO.Path]::GetFullPath($ShortcutPath)
    $targetPath = [IO.Path]::GetFullPath($TargetPath)
    $parent = [IO.Path]::GetDirectoryName($shortcutPath)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw 'Shortcut folder is unavailable.'
    }
    $shell = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $shortcutPath) {
        # Never replace a user's unrelated shortcut or customized existing link.
        try {
            $existing = $shell.CreateShortcut($shortcutPath)
            if ([string]::Equals($existing.TargetPath, $targetPath, [StringComparison]::OrdinalIgnoreCase) -and -not $existing.Arguments) {
                return 'exists'
            }
        } catch { }
        return 'conflict'
    }

    $temporary = Join-Path $parent ('Morf-' + [Guid]::NewGuid().ToString('N') + '.tmp.lnk')
    try {
        $link = $shell.CreateShortcut($temporary)
        $link.TargetPath = $targetPath
        $link.WorkingDirectory = [IO.Path]::GetDirectoryName($targetPath)
        $link.Description = 'Morf'
        $link.IconLocation = $targetPath + ',0'
        $link.Save()
        # Move fails if a file appeared meanwhile; never replace it.
        [IO.File]::Move($temporary, $shortcutPath)
        return 'created'
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
