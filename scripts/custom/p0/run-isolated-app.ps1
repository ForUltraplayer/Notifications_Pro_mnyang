# @file run-isolated-app.ps1
# @author: rudals252
# @brief P0 only: launch the isolated NotificationsPro build with AppData redirected to .p0\appdata,
#        wait for it, record technical start/exit metadata, and propagate its exit code
#        (so Task Scheduler sees the app's own exit code). Never records notification content.
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$ExePath
)
$ErrorActionPreference = 'Stop'
# PowerShell 5.1 leaves $PSScriptRoot empty inside param defaults, so resolve it here.
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path }

if (-not $ExePath) {
    $ExePath = Join-Path $RepoRoot '.p0\isolated-src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0\NotificationsPro.exe'
}
$appData = Join-Path $RepoRoot '.p0\appdata'
$logPath = Join-Path $RepoRoot '.p0\wrapper.log'
New-Item -ItemType Directory -Force -Path $appData | Out-Null

# The isolated build reads NP_P0_APPDATA (fail-closed). APPDATA is set too for child consistency.
$env:NP_P0_APPDATA = $appData
$env:APPDATA = $appData

function Write-WrapperLog([string]$Event, [hashtable]$Data) {
    $Data['at'] = [DateTime]::Now.ToString('o')
    $Data['event'] = $Event
    $Data['wrapper_pid'] = $PID
    $Data['wrapper_session'] = (Get-Process -Id $PID).SessionId
    $Data['wrapper_user'] = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $Data['wrapper_elevated'] = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    ($Data | ConvertTo-Json -Compress) | Add-Content -LiteralPath $logPath -Encoding utf8
}

$proc = Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath) -PassThru
Write-WrapperLog 'started' @{ app_pid = $proc.Id; exe = $ExePath }
$proc.WaitForExit()
$code = $proc.ExitCode
Write-WrapperLog 'exited' @{ app_pid = $proc.Id; exit_code = $code }
exit $code
