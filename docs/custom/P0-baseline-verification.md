<!-- @file P0-baseline-verification.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro 커스텀 포크 P0 기반 검증 결과(T1~T6), 앱별 원음 판정표, 1초 조회 대안 제안, 미완료·결정 요청. -->

# P0 기반 검증 보고서

- 작성일: 2026-10-09 (Asia/Seoul), 측정 시간대 11:27~11:47
- 구현 담당: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: 사용자(rudals252)
- 기준: 설계서 v1.1 (`C:\Users\Administrator\Desktop\ai-tts\docs\notifications-pro-integrated-design.md`) §1, §8, §9, §13 P0
- 저장소: `F:\dev\notifications-pro-custom`, 브랜치 `custom/base`, HEAD `47d33985fb7584607792574d8587b4cebc4ebfb9` (v1.1.10.37). commit 없음
- 환경: Windows 11 Pro 10.0.26300, dotnet SDK 10.0.401, net8 런타임. 계정은 내장 Administrator(SID …-500). **UAC 비활성(EnableLUA=0)**
- 앱 본체 소스(`src/NotificationsPro/**`) 수정 없음. 레지스트리·Windows 설정 값 직접 변경 없음(임시 스케줄러 작업 등록·삭제만 수행)

## 0. 결론 요약

1. **언패키지 WinRT는 조회는 되고 구독은 안 된다(실측).** `RequestAccessAsync` = `Allowed`(프롬프트 없음), `GetNotificationsAsync` 성공(25건, 약 250ms). `NotificationChanged` 등록은 `COMException 0x80070490`(Element not found)로 실패해 이벤트 수신이 0건이다. WindowsAppSDK #6172와 같은 증상이다. **1초 주기 조회는 로컬 테스트 토스트 5건을 모두 220~275ms 안에 감지했고, 실제 Codex 알림 3건도 0.30~1.06초 안에 감지했다(900초 관찰, 조회 895회, 오류 0).** → 설계 §9.1에 따라 **1초 조회 대안 채택 조건 충족**.
2. **원본 앱을 언패키지로 실행하면 지금 코드에서는 접근성 모드로 떨어진다.** `StartWinRTListening()`에서 구독(`+=`)이 예외를 던지고, 이 예외가 조회 경로까지 함께 포기시킨다. 격리 빌드의 트레이 표시는 `Listening via Accessibility`였고 로컬 토스트를 수신했다. 설치된 원본 MSIX는 같은 시각 `Listening via WinRT`로 동작 중이었다. 조회와 구독을 분리하면 언패키지에서도 WinRT 조회(AUMID 확보 가능)를 쓸 수 있다(§8 제안).
3. **작업 스케줄러: 대화형 세션 실행·72시간 해제는 확인했다. 실패 시 재시작은 동작하지 않았다.** 등록 XML은 `InteractiveToken`, `ExecutionTimeLimit=PT0S`, `RestartOnFailure 3회/PT1M`이었다. 실행된 프로세스는 현재 콘솔 세션 1에 떴다. 그러나 강제 종료(종료 코드 −1, `LastTaskResult=4294967295`) 후 80초 동안 **재시작이 일어나지 않았다.** 스케줄러의 실패 재시작에 R08 선택 기능을 의존할 수 없다. 임시 작업은 삭제했고 삭제도 확인했다.
4. **이미 생긴 문제(중대): 기준 `dotnet test` 실행이 원본 앱의 실제 `%AppData%\NotificationsPro\settings.json`을 덮어썼다.** 파일 mtime(11:02:59)이 테스트 빌드 시각(11:02:57)과 일치하고, 값에 테스트 고유값(`NotificationDuration=60`, `MaxVisibleNotifications=3`, `AnimationDurationMs=0`)이 들어 있다. `icons\__tests`, `icons-escape`, `backgrounds-escape` 폴더도 테스트가 만들었다. 이 문제는 이번 P0 작업 전에 생겼고, 이번 작업 중에는 해당 파일이 바뀌지 않았다. 복구는 사용자 결정 사항이다(§9).
5. **원음 음소거:** ChatGPT/Codex·Claude·Discord는 Windows 토스트 경로를 쓰므로 Windows 앱별 "소리 재생" 끄기가 1차 안내 대상이다(실측 미완). 카카오톡은 Windows 알림 설정 키가 아예 없어 자체 알림창·자체 소리로 추정한다. 이 경우 WinRT로 수신할 수 없을 가능성이 높다(미검증). **ChatGPT와 Codex는 같은 AUMID(`OpenAI.Codex_2p2nqsd0c76g0!App`)를 쓰므로 AUMID로는 둘을 구별할 수 없다.** 사운드 구성표 실험은 읽기만 했고, 실험 스크립트는 작성만 하고 실행하지 않았다.

## 1. T1 — 언패키지 WinRT 리스너: 조회와 구독 각각 실측

### 1.1 프로브

- 소스: `tools/P0Probe/P0Probe.csproj`, `tools/P0Probe/Program.cs` (`net8.0-windows10.0.19041.0`, 메인 csproj와 같은 TFM 기반 WinRT 투영)
- 출력 항목: AUMID, 알림 ID, 생성 시각, 이벤트 종류, 오류 HRESULT뿐이다. 토스트 텍스트는 읽지도 출력하지도 않는다.
- 테스트 알림: 프로브가 `ToastNotificationManager.CreateToastNotifier(<Windows PowerShell AUMID>)`로 로컬 발행했다(무음 `audio silent`, 일반 기술 문구). 종료 시 `History.Remove(tag, group, aumid)`로 자신이 만든 토스트만 정리했다.
- 빌드: `dotnet build tools/P0Probe/P0Probe.csproj` → 경고 0, 오류 0

