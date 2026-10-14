param(
    [Parameter(Mandatory = $true)][string]$LogName,
    [string[]]$Extra = @(),
    [switch]$NoQuit
)
# Start-Process -Wait also waits for the VBCSCompiler server that Unity spawns,
# so wait only for the Unity process itself via -PassThru + WaitForExit.
$u = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe'
$proj = 'D:\Projects\tripletriad'
$log = Join-Path $proj "Logs\$LogName"
New-Item -ItemType Directory -Force (Join-Path $proj 'Logs') | Out-Null
$running = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.ToLower().Contains($proj.ToLower()) }
if ($running) { Write-Output "Unity already has this project open: $($running.ProcessId -join ',')"; exit 99 }
if (Test-Path (Join-Path $proj 'Temp\UnityLockfile')) { Remove-Item (Join-Path $proj 'Temp\UnityLockfile') -Force; Write-Output 'removed stale UnityLockfile' }
$args2 = @('-batchmode', '-nographics', '-projectPath', "`"$proj`"", '-logFile', "`"$log`"")
if (-not $NoQuit) { $args2 = @('-quit') + $args2 }
$args2 += $Extra
$t = Get-Date
$proc = Start-Process -FilePath $u -ArgumentList $args2 -PassThru
$null = $proc.Handle
$proc.WaitForExit()
$sec = [int]((Get-Date) - $t).TotalSeconds
Write-Output "exit=$($proc.ExitCode) sec=$sec log=$log"
Select-String -Path $log -Pattern 'error CS|Scripts have compiler errors|Compilation failed|\[Edgewise\]|Build Finished|Exception:' | Select-Object -First 40 | ForEach-Object { $_.Line }
