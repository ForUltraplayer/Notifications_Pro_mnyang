<!-- @file MIGRATION-ko.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang 설치·전환 가이드(사용자용). 사전 조건, 설치, 첫 실행, 원본 설정 가져오기, WAV 규칙, 원래 알림음 끄기, 자동 실행, 기존 PS1 감시기·원본 앱에서의 전환과 이중 재생 확인, 되돌리기, 알려진 제한, 사용자 검증 체크리스트. -->

# Notifications Pro KR-Mnyang 설치·전환 가이드

이 문서는 이 PC에서 **기존 PS1 WAV 감시기**와 **원본 Notifications Pro(MSIX)**를 쓰던 상태에서 포크 앱(Notifications Pro KR-Mnyang)으로 옮기는 절차를 설명합니다. 명령은 모두 사용자가 직접 실행합니다. 스크립트는 기존 PS1 도구·원본 앱·사용자 WAV를 건드리지 않습니다.

> 표기: **(미검증)**은 2026-10-09 기준 실제 동작을 확인하지 못한 항목입니다. 직접 확인한 뒤 §14 체크리스트에 기록하세요.

## 목차

1. 사전 조건
2. 빌드와 설치
3. 첫 실행
4. 원본 설정 가져오기
5. WAV 보관함과 소리 규칙
6. ChatGPT와 Codex는 같은 앱으로 보입니다
7. Windows 알림 소리 끄기(앱별, 수동)
8. Discord·카카오톡의 한계
9. 로그온 자동 실행과 트레이로 시작
10. 기존 PS1 감시기 중지와 자동 실행 해제
11. 이중 재생 확인
12. 원본 앱: 공존 또는 종료
13. 업데이트와 되돌리기
14. 알려진 제한
15. 사용자 검증 체크리스트

---

## 1. 사전 조건

| 항목 | 내용 |
|---|---|
| OS | Windows 10/11 x64. 확인한 환경은 이 PC(Windows 11 10.0.26300)뿐입니다. |
| 런타임 | **.NET 8 Desktop Runtime (x64)**. 이 배포본은 런타임을 포함하지 않습니다(프레임워크 종속). |
| 권한 | 관리자 권한 필요 없음. 설치 위치는 `%LocalAppData%\Programs\NotificationsProKR-Mnyang\`입니다. |
| 빌드(이 PC에서 직접 만들 때) | .NET SDK 8 이상 |

런타임 확인(PowerShell):

```powershell
dotnet --list-runtimes | Select-String 'Microsoft.WindowsDesktop.App 8\.'
# dotnet 명령이 없으면 폴더로 확인
Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App" -Directory | Where-Object Name -like '8.*'
```

`Microsoft.WindowsDesktop.App 8.0.x`가 한 줄 이상 나오면 됩니다(이 PC: 8.0.31). 없으면 <https://dotnet.microsoft.com/download/dotnet/8.0>에서 **Desktop Runtime** x64를 설치합니다.

**설치 전에 개발용 포크 인스턴스를 종료하세요.** 포크는 한 세션에 하나만 실행됩니다. `src\NotificationsPro\bin\Debug\…`에서 실행한 개발 빌드가 떠 있으면 설치본을 실행해도 그 개발 빌드의 설정창만 열립니다. 트레이 아이콘 메뉴의 **종료**로 끝내세요. P4 이전 개발 빌드는 `--quit` 명령을 받지 못하므로 트레이 메뉴로 종료해야 합니다.

개발 빌드와 설치본은 **같은 데이터 폴더**(`%AppData%\NotificationsProKR-Mnyang`)를 씁니다. 개발 빌드에서 만든 설정·소리·프로필이 설치본에 그대로 이어집니다.

## 2. 빌드와 설치

저장소 폴더(`F:\dev\notifications-pro-custom`)에서 실행합니다. `-ExecutionPolicy Bypass`는 그 PowerShell 프로세스에만 적용되며 시스템 설정을 바꾸지 않습니다.

```powershell
cd F:\dev\notifications-pro-custom

# 1) 배포 산출물 만들기 → dist\NotificationsProKR-Mnyang\
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\publish.ps1