### 1.2 실행 명령과 결과 (실제 Windows 검증)

```powershell
P0Probe.exe probe --seconds 25 --toasts 2 --out .p0\t1-run1-noaumid.jsonl
P0Probe.exe probe --seconds 20 --toasts 2 --set-aumid NotificationsProCustom.P0Probe --out .p0\t1-run2-aumid.jsonl
P0Probe.exe probe --seconds 900 --toasts 0 --skip-request --out .p0\t1-run3-passive.jsonl   # 실제 앱 알림 관찰
```

| 항목 | run1 (명시 AUMID 없음) | run2 (명시 AUMID 설정) |
|---|---|---|
| 프로세스 | 패키지 ID 없음, 세션 1, MTA | 동일 |
| (a) `GetAccessStatus` 전/후, `RequestAccessAsync` | Allowed / Allowed / Allowed (프롬프트 없음) | 동일 |
| (b) `GetNotificationsAsync(Toast)` | 성공, 25건, 243ms | 성공, 25건, 290ms |
| (c) `NotificationChanged +=` | **실패 `COMException 0x80070490`** (메시지 공란) | **실패 `0x80070490`** |
| (c) 실제 이벤트 수신 | 0건 | 0건 |
| (d) 1초 조회 새 알림 감지 | 2/2, 발행 후 256ms·259ms | 2/2, 224ms·273ms |
| 조회 오류 | 0/25회 | 0/20회 |

오류 원문(run1):

```json
{"evt":"subscribe","data":{"ok":false,"error":{"type":"COMException","hresult":"0x80070490","message":""}}}
```

추가 관찰:

- T2에서 같은 방식으로 1건을 더 보냈다. 감지는 256ms였다. 로컬 토스트 합계는 5/5 감지다.
- **실제 앱 알림(수동 관찰, 본문 미열람):** passive run에서 `OpenAI.Codex_2p2nqsd0c76g0!App` 알림을 조회가 감지했다. 생성 11:35:24.74 → 감지 11:35:25.80, 생성 11:38:59.77 → 감지 11:39:00.07. 결과 전체는 §1.4에 적었다.
- **트레이 풍선 알림은 앱 식별이 안 된다:** 격리 앱의 첫 실행 풍선(`NotifyIcon.ShowBalloonTip`)이 목록에 나타났다(ID 27058, 11:34:19). 그러나 `AppInfo.AppUserModelId` 접근이 `0x80004001`(E_NOTIMPL)을 던졌다. 기존 목록의 다른 1건(ID 26905)도 같은 오류다. 풍선 팁을 쓰는 Win32 앱은 WinRT 경로에서 AUMID·표시 이름을 얻지 못할 수 있다. 원본 `ExtractAndForwardWinRT`는 이 예외를 `catch {}`로 삼키므로 해당 알림이 조용히 버려진다(코드 읽기 기준).
- 첫 `notifier.Setting` 조회 1회가 `0x80070490`을 반환했지만 토스트는 정상 표시·감지됐다. 이후 조회는 모두 `Enabled`였다.
- 권한 프롬프트·설정 변경 요구는 없었다. 이 PC는 이미 데스크톱 앱의 알림 접근이 허용된 상태로 보인다. PS1 감시기도 `Allowed`로 동작 중이다.

### 1.3 판정

| 항목 | 판정 | 근거 |
|---|---|---|
| 언패키지 조회 | **통과(이 PC·이 빌드)** | 실측 |
| 언패키지 구독 | **실패** — 등록 단계 예외 | 실측, #6172와 같은 증상 |
| 1초 조회 대안 | **채택 조건 충족** — 로컬 5/5, 실제 Codex 알림 감지 | 실측 |
| 권한 철회 시 동작 | **미검증** — 사용자 조작 필요(§10) | — |
| 재로그인 후 권한 유지 | **미검증** | — |

### 1.4 장시간 수동 관찰(passive) 결과

- 실행: 11:32:26~11:47:26(900초), 테스트 토스트 발행 없음, `--skip-request`.
- 결과: 조회 895회, 오류 0회, 권한 변화 없음(`Allowed` 유지), 구독 실패(`0x80070490`), 이벤트 0건.
- 기준 집합(25건)을 제외하고 새로 감지한 알림은 5건이다(본문 미열람).

| 생성 시각 | AUMID | 감지 지연 | 비고 |
|---|---|---|---|
| 11:34:19.91 | `(error 80004001)` | 0.39s | 격리 앱 첫 실행 풍선 팁 |
| 11:35:13.22 | PowerShell AUMID | 0.47s | T2 로컬 테스트 토스트 |
| 11:35:24.74 | `OpenAI.Codex_2p2nqsd0c76g0!App` | 1.06s | 실제 앱 알림 |
| 11:38:59.77 | `OpenAI.Codex_2p2nqsd0c76g0!App` | 0.31s | 실제 앱 알림 |
| 11:44:08.63 | `OpenAI.Codex_2p2nqsd0c76g0!App` | 0.30s | 실제 앱 알림 |

- 관찰 창 동안 Claude·Discord·카카오톡의 새 알림은 없었다. 이 세 앱의 새 알림 감지는 미검증이다.

## 2. 기존 앱 코드의 조회·구독·대체 처리 (읽기 결과)

`src/NotificationsPro/Services/NotificationListener.cs`:

