# @file install.ps1
# @author: rudals252
# @brief Notifications Pro KR-Mnyang를 현재 사용자용으로 설치하거나 업데이트한다(관리자 권한 불필요).
#        %LocalAppData%\Programs\NotificationsProKR-Mnyang\에 publish 산출물을 복사하고 시작 메뉴 바로가기 1개를 만든다.
#        같은 경로(설치 위치 또는 -SourceDir)에서 실행 중인 인스턴스에만 --quit을 보내 최대 10초 기다리며, 강제 종료하지 않는다.
#        로그온 자동 실행 작업·AppData·원본 앱·기존 PS1 감시기·사용자 WAV는 건드리지 않는다. 설치 후 앱을 시작하지 않는다.
#        -WhatIf / -Confirm을 지원한다.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # publish 산출물 폴더. 기본값은 <저장소>\dist\NotificationsProKR-Mnyang
    [string]$SourceDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$exeName = 'NotificationsProKR-Mnyang.exe'
$productName = 'Notifications Pro KR-Mnyang'
$logonTaskName = 'NotificationsProKR-Mnyang-Logon'
$quitTimeoutSeconds = 10
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $SourceDir) { $SourceDir = Join-Path $repoRoot 'dist\NotificationsProKR-Mnyang' }
$SourceDir = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\')
$installDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\NotificationsProKR-Mnyang'
$installExe = Join-Path $installDir $exeName
$sourceExe = Join-Path $SourceDir $exeName
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) "$productName.lnk"

function Test-SamePath([string]$a, [string]$b) {
    if (-not $a -or -not $b) { return $false }
    return [string]::Equals([IO.Path]::GetFullPath($a).TrimEnd('\'), [IO.Path]::GetFullPath($b).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)
}

if (-not ('ForkScripts.ProcessPath' -as [type])) {
    Add-Type -Namespace ForkScripts -Name ProcessPath -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true)] public static extern System.IntPtr OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(System.IntPtr h, int flags, System.Text.StringBuilder sb, ref int size);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
'@
}

function Get-ProcessImagePath([System.Diagnostics.Process]$proc) {
    # MainModule fails for instances started by Task Scheduler (logon task); the limited-access query still works.
    try { $p = [string]$proc.MainModule.FileName; if ($p) { return $p } } catch { }
    $h = [ForkScripts.ProcessPath]::OpenProcess(0x1000, $false, $proc.Id)  # PROCESS_QUERY_LIMITED_INFORMATION
    if ($h -eq [IntPtr]::Zero) { return '' }
    try {
        $sb = New-Object System.Text.StringBuilder 1024
        $size = $sb.Capacity
        if ([ForkScripts.ProcessPath]::QueryFullProcessImageName($h, 0, $sb, [ref]$size)) { return $sb.ToString() }
        return ''
    } finally { [void][ForkScripts.ProcessPath]::CloseHandle($h) }
}

function Get-ForkProcesses {
    # 이름이 같은 프로세스를 모두 찾되, 판단은 실행 파일 전체 경로로 한다(설계 §15). 경로를 읽지 못하면 빈 문자열(대상 아님).
    @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue | ForEach-Object {
        $proc = $_
        $path = Get-ProcessImagePath $proc
        [pscustomobject]@{ ProcessId = [int]$proc.Id; SessionId = [int]$proc.SessionId; Path = $path }
    })
}

