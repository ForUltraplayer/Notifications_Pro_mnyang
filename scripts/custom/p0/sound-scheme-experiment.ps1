# @file sound-scheme-experiment.ps1
# @author: rudals252
# @brief P0 experiment draft (NOT EXECUTED in P0): back up, silence, and restore the Windows sound
#        scheme event for the default toast sound (HKCU\AppEvents\Schemes\Apps\.Default\<Event>\.Current).
#        Default action is read-only Status. Apply/Restore require -ConfirmRegistryChange and must only be
#        run after explicit user approval. Restore never overwrites a value the user changed meanwhile.
[CmdletBinding(DefaultParameterSetName = 'Status')]
param(
    [Parameter(ParameterSetName = 'Status')][switch]$Status,
    [Parameter(ParameterSetName = 'Backup')][switch]$Backup,
    [Parameter(ParameterSetName = 'Apply')][switch]$Apply,
    [Parameter(ParameterSetName = 'Restore')][switch]$Restore,
    [Parameter(ParameterSetName = 'Apply')][Parameter(ParameterSetName = 'Restore')][switch]$ConfirmRegistryChange,
    # Only the generic toast sound is in scope by default. Add events only after they are measured.
    [string[]]$Events = @('Notification.Default'),
    [string]$StatePath
)
$ErrorActionPreference = 'Stop'
if (-not $StatePath) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    $StatePath = Join-Path $repoRoot '.p0\sound-scheme-state.json'
}
$schemeRoot = 'HKCU:\AppEvents\Schemes\Apps\.Default'
# Marker value written by Apply: empty string means "no sound" for a scheme event.
$appliedValue = ''

function Read-EventValue([string]$EventName) {
    $key = Join-Path $schemeRoot "$EventName\.Current"
    if (-not (Test-Path -LiteralPath $key)) { return [pscustomobject]@{ event = $EventName; keyExists = $false; hasDefault = $false; value = $null } }
    $item = Get-Item -LiteralPath $key
    $names = $item.GetValueNames()
    $hasDefault = $names -contains ''
    return [pscustomobject]@{ event = $EventName; keyExists = $true; hasDefault = $hasDefault; value = if ($hasDefault) { [string]$item.GetValue('') } else { $null } }
}

function Write-EventValue([string]$EventName, $Value, [bool]$Present) {
    $key = Join-Path $schemeRoot "$EventName\.Current"
    if ($Present) {
        Set-ItemProperty -LiteralPath $key -Name '(default)' -Value $Value
    } else {
        # The original value was absent: remove the default value instead of writing an empty one.
        Remove-ItemProperty -LiteralPath $key -Name '(default)' -ErrorAction SilentlyContinue
    }
}

$schemeName = (Get-Item 'HKCU:\AppEvents\Schemes').GetValue('')
$current = $Events | ForEach-Object { Read-EventValue $_ }

switch ($PSCmdlet.ParameterSetName) {
    'Status' {
        [pscustomobject]@{ scheme = $schemeName; events = $current; statePath = $StatePath; stateExists = (Test-Path $StatePath) } | ConvertTo-Json -Depth 5
    }
    'Backup' {
        if (Test-Path $StatePath) { throw "State file already exists; restore or inspect it first: $StatePath" }
        New-Item -ItemType Directory -Force -Path (Split-Path $StatePath) | Out-Null
        [pscustomobject]@{ createdAt = [DateTime]::Now.ToString('o'); scheme = $schemeName; original = $current; applied = $false } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StatePath -Encoding utf8
        Write-Output "BACKUP written: $StatePath"
    }
    'Apply' {
        if (-not $ConfirmRegistryChange) { throw 'Apply changes the registry. Re-run with -ConfirmRegistryChange after user approval.' }
        if (-not (Test-Path $StatePath)) { throw 'Run -Backup first.' }
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        if ($state.applied) { throw 'Already applied according to state file.' }
        foreach ($e in $state.original) {
            if (-not $e.keyExists) { Write-Warning "Skip $($e.event): key does not exist"; continue }
            Write-EventValue $e.event $appliedValue $true
        }
        $state.applied = $true
        $state | Add-Member -NotePropertyName appliedAt -NotePropertyValue ([DateTime]::Now.ToString('o')) -Force
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StatePath -Encoding utf8
        Write-Output 'APPLIED (no WM_SETTINGCHANGE broadcast; when the change takes effect is part of the experiment).'
    }
    'Restore' {
        if (-not $ConfirmRegistryChange) { throw 'Restore changes the registry. Re-run with -ConfirmRegistryChange after user approval.' }
        if (-not (Test-Path $StatePath)) { throw "No state file: $StatePath" }
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        $conflicts = @()
        foreach ($e in $state.original) {
            if (-not $e.keyExists) { continue }
            $now = Read-EventValue $e.event
            # Only restore values that still equal what Apply wrote; otherwise the user changed it meanwhile.
            if ($now.hasDefault -and $now.value -eq $appliedValue) {
                Write-EventValue $e.event $e.value ([bool]$e.hasDefault)
            } else {
                $conflicts += $e.event
            }
        }
        if ($conflicts.Count -gt 0) {
            Write-Warning "Not restored (changed after Apply, kept as is): $($conflicts -join ', ')"
        }
        $done = Join-Path (Split-Path $StatePath) ("sound-scheme-state.restored-{0:yyyyMMddHHmmss}.json" -f (Get-Date))
        Move-Item -LiteralPath $StatePath -Destination $done
        Write-Output "RESTORE finished; state archived to $done"
    }
}
