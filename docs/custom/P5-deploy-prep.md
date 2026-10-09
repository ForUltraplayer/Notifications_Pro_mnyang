<!-- @file P5-deploy-prep.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang P5 준비(배포 산출물·설치/제거 스크립트·전환 가이드) 보고서: publish 결과, 스크립트 설계와 WhatIf 출력 원문, OBS 조회, 실행 검증 미실시 목록과 이후 실측 순서, 검토자 결정 요청. -->

# P5 준비 — 배포 산출물·설치/제거 스크립트·전환 가이드 보고서

- 기준: HEAD `3ef475f`(P4), 브랜치 `custom/base`. **미커밋.**
- 구현: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: rudals252
- 설계 근거: `ai-tts/docs/notifications-pro-integrated-design.md` v1.1 §2.2·§2.3·§9.3·§11.2·§11.3·§13 P5·§14.2·§15. P4 보고서 §9·§10·§11, P1-B §2.3, P0 §4
- 제약: 사용자 포크 인스턴스 PID 7716(`bin\Debug`, 16:16:46 시작) 실행 중 → **종료하지 않음, 앱·설치/제거 스크립트 실제 실행 없음.** 시작 메뉴·스케줄러·레지스트리 변경 없음. ai-tts 폴더는 읽기만 함. 사용자 WAV 복사 없음.

## 1. 결론