- `InitializeAsync()`: 강제 접근성이 아니면 `RequestAccessAsync()`를 호출한다. `Allowed`이면 `IsAccessGranted=true`로 두고 `StartWinRTListening()`을 호출한다. 이 전체가 하나의 `try`에 들어 있어, 어떤 예외든 나면 `StartAccessibilityCapture()`로 넘어간다.
- `StartWinRTListening()`: `_isRunning=true`로 바꾼 뒤 `_listener.NotificationChanged += …`를 실행하고, 이어서 `SeedAndStartPollingAsync()`를 호출한다. **언패키지에서는 `+=`가 예외를 던지므로 시드·조회까지 가지 못하고 접근성 모드로 전환된다.** 예외 시점에 `_isRunning`은 이미 true다.
- `SeedAndStartPollingAsync()`: 기준 집합을 시드한 뒤 **2초** `DispatcherTimer`로 조회한다. 중첩은 `_isPolling`으로 막는다. 중복 판정 키는 `uint Id`뿐이다(`_seenIds`, 5000 초과 시 상위 1000개만 남김).
- 조회 예외 시 `SwitchToAccessibility()`로 넘어간다. 권한 상태(`GetAccessStatus`)는 조회 중에 다시 확인하지 않는다.
- 주석(164~168행)에는 "언패키지에서 Allowed가 나와도 WinRT가 전달하지 않는다"고 적혀 있다. 이번 실측에서는 **조회는 전달됐고 실패는 구독 등록뿐이었다.** 이 주석은 조회 경로에 대해서는 현재 PC 기준으로 맞지 않는다.
- 언패키지일 때 AUMID는 `App.xaml.cs`에서 `NotificationsPro.App`으로 설정된다. 단일 인스턴스 mutex는 **없다**(전체 소스 grep 결과 0건).

## 3. T2 — 기존 GUI 앱(언패키지 빌드) 실제 실행

### 3.1 격리 방식 변경 (중요)

- 지시된 `$env:APPDATA` 교체로는 격리되지 않는다. 앱은 `Environment.GetFolderPath(SpecialFolder.ApplicationData)`를 쓰는데, 이 API는 환경 변수를 무시한다. 실측:

```json
{"evt":"appdata","data":{"env_APPDATA":"F:\\dev\\notifications-pro-custom\\.p0\\appdata","GetFolderPath_ApplicationData":"C:\\Users\\Administrator\\AppData\\Roaming"}}
```

- 그래서 수정하지 않은 원본 빌드는 실행하지 않았다. 대신 `scripts/custom/p0/build-isolated-app.ps1`을 만들었다. 이 스크립트는 `src/NotificationsPro`를 `.p0/isolated-src/`로 복사하고, 복사본의 4곳(`ManagedAssetPathHelper`, `ProfileManager`, `SettingsManager`, `ThemeManager`)만 `NP_P0_APPDATA` 환경 변수를 쓰도록 치환한다. 변수가 없으면 시작 시 예외가 나므로 실제 AppData에 쓸 수 없다(fail-closed). 남은 `SpecialFolder.ApplicationData` 참조가 있으면 빌드를 중단하는 검사도 넣었다. `src/`는 변경하지 않았다. 빌드 결과는 경고 0, 오류 0이다.
- 실행 래퍼 `scripts/custom/p0/run-isolated-app.ps1`은 `NP_P0_APPDATA`와 `APPDATA`를 `.p0\appdata`로 설정하고, exe를 실행·대기한 뒤 종료 코드를 그대로 반환한다. `.p0\wrapper.log`에는 PID·세션·종료 코드·권한 상승 여부만 기록한다.

### 3.2 실행 전 상태 (조회만 함, 종료하지 않음)

| 대상 | 상태 |
|---|---|
| 원본 `NotificationsPro.exe` | PID 38504, 세션 1, `C:\Program Files\WindowsApps\NotificationsPro_1.1.10.37_neutral__h8abfdfjdvdc6\`, 10:18:38 시작 |
| PS1 감시기 | PID 8252 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "…\watch_windows_notifications.ps1"`, 세션 1, 08:34:32 시작 |
| 실제 `settings.json` | mtime `2026-10-09T11:02:59.5396088+09:00`, 6249 bytes (§0-4 참조) |

### 3.3 결과

- 격리 앱 PID 31012가 세션 1에서 시작됐다. 데이터는 `.p0\appdata\NotificationsPro\settings.json`에만 생성됐다.
- 수신 모드는 트레이 툴팁을 UI Automation으로 읽어 확인했다(`scripts/custom/p0/read-tray-status.ps1`). 숨김 아이콘 플라이아웃을 열고 이름만 읽었다.

| 앱 | 툴팁(기술 상태만) |
|---|---|
| 격리 언패키지 빌드 | `Listening via Accessibility` — `Accessibility mode — 1 captured …` → 로컬 토스트 후 `3 captured` |
| 원본 MSIX(실행 중) | `Listening via WinRT` — `WinRT — 36 captured, 25 in system (poll #2290)` → 이후 `38 captured` |

