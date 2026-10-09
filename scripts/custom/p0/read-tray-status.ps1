# @file read-tray-status.ps1
# @author: rudals252
# @brief P0 only: read-only UI Automation scan of tray elements whose accessible name starts with
#        "Notifications Pro", to read the listener-mode tooltip (technical status only).
#        With -OpenOverflow it toggles the "show hidden icons" flyout open, scans, then toggles it closed.
#        No settings are changed.
[CmdletBinding()]
param([switch]$OpenOverflow)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement

function Find-ByClass([string]$cls) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, $cls)
    return $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
}

$chevron = $null
if ($OpenOverflow) {
    $tray = (Find-ByClass 'Shell_TrayWnd') | Select-Object -First 1
    $idCond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'SystemTray.NormalButton')
    foreach ($b in $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $idCond)) {
        if ($b.Current.AutomationId -eq 'SystemTrayIcon' -and $b.Current.Name -match '숨겨진 아이콘|hidden icons') { $chevron = $b; break }
    }
    if ($chevron) {
        $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 800
    }
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($cls in @('Shell_TrayWnd', 'Shell_SecondaryTrayWnd', 'NotifyIconOverflowWindow', 'TopLevelWindowForOverflowXamlIsland')) {
    foreach ($win in (Find-ByClass $cls)) {
        foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            $name = $el.Current.Name
            if ($name -and $name -like 'Notifications Pro*') {
                $results.Add([pscustomobject]@{ host = $cls; name = ($name -replace "`r?`n", ' || ') })
            }
        }
    }
}

if ($chevron) {
    # Toggle the flyout closed again. The flyout can also auto-dismiss on focus change, in which case
    # this second Invoke re-opens it; observed once in P0. Re-run without -OpenOverflow to check:
    # a match without toggling means the flyout is still open.
    try { $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { }
}

if ($results.Count -eq 0) { Write-Output 'NO_MATCH' }
$results | Format-List