# 2) 무엇을 할지 먼저 확인(아무것도 바꾸지 않음)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\install.ps1 -WhatIf

# 3) 설치
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\install.ps1
```

설치 스크립트가 하는 일:

- `dist\NotificationsProKR-Mnyang\`의 파일을 `%LocalAppData%\Programs\NotificationsProKR-Mnyang\`에 복사합니다. 업데이트 때는 덮어쓰고, 새 버전에 없는 이전 파일은 지웁니다.
- 시작 메뉴에 `Notifications Pro KR-Mnyang` 바로가기 1개를 만듭니다.
- 설치 위치나 `dist` 위치에서 실행 중인 포크가 있으면 `--quit`으로 정상 종료를 요청하고 최대 10초 기다립니다. 끝나지 않으면 **강제 종료하지 않고 중단**합니다. 다른 위치(예: 개발 빌드)에서 실행 중인 포크는 건드리지 않고 경고만 출력합니다.
- 하지 않는 일: 앱 시작, 로그온 자동 실행 등록, `%AppData%\NotificationsProKR-Mnyang` 변경, 원본 앱·PS1 감시기·WAV 변경, 레지스트리 변경.

배포 폴더에는 `LICENSE`, `THIRD_PARTY_NOTICES.md`, `licenses\`, `NOTICE-fork.md`(수정 고지), `CHANGELOG-fork.md`, `settings.example.json`이 함께 들어 있습니다. 사용자 WAV·설정·알림 내용은 들어 있지 않습니다.

## 3. 첫 실행

1. 시작 메뉴에서 **Notifications Pro KR-Mnyang**을 실행합니다.
2. 데이터 폴더에 `settings.json`이 없으면(처음 설치) 설정창이 열립니다. 이 PC는 개발 빌드에서 쓰던 설정이 이미 있으므로, `트레이로 시작` 값(§9.2)에 따라 설정창이 열리거나 트레이에만 나타납니다.
3. **언어:** 시스템 탭 > 언어에서 `한국어 / English / OS 기본값`을 고릅니다. 이 PC 전용 값입니다.
4. **알림 접근 확인:**
   - 트레이 아이콘에 마우스를 올려 툴팁이 `WinRT(1초 조회) 방식으로 수신 중`(영어 UI: `Listening via WinRT (polling 1s)`)처럼 WinRT로 표시되는지 확인합니다.
   - 시스템 탭 > 알림 접근에서 캡처 방식이 WinRT 계열인지 봅니다. `접근성 대체`로 나오면 `접근 확인 다시 시도`를 누르고, 그래도 안 되면 `Windows 알림 접근 열기`로 Windows 설정을 확인합니다.
   - P0 시험에서 언패키지 실행은 권한 요청 창 없이 `Allowed`였고, 이벤트 구독 대신 1초 주기 조회로 알림을 받았습니다. 설치본 경로에서도 같은지는 **(미검증)**입니다.
5. 시험 알림: 실제 앱 알림을 받거나, 실행 중에 `"%LocalAppData%\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.exe" --send-test`를 실행해 오버레이 표시를 확인합니다.

## 4. 원본 설정 가져오기

원본 Notifications Pro의 테마·프로필·필터·오버레이 설정을 포크로 복사합니다. **원본 파일은 읽기만 하고 바꾸지 않습니다.**

1. (권장) 원본 앱을 트레이 메뉴에서 종료합니다. 가져오는 동안 원본이 설정을 다시 쓰지 않게 하기 위해서입니다.
2. 포크 설정창 **프로필 탭 > 가져오기 / 내보내기 > `원본 앱에서 가져오기`**를 누릅니다.
3. 후보 위치:
   - **MSIX 원본(기본 후보):** `%LocalAppData%\Packages\NotificationsPro_h8abfdfjdvdc6\LocalCache\Roaming\NotificationsPro\`. 원본 앱은 MSIX 패키지라서 앱 안에서는 `%AppData%\NotificationsPro`로 보이지만 실제 파일은 이 가상화 경로에 저장됩니다. 이 PC에는 이 위치에 `settings.json`이 있습니다(2026-10-09 확인).
   - **언패키지 원본:** `%AppData%\NotificationsPro\`. 이 PC에는 없습니다.
   - 둘 다 있으면 대화상자에서 `예 = MSIX`, `아니요 = 언패키지`, `취소 = 가져오지 않음`으로 고릅니다.
4. 가져오기 동작:
   - 현재 포크 설정을 `settings.json.pre-import-<시각>`으로 먼저 백업합니다. 내용이 다른 테마·소리 파일은 덮어쓰지 않고 `pre-import-<시각>\` 폴더로 옮긴 뒤 복사합니다.
   - 이 PC 전용 값(언어, 소리 보관함 폴더, 원래 소리 확인 상태, 트레이로 시작)은 현재 값을 유지합니다.
   - 가져온 설정은 원본처럼 동작하도록 `소리 재생 기준 = 화면 수용 기준`, `앱별 최소 간격 = 0(끔)`으로 시작합니다. 원본의 기본 WAV·앱별 WAV는 파일 1개짜리 규칙으로 바뀝니다.
5. **가져오기는 현재 설정을 바꿉니다.** 소리 규칙을 먼저 만들었다면 가져오기 뒤에 다시 확인하세요. 가능하면 가져오기를 먼저 하고 §5를 진행하세요.

## 5. WAV 보관함과 소리 규칙

### 5.1 WAV 가져오기

기존 PS1 감시기는 `C:\Users\Administrator\Desktop\ai-tts\notification-sounds\mornye-complete.wav`를 재생합니다(`windows-notify.json`의 `wav`). 다른 WAV도 같은 방법으로 넣습니다.

- **소리 탭 > 소리 보관함 > `파일 가져오기...`**: 선택한 WAV를 앱 관리 폴더(`%AppData%\NotificationsProKR-Mnyang\sounds`)에 **복사**합니다. 원본 파일은 그대로이고, 이름이 겹치면 고유한 이름을 붙입니다.
- **`폴더 추가...`**: 이 PC의 로컬 폴더를 복사하지 않고 참조합니다. 네트워크 폴더와 링크·정션은 거부합니다. 폴더를 옮기거나 드라이브가 빠지면 그 파일은 재생할 수 없음으로 표시되고, `새로 고침`이나 `참조 해제` 후 다시 추가하면 됩니다.
- 목록의 미리 듣기로 소리를 확인합니다. `목록에서 제거`는 파일을 지우지 않고, `파일 삭제...`는 관리 폴더의 파일만 확인 후 지웁니다.

### 5.2 규칙 정하기

- **기본 소리(소리 탭):** 앱 전용 규칙이 없는 모든 앱에 씁니다. 아래 보관함에서 파일을 체크하면 목록에 들어가고, 재생 방식을 `단일 / 랜덤 / 순차`로 고릅니다. `의도적 무음(명시적 무음)`을 켜면 전용 목록이 없는 앱은 소리를 내지 않습니다.
- **앱별(앱 탭):** 앱 카드마다 `기본 소리 사용 / 이 앱 무음 / 이 앱 전용 목록`을 고릅니다. 앱은 **이번 실행에서 알림을 한 번 받은 뒤** 앱 탭에 나타납니다.
- **재생 정책(소리 탭 > 재생):** 소리는 한 번에 하나씩 재생합니다. `앱별 최소 간격(초)`은 같은 앱의 소리가 그 시간 안에 다시 오면 건너뜁니다. 기존 PS1 감시기는 4초였습니다. 가져온 설정은 0(끔)으로 시작하므로 PS1과 비슷하게 쓰려면 4로 바꾸세요.
- **소리 재생 기준:** `화면 수용 기준`은 오버레이가 받아들인 알림에만, `정책 통과 기준`은 오버레이가 가득 차도 필터를 통과한 모든 알림에 재생합니다.

**PS1 감시기와 같은 범위(ChatGPT/Codex·Claude에서만 소리)로 쓰려면:**

1. 기본 소리에서 `의도적 무음(명시적 무음)`을 켭니다.
2. ChatGPT(Codex 포함)와 Claude 알림을 한 번씩 받은 뒤, 앱 탭에서 두 카드를 `이 앱 전용 목록`으로 바꾸고 WAV를 체크합니다.
3. 소리 탭 > 재생의 `앱별 최소 간격(초)`을 4로 둡니다.

## 6. ChatGPT와 Codex는 같은 앱으로 보입니다

ChatGPT 데스크톱 앱과 Codex는 같은 Windows 앱 ID(AUMID `OpenAI.Codex_2p2nqsd0c76g0!App`)로 알림을 보냅니다(P0 확인).

- 이 앱의 소리 규칙은 AUMID를 먼저 봅니다. 그래서 **ChatGPT와 Codex 알림을 서로 다른 WAV로 나눌 수 없습니다.**
- Windows 알림 설정에도 앱이 하나로 보이므로 원래 소리도 둘을 함께 켜고 끕니다.
- 앱 탭의 "원래 소리" 확인 상태도 AUMID 기준이라 두 카드가 함께 바뀝니다.

## 7. Windows 알림 소리 끄기(앱별, 수동)

이 앱은 Windows 설정을 읽거나 바꾸지 않습니다. WAV만 들리게 하려면 각 앱의 **원래 알림음**을 직접 꺼야 합니다.

1. 시작 > 설정 > 시스템 > 알림 > "앱 및 기타 보낸 사람의 알림"에서 앱(ChatGPT, Claude, Discord 등)을 선택합니다.
2. **"알림이 도착하면 소리 재생"**을 끕니다(영어 UI: *Play a sound when a notification arrives*). 한국어 화면의 정확한 문구는 **(미검증)**입니다.
3. **알림 자체(앱의 알림 켜기)와 배너는 끄지 마세요.** 알림을 끄면 이 앱도 그 알림을 받지 못합니다.
4. 앱 탭의 해당 카드 "원래 소리" 상자에서 `Windows 알림 설정 열기`로 같은 화면을 열 수 있습니다. 끈 뒤에는 `사용자 확인 완료로 표시`를 누르세요. 앱이 확인한 것이 아니라 사용자가 알려 준 상태로 저장됩니다. 되돌린 뒤에는 `다시 확인 필요`를 누릅니다.
5. 실제 알림 하나로 원래 소리 없이 WAV만 나는지, 배너와 알림 센터 항목은 남는지 확인합니다.

주의:

- 시스템 탭의 `알림 팝업 숨기기`를 켜면 Windows 팝업과 **알림 센터 항목까지** 지웁니다. 원래 소리를 끄는 기능이 아닙니다.
- 이 설정들은 포크를 제거해도 자동으로 돌아오지 않습니다(§13.3).

## 8. Discord·카카오톡의 한계

- **Discord:** Windows 알림 소리 외에 Discord 자체 소리가 있습니다. Discord 사용자 설정 > 알림에서 데스크톱 알림은 켠 채로 두고, 같은 화면의 소리 항목에서 메시지 소리를 끕니다. 메뉴 이름과 통화 소리 영향은 **(미검증)**입니다. 다른 사람이 보낸 실제 메시지로만 확인할 수 있으므로 사용자가 직접 시험하세요.
- **카카오톡:** 이 PC에서는 Windows 알림 설정에 카카오톡 항목이 없었습니다. 자체 알림 창과 자체 소리를 쓰는 것으로 보이며, **이 앱이 카카오톡 알림을 받지 못할 가능성이 높습니다(미검증).** 앱 탭에 나타나도 원래 소리 처리는 "안내 미지원"으로 표시됩니다. 카카오톡 소리는 카카오톡 설정에서 직접 관리하세요.

## 9. 로그온 자동 실행과 트레이로 시작

### 9.1 로그온 자동 실행

1. **설치본**(시작 메뉴로 실행한 것)에서 시스템 탭 > 시작 > `Windows 로그인 시 자동 실행`을 켭니다. 작업은 **지금 실행 중인 exe 경로**로 등록되므로 개발 빌드에서 켜면 개발 빌드가 등록됩니다.
2. 상태 줄이 `등록됨: Windows에 로그인하면 트레이로 시작합니다.`인지 확인합니다. 작업 이름은 `NotificationsProKR-Mnyang-Logon`입니다.
3. `작업이 다른 위치의 실행 파일을 시작합니다`가 보이면 옵션을 껐다가 다시 켜서 설치본으로 다시 등록합니다.
4. 로그아웃 후 다시 로그인해 설정창 없이 트레이 아이콘만 나오는지, 툴팁이 WinRT(1초 조회)인지 확인합니다. 재부팅 후에도 확인합니다.
5. **기존 PS1 자동 실행을 먼저 해제(§10)한 뒤 켜세요.** 둘 다 켜져 있으면 로그인할 때마다 두 프로그램이 함께 소리를 냅니다.

설치 스크립트는 이 작업을 등록하지 않습니다. 제거 스크립트는 작업을 해제합니다. 앱 폴더를 손으로 지우기 전에는 시스템 탭의 `작업 등록 해제`를 먼저 누르세요.

### 9.2 트레이로 시작

이 PC의 기존 설정에는 이전 개발 빌드가 쓴 `트레이로 시작 = 꺼짐`이 남아 있습니다. 그래서 직접 실행할 때마다 설정창이 열립니다. **시스템 탭 > 시작 > `트레이로 시작`을 켜세요.** 로그온 자동 실행은 이 값과 관계없이 항상 트레이로 시작합니다.

## 10. 기존 PS1 감시기 중지와 자동 실행 해제

기존 도구(`C:\Users\Administrator\Desktop\ai-tts\`)는 **지우지 마세요.** 포크 검증이 끝날 때까지 되돌리기용으로 둡니다. 아래 명령은 파일을 지우지 않습니다.

### 10.1 상태 확인

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Administrator\Desktop\ai-tts\windows-notify.ps1" -Action Status
```

