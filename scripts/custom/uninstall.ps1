# @file uninstall.ps1
# @author: rudals252
# @brief Notifications Pro KR-Mnyang 설치본을 제거한다. 설치 경로에서 실행 중이면 --quit으로 정상 종료를 요청하고 최대 10초 기다린다
#        (강제 종료 없음) → 로그온 작업 NotificationsProKR-Mnyang-Logon 해제(없으면 무시) → 프로그램 폴더·시작 메뉴 바로가기 삭제.
#        AppData(설정·소리·프로필)는 기본 보존하고 -RemoveData를 줄 때만 목록을 보여 준 뒤 삭제한다.
#        Windows 알림 소리 토글 등 수동으로 바꾼 설정은 되돌리지 않고 안내만 출력한다. -WhatIf / -Confirm을 지원한다.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # %AppData%\NotificationsProKR-Mnyang(설정·가져온 WAV 사본·프로필·백업)도 삭제한다.
    [switch]$RemoveData,
    # -RemoveData의 추가 확인 질문을 생략한다.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$exeName = 'NotificationsProKR-Mnyang.exe'
$productName = 'Notifications Pro KR-Mnyang'
$logonTaskName = 'NotificationsProKR-Mnyang-Logon'
$quitTimeoutSeconds = 10
$installDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\NotificationsProKR-Mnyang'
$installExe = Join-Path $installDir $exeName
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) "$productName.lnk"
$dataDir = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'NotificationsProKR-Mnyang'

