# @file snapshot-notification-settings.ps1
# @author: rudals252
# @brief P0 read-only helper: snapshot per-app Windows notification setting values
#        (HKCU\...\Notifications\Settings\<AUMID>) to JSON, or compare two snapshots, so the user can see
#        which value the "Play a sound when a notification arrives" toggle writes. Never writes the registry.
[CmdletBinding(DefaultParameterSetName = 'Snapshot')]
param(
    [Parameter(ParameterSetName = 'Snapshot', Mandatory)][string]$Out,
    [Parameter(ParameterSetName = 'Compare', Mandatory)][string]$Before,
    [Parameter(ParameterSetName = 'Compare', Mandatory)][string]$After,
    [string[]]$AppIds = @(
        'OpenAI.Codex_2p2nqsd0c76g0!App',
        'Claude_pzs8sxrjxfjjc!Claude',
        'com.squirrel.Discord.Discord',
        '{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Kakao\KakaoTalk\KakaoTalk.exe'
    )
)
$ErrorActionPreference = 'Stop'
$base = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
# Counters/timestamps change on every notification; exclude them from comparisons.
$volatile = @('LastNotificationAddedTime', 'PeriodicNotificationCount', 'PeriodicInteractionCount')

if ($PSCmdlet.ParameterSetName -eq 'Snapshot') {
    $snap = [ordered]@{ takenAt = [DateTime]::Now.ToString('o'); apps = [ordered]@{} }
    foreach ($id in $AppIds) {
        $key = Join-Path $base $id
        $vals = [ordered]@{}
        if (Test-Path -LiteralPath $key) {
            $item = Get-Item -LiteralPath $key
            foreach ($n in $item.GetValueNames()) { $vals[$n] = [string]$item.GetValue($n) }
            $snap.apps[$id] = [ordered]@{ keyExists = $true; values = $vals }
        } else {
            $snap.apps[$id] = [ordered]@{ keyExists = $false; values = $vals }
        }
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($Out))) | Out-Null
    $snap | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Out -Encoding utf8
    Write-Output "SNAPSHOT $Out"
    return
}

$a = Get-Content -LiteralPath $Before -Raw | ConvertFrom-Json
$b = Get-Content -LiteralPath $After -Raw | ConvertFrom-Json
foreach ($id in ($a.apps.PSObject.Properties.Name + $b.apps.PSObject.Properties.Name | Sort-Object -Unique)) {
    $va = $a.apps.$id.values; $vb = $b.apps.$id.values
    $names = @($va.PSObject.Properties.Name) + @($vb.PSObject.Properties.Name) | Where-Object { $_ -and $volatile -notcontains $_ } | Sort-Object -Unique
    foreach ($n in $names) {
        $x = $va.$n; $y = $vb.$n
        if ($x -ne $y) { Write-Output ("[{0}] {1}: '{2}' -> '{3}'" -f $id, $n, $x, $y) }
    }
}