- 격리 앱의 증가분 2건은 로컬 테스트 토스트와 같은 시간대의 실제 Codex 알림으로 추정한다(passive 로그 시각 대조, 본문 미열람).
- 접근성 모드로 떨어진 원인은 §2 코드 경로와 T1 실측(구독 예외)으로 **추론**했다. 앱의 초기 상태 문구 `WinRT unavailable (COMException)…`은 이후 상태 갱신으로 덮여 직접 보지 못했다.
- 이중 재생: 격리 앱은 기본값 `SoundEnabled=false`라 소리를 내지 않았다. PS1 감시기는 Codex 알림 대상이므로 평소대로 동작했을 것이다(소리 청취는 하지 않음). 원본 앱의 현재 `settings.json`은 `SoundEnabled=false`다.
- 종료: `taskkill /PID`(WM_CLOSE)로는 종료되지 않았다(트레이 앱). 경로를 확인한 뒤 `Stop-Process -Id 31012 -Force`로 종료했고, 래퍼 기록은 `exit_code=-1`이다. 트레이 메뉴의 정상 "Exit" 경로는 UI 조작이 필요해 시험하지 않았다.
- 실행 후 실제 `settings.json` mtime은 그대로 `11:02:59.5396088`이다.
- **부수 효과:** 격리 앱의 첫 실행 풍선 때문에 Windows가 `HKCU\…\Notifications\Settings\NotificationsPro.App` 키를 11:34:19에 **자동 생성**했다. 값은 `LastNotificationAddedTime`, `PeriodicNotificationCount=1`이다. 직접 쓴 값은 아니지만 레지스트리에 흔적이 남았다. 테스트 토스트로 PowerShell AUMID 키의 카운터도 올라갔다.

## 4. T3 — 작업 스케줄러 (임시 작업, 삭제 완료)

- 스크립트: `scripts/custom/p0/register-temp-task.ps1 -Register` / `-Remove`
- 작업 `NotificationsProCustom-P0-Temp`: 트리거 없음(온디맨드), 현재 사용자, `-LogonType Interactive`, `-RunLevel Limited`, `ExecutionTimeLimit=0`, 재시작 3회/1분, `MultipleInstances IgnoreNew`. 동작은 래퍼(`powershell.exe -File run-isolated-app.ps1`)이고 작업 디렉터리는 격리 exe 폴더다.

### 4.1 Export XML 확인 (발췌, `.p0\t3-task-export.xml`)

```xml
<LogonType>InteractiveToken</LogonType>
<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
<RestartOnFailure><Count>3</Count><Interval>PT1M</Interval></RestartOnFailure>
<Triggers />
<WorkingDirectory>F:\dev\notifications-pro-custom\.p0\isolated-src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0</WorkingDirectory>
```

`RunLevel` 요소가 없으므로 기본값 LeastPrivilege다(의도대로).

### 4.2 실행·종료 실측

| 시나리오 | 결과 |
|---|---|
| `Start-ScheduledTask` | 래퍼 PID 1816 → 앱 PID 6080, **둘 다 세션 1**(`query session`: console/Administrator/ID 1/Active), 소유자 `DESKTOP-QRH1UBQ\Administrator`. 실행 중 `LastTaskResult=267009`(0x41301, 실행 중). 트레이 상태 `Listening via Accessibility` |
| 권한 수준 | 래퍼 `wrapper_elevated=true`. **이 PC는 UAC가 꺼져 있어(EnableLUA=0) "최고 권한 아님" 설정이 실효가 없다.** 비교로 본 셸도 elevated=True였다 |
| 앱 강제 종료(`Stop-Process -Force`) | 래퍼 `exit_code=-1` → `LastTaskResult=4294967295`(0xFFFFFFFF), 상태 Ready |
| 재시작 대기(80초 1회) | **재시작 없음**(LastRunTime 11:37:25 그대로, 새 프로세스 없음) |
| `Stop-ScheduledTask` | `LastTaskResult=267014`(0x41306, 사용자 종료). **래퍼만 종료되고 앱(PID 38992)은 고아 프로세스로 남았다** → 경로 확인 후 수동 종료 |
| 정상 종료 코드 0 (프로브 `exit --code 0`) | `LastTaskResult=0` |
| 종료 코드 1 (프로브 `exit --code 1`) | `LastTaskResult=1`. 약 29초 뒤까지 재시작 없음(1분 경과 전 삭제, 재시작 여부 판정에는 불충분) |
| TaskScheduler/Operational 로그 | 비활성(`IsEnabled=False`)이라 이벤트 근거 없음 |
| 삭제 | `Unregister-ScheduledTask` 후 `Get-ScheduledTask` → 개체 없음 오류, `schtasks /Query /TN NotificationsProCustom-P0-Temp` → `ERROR: The system cannot find the file specified.` |

### 4.3 판정과 설계 영향

- 대화형 세션 실행: **통과**(온디맨드 시작 기준). 로그온 트리거·재로그인·재부팅은 **사용자 수동 검증 필요**.
- 72시간 제한 해제: 설정값은 **확인**(`PT0S`). 72시간 경과 실측은 미실시.
- 실패 재시작(선택 기능): **이 구성으로는 미지원**. 0이 아닌 종료 코드(−1)를 스케줄러가 "실패"로 보고 재시작하지 않았다. R08 선택 재시작을 구현하려면 다른 방식이 필요하다(예: 반복 트리거+`IgnoreNew`, 또는 앱 자체 처리). 검토 대상이다.
- 래퍼 사용 금지 권고: 실제 배포 작업은 **exe를 직접 실행**해야 한다. 래퍼를 두면 `Stop-ScheduledTask`로 앱이 멈추지 않는다(고아 프로세스). 포크는 자체 AppData 폴더를 쓰므로 래퍼가 필요 없다.

## 5. T4 — 원음 수동 음소거 안내 조사 (읽기 전용)

### 5.1 설치 확인