function Test-SamePath([string]$a, [string]$b) {
    if (-not $a -or -not $b) { return $false }
    return [string]::Equals([IO.Path]::GetFullPath($a).TrimEnd('\'), [IO.Path]::GetFullPath($b).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)
}

function Get-ForkProcesses {
    # 이름이 같은 프로세스를 모두 찾되, 판단은 실행 파일 전체 경로로 한다(설계 §15). 경로를 읽지 못하면 빈 문자열(대상 아님).
    @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue | ForEach-Object {
        $proc = $_
        $path = ''
        try { $path = [string]$proc.MainModule.FileName } catch { $path = '' }
        [pscustomobject]@{ ProcessId = [int]$proc.Id; SessionId = [int]$proc.SessionId; Path = $path }
    })
}

function Request-Quit([object]$proc) {
    Write-Output "  --quit 요청: PID $($proc.ProcessId) ($($proc.Path))"
    $sender = Start-Process -FilePath $proc.Path -ArgumentList '--quit' -PassThru -WindowStyle Hidden
    if (-not $sender.WaitForExit(15000)) {
        throw "--quit 전달 프로세스가 15초 안에 끝나지 않았습니다(PID $($sender.Id)). 아무것도 지우지 않았습니다."
    }
    if ($sender.ExitCode -ne 0) {
        throw "--quit 전달에 실패했습니다(종료 코드 $($sender.ExitCode)). 트레이 메뉴에서 직접 종료한 뒤 다시 실행하세요. 아무것도 지우지 않았습니다."
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($quitTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue)) {
            Write-Output "  종료 확인: PID $($proc.ProcessId)"
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "PID $($proc.ProcessId)가 $quitTimeoutSeconds초 안에 종료되지 않았습니다. 강제 종료하지 않습니다. 트레이 메뉴에서 종료한 뒤 다시 실행하세요. 아무것도 지우지 않았습니다."
}

Write-Output "== $productName 제거 =="
Write-Output "프로그램 : $installDir"
Write-Output "데이터   : $dataDir ($(if ($RemoveData) { '삭제 대상(-RemoveData)' } else { '보존' }))"

# 1. 설치 경로에서 실행 중인 인스턴스만 정상 종료 요청
$currentSession = (Get-Process -Id $PID).SessionId
$running = @(Get-ForkProcesses)
$ours = @($running | Where-Object { Test-SamePath $_.Path $installExe })
foreach ($o in @($running | Where-Object { -not (Test-SamePath $_.Path $installExe) })) {
    Write-Warning "다른 위치의 포크 인스턴스가 실행 중입니다(PID $($o.ProcessId), $($o.Path)). 건드리지 않습니다."
}
$oursOtherSession = @($ours | Where-Object { $_.SessionId -ne $currentSession })
if ($oursOtherSession.Count -gt 0) {
    throw "다른 Windows 세션에서 설치본이 실행 중입니다(PID $(($oursOtherSession | ForEach-Object { $_.ProcessId }) -join ', ')). 그 세션에서 종료한 뒤 다시 실행하세요. 아무것도 지우지 않았습니다."
}
foreach ($p in @($ours | Where-Object { $_.SessionId -eq $currentSession })) {
    if ($PSCmdlet.ShouldProcess("PID $($p.ProcessId) ($($p.Path))", '--quit으로 정상 종료 요청 후 최대 10초 대기')) { Request-Quit $p }
}
if ($ours.Count -eq 0) { Write-Output '실행 중인 설치본: 없음' }

# 2. 로그온 작업 해제(EXE 삭제 전, 설계 §9.3). 없으면 무시하고, 조회·해제 실패 시 프로그램 파일을 지우지 않고 중단한다.
$taskExists = $false
try {
    $svc = New-Object -ComObject Schedule.Service
    $svc.Connect()
    $task = $svc.GetFolder('\').GetTask($logonTaskName)
    $taskExists = $true
    foreach ($action in $task.Definition.Actions) { if ($action.Type -eq 0) { Write-Output "로그온 작업 대상 exe: $($action.Path)" } }
} catch {
    $hr = if ($_.Exception -is [System.Runtime.InteropServices.COMException]) { $_.Exception.ErrorCode } elseif ($_.Exception.InnerException) { $_.Exception.InnerException.HResult } else { $_.Exception.HResult }
    if ($hr -ne -2147024894) {  # 0x80070002 = 작업 없음
        throw ("로그온 작업 상태를 확인하지 못했습니다(0x{0:X8}). 프로그램 파일은 지우지 않았습니다. 앱 시스템 탭이나 작업 스케줄러에서 확인한 뒤 다시 실행하세요." -f $hr)
    }
}
if ($taskExists) {
    if ($PSCmdlet.ShouldProcess($logonTaskName, '로그온 작업 해제 (schtasks /Delete /F)')) {
        $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        try { $out = (& schtasks.exe /Delete /TN $logonTaskName /F 2>&1 | Out-String).Trim(); $code = $LASTEXITCODE }
        finally { $ErrorActionPreference = $eap }
        if ($code -ne 0) {
            throw "로그온 작업을 해제하지 못했습니다(종료 코드 $code): $out 프로그램 파일은 지우지 않았습니다. 앱 시스템 탭의 '작업 등록 해제'나 작업 스케줄러에서 해제한 뒤 다시 실행하세요."
        }
        Write-Output "로그온 작업 해제: $logonTaskName ($out)"
    }
} else {
    Write-Output "로그온 작업($logonTaskName): 없음"
}

# 3. 프로그램 폴더·바로가기 삭제
if (Test-Path -LiteralPath $installDir) {
    if (-not (Test-Path -LiteralPath $installExe -PathType Leaf)) {
        Write-Warning "설치 폴더에 $exeName 이 없어 이 앱 폴더인지 확인할 수 없습니다. 지우지 않습니다: $installDir"
    } elseif ($PSCmdlet.ShouldProcess($installDir, '프로그램 폴더 삭제')) {
        Remove-Item -LiteralPath $installDir -Recurse -Force
        Write-Output "프로그램 폴더 삭제: $installDir"
    }
} else {
    Write-Output '프로그램 폴더: 없음'
}
if (Test-Path -LiteralPath $shortcutPath) {
    if ($PSCmdlet.ShouldProcess($shortcutPath, '시작 메뉴 바로가기 삭제')) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Output "바로가기 삭제: $shortcutPath"
    }
} else {
    Write-Output '시작 메뉴 바로가기: 없음'
}

# 4. 데이터: 기본 보존, -RemoveData일 때만 목록 표시 후 삭제
if (Test-Path -LiteralPath $dataDir) {
    if ($RemoveData) {
        Write-Output ''
        Write-Output "== 삭제할 데이터 목록: $dataDir =="
        $items = @(Get-ChildItem -LiteralPath $dataDir -Force)
        foreach ($i in $items) {
            if ($i.PSIsContainer) {
                $inner = @(Get-ChildItem -LiteralPath $i.FullName -Recurse -File -Force)
                $size = ($inner | Measure-Object Length -Sum).Sum
                Write-Output ('  [폴더] {0}  (파일 {1}개, {2:N0} bytes)' -f $i.Name, $inner.Count, [double]$size)
            } else {
                Write-Output ('  [파일] {0}  ({1:N0} bytes)' -f $i.Name, $i.Length)
            }
        }
        Write-Output '  주의: sounds\의 WAV는 가져올 때 만든 사본입니다. 원래 WAV 파일과 외부 폴더로 추가한 WAV는 지우지 않습니다.'
        Write-Output '  원본 Notifications Pro 데이터(%AppData%\NotificationsPro, MSIX LocalCache)는 대상이 아닙니다.'
        if ($PSCmdlet.ShouldProcess($dataDir, '데이터 폴더 삭제(되돌릴 수 없음)')) {
            if ($Force -or $PSCmdlet.ShouldContinue("$dataDir 을(를) 삭제할까요? 되돌릴 수 없습니다.", '데이터 삭제 확인')) {
                Remove-Item -LiteralPath $dataDir -Recurse -Force
                Write-Output "데이터 폴더 삭제: $dataDir"
            } else {
                Write-Output '데이터 삭제를 취소했습니다. 데이터는 그대로입니다.'
            }
        }
    } else {
        Write-Output "데이터 보존: $dataDir (삭제하려면 -RemoveData)"
    }
} else {
    Write-Output '데이터 폴더: 없음'
}

# 5. 수동 복원 안내(이 스크립트는 바꾸지 않음)
Write-Output ''
Write-Output '== 직접 되돌려야 하는 설정 =='
Write-Output '- Windows 알림 소리: 설정 > 시스템 > 알림 > 앱별로 "알림이 도착하면 소리 재생"을 다시 켜세요(ChatGPT/Codex, Claude, Discord 등 끈 앱마다).'
Write-Output '- Discord: 사용자 설정 > 알림 > 소리에서 끈 항목(예: 메시지)을 다시 켜세요.'
Write-Output '- 기존 PS1 감시기로 돌아가려면 docs\custom\MIGRATION-ko.md의 "되돌리기"를 따르세요.'
if ($WhatIfPreference) { Write-Output ''; Write-Output 'WhatIf: 실제로 바뀐 것은 없습니다.' }
