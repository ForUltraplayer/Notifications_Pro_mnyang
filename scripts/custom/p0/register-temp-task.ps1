# @file register-temp-task.ps1
# @author: rudals252
# @brief P0 only: register / inspect / remove the on-demand temporary task NotificationsProCustom-P0-Temp
#        (no triggers, current user, interactive logon only, least privilege, no execution time limit,
#        restart on failure every 1 minute up to 3 times). Always run -Remove after the check.
[CmdletBinding(DefaultParameterSetName = 'Register')]
param(
    [Parameter(ParameterSetName = 'Register')][switch]$Register,
    [Parameter(ParameterSetName = 'Remove')][switch]$Remove,
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path }
$taskName = 'NotificationsProCustom-P0-Temp'

if ($Remove) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw "Task still exists: $taskName" }
    Write-Output "REMOVED $taskName"
    return
}

$wrapper = Join-Path $RepoRoot 'scripts\custom\p0\run-isolated-app.ps1'
$exeDir = Join-Path $RepoRoot '.p0\isolated-src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0'
if (-not (Test-Path (Join-Path $exeDir 'NotificationsPro.exe'))) { throw "Isolated build not found. Run build-isolated-app.ps1 first." }

$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$wrapper`"" `
    -WorkingDirectory $exeDir
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
# No -Trigger: on-demand only (no logon trigger) for P0.
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings `
    -Description 'P0 temporary verification task. Safe to delete.' | Out-Null
Write-Output "REGISTERED $taskName"
