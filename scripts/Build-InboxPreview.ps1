param([string]$OutputDirectory = 'artifacts/inbox-preview')

$ErrorActionPreference = 'Stop'

if (-not [Environment]::Is64BitProcess) { throw 'Run this script from 64-bit PowerShell.' }
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src/Widgates'
$output = Join-Path $repo $OutputDirectory
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32/WinMetadata'
$gac = Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL'
$references = @(
    (Join-Path $framework 'WPF/WindowsBase.dll')
    (Join-Path $framework 'WPF/PresentationCore.dll')
    (Join-Path $framework 'WPF/PresentationFramework.dll')
    (Join-Path $framework 'WPF/UIAutomationTypes.dll')
    (Join-Path $framework 'System.Xaml.dll')
    (Join-Path $metadata 'Windows.Foundation.winmd')
    (Join-Path $metadata 'Windows.Media.winmd')
    (Join-Path $metadata 'Windows.Storage.winmd')
    (Join-Path $metadata 'Windows.UI.winmd')
    (Join-Path $metadata 'Windows.ApplicationModel.winmd')
    (Join-Path $gac 'System.Runtime/v4.0_4.0.0.0__b03f5f7f11d50a3a/System.Runtime.dll')
    (Join-Path $gac 'System.Collections/v4.0_4.0.0.0__b03f5f7f11d50a3a/System.Collections.dll')
    (Join-Path $gac 'System.Runtime.InteropServices.WindowsRuntime/v4.0_4.0.0.0__b03f5f7f11d50a3a/System.Runtime.InteropServices.WindowsRuntime.dll')
)

New-Item -ItemType Directory -Force -Path $output | Out-Null
$flags = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001', '/warnaserror', '/define:INBOX_PREVIEW',
    "/win32manifest:$project/app.manifest", "/win32icon:$project/Assets/Brand/morf.ico", "/out:$output/WindowsWidgates.exe")
$brandResources = Get-ChildItem -LiteralPath (Join-Path $project 'Assets/Brand') -Filter '*.png' |
    ForEach-Object { "/resource:$($_.FullName),Widgates.Brand.$($_.Name)" }
$flags += $brandResources
$referenceFlags = $references | ForEach-Object { "/reference:$_" }
$sources = Get-ChildItem -LiteralPath $project -Filter '*.cs' | Select-Object -ExpandProperty FullName
& (Join-Path $framework 'csc.exe') @flags @referenceFlags @sources
if ($LASTEXITCODE -ne 0) { throw "Preview compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath (Join-Path $output 'WindowsWidgates.exe')