| 앱 | 식별 | 버전 | 실행 중 |
|---|---|---|---|
| ChatGPT (Codex 포함) | StartApps `ChatGPT` → AUMID `OpenAI.Codex_2p2nqsd0c76g0!App` (Store 패키지 `OpenAI.Codex`) | 26.1002.7124.0 | 예 |
| Codex | 별도 시작 항목 없음. 작업 표시줄의 ChatGPT 창도 같은 AUMID | (위와 같은 패키지) | 예 |
| Claude | AUMID `Claude_pzs8sxrjxfjjc!Claude` (패키지 `Claude`, Developer 서명) | 2.26454.2.0 | 예 |
| Discord | AUMID `com.squirrel.Discord.Discord` (Squirrel 설치, `%LocalAppData%\Discord\app-1.0.9261`) | 1.0.9261 | 예 |
| Discord Canary | AUMID `com.squirrel.DiscordCanary.DiscordCanary` (설치됨, 대상 외) | 미확인 | — |
| 카카오톡 | AUMID `{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Kakao\KakaoTalk\KakaoTalk.exe` | 26.8.2.5324 | 예 |

### 5.2 `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\<AUMID>` (읽기만 함)

| AUMID | 존재하는 값 |
|---|---|
| `OpenAI.Codex_2p2nqsd0c76g0!App` | `LastNotificationAddedTime`(=2026-10-09 11:38:59), `IsOptOutCandidate=0`, `PeriodicNotificationCount=253` |
| `Claude_pzs8sxrjxfjjc!Claude` | `LastNotificationAddedTime`(=11:15:04), `PeriodicNotificationCount=38` |
| `com.squirrel.Discord.Discord` | `LastNotificationAddedTime`(=2026-10-07 22:36:17), `LastOptOutTime`(=2026-09-09 14:47:06), `PeriodicNotificationCount=156`, `PeriodicInteractionCount=1` |
| 카카오톡 AUMID | **키 없음** |
| `NotificationsPro_h8abfdfjdvdc6!App`(원본) | 카운터만 |
| `NotificationsPro.App`(격리 빌드) | 카운터만(이번 P0 실행으로 자동 생성) |

- 대상 앱 어디에도 알림 끔·소리 끔에 해당하는 사용자 지정 값이 없다. 모두 기본 상태로 해석한다(추정).
- "소리 재생" 토글이 어떤 값 이름을 쓰는지는 **미확인**이다. 문서화되지 않았고, 값을 바꿔 볼 수 없었다. 읽기 전용 확인 도구로 `scripts/custom/p0/snapshot-notification-settings.ps1`을 만들고 기준 스냅샷을 `.p0\notif-settings-baseline.json`에 저장했다. 사용자가 토글한 뒤 `-Compare`로 차이를 보면 값 이름을 확인할 수 있다.
- 시스템 전역 `HKCU\…\PushNotifications\ToastEnabled` 값은 없다(기본값).

### 5.3 사용자 안내 경로 (Windows 11)

- **Windows 앱별 소리:** 시작 > 설정 > 시스템 > 알림 > "앱 및 기타 보낸 사람의 알림"에서 앱 선택 > **"알림이 도착하면 소리 재생"** 끄기.
  - 근거: Microsoft 지원 문서(영문 UI 기준 경로 `Settings > System > Notifications`, 앱 선택 후 "Play a sound" 토글). 한국어 UI의 정확한 문구는 이 PC에서 화면으로 확인하지 않았다(**실측 미완**).
- **Discord:** 사용자 설정 > 알림 > "데스크톱 알림 활성화"(Enable Desktop Notifications)는 켠 채로 둔다. 같은 화면의 "소리(Sounds)"에서 **"메시지(Message)"** 소리를 끈다. 근거: Discord 공식 블로그. 실제 UI는 로그인이 필요해 보지 않았다(**실측 미완**).
- **카카오톡:** 공식 고객센터 문서를 찾지 못했다. 제3자 자료로는 PC 카카오톡의 알림 켜기/끄기, 채팅방별 알림 끄기, 채팅방별 알림음 지정이 있다. 소리만 끄는 정확한 메뉴 경로는 **미확인**이다(**실측 미완**, 사용자 확인 필요).

## 6. 설계서 §8.2 앱별 판정표

| 기록 항목 | ChatGPT / Codex | Claude | Discord | 카카오톡 |
|---|---|---|---|---|
| 앱·OS 버전 | OpenAI.Codex 26.1002.7124.0 / Win11 10.0.26300 | Claude 2.26454.2.0 / 동일 | 1.0.9261 / 동일 | 26.8.2.5324 / 동일 |
| 알림 경로 | Windows toast(실측: 조회 목록에 존재, 카운터 253) | Windows toast(조회 목록에 존재, 카운터 38) | Windows toast(카운터 156) + 자체 소리(공식 문서상 Sounds 항목, 실측 미완) | **Windows toast 기록 없음**(설정 키 부재) → 자체 창·자체 소리로 추정(미검증) |
| 수신 결과 WinRT | 조회로 감지 **실측**(11:35:24, 11:38:59 실제 알림). 구독 불가 | 조회 목록에 존재(기존 알림). 새 알림 감지는 이번 관찰 창에서 미발생 → 미검증 | 미검증(관찰 창에 알림 없음) | 미지원 추정(toast가 아님), 미검증 |
| 수신 결과 UIA | 격리 앱 접근성 모드 수신 건수 증가로 간접 확인(추정) | 미검증 | 미검증 | 자체 창은 원본 접근성 필터(ShellExperienceHost 계열 창만)에 걸리지 않을 가능성이 높음, 미검증 |
| 원음 끄기 | Windows 앱별 "알림이 도착하면 소리 재생" 끄기. 앱 자체 소리 존재 여부 미확인 | 같음 | Windows 토글 + Discord "Sounds > Message" 끄기 | 앱 내부 알림음 설정(경로 미확인) |
| 구성표 실험 해당 | 앱이 기본 토스트음을 쓰면 `Notification.Default`에 해당(미검증) | 같음 | 일부 해당 가능(자체 소리는 무관) | 해당 없음 추정 |
| 부작용 | 같은 AUMID라 ChatGPT와 Codex를 따로 끌 수 없음 | — | 메시지 소리를 끄면 앱 내부 소리 전체에 적용될 수 있음(통화 소리는 별도 항목, 미확인) | — |
| 복원 | 사용자가 같은 토글을 다시 켬(수동, 자동 복원 대상 아님) | 같음 | 같음 | 같음 |
| 결과 | **부분 지원**(수신 실측, 음소거 실측 미완) | **미검증**(수신 경로만 확인) | **미검증** | **미지원 가능성 높음 / 미검증** |

