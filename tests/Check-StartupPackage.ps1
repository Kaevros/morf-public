param([Parameter(Mandatory=$true)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
[xml]$manifest = Get-Content -LiteralPath $ManifestPath -Raw
$tasks = @($manifest.SelectNodes("//*[local-name()='StartupTask' and @TaskId='MorfStartup']"))
if ($tasks.Count -ne 1) { throw 'A packaged Morf must declare exactly one native Windows startup task.' }
$task = $tasks[0]
if ($task.Enabled -ne 'false' -or $task.DisplayName -ne 'Morf') { throw 'Packaged startup must remain opt-in and identify Morf.' }
if ($task.ParentNode.Category -ne 'windows.startupTask' -or $task.ParentNode.Executable -ne 'WindowsWidgates.exe' -or $task.ParentNode.EntryPoint -ne 'Windows.FullTrustApplication') { throw 'Startup must resolve the current package executable, without an absolute version-specific path.' }
Write-Output 'PASS Signed-package manifest declares an opt-in, version-independent Windows startup task.'