실행 중이면 상태 JSON이, 아니면 `Mornye notification player is not running.`이 나옵니다.

### 10.2 감시기 중지

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Administrator\Desktop\ai-tts\windows-notify.ps1" -Action Stop
```

- `research\windows-notify-process.json`에 저장된 PID의 명령줄이 `research\watch_windows_notifications.ps1`을 포함할 때만 그 프로세스를 종료하고, PID 파일을 지우고, `research\windows-notify-state.json`의 상태를 `stopped`로 바꿉니다.
- 출력: `Mornye notification player stopped.`

### 10.3 로그온 자동 실행 해제

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Administrator\Desktop\ai-tts\windows-notify-autostart.ps1" -Action Disable
```

- 현재 사용자 시작프로그램 폴더의 `Mornye Windows Notifications.lnk`를 지웁니다. 바로가기가 다른 곳을 가리키면 아무것도 바꾸지 않고 오류를 냅니다.
- 실행 중인 감시기는 멈추지 않습니다(그래서 §10.2를 먼저 실행).
- 출력: `Login auto-start disabled. An already running notification player is not stopped.`

확인:

```powershell
Get-ChildItem ([Environment]::GetFolderPath('Startup')) | Select-Object Name
```

`Mornye Windows Notifications.lnk`가 없어야 합니다.