## 7. T5 — Windows 사운드 구성표 (읽기 전용)

### 7.1 현재 값

- `HKCU\AppEvents\Schemes` 기본값 = `.Default`(구성표 이름 목록: `.Default`=`@mmres.dll,-800`, `.None`=`@mmres.dll,-801`)
- `HKCU\AppEvents\Schemes\Apps\.Default\<이벤트>\.Current` (모두 `.Default` 값과 같음):

| 이벤트 | `.Current` | 용도(추정 포함) |
|---|---|---|
| `Notification.Default` | `C:\Windows\media\Windows Notify System Generic.wav` | **토스트 기본음**(`ms-winsoundevent:Notification.Default`). 토스트 XML에 audio가 없으면 이 소리 |
| `Notification.IM` / `Notification.SMS` | `Windows Notify Messaging.wav` | 앱이 audio src로 지정할 때만 |
| `Notification.Mail` | `Windows Notify Email.wav` | 〃 |
| `Notification.Reminder` | `Windows Notify Calendar.wav` | 〃 |
| `Notification.Looping.Alarm*`, `Notification.Looping.Call*` | `Alarm01~10.wav`, `Ring01~10.wav` | 알람·통화(변경 대상 아님) |
| `Notification.Proximity` | `Windows Proximity Notification.wav` | 근접 공유 |
| `SystemNotification`, `.Default`(기본 경고음) | `Windows Background.wav` | 시스템 경고 — 변경 대상 아님 |
| `MessageNudge` | `Windows Message Nudge.wav` | — |

- `Apps` 아래 앱별 구성표 키는 `.Default`, `Explorer`, `sapisvr`뿐이다. Discord·카카오톡 전용 이벤트는 등록돼 있지 않다.

### 7.2 영향 범위 추정

- `Notification.Default`를 비우면 **audio를 지정하지 않은 모든 앱의 토스트 기본음**이 사라진다. 대상 외 앱도 포함되며, 앱별 제어가 아니다.
- 앱이 `Notification.IM` 등을 지정하거나 자체 소리를 재생하면 영향이 없다. WinRT 리스너 API(`UserNotification.Notification`)에는 audio 정보가 노출되지 않아, 어떤 앱이 어떤 이벤트를 쓰는지 프로그램으로 판정할 수 없다. 청취 실험이 필요하다.
- 원본 앱의 "Windows 기본 소리" 재생 옵션이 이 이벤트를 쓰는 경우에도 영향이 있을 수 있다(미검증).
- 레지스트리 변경이 즉시 반영되는지(브로드캐스트 필요 여부)도 미검증이다.

### 7.3 백업·복원 설계안 (스크립트 작성만, 미실행)

- `scripts/custom/p0/sound-scheme-experiment.ps1`
  - 기본 동작 `-Status`(읽기 전용). `-Backup`은 원래 값과 **값 부재 여부**를 `.p0\sound-scheme-state.json`에 기록한다.
  - `-Apply -ConfirmRegistryChange`는 대상 이벤트(기본 `Notification.Default` 하나)의 `.Current`를 빈 문자열로 바꾼다.
  - `-Restore -ConfirmRegistryChange`는 **현재 값이 Apply가 쓴 값과 같을 때만** 원래 값으로 되돌린다. 사용자가 중간에 바꿨으면 덮어쓰지 않고 경고한다. 원래 값이 없었으면 값을 삭제한다. 복원 후 상태 파일은 보관 이름으로 이동한다.
  - 강제 종료 대비: 다음 시작 시 상태 파일이 남아 있고 `applied=true`이면 "복원 필요"로 표시하는 방식을 제안한다(앱 통합 시).
- 구문 검사만 했다(PowerShell Parser, 오류 0). **실행하지 않았다.**

## 8. 1초 조회 대안 설계 제안 (앱 소스 미수정, 제안)

대상: `src/NotificationsPro/Services/NotificationListener.cs`

