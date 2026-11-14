param([string]$ChecksDirectory = 'artifacts/inbox-checks', [string]$PreviewDirectory = 'artifacts/inbox-preview')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Build-InboxPreview.ps1') -OutputDirectory $PreviewDirectory
$checkOutput = Join-Path $repo $ChecksDirectory
New-Item -ItemType Directory -Force -Path $checkOutput | Out-Null
$fixtureTarget = Join-Path $checkOutput 'junction-target'
$fixtureLink = Join-Path $checkOutput 'junction-fixture'
New-Item -ItemType Directory -Force -Path $fixtureTarget | Out-Null
Set-Content -LiteralPath (Join-Path $fixtureTarget 'Document.txt') -Value 'Local junction regression fixture'
if (-not (Test-Path -LiteralPath $fixtureLink)) {
    New-Item -ItemType Junction -Path $fixtureLink -Target $fixtureTarget | Out-Null
}
$checkFlags = $flags | Where-Object { -not $_.StartsWith('/out:') -and -not $_.StartsWith('/target:') }
$checkSources = Get-ChildItem -LiteralPath (Join-Path $repo 'tests/Widgates.Checks') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& (Join-Path $framework 'csc.exe') @checkFlags '/target:exe' '/main:Widgates.Checks' "/out:$checkOutput/Checks.exe" @referenceFlags @sources @checkSources
if ($LASTEXITCODE -ne 0) { throw "Inbox checks compilation failed ($LASTEXITCODE)." }
& (Join-Path $checkOutput 'Checks.exe') $checkOutput
if ($LASTEXITCODE -ne 0) { throw "Inbox checks failed ($LASTEXITCODE)." }