### 10.4 권장 순서

1. 포크 설치(§2) → 2. PS1 감시기 중지(§10.2) → 3. 포크에서 원본 설정 가져오기·WAV 규칙·원래 소리 끄기(§4~§7) → 4. 실제 알림으로 확인(§11) → 5. PS1 자동 실행 해제(§10.3) → 6. 포크 로그온 자동 실행 켜기(§9.1) → 7. 재로그인 후 다시 확인(§11).

## 11. 이중 재생 확인

소리를 내는 프로그램이 하나뿐인지 다음 순서로 확인합니다.

```powershell
# 1) PS1 감시기: 아무것도 나오지 않아야 함
Get-CimInstance Win32_Process |
  Where-Object { $_.CommandLine -like '*watch_windows_notifications*' } |
  Select-Object ProcessId, CommandLine

# 2) 원본 Notifications Pro: 종료를 택했다면 아무것도 나오지 않아야 함(§12)
Get-Process NotificationsPro -ErrorAction SilentlyContinue | Select-Object Id, Path

# 3) 포크: 정확히 1개, 경로가 설치 위치여야 함
Get-Process NotificationsProKR-Mnyang -ErrorAction SilentlyContinue | Select-Object Id, Path

# 4) 자동 실행: PS1 바로가기 없음, 포크 작업 1개
Get-ChildItem ([Environment]::GetFolderPath('Startup')) | Select-Object Name
schtasks /Query /TN NotificationsProKR-Mnyang-Logon
```