1. **구독과 조회를 분리한다.** `StartWinRTListening()`에서 `NotificationChanged +=`를 별도 `try/catch`로 감싼다. 실패하면 HRESULT를 진단 상태(제안 이름 `SubscriptionState`)에 기록하고 예외를 바깥으로 던지지 않는다. `_isRunning=true`는 등록·시드가 끝난 뒤로 옮긴다.
2. **조회 주기:** 구독이 성공하면 기존처럼 이벤트+보조 조회(2초)를 쓴다. 구독이 실패하면 **1초 조회 전용**(제안 상수 `PollOnlyInterval = 1s`)으로 간다. 타이머 틱은 기존 `_isPolling` 가드로 중첩을 막는다. 5초 조회 타임아웃을 두고, 타임아웃 시 그 틱을 건너뛴다.
3. **기준 집합:** 기존 `SeedAndStartPollingAsync()`의 시드를 유지한다(시작·재시도 때 존재하던 알림 소급 처리 없음).
4. **이벤트 중복 키:** `uint Id` 대신 `(AUMID 또는 "unknown", Id, CreationTime)`을 쓴다. ID 재사용에 대비해 PS1과 같은 규칙을 쓰고, 현재 목록에 없는 키는 `IntersectWith`로 정리한다(5000/1000 트림 대체). 구독이 살아 있는 패키지 실행에서도 이벤트와 조회가 같은 키를 공유해야 한 번만 처리된다.
5. **권한 철회 구별:** 매 틱 `GetAccessStatus()`를 확인한다. `Allowed`가 아니면 "새 알림 없음"이 아니라 "권한 철회/거부" 상태로 표시하고 조회를 멈춘다. 접근성 자동 전환 여부는 기존 캡처 모드 정책을 따른다.
6. **AppInfo 예외(풍선 팁, E_NOTIMPL) 처리:** `ExtractAndForwardWinRT`에서 AUMID/표시 이름을 각각 안전하게 읽는다. 실패하면 앱 식별 "unknown"으로 전달할지, 지금처럼 버릴지를 결정해야 한다(사용자/검토자 결정 요청).
7. **모드 정책(Auto):** "조회 성공 + 구독 실패"이면 접근성으로 내려가지 않고 `WinRT (polling)` 모드를 유지한다. AUMID를 얻을 수 있어 R05 앱 매칭에 유리하다. 조회 자체가 실패할 때만 접근성으로 전환한다. 진단·트레이 문구는 `WinRT (events)` / `WinRT (polling 1s)` / `Accessibility`로 나눈다.
8. **테스트:** 순수 로직(키 생성·신규 판정·정리·권한 상태 분기)을 작은 클래스로 분리해 `NotificationListenerTests`에서 검증하는 것을 제안한다(제안 클래스명 `WinRtPollTracker`, 기존에 없음).
9. 성능 참고: 조회 1회 약 250ms(25건 기준)이고 1초 주기에서 누락·중첩은 관찰되지 않았다(20~900회 조회, 오류 0). CPU 측정은 하지 않았다.

## 9. 기존 테스트가 실제 AppData를 수정하는 문제 (P0 이전 발생, 중대)

- 근거:
  - `tests/NotificationsPro.Tests/QueueManagerTests.cs`의 `CreateSettings()`, `AccessibilityTests.cs` 135행이 인자 없는 `new SettingsManager()`(실제 `%AppData%\NotificationsPro`)를 쓴다.
  - `QueueManager.Pause/Resume/MuteApp/UnmuteApp`은 `_settingsManager.Apply()` → `Save()`를 호출하므로 테스트가 실제 파일에 쓴다.
  - `IconServiceTests`·`BackgroundImageServiceTests`는 실제 관리 폴더 옆에 `*-escape` 폴더를 만든다.
- 현재 실제 파일 상태: `NotificationDuration=60`, `MaxVisibleNotifications=3`, `AnimationDurationMs=0`(테스트값), `SoundEnabled=false`, `PerAppSounds={}`. 테스트 이전에 파일이 있었는지, 사용자 설정이 있었는지는 **알 수 없다.**
- 원본 앱(PID 38504, 10:18 시작)은 테스트 이전 값을 메모리에 들고 있을 가능성이 높다. **원본 앱을 재시작하면 테스트값이 로드된다.** 복구 방법(원본 앱에서 설정을 한 번 저장해 메모리 값으로 되쓰기, 프로필/내보내기 백업에서 복원 등)은 사용자가 결정한다. 이번 P0에서는 아무것도 변경하지 않았다.
- 재발 방지: 이번 P0에서는 `dotnet test`를 다시 실행하지 않았다. P1 착수 전에 테스트의 기본 경로 사용을 임시 폴더로 격리하는 작업을 제안한다(테스트 코드 변경이라 승인 필요).

## 10. T6 — 포크 식별자 제안 (결정은 사용자, 모두 제안이며 현재 존재하지 않음)

원본 값(확인): 제품명 `Notifications Pro`, exe `NotificationsPro.exe`, AppData `%AppData%\NotificationsPro`, 언패키지 AUMID `NotificationsPro.App`, 패키지 AUMID `NotificationsPro_h8abfdfjdvdc6!App`, StartupTaskId `NotificationsProStartupId`, mutex 없음.

| 항목 | A안: 파생 명시 | B안: 한국어 포크 | C안: 별도 브랜드 |
|---|---|---|---|
| 제품명 | Notifications Pro Custom | Notifications Pro KR | AlertVoice (알림보이스) |
| exe | `NotificationsProCustom.exe` | `NotificationsProKR.exe` | `AlertVoice.exe` |
| AppData | `%AppData%\NotificationsProCustom` | `%AppData%\NotificationsProKR` | `%AppData%\AlertVoice` |
| mutex | `Local\NotificationsProCustom.SingleInstance` | `Local\NotificationsProKR.SingleInstance` | `Local\AlertVoice.SingleInstance` |
| AUMID | `rudals252.NotificationsProCustom` | `rudals252.NotificationsProKR` | `rudals252.AlertVoice` |
| 스케줄러 작업 | `NotificationsProCustom-Logon` | `NotificationsProKR-Logon` | `AlertVoice-Logon` |

- 추천은 A안이다. P0 임시 작업 이름과 이어지고, GPL 파생 고지와 원본 구별이 모두 명확하다.
- 함께 바꿔야 할 지점(제안): `ShouldIgnoreCapturedNotification`의 자기 앱 이름 필터(현재 "Notifications Pro"/"NotificationsPro"). 포크 이름으로 바꾸면 원본 앱의 알림은 더 이상 무시되지 않는다. 이를 유지할지는 결정 사항이다. 그 밖에 csproj `Product/AssemblyTitle`, 언패키지 AUMID 문자열, 4개 AppData 경로, 첫 실행 풍선 문구도 바꿔야 한다. 단일 인스턴스 mutex는 원본에 없으므로 R08 설계(§9.2)에 맞춰 새로 추가해야 한다.

