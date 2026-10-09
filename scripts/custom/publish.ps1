# @file publish.ps1
# @author: rudals252
# @brief Notifications Pro KR-Mnyang 배포 산출물을 만든다. 프레임워크 종속(.NET 8 Desktop Runtime 필요) win-x64
#        언패키지 publish를 dist\NotificationsProKR-Mnyang\에 만들고 GPL 고지 파일과 settings.example.json을 복사한 뒤
#        파일 목록·exe 버전·ko-KR 위성 어셈블리·사용자 데이터 미포함을 확인한다. 앱을 실행하지 않는다.
[CmdletBinding()]
param(
    # 출력 폴더. 기본값은 <저장소>\dist\NotificationsProKR-Mnyang
    [string]$OutputDir,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'src\NotificationsPro\NotificationsPro.csproj'
$exeName = 'NotificationsProKR-Mnyang.exe'
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'dist\NotificationsProKR-Mnyang' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "프로젝트 파일이 없습니다: $project" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK를 찾지 못했습니다.' }

# 이전 산출물 정리: 저장소 dist\ 아래 폴더만 지운다(다른 위치를 지정했으면 지우지 않고 중단).
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist')) + [IO.Path]::DirectorySeparatorChar
if (Test-Path -LiteralPath $OutputDir) {
    if (-not $OutputDir.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "출력 폴더가 이미 있고 저장소 dist\ 밖이라 지우지 않습니다: $OutputDir"
    }
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

Write-Output "publish: $Configuration / win-x64 / framework-dependent -> $OutputDir"
& dotnet publish $project -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=false -o $OutputDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패(종료 코드 $LASTEXITCODE)" }

# GPL 고지·예시 설정 복사. THIRD_PARTY_NOTICES.md가 licenses\ 폴더를 가리키므로 함께 복사한다.
$copies = @(
    @{ From = 'LICENSE'; To = 'LICENSE' },
    @{ From = 'THIRD_PARTY_NOTICES.md'; To = 'THIRD_PARTY_NOTICES.md' },
    @{ From = 'licenses\OpenDyslexic-OFL-1.1.txt'; To = 'licenses\OpenDyslexic-OFL-1.1.txt' },
    @{ From = 'docs\custom\NOTICE-fork.md'; To = 'NOTICE-fork.md' },
    @{ From = 'docs\custom\CHANGELOG-fork.md'; To = 'CHANGELOG-fork.md' },
    @{ From = 'settings.example.json'; To = 'settings.example.json' }
)
foreach ($c in $copies) {
    $src = Join-Path $repoRoot $c.From
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "고지/예시 파일이 없습니다: $src" }
    $dst = Join-Path $OutputDir $c.To
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir | Out-Null }
    Copy-Item -LiteralPath $src -Destination $dst -Force
}

# 확인 1: 필수 파일
$required = @(
    $exeName,
    'NotificationsProKR-Mnyang.dll',
    'NotificationsProKR-Mnyang.runtimeconfig.json',
    'ko-KR\NotificationsProKR-Mnyang.resources.dll',
    'LICENSE', 'THIRD_PARTY_NOTICES.md', 'NOTICE-fork.md', 'CHANGELOG-fork.md', 'settings.example.json'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $OutputDir $_) -PathType Leaf) })
if ($missing.Count -gt 0) { throw "산출물에 필수 파일이 없습니다: $($missing -join ', ')" }

# 확인 2: 사용자 데이터·설정·WAV가 섞이지 않았는지(설정 원본 settings.json, 실행 표식, 사용자 소리)
$forbidden = @(Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Where-Object {
    $_.Name -ieq 'settings.json' -or $_.Name -ieq 'run-state.json' -or $_.Extension -ieq '.wav' -or
    $_.Name -like 'settings.json.*'
})
if ($forbidden.Count -gt 0) { throw "산출물에 포함하면 안 되는 파일이 있습니다: $($forbidden.FullName -join ', ')" }

# 확인 3: 프레임워크 종속 여부(runtimeconfig의 프레임워크)
$rc = Get-Content -LiteralPath (Join-Path $OutputDir 'NotificationsProKR-Mnyang.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = @()
if ($rc.runtimeOptions.frameworks) { $frameworks = @($rc.runtimeOptions.frameworks | ForEach-Object { "$($_.name) $($_.version)" }) }
elseif ($rc.runtimeOptions.framework) { $frameworks = @("$($rc.runtimeOptions.framework.name) $($rc.runtimeOptions.framework.version)") }

$exe = Get-Item -LiteralPath (Join-Path $OutputDir $exeName)
$files = @(Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Sort-Object FullName)
$total = ($files | Measure-Object Length -Sum).Sum

Write-Output ''
Write-Output '== 산출물 파일 =='
foreach ($f in $files) {
    $rel = $f.FullName.Substring($OutputDir.Length).TrimStart('\')
    Write-Output ('{0,12:N0}  {1}' -f $f.Length, $rel)
}
Write-Output ('파일 {0}개, 합계 {1:N0} bytes ({2:N2} MB)' -f $files.Count, $total, ($total / 1MB))
Write-Output ''
Write-Output '== exe 버전 정보 =='
Write-Output "FileVersion    : $($exe.VersionInfo.FileVersion)"
Write-Output "ProductVersion : $($exe.VersionInfo.ProductVersion)"
Write-Output "ProductName    : $($exe.VersionInfo.ProductName)"
Write-Output "FileDescription: $($exe.VersionInfo.FileDescription)"
Write-Output "프레임워크      : $($frameworks -join '; ')"
Write-Output "ko-KR 위성 어셈블리: 있음 (ko-KR\NotificationsProKR-Mnyang.resources.dll)"
Write-Output '사용자 데이터 검사: settings.json / run-state.json / *.wav 없음'
Write-Output ''
Write-Output "완료. 앱은 실행하지 않았습니다. 설치: scripts\custom\install.ps1 (기본 원본: $OutputDir)"