그다음 실제 알림으로 확인합니다.

1. ChatGPT(또는 Codex)·Claude 알림을 한 번 받습니다.
2. **WAV가 한 번만** 들리고 Windows 기본 알림음이 없는지 듣습니다. 같은 WAV가 겹쳐 두 번 들리면 PS1 감시기나 원본 앱이 아직 실행 중입니다.
3. 포크 소리 탭 > `재생 상태` 줄의 `시작` 수가 알림 1건마다 1씩 느는지 봅니다(앱 이름·내용은 표시하지 않음).
4. 재로그인·재부팅 뒤에도 1~3을 반복합니다.

## 12. 원본 앱: 공존 또는 종료

원본 Notifications Pro(MSIX)는 포크와 별개 프로그램입니다. 데이터 폴더·실행 파일·자동 실행이 모두 다르고, 포크는 원본을 수정하거나 종료하지 않습니다. 둘 다 실행하면 오버레이와 소리가 각각 납니다.

- **A. 원본 종료(권장):** 원본 트레이 메뉴에서 종료합니다. 원본의 System 탭에서 `Start with Windows`를 끄거나, Windows 설정 > 앱 > 시작 프로그램에서 원본을 끕니다. 원본 앱은 제거하지 않고 둡니다(되돌리기용, 설정 원본 보존).
- **B. 공존:** 원본의 Sounds 탭에서 알림 소리를 끄고, 오버레이 위치가 겹치지 않게 합니다. 어느 한쪽에서 `알림 팝업 숨기기`(원본: `Suppress Toast Popups`)를 켜면 알림 센터 항목이 지워져 다른 쪽이 그 알림을 놓칠 수 있습니다(**미검증**). 공존 중에는 양쪽 모두 끄세요.