function Request-Quit([object]$proc) {
    # --quit은 현재 세션의 주 인스턴스에 이름 있는 파이프로 전달된다. 실행 중인 것과 같은 exe로 보낸다.
    Write-Output "  --quit 요청: PID $($proc.ProcessId) ($($proc.Path))"
    $sender = Start-Process -FilePath $proc.Path -ArgumentList '--quit' -PassThru -WindowStyle Hidden
    if (-not $sender.WaitForExit(15000)) {
        throw "--quit 전달 프로세스가 15초 안에 끝나지 않았습니다(PID $($sender.Id)). 파일은 바꾸지 않았습니다."
    }
    if ($sender.ExitCode -ne 0) {
        throw "--quit 전달에 실패했습니다(종료 코드 $($sender.ExitCode)). 명령 전달을 지원하지 않는 이전 빌드일 수 있습니다. 트레이 메뉴에서 직접 종료한 뒤 다시 실행하세요. 파일은 바꾸지 않았습니다."
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($quitTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue)) {
            Write-Output "  종료 확인: PID $($proc.ProcessId)"
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "PID $($proc.ProcessId)가 ${quitTimeoutSeconds}초 안에 종료되지 않았습니다. 강제 종료하지 않습니다. 트레이 메뉴에서 종료한 뒤 다시 실행하세요. 파일은 바꾸지 않았습니다."
}

function Get-LogonTaskCommand {
    # 읽기 전용 조회. 등록하거나 바꾸지 않는다. 작업이 없거나 조회할 수 없으면 $null.
    try {
        $svc = New-Object -ComObject Schedule.Service
        $svc.Connect()
        $task = $svc.GetFolder('\').GetTask($logonTaskName)
        foreach ($action in $task.Definition.Actions) { if ($action.Type -eq 0) { return [string]$action.Path } }
        return ''
    } catch { return $null }
}

Write-Output "== $productName 설치/업데이트 =="
Write-Output "원본 : $SourceDir"
Write-Output "설치 : $installDir"

# 1. 원본 확인
if (Test-SamePath $SourceDir $installDir) { throw '원본 폴더와 설치 폴더가 같습니다.' }
$required = @($exeName, 'NotificationsProKR-Mnyang.dll', 'NotificationsProKR-Mnyang.runtimeconfig.json',
    'ko-KR\NotificationsProKR-Mnyang.resources.dll', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'NOTICE-fork.md')
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $SourceDir $_) -PathType Leaf) })
if ($missing.Count -gt 0) { throw "원본에 필요한 파일이 없습니다: $($missing -join ', '). 먼저 scripts\custom\publish.ps1을 실행하세요." }
$sourceFiles = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -File)
$forbidden = @($sourceFiles | Where-Object {
    $_.Name -ieq 'settings.json' -or $_.Name -like 'settings.json.*' -or $_.Name -ieq 'run-state.json' -or $_.Extension -ieq '.wav' })
if ($forbidden.Count -gt 0) { throw "원본에 사용자 데이터로 보이는 파일이 있어 중단합니다: $(($forbidden | ForEach-Object { $_.FullName }) -join ', ')" }

# 2. .NET 8 Desktop Runtime 확인(프레임워크 종속 빌드)
$runtimeRoots = @(Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App')
if ($env:DOTNET_ROOT) { $runtimeRoots += (Join-Path $env:DOTNET_ROOT 'shared\Microsoft.WindowsDesktop.App') }
$desktop8 = @($runtimeRoots | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
    Get-ChildItem -LiteralPath $_ -Directory | Where-Object { $_.Name -like '8.*' } })
if ($desktop8.Count -eq 0) {
    throw '.NET 8 Desktop Runtime(x64)을 찾지 못했습니다. https://dotnet.microsoft.com/download/dotnet/8.0 에서 Desktop Runtime을 설치한 뒤 다시 실행하세요. 아무것도 바꾸지 않았습니다.'
}
Write-Output ".NET 8 Desktop Runtime: $(($desktop8 | ForEach-Object { $_.Name }) -join ', ')"

# 3. 실행 중인 인스턴스: 설치 경로 또는 원본 경로의 exe만 대상으로 한다.
$currentSession = (Get-Process -Id $PID).SessionId
$running = @(Get-ForkProcesses)
$ours = @($running | Where-Object { (Test-SamePath $_.Path $installExe) -or (Test-SamePath $_.Path $sourceExe) })
$others = @($running | Where-Object { -not ((Test-SamePath $_.Path $installExe) -or (Test-SamePath $_.Path $sourceExe)) })
foreach ($o in $others) {
    Write-Warning "다른 위치의 포크 인스턴스가 실행 중입니다(PID $($o.ProcessId), 세션 $($o.SessionId), $($o.Path)). 건드리지 않습니다. 이 인스턴스가 떠 있는 동안 새로 설치한 앱을 실행하면 이 인스턴스의 설정창만 열립니다(단일 인스턴스)."
}
$oursOtherSession = @($ours | Where-Object { $_.SessionId -ne $currentSession })
if ($oursOtherSession.Count -gt 0) {
    throw "다른 Windows 세션에서 같은 경로의 인스턴스가 실행 중입니다(PID $(($oursOtherSession | ForEach-Object { $_.ProcessId }) -join ', ')). 그 세션에서 종료한 뒤 다시 실행하세요. 아무것도 바꾸지 않았습니다."
}
$quitDone = $false
foreach ($p in @($ours | Where-Object { $_.SessionId -eq $currentSession })) {
    if ($PSCmdlet.ShouldProcess("PID $($p.ProcessId) ($($p.Path))", '--quit으로 정상 종료 요청 후 최대 10초 대기')) {
        Request-Quit $p
        $quitDone = $true
    }
}
if ($ours.Count -eq 0) { Write-Output '실행 중인 설치본/원본 인스턴스: 없음' }