## 11. 자동 검증 vs 실제 Windows 검증 구분

| 구분 | 내용 |
|---|---|
| 자동(빌드·정적) | 프로브 빌드 경고 0·오류 0. 격리 앱 복사본 빌드 경고 0·오류 0. PS 스크립트 6개 구문 검사 오류 0. 앱 소스 코드 읽기 분석 |
| 실제 Windows | T1 조회·구독·1초 조회(로컬 토스트 5건, 실제 Codex 알림). T2 격리 앱 실행·수신 모드·로컬 토스트 수신. T3 작업 등록·XML·세션·종료 코드·재시작 미발생·고아 프로세스·삭제. T4·T5 레지스트리 읽기·StartApps·AppxPackage |
| 미실행 | `dotnet test` 재실행(실제 AppData 오염 위험), 구성표 변경, 레지스트리·설정 변경, 재로그인·재부팅, 앱 내부 UI 확인 |

## 12. 미완료·사용자 수동 검증 필요

1. Windows 설정 > 개인 정보 > 알림 접근 **철회 → 프로브 재실행**(`P0Probe.exe probe --seconds 20`). 이후 다시 허용. 권한 철회 시 `GetAccessStatus`·조회 결과가 어떻게 나오는지 확인한다.
2. **로그온 트리거 작업으로 재로그인·재부팅** 후 트레이·리스너 준비 확인. 포크 식별자 확정 후 실제 exe 직접 실행 작업으로 한다.
3. ChatGPT/Codex, Claude, Discord 각각 **"알림이 도착하면 소리 재생" 끄기 → 실제 알림에서 원음 없음 + 배너·수신 유지** 확인. 토글 전후로 `snapshot-notification-settings.ps1`을 실행해 값 이름을 기록한다.
4. Discord "Sounds > Message" 끄기, 카카오톡 앱 내부 알림음 끄기의 실제 메뉴·효과 확인. 카카오톡 알림이 Windows toast인지 자체 창인지 화면으로 확인한다.
5. Claude·Discord 새 알림의 WinRT 조회 감지(이번 관찰 창에 해당 앱 알림 없음).
6. 사운드 구성표 실험 실행 여부 승인(§7.3 스크립트).
7. 72시간 이상 연속 실행, 정상 "Exit" 메뉴 종료 후의 `LastTaskResult`.

## 13. 검토자/사용자 결정 요청

1. **원본 `settings.json` 테스트값 오염 복구 여부와 방법**(§9). 원본 앱 재시작 전에 결정해야 한다.
2. **언패키지 수신 방식:** "구독 실패 시 WinRT 1초 조회 유지(접근성으로 내려가지 않음)"를 P1 기본 정책으로 채택할지(§8-7).
3. **R08 선택 재시작:** 스케줄러 `RestartOnFailure`가 비정상 종료 코드에서 동작하지 않았다. 대안(반복 트리거 등)을 검토할지, 선택 기능을 보류할지.
4. **포크 식별자** A/B/C안 선택(§10)과 자기 앱 이름 필터 처리.
5. **ChatGPT와 Codex가 같은 AUMID** → 앱별 WAV를 하나로 묶을지, 표시 이름 등 다른 구별 수단을 조사할지.
6. **AUMID를 읽을 수 없는 알림(풍선 팁)** 처리: unknown으로 전달할지, 버릴지.
7. 테스트의 실제 AppData 사용을 임시 폴더로 격리하는 테스트 코드 수정 승인(P1 선행 제안).
8. 이 PC의 UAC 비활성(EnableLUA=0) 상태에서는 모든 실행이 관리자 권한이다. 이를 배포 문서에 명시할지.

## 14. 변경 파일과 산출물

- 추가: `tools/P0Probe/P0Probe.csproj`, `tools/P0Probe/Program.cs`
- 추가: `scripts/custom/p0/build-isolated-app.ps1`, `run-isolated-app.ps1`, `read-tray-status.ps1`, `register-temp-task.ps1`, `snapshot-notification-settings.ps1`, `sound-scheme-experiment.ps1`(미실행)
- 추가: `docs/custom/P0-baseline-verification.md`(이 문서)
- 수정: `.gitignore`(`.p0/` 추가)
- 로컬 전용(gitignored): `.p0/`(격리 AppData, 격리 빌드 복사본, 프로브 JSONL 로그, 작업 XML, 래퍼 로그, 알림 설정 스냅샷). 프로브 로그에는 AUMID·ID·시각만 있다.

## 15. 참고

- [WindowsAppSDK #6172 — 언패키지 NotificationChanged 0x80070490](https://github.com/microsoft/WindowsAppSDK/issues/6172) (사용자 보고, 공식 답변 없음, 2026-10-09 열람)
- [Windows 알림 설정(Microsoft 지원)](https://support.microsoft.com/en-us/windows/experience/notifications-and-do-not-disturb-in-windows)
- [Discord 데스크톱 알림 관리(공식 블로그)](https://discord.com/blog/how-to-manage-your-discord-desktop-notifications)
- [작업 스케줄러 RestartInterval](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-restartinterval), [ExecutionTimeLimit](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-executiontimelimit)
- 카카오톡 PC 알림 설정: 공식 고객센터 문서 미확인. 제3자 자료만 확인(예: [extrememanual.net 채팅방별 알림음](https://extrememanual.net/48980))