## 13. 업데이트와 되돌리기

### 13.1 업데이트

```powershell
cd F:\dev\notifications-pro-custom
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\publish.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\install.ps1
```

설치본이 실행 중이면 `--quit`으로 정상 종료를 요청하고 10초 안에 끝나면 덮어씁니다. **자동으로 다시 시작하지 않습니다.** 시작 메뉴에서 다시 실행하세요. 설정·소리·프로필은 그대로입니다.

### 13.2 제거

```powershell
# 확인만
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\uninstall.ps1 -WhatIf
# 제거(데이터 보존)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\uninstall.ps1
# 데이터까지 삭제: 목록을 보여 준 뒤 한 번 더 묻습니다
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\custom\uninstall.ps1 -RemoveData
```

순서: 설치본 정상 종료 요청(최대 10초, 강제 종료 없음) → 로그온 작업 `NotificationsProKR-Mnyang-Logon` 해제(없으면 넘어감, 해제 실패 시 파일을 지우지 않고 중단) → 프로그램 폴더와 시작 메뉴 바로가기 삭제 → `%AppData%\NotificationsProKR-Mnyang`은 기본 보존.

`-RemoveData`로 지우는 `sounds\`의 WAV는 가져올 때 만든 사본입니다. 원래 WAV와 `폴더 추가`로 참조한 폴더는 지우지 않습니다. 원본 앱 데이터(MSIX LocalCache, `%AppData%\NotificationsPro`)도 대상이 아닙니다.

### 13.3 이전 상태로 되돌리기

1. 포크 제거(§13.2).
2. 끈 Windows 알림 소리를 앱별로 다시 켭니다(설정 > 시스템 > 알림 > 앱 > "알림이 도착하면 소리 재생"). Discord에서 끈 소리도 다시 켭니다.
3. PS1 감시기 다시 시작과 자동 실행 복구:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Administrator\Desktop\ai-tts\windows-notify.ps1" -Action Start
   powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Administrator\Desktop\ai-tts\windows-notify-autostart.ps1" -Action Enable
   ```

   `-Action Start`는 준비되면 `Mornye notification player ready (PID …).`를 출력합니다.