# 4. 파일 복사(덮어쓰기) 후 원본에 없는 이전 버전 파일 정리
$isUpdate = Test-Path -LiteralPath $installExe -PathType Leaf
if (-not (Test-Path -LiteralPath $installDir)) {
    if ($PSCmdlet.ShouldProcess($installDir, '설치 폴더 만들기')) { New-Item -ItemType Directory -Path $installDir -Force | Out-Null }
}
$copied = 0
foreach ($f in $sourceFiles) {
    $rel = $f.FullName.Substring($SourceDir.Length).TrimStart('\')
    $dst = Join-Path $installDir $rel
    if ($PSCmdlet.ShouldProcess($dst, "복사 ($($f.Length) bytes)")) {
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
        $copied++
    }
}
if (Test-Path -LiteralPath $installDir) {
    $sourceRel = @{}
    foreach ($f in $sourceFiles) { $sourceRel[$f.FullName.Substring($SourceDir.Length).TrimStart('\').ToLowerInvariant()] = $true }
    $stale = @(Get-ChildItem -LiteralPath $installDir -Recurse -File | Where-Object {
        -not $sourceRel.ContainsKey($_.FullName.Substring($installDir.Length).TrimStart('\').ToLowerInvariant()) })
    foreach ($s in $stale) {
        if ($PSCmdlet.ShouldProcess($s.FullName, '이전 버전에만 있던 파일 삭제')) { Remove-Item -LiteralPath $s.FullName -Force }
    }
}

# 5. 시작 메뉴 바로가기(1개)
if ($PSCmdlet.ShouldProcess($shortcutPath, "시작 메뉴 바로가기 만들기/갱신 -> $installExe")) {
    $shell = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $shortcutPath) {
        $old = $shell.CreateShortcut($shortcutPath).TargetPath
        if (-not (Test-SamePath $old $installExe)) { Write-Output "  기존 바로가기 대상: $old -> 설치본으로 바꿉니다." }
    }
    $lnk = $shell.CreateShortcut($shortcutPath)
    $lnk.TargetPath = $installExe
    $lnk.Arguments = ''
    $lnk.WorkingDirectory = $installDir
    $lnk.IconLocation = "$installExe,0"
    $lnk.Description = 'Windows 알림을 오버레이와 WAV로 알려 주는 트레이 앱(Notifications Pro 수정본)'
    $lnk.Save()
}

# 6. 로그온 작업은 등록하지 않는다. 다른 exe를 가리키면 안내만 한다.
$taskCommand = Get-LogonTaskCommand
if ($null -eq $taskCommand) {
    Write-Output "로그온 작업($logonTaskName): 미등록(또는 조회 불가). 자동 실행은 앱의 시스템 탭에서 켜세요."
} elseif (Test-SamePath $taskCommand $installExe) {
    Write-Output "로그온 작업($logonTaskName): 설치본을 가리킴(변경 없음)."
} else {
    Write-Warning "로그온 작업($logonTaskName)이 다른 실행 파일을 가리킵니다: $taskCommand. 설치본을 실행한 뒤 시스템 탭에서 'Windows 로그인 시 자동 실행'을 껐다가 다시 켜면 설치본으로 다시 등록됩니다. 이 스크립트는 작업을 바꾸지 않습니다."
}

Write-Output ''
if ($WhatIfPreference) {
    Write-Output 'WhatIf: 실제로 바뀐 것은 없습니다.'
} else {
    $verb = if ($isUpdate) { '업데이트' } else { '설치' }
    Write-Output "$verb 완료: 파일 ${copied}개 복사. 앱은 시작하지 않았습니다."
    if ($quitDone) { Write-Output '실행 중이던 인스턴스를 종료했습니다. 자동으로 다시 시작하지 않습니다.' }
    Write-Output "시작: 시작 메뉴 '$productName' 또는 $installExe"
    Write-Output '설정·소리·프로필(%AppData%\NotificationsProKR-Mnyang)은 그대로 유지됩니다.'
}