1. **publish 성공.** `dist\NotificationsProKR-Mnyang\`에 파일 14개, 27,236,494 bytes(25.97 MB). 프레임워크 종속(`Microsoft.NETCore.App 8.0.0`, `Microsoft.WindowsDesktop.App 8.0.0`) win-x64. exe `FileVersion 1.1.10.37`, `ProductVersion 1.1.10.37+3ef475f7…`. **ko-KR 위성 어셈블리 `ko-KR\NotificationsProKR-Mnyang.resources.dll`(192,000 bytes) 있음.** GPL 고지와 `settings.example.json`이 들어 있고 `settings.json`·`run-state.json`·`*.wav`는 없다. 빌드 경고 0. 앱은 실행하지 않았다.
2. **설치·제거 스크립트를 작성했다.** `install.ps1`·`uninstall.ps1`은 PowerShell 7.6.6과 Windows PowerShell 5.1 모두 구문 오류 0이다. `-WhatIf`만 실행했고 부작용이 없음을 확인했다(설치 폴더·바로가기 없음 유지, 포크 `settings.json` SHA-256 `0C9198D4…CD561A` 불변, PID 7716 그대로).
3. **전환 가이드(`MIGRATION-ko.md`)·포크 README·GPL 수정 고지(`NOTICE-fork.md`)를 작성했다.** PS1 중지 명령은 `windows-notify.ps1`·`windows-notify-autostart.ps1`을 읽어 그대로 적었고 실행하지 않았다. P4 §11 체크리스트를 통합했다.
4. **회귀: `dotnet test` 607/607 통과**(출력 경로 우회, 경고 0). OBS Studio 32.1.2가 설치돼 있으나 실행하지 않아 **OBS 회귀는 미검증**이다.
5. **실제 실행 검증은 전부 미실시**다(§8). 조회 중 두 가지를 새로 확인했다. ① PID 7716은 **P4 이전 빌드**(dll 16:15:55, `--quit`·CLI 파이프·`run-state.json` 문자열 없음)라 `--quit`으로 끌 수 없다. 트레이 메뉴로 종료해야 한다. ② **기존 PS1 감시기(PID 8252, 08:34 시작)가 포크와 동시에 실행 중**이다. 포크 소리 규칙이 켜져 있으면 지금도 이중 재생이 날 수 있다(건드리지 않음).

## 2. 변경 파일 (모두 미커밋)

| 구분 | 파일 |
|---|---|
| 신규 스크립트 | `scripts/custom/publish.ps1`, `scripts/custom/install.ps1`, `scripts/custom/uninstall.ps1` (UTF-8 BOM: Windows PowerShell 5.1에서 한국어 출력용) |
| 신규 문서 | `docs/custom/NOTICE-fork.md`, `docs/custom/MIGRATION-ko.md`, `docs/custom/README-fork.md`, 이 보고서 |
| 수정 | `.gitignore`(`dist/` 1줄 추가), `docs/custom/CHANGELOG-fork.md`(P5 준비 항목) |
| 미변경 | `src/**`, `tests/**`, upstream `README.md`·`CHANGELOG.md`, `csproj`(버전 접미사는 제안만, §3.4) |
| 생성(무시됨) | `dist/NotificationsProKR-Mnyang/**`, `src/NotificationsPro/bin/Release/**`, `obj/Release/**` |

## 3. T1 — publish (`scripts/custom/publish.ps1`)

### 3.1 동작

1. `dotnet publish src/NotificationsPro/NotificationsPro.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o dist\NotificationsProKR-Mnyang`
2. 이전 산출물은 저장소 `dist\` 아래일 때만 지운다(밖이면 중단).
3. 복사: `LICENSE`, `THIRD_PARTY_NOTICES.md`, `licenses\OpenDyslexic-OFL-1.1.txt`, `docs\custom\NOTICE-fork.md` → `NOTICE-fork.md`, `docs\custom\CHANGELOG-fork.md` → `CHANGELOG-fork.md`, `settings.example.json`.
   - 지시 외로 `licenses\`와 `CHANGELOG-fork.md`를 추가했다. `THIRD_PARTY_NOTICES.md`가 `licenses/OpenDyslexic-OFL-1.1.txt`를 가리키고, `NOTICE-fork.md`가 수정 내용을 `CHANGELOG-fork.md`로 안내하기 때문이다(GPL 제5조 a의 수정 고지). 결정 요청 §9-2.
4. 검사: 필수 파일 9개, 금지 파일(`settings.json`, `settings.json.*`, `run-state.json`, `*.wav`) 없음, runtimeconfig의 프레임워크, 파일 목록·크기, exe 버전 정보.

Debug 잠금과는 겹치지 않았다. 출력은 `bin\Release\…\win-x64`와 `dist\`이고 `-p:OutDir` 우회는 필요 없었다.

### 3.2 실행 결과 원문

```text
publish: Release / win-x64 / framework-dependent -> F:\dev\notifications-pro-custom\dist\NotificationsProKR-Mnyang
  복원할 프로젝트를 확인하는 중...
  F:\dev\notifications-pro-custom\src\NotificationsPro\NotificationsPro.csproj을(를) 163밀리초 동안 복원했습니다.
  NotificationsPro -> F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Release\net8.0-windows10.0.19041.0\win-x64\NotificationsProKR-Mnyang.dll
  NotificationsPro -> F:\dev\notifications-pro-custom\dist\NotificationsProKR-Mnyang\

== 산출물 파일 ==
      17,050  CHANGELOG-fork.md
     192,000  ko-KR\NotificationsProKR-Mnyang.resources.dll
      36,639  LICENSE
       4,434  licenses\OpenDyslexic-OFL-1.1.txt
  24,877,600  Microsoft.Windows.SDK.NET.dll
       3,346  NOTICE-fork.md
       1,330  NotificationsProKR-Mnyang.deps.json
   1,155,072  NotificationsProKR-Mnyang.dll
     152,064  NotificationsProKR-Mnyang.exe
     260,252  NotificationsProKR-Mnyang.pdb
         429  NotificationsProKR-Mnyang.runtimeconfig.json
       6,899  settings.example.json
         435  THIRD_PARTY_NOTICES.md
     528,944  WinRT.Runtime.dll
파일 14개, 합계 27,236,494 bytes (25.97 MB)

== exe 버전 정보 ==
FileVersion    : 1.1.10.37
ProductVersion : 1.1.10.37+3ef475f72c2e001bc122e286fc3b613a117ba374
ProductName    : Notifications Pro KR-Mnyang
FileDescription: Notifications Pro KR-Mnyang
프레임워크      : Microsoft.NETCore.App 8.0.0; Microsoft.WindowsDesktop.App 8.0.0
ko-KR 위성 어셈블리: 있음 (ko-KR\NotificationsProKR-Mnyang.resources.dll)
사용자 데이터 검사: settings.json / run-state.json / *.wav 없음

완료. 앱은 실행하지 않았습니다. 설치: scripts\custom\install.ps1 (기본 원본: F:\dev\notifications-pro-custom\dist\NotificationsProKR-Mnyang)
```

- `CHANGELOG-fork.md` 크기 17,050은 이번 P5 항목을 추가하기 **전** 파일이다. 다음 publish 때 갱신된다.
- `ProductVersion`의 `+3ef475f…`는 SDK가 붙인 HEAD 커밋이다. 작업 트리의 미커밋 변경은 스크립트·문서뿐이고 `src/`는 HEAD와 같다.
- OpenDyslexic 글꼴은 어셈블리에 리소스로 들어 있어 별도 파일이 없다(csproj `<Resource>`).

### 3.3 버전 문자열 (제안만, 이번에 변경하지 않음)

| 안 | csproj | 결과 | 비고 |
|---|---|---|---|
| A(권장) | `<Version>1.1.10.37-mnyang.1</Version>` | `AssemblyVersion`·`FileVersion` = `1.1.10.37`, `ProductVersion` = `1.1.10.37-mnyang.1+<커밋>` | 원본 기준 버전이 그대로 보이고 포크 판별 가능. 단 About 대화상자와 `run-state.json`은 `AssemblyVersion`(`GetName().Version`)을 쓰므로 접미사가 보이지 않는다. 보이게 하려면 `GetCurrentVersionString` 수정 필요(별도 승인) |
| B | `<InformationalVersion>1.1.10.37-mnyang.1</InformationalVersion>`만 추가 | 위와 같되 `Version` 불변 | upstream csproj 충돌 범위가 더 작음 |
| C | 네 번째 자리 사용(예: `1.1.10.1037`) | 모든 버전 필드에 반영 | upstream 버전과 혼동, 비권장 |

## 4. T2 — 설치·제거 스크립트

### 4.1 `install.ps1` (`[CmdletBinding(SupportsShouldProcess)]`, `-SourceDir`)

1. 원본 확인: 설치 폴더와 같으면 중단. 필수 파일 7개 확인. 사용자 데이터로 보이는 파일(`settings.json*`, `run-state.json`, `*.wav`)이 있으면 중단.
2. .NET 8 Desktop Runtime 확인: `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.*`(와 `DOTNET_ROOT`). 없으면 다운로드 안내 후 중단.
3. 실행 중 인스턴스(경로 기준, 설계 §15): `Get-Process NotificationsProKR-Mnyang`의 `MainModule.FileName`을 비교한다.
   - **설치 exe 또는 `-SourceDir` exe와 같은 경로**만 대상이다. 다른 세션이면 중단한다. 현재 세션이면 그 exe로 `--quit`을 보내고(전달 프로세스 종료 코드 0 확인) 해당 PID가 10초 안에 끝나는지 250ms 간격으로 확인한다. 안 끝나거나 전달이 실패하면 **강제 종료 없이 중단**한다(파일 미변경).
   - **다른 경로의 포크**(예: 개발 빌드)는 건드리지 않고 경고만 낸다. `--quit`은 세션 전체 파이프로 주 인스턴스에 가므로, 경로가 다른 인스턴스가 있을 때 보내면 그 인스턴스를 끄게 된다. 그래서 보내지 않는다.
4. 복사: 원본 파일을 덮어쓴다. 설치 폴더에만 있는 이전 버전 파일은 지운다(결정 요청 §9-3).
5. 시작 메뉴 `%AppData%\Microsoft\Windows\Start Menu\Programs\Notifications Pro KR-Mnyang.lnk`(대상 exe, 작업 폴더 설치 폴더, 인자 없음). 기존 바로가기가 다른 곳을 가리키면 그 경로를 출력하고 바꾼다.
6. 로그온 작업은 등록하지 않는다. Task Scheduler COM으로 **읽기만** 해서 다른 exe를 가리키면 "시스템 탭에서 껐다 켜기" 안내를 출력한다.
7. 앱을 시작하지 않는다. 종료시켰으면 "자동으로 다시 시작하지 않습니다"를 출력한다.

### 4.2 `uninstall.ps1` (`-RemoveData`, `-Force`)

1. 설치 exe 경로의 인스턴스에만 `--quit` → 최대 10초 → 안 끝나면 중단(강제 종료 없음). 다른 경로는 경고만.
2. 로그온 작업: COM으로 존재·대상 exe를 조회한다. 없음(`0x80070002`, 이 PC에서 `FileNotFoundException`으로 확인)이면 넘어간다. 있으면 `schtasks /Delete /TN NotificationsProKR-Mnyang-Logon /F`. 조회·해제가 실패하면 **프로그램 파일을 지우지 않고 중단**한다(설계 §9.3 "EXE 삭제 전 해제").
3. 프로그램 폴더는 안에 `NotificationsProKR-Mnyang.exe`가 있을 때만 지운다. 바로가기를 지운다.
4. `%AppData%\NotificationsProKR-Mnyang`: 기본 보존. `-RemoveData`이면 항목별 목록(폴더는 파일 수·크기)을 보여 준 뒤 `ShouldProcess`와 `ShouldContinue` 확인을 거쳐 지운다. `-Force`는 추가 확인만 생략한다(지시 외 추가, 결정 요청 §9-4). `sounds\`는 가져온 사본이라는 점과 원본 앱 데이터가 대상이 아니라는 점을 출력한다.
5. Windows 알림 소리 토글·Discord 소리·PS1 복귀는 안내만 출력한다.

### 4.3 호환성 처리

- Windows PowerShell 5.1에서 `-WhatIf`가 `ForEach-Object <멤버명>`과 CIM 모듈 자동 로드(별칭 설정)에도 전파됐다. 각각 출력 누락과 "Set Alias" 소음이 생겼다. 그래서 스크립트 블록 형태로 바꾸고 프로세스 조회를 `Get-CimInstance`에서 `Get-Process`로 바꿨다.
- 5.1에서 네이티브 명령의 stderr 리디렉션이 `ErrorActionPreference=Stop`일 때 종료 오류가 되는 문제를 피하려고 `schtasks` 호출 구간만 `Continue`로 둔다.

### 4.4 구문 검사 (`[System.Management.Automation.Language.Parser]::ParseFile`)

```text
publish.ps1 pwsh parse errors=0
install.ps1 pwsh parse errors=0
uninstall.ps1 pwsh parse errors=0
publish.ps1 PS5.1 parse errors=0
install.ps1 PS5.1 parse errors=0
uninstall.ps1 PS5.1 parse errors=0
```

(pwsh = 7.6.6, PS5.1 = 5.1.26100.9444)

### 4.5 WhatIf 출력 원문

`install.ps1 -WhatIf` (Windows PowerShell 5.1):

```text
== Notifications Pro KR-Mnyang 설치/업데이트 ==
원본 : F:\dev\notifications-pro-custom\dist\NotificationsProKR-Mnyang
설치 : C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang
.NET 8 Desktop Runtime: 8.0.31
WARNING: 다른 위치의 포크 인스턴스가 실행 중입니다(PID 7716, 세션 1, 
F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe
). 건드리지 않습니다. 이 인스턴스가 떠 있는 동안 새로 설치한 앱을 실행하면 이 인스턴스의 설정창만 열립니다(단일 인스턴스).
실행 중인 설치본/원본 인스턴스: 없음
What if: Performing the operation "설치 폴더 만들기" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang".
What if: Performing the operation "복사 (17050 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\CHANGELOG-fork.md".
What if: Performing the operation "복사 (36639 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\LICENSE".
What if: Performing the operation "복사 (24877600 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\Microsoft.Windows.SDK.NET.dll".
What if: Performing the operation "복사 (3346 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NOTICE-fork.md".
What if: Performing the operation "복사 (1330 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.deps.json".
What if: Performing the operation "복사 (1155072 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.dll".
What if: Performing the operation "복사 (152064 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.exe".
What if: Performing the operation "복사 (260252 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.pdb".
What if: Performing the operation "복사 (429 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.runtimeconfig.json".
What if: Performing the operation "복사 (6899 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\settings.example.json".
What if: Performing the operation "복사 (435 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\THIRD_PARTY_NOTICES.md".
What if: Performing the operation "복사 (528944 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\WinRT.Runtime.dll".
What if: Performing the operation "복사 (192000 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\ko-KR\NotificationsProKR-Mnyang.resources.dll".
What if: Performing the operation "복사 (4434 bytes)" on target "C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\licenses\OpenDyslexic-OFL-1.1.txt".
What if: Performing the operation "시작 메뉴 바로가기 만들기/갱신 -> C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.exe" on target "C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Notifications Pro KR-Mnyang.lnk".
로그온 작업(NotificationsProKR-Mnyang-Logon): 미등록(또는 조회 불가). 자동 실행은 앱의 시스템 탭에서 켜세요.

WhatIf: 실제로 바뀐 것은 없습니다.
```

PowerShell 7.6.6에서도 같은 내용이다(What if 줄은 한국어 형식 `대상 "…"에 "…" 작업을 수행하는 중입니다.`).

`uninstall.ps1 -WhatIf` (PowerShell 7.6.6):

```text
== Notifications Pro KR-Mnyang 제거 ==
프로그램 : C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang
데이터   : C:\Users\Administrator\AppData\Roaming\NotificationsProKR-Mnyang (보존)
다른 위치의 포크 인스턴스가 실행 중입니다(PID 7716, F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe). 건드리지 않습니다.
실행 중인 설치본: 없음
로그온 작업(NotificationsProKR-Mnyang-Logon): 없음
프로그램 폴더: 없음
시작 메뉴 바로가기: 없음
데이터 보존: C:\Users\Administrator\AppData\Roaming\NotificationsProKR-Mnyang (삭제하려면 -RemoveData)

== 직접 되돌려야 하는 설정 ==
- Windows 알림 소리: 설정 > 시스템 > 알림 > 앱별로 "알림이 도착하면 소리 재생"을 다시 켜세요(ChatGPT/Codex, Claude, Discord 등 끈 앱마다).
- Discord: 사용자 설정 > 알림 > 소리에서 끈 항목(예: 메시지)을 다시 켜세요.
- 기존 PS1 감시기로 돌아가려면 docs\custom\MIGRATION-ko.md의 "되돌리기"를 따르세요.

WhatIf: 실제로 바뀐 것은 없습니다.
```

`uninstall.ps1 -WhatIf -RemoveData` (Windows PowerShell 5.1):

```text
== Notifications Pro KR-Mnyang 제거 ==
프로그램 : C:\Users\Administrator\AppData\Local\Programs\NotificationsProKR-Mnyang
데이터   : C:\Users\Administrator\AppData\Roaming\NotificationsProKR-Mnyang (삭제 대상(-RemoveData))
WARNING: 다른 위치의 포크 인스턴스가 실행 중입니다(PID 7716, 
F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe
). 건드리지 않습니다.
실행 중인 설치본: 없음
로그온 작업(NotificationsProKR-Mnyang-Logon): 없음
프로그램 폴더: 없음
시작 메뉴 바로가기: 없음

== 삭제할 데이터 목록: C:\Users\Administrator\AppData\Roaming\NotificationsProKR-Mnyang ==
  [파일] settings.json  (6,748 bytes)
  주의: sounds\의 WAV는 가져올 때 만든 사본입니다. 원래 WAV 파일과 외부 폴더로 추가한 WAV는 지우지 않습니다.
  원본 Notifications Pro 데이터(%AppData%\NotificationsPro, MSIX LocalCache)는 대상이 아닙니다.
What if: Performing the operation "데이터 폴더 삭제(되돌릴 수 없음)" on target "C:\Users\Administrator\AppData\Roaming\NotificationsProKR-Mnyang".

== 직접 되돌려야 하는 설정 ==
- Windows 알림 소리: 설정 > 시스템 > 알림 > 앱별로 "알림이 도착하면 소리 재생"을 다시 켜세요(ChatGPT/Codex, Claude, Discord 등 끈 앱마다).
- Discord: 사용자 설정 > 알림 > 소리에서 끈 항목(예: 메시지)을 다시 켜세요.
- 기존 PS1 감시기로 돌아가려면 docs\custom\MIGRATION-ko.md의 "되돌리기"를 따르세요.

WhatIf: 실제로 바뀐 것은 없습니다.
```

WhatIf 뒤 확인:

```text
Test-Path %LocalAppData%\Programs\NotificationsProKR-Mnyang            -> False
Test-Path <Start Menu>\Programs\Notifications Pro KR-Mnyang.lnk        -> False
Get-FileHash %AppData%\NotificationsProKR-Mnyang\settings.json         -> 0C9198D409253C24B6551D858F6889A0A5F195D30BD6411509BAFFA378CD561A (P4 기록과 동일)
Get-Process -Id 7716                                                   -> 7716, 2026-10-09 오후 4:16:46 (그대로)
```

`--quit` 전달·대기·중단 경로, 실제 복사·덮어쓰기·이전 파일 삭제, 바로가기 생성, 작업 해제, `-RemoveData` 확인 질문은 **WhatIf에서 실행되지 않으므로 미검증**이다(§8).

## 5. T3 — 전환 가이드 (`docs/custom/MIGRATION-ko.md`)

구성: 사전 조건(런타임 확인 명령 2종, 개발 인스턴스 종료, 데이터 폴더 공유) → 빌드·설치 → 첫 실행(언어, 알림 접근, 툴팁 `WinRT(1초 조회) 방식으로 수신 중`) → 원본 설정 가져오기(프로필 탭 `원본 앱에서 가져오기`, MSIX LocalCache가 기본 후보인 이유, 백업·이 PC 전용 값 유지·`화면 수용 기준`/간격 0) → WAV 보관함(`파일 가져오기...`로 `ai-tts\notification-sounds\mornye-complete.wav` 복사, `폴더 추가...`) → PS1과 같은 범위 설정 예(기본 명시적 무음 + ChatGPT·Claude 전용 목록 + 간격 4초) → ChatGPT/Codex 동일 AUMID → Windows 앱별 소리 끄기 → Discord·카카오톡 → 로그온 자동 실행(설치본에서 켜기, 다른 exe 경고)·트레이로 시작(이 PC `false`) → PS1 상태/중지/자동 실행 해제 명령 원문과 동작 설명 → 이중 재생 확인(프로세스·시작프로그램·작업 조회 명령, 실제 알림, 재생 상태 `시작` 카운터) → 원본 앱 공존/종료 → 업데이트·제거·되돌리기(PS1 `-Action Start`, 자동 실행 `-Action Enable`) → 알려진 제한 → 체크리스트(P5 설치·전환 + P4 §11 전부).

문구 근거: UI 문구는 `Strings.ko-KR.resx`의 실제 값(`System_LogonTask`, `System_StartMinimizedToTray`, `Profiles_ImportFromOriginalApp`, `Sounds_ImportFiles`, `Sounds_AddFolder`, `Sounds_SilentPurposeExplicitSilence`, `Apps_ChoiceOwnList`, `ManualMute_*`, `Tray_TooltipListening`+`Listener_ModeWinRtPolling` 등)에서 가져왔다. PS1 명령·출력 문구는 `windows-notify.ps1`(46줄)·`windows-notify-autostart.ps1`(69줄)에서 그대로 옮겼다. 한국어 Windows 토글 문구와 Discord 메뉴는 기존 보고서처럼 **(미검증)**으로 표시했다.

## 6. T4 — OBS 조회와 회귀

### 6.1 OBS (조회만, 실행 안 함)

| 조회 | 결과 |
|---|---|
| `HKLM\…\Uninstall\*` DisplayName `OBS Studio` | 있음, DisplayVersion `32.1.2`(InstallLocation 비어 있음) |
| `HKLM:\SOFTWARE\OBS Studio` | 있음 |
| `C:\Program Files\obs-studio` | 있음 |
| `%AppData%\obs-studio` | 있음(사용자 설정 존재) |
| `obs64`/`obs32` 프로세스 | 없음 |

→ **설치됨, 포크로 시험하지 않음 = OBS 회귀 미검증.** 설계 §14.2 OBS 행(크로마키·고정 캡처 유지)은 사용자 수동 확인 대상으로 가이드 §15.2에 넣었다.

### 6.2 dotnet test

```text
dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj "-p:OutDir=<scratchpad>\p5test\" --nologo
  NotificationsPro -> <scratchpad>\p5test\NotificationsProKR-Mnyang.dll
  NotificationsPro.Tests -> <scratchpad>\p5test\NotificationsPro.Tests.dll
통과!  - 실패:     0, 통과:   607, 건너뜀:     0, 전체:   607, 기간: 9 s - NotificationsPro.Tests.dll (net8.0)
```

경고 0. 기준 607과 같다(소스 변경 없음).

## 7. 조회한 현재 상태 (모두 읽기 전용)

| 항목 | 결과 |
|---|---|
| 포크 | PID 7716 `F:\…\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe`, 16:16:46 시작. 로드된 dll 16:15:55 빌드(P3-B 커밋 16:49 이전 작업 트리), `--quit`·`NotificationsProKR-Mnyang.Cli.`·`run-state.json` 문자열 없음 → **P4 이전 빌드** |
| PS1 감시기 | **PID 8252 실행 중**(powershell, 명령줄에 `watch_windows_notifications`, 08:34:32 시작). 시작프로그램 `Mornye Windows Notifications.lnk` 있음 |
| 원본 앱 | MSIX `NotificationsPro_1.1.10.37_neutral__h8abfdfjdvdc6` 설치, 프로세스 없음. `…\Packages\NotificationsPro_h8abfdfjdvdc6\LocalCache\Roaming\NotificationsPro\settings.json`(6,356 bytes, 16:16:54) 있음. `%AppData%\NotificationsPro` 없음 |
| 포크 데이터 | `%AppData%\NotificationsProKR-Mnyang\settings.json`만 있음(6,748 bytes, 해시 불변) |
| 런타임 | `Microsoft.WindowsDesktop.App 8.0.31`(그 외 6.0.36·9.0.20·10.0.12) |
| 설치 위치·작업 | `%LocalAppData%\Programs\NotificationsProKR-Mnyang` 없음, `NotificationsProKR-Mnyang-Logon` 없음 |
| PS1 설정 | `windows-notify.json`: 앱 ChatGPT/Codex/Claude, AUMID 2개, `notification-sounds/mornye-complete.wav`, 쿨다운 4초 |

## 8. 실행 검증 미실시 항목 (사유: 사용자 인스턴스 PID 7716 실행 중, 종료·스크립트 실행 금지)

### 8.1 P5 미실시

1. `install.ps1` 실제 실행: 폴더 생성·복사·바로가기 생성, 재실행 시 덮어쓰기와 이전 파일 삭제
2. 실행 중 설치본에 대한 `--quit` 전달(종료 코드 0)·10초 안 종료 확인·종료 후 덮어쓰기, 안 끝날 때 중단 경로, 이전 빌드라 전달이 실패할 때 중단 경로
3. 설치본 첫 실행: 런타임 로드, 한국어 위성 어셈블리 적용, 알림 접근·1초 조회 수신, 오버레이
4. 설치본 경로에서 로그온 작업 등록·재로그인·재부팅(P4 §11.1을 설치본으로)
5. `install.ps1`의 "로그온 작업이 다른 exe를 가리킴" 경고 경로
6. `uninstall.ps1` 실제 실행: `--quit`, 작업 해제(`schtasks /Delete`), 폴더·바로가기 삭제, 데이터 보존, `-RemoveData` 목록·확인·삭제, 작업 해제 실패 시 중단
7. 원본 설정 가져오기 UI 실제 실행(원본 MSIX 경로, 원본 파일 불변)
8. WAV 가져오기와 규칙 설정 후 실제 알림 재생, PS1 중지 후 이중 재생 없음(§14.2 "원본과 공존·전환")
9. 원본 앱 종료/공존 시 동작, `알림 팝업 숨기기` 상호 영향
10. OBS 크로마키·고정 캡처(§14.2 OBS)
11. 외부 WAV 폴더 분리·재연결, 출력 장치 변경(§14.2 드라이브·장치)
12. 앱 제거 후 원본 설정 보존(§14.2 앱 제거)
13. 다른 PC·UAC 켜진 환경(전부 미검증, 시험 범위는 이 PC뿐)
14. SmartScreen 등 서명 없는 exe 경고 여부

### 8.2 P4에서 이월된 미실시 (P4 보고서 §10)

- 실제 COM 작업 등록·XML 내보내기·삭제, 로그온 실행
- 강제 종료 후 재실행 안내, `--quit` 후 오탐 없음, 로그오프·Windows 종료 시 표식 정리(`SessionEnding`)
- 두 프로세스 사이 CLI 전달(`--pause` 등)
- 앱 탭 버튼 조작, `ms-settings:notifications` 열기, 앱별 실제 원음 무음, 한국어 Windows·Discord 메뉴 문구
- 실제 창 렌더링·잘림·배율·IME, 트레이로 시작 실제 동작

### 8.3 인스턴스 종료 후 진행 순서 (제안)

0. **사용자:** 트레이 메뉴 > 종료로 PID 7716을 끈다(P4 이전 빌드라 `--quit` 불가). PS1 감시기(PID 8252)를 계속 둘지 정한다. 두면 소리 시험 때 이중 재생이 섞인다.
1. 확인: `Get-Process NotificationsProKR-Mnyang` 없음. 포크 `settings.json` 해시를 기록한다.
2. `publish.ps1` 재실행(이 보고서와 CHANGELOG가 반영된 산출물) → `install.ps1 -WhatIf` → `install.ps1`. 폴더·파일 14개·바로가기, 앱 미시작, 데이터 해시 불변을 확인한다(§8.1-1).
3. 설치본 실행 → 툴팁·언어·소리 탭 진단 줄을 확인한다(§8.1-3).
4. 설치본 실행 중 `install.ps1` 재실행 → `--quit` 종료 코드 0, 10초 안 종료, `run-state.json` 삭제 확인 → 재실행 시 "기록 없음"(§8.1-2, P4 `--quit` 오탐 없음 겸함).
5. P4 T5를 설치본으로 진행: 경로 확인 후 `Stop-Process` → 재실행 → `PreviousRunStatusText` 미완료 안내 → `--quit` → 재실행 시 정상 → 두 번째 실행 `--pause` → 툴팁 일시 정지 접미사 → 임시 작업(트리거 없음) 등록·`Export-ScheduledTask` XML 확인·삭제.
6. `uninstall.ps1 -WhatIf` → (승인 시) `uninstall.ps1` → 작업·폴더·바로가기 없음, 데이터 보존 확인 → `install.ps1`로 재설치(§8.1-6).
7. 사용자 수동: 가이드 §15 체크리스트(로그온·재로그인·재부팅, 원본 가져오기, WAV 규칙, 앱별 원음 끄기, PS1 중지와 이중 재생, 원본 공존/종료, OBS, 드라이브·장치).

1~6은 포크 인스턴스를 종료·재시작하므로 진행 전에 사용자 승인이 필요하다.

## 9. 검토자 결정 요청

1. **버전 접미사(§3.3):** A(`Version`에 `-mnyang.1`), B(`InformationalVersion`만), 현행 유지 중 선택. A·B는 About에 접미사가 보이지 않는다. 보이게 할지도 함께 정해 달라.
2. **배포물 구성:** 지시 외로 `licenses\OpenDyslexic-OFL-1.1.txt`와 `CHANGELOG-fork.md`를 넣었다(고지 파일이 참조함). `NotificationsProKR-Mnyang.pdb`(260 KB)는 기본 publish 그대로 포함했다. 유지할지, `-p:DebugType=none`으로 뺄지 정해 달라.
3. **설치 시 이전 버전 파일 삭제:** 업데이트 때 설치 폴더에만 있는 파일을 지운다. 이 폴더는 스크립트 전용이라는 가정이다. 이 동작을 확정할지 정해 달라. 대안은 삭제 없이 덮어쓰기만 하는 것이다.
4. **`uninstall.ps1 -RemoveData`의 추가 확인(`ShouldContinue`)과 `-Force`:** 지시 외로 추가했다. 유지할지 정해 달라.
5. **`-SourceDir` 경로 인스턴스 종료:** 지시대로 설치 경로뿐 아니라 원본(dist) 경로에서 실행 중인 인스턴스에도 `--quit`을 보낸다. 다른 경로(개발 빌드)는 경고만 하고 설치를 계속한다. 이때 중단으로 바꿀지 정해 달라.
6. **제거 시 작업 해제 범위:** `NotificationsProKR-Mnyang-Logon`이 설치본이 아닌 exe(예: 개발 빌드)를 가리켜도 결정대로 해제한다(대상 exe는 출력). 이대로 둘지 정해 달라.
7. **PS1 감시기 동시 실행(PID 8252):** 현재 포크와 같이 떠 있다. 사용자에게 지금 중지를 안내할지, 실측(§8.3) 때까지 둘지 정해 달라.
8. **원본 가져오기 선택 UI:** P1-B는 "선택 UI를 정식으로 다듬는 것은 P5"로 남겼다. 이번 범위(배포·전환 준비)에는 넣지 않았다. 현재 예/아니오/취소 대화상자로 충분한지, 별도 작업으로 할지 정해 달라.
9. **P4 미결 B:** 한 이름에 AUMID가 여러 개일 때의 확인 상태 저장 키 규칙(P4 §9 B)은 아직 회신이 없다. (P4 §9 A는 이번 지시대로 코드 변경 없이 가이드 §9.2 안내로 처리했다.)