4. 원본 앱을 다시 쓰려면 원본에서 `Start with Windows`를 다시 켭니다.

## 14. 알려진 제한

- **실패 시 자동 재시작 없음.** 비정상 종료되면 다시 실행해야 하며, 다음 시작 때 트레이 풍선과 시스템 탭 "이전 실행" 줄로 알려 줍니다. 원인은 판별하지 않습니다.
- **카카오톡 알림은 받지 못할 수 있습니다**(§8).
- **ChatGPT와 Codex는 구별할 수 없습니다**(§6).
- **원래 알림음은 수동으로 꺼야 합니다.** Windows 사운드 구성표를 바꾸는 실험 기능은 채택하지 않았습니다.
- **다른 PC는 확인하지 않았습니다.** 이 PC(Windows 11 10.0.26300, UAC 꺼짐)에서만 시험했습니다. UAC가 켜진 PC의 작업 권한 동작은 **(미검증)**입니다.
- **배포 산출물을 실제로 설치·실행한 시험은 아직 하지 않았습니다**(2026-10-09, 개발 인스턴스 실행 중). §15를 진행하며 확인합니다.
- OBS 크로마키·고정 캡처는 이 PC에 OBS Studio 32.1.2가 설치되어 있지만 포크로 시험하지 않았습니다(**미검증**).
- 서명되지 않은 실행 파일이라 SmartScreen 등 경고가 나올 수 있습니다(**미검증**).

## 15. 사용자 검증 체크리스트

P4 보고서 §11 항목을 포함합니다. 확인한 날짜와 결과를 적어 두세요.

### 15.1 설치·제거 (P5)

- [ ] `publish.ps1` 출력에 `ko-KR 위성 어셈블리: 있음`, `사용자 데이터 검사: … 없음`이 나온다.
- [ ] `install.ps1 -WhatIf`가 아무것도 바꾸지 않는다(설치 폴더·바로가기 없음 그대로).
- [ ] `install.ps1` 후 `%LocalAppData%\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.exe`와 시작 메뉴 `Notifications Pro KR-Mnyang`이 생긴다. 앱은 자동으로 시작되지 않는다.
- [ ] 시작 메뉴로 실행하면 트레이 아이콘이 나오고, 시스템 탭 언어를 한국어로 바꾸면 화면이 한국어로 바뀐다.
- [ ] 설치본 실행 중에 `install.ps1`을 다시 실행하면 `--quit` 요청 → `종료 확인` → 복사 → "자동으로 다시 시작하지 않습니다"가 나온다.
- [ ] 개발 빌드가 실행 중일 때 `install.ps1`은 그 인스턴스를 종료하지 않고 경고만 낸다.
- [ ] `uninstall.ps1` 후 프로그램 폴더·바로가기·로그온 작업이 없어지고 `%AppData%\NotificationsProKR-Mnyang`은 남는다.
- [ ] `uninstall.ps1 -RemoveData`는 삭제 목록을 먼저 보여 주고 확인을 묻는다.

### 15.2 전환 (P5)

- [ ] 원본 설정 가져오기 후 테마·프로필·필터가 원본과 같다. 원본 `settings.json`의 수정 시각이 바뀌지 않았다.
- [ ] `mornye-complete.wav`를 가져와 규칙을 만들고 ChatGPT/Codex·Claude 알림에서 재생된다. 원본 WAV 파일은 그대로 있다.
- [ ] PS1 감시기 중지·자동 실행 해제 후 §11 확인에서 이중 재생이 없다.
- [ ] 원본 앱 종료 또는 공존 설정 후 소리·오버레이가 한 벌만 나온다.
- [ ] 되돌리기(§13.3) 후 PS1 감시기가 다시 WAV를 재생한다.
- [ ] (OBS를 쓴다면) 포크 오버레이의 크로마키·고정 캡처가 원본과 같게 잡힌다.
- [ ] 외부 WAV 폴더를 옮기거나 드라이브를 뺐다가 다시 연결하면 `새로 고침`으로 복구된다. 출력 장치를 바꾼 뒤에도 소리가 난다.

### 15.3 로그온 자동 실행 (R07, P4 §11.1)

- [ ] 설치본에서 `Windows 로그인 시 자동 실행`을 켜면 상태 줄이 "등록됨"이 된다.
- [ ] 작업 스케줄러(`taskschd.msc`) 루트의 `NotificationsProKR-Mnyang-Logon`: 동작이 설치본 exe 직접 실행이고 인자가 `--start-minimized`, "사용자가 로그온할 때만 실행", 최고 권한 아님, 작업 중지 시간 없음.
- [ ] 로그아웃 후 다시 로그인하면 설정창 없이 트레이 아이콘만 나오고, 툴팁이 `WinRT(1초 조회) 방식으로 수신 중`(영어: `Listening via WinRT (polling 1s)`), 오버레이는 저장된 표시 상태대로다.
- [ ] 재부팅 후에도 같다.
- [ ] 작업 스케줄러에서 작업을 "사용 안 함"으로 바꾸고 `상태 새로 고침`을 누르면 "등록됨(비활성)", 토글 꺼짐이다. 앱이 작업을 다시 켜지 않는다.
- [ ] `작업 등록 해제`를 누르면 "미등록"이 되고 작업 스케줄러에서도 사라진다.

### 15.4 비정상 종료 안내 (R08, P4 §11.2)

- [ ] 작업 관리자 "작업 끝내기"로 종료한 뒤 다시 실행하면 트레이 풍선과 시스템 탭 "이전 실행" 줄에 안내가 나온다.
- [ ] 트레이 "종료" 후 다시 실행하면 "정상적으로 끝나지 않은 이전 실행 기록이 없습니다"가 나온다.
- [ ] 실행한 채 로그아웃하거나 Windows를 다시 시작한 뒤 다음 실행에서 오탐이 없다.

### 15.5 CLI (K12, P4 §11.3)

- [ ] 실행 중에 `"%LocalAppData%\Programs\NotificationsProKR-Mnyang\NotificationsProKR-Mnyang.exe" --pause`를 실행하면 기존 인스턴스가 한 번만 일시 정지되고(트레이 아이콘 흐림) 새 트레이 아이콘이 생기지 않으며 `echo %errorlevel%`이 0이다. `--resume`, `--theme <이름>`, `--hide`/`--show`, `--send-test`도 같다.
- [ ] 인자 없이 두 번째로 실행하면 설정창만 열린다.

### 15.6 원래 소리 수동 음소거 (R09, P4 §11.4)

- [ ] ChatGPT/Codex·Claude: 알림을 한 번 받은 뒤 앱 탭에 "설정 필요"와 안내가 보인다. `Windows 알림 설정 열기`로 "알림이 도착하면 소리 재생"을 끄고 실제 알림에서 원래 소리 없이 WAV만 나며 배너·알림 센터는 유지된다. `사용자 확인 완료로 표시` 후 앱을 다시 시작해도 유지된다. 한국어 Windows 토글 문구가 안내문과 같다.
- [ ] Discord: Windows 토글을 끄고 Discord 소리 설정에서 메시지 소리를 끈 뒤, 다른 사람이 보낸 실제 메시지에서 원래 소리가 없고 배너가 유지되며 통화 소리는 정상이다(메시지 시험은 사용자가 직접). 메뉴 이름이 안내문과 같다.
- [ ] 카카오톡: 앱 탭에 나타나면 "안내 미지원"이다. 카카오톡 알림이 실제로 수신되는지 기록한다.
- [ ] `다시 확인 필요`를 누르면 "설정 필요"로 돌아간다.
- [ ] ChatGPT와 Codex 카드가 둘 다 보이면 한쪽에서 확인 완료를 누를 때 다른 카드도 같은 상태가 된다.
- [ ] 시스템 탭에서 `트레이로 시작`을 켠 뒤 직접 다시 실행하면 트레이로만 시작한다(§9.2).
