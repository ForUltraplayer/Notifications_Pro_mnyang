<!-- @file P1A-identity-listener.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro 커스텀 포크 P1-A(테스트 AppData 격리, 포크 식별자·단일 인스턴스, 언패키지 WinRT 리스너 1초 조회) 구현·검증 보고서. -->

# P1-A 구현 보고서: 테스트 격리 + 포크 식별자 + 언패키지 WinRT 리스너

- 작성일: 2026-10-09 (Asia/Seoul)
- 구현 담당: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: 사용자(rudals252)
- 기준: 설계서 v1.1 §6, §9, §11.3, §12, §14.1, §16.2. P0 보고서 §2, §8, §9, §10
- 저장소: `F:\dev\notifications-pro-custom`, 브랜치 `custom/base`
- 기준 커밋: 작업 시작 시 HEAD `47d3398`. 작업 중 P0 산출물 커밋 `ce6b5e7`(작성자 ForUltraPlayer, Co-Authored-By Claude Fable 5.1, 13:39)이 추가됐다. 이 커밋은 이번 변경 파일을 건드리지 않았다. **이번 작업은 commit하지 않았다**(작업 트리 변경만)
- 범위 밖(손대지 않음): `src/NotificationsPro.Package/**`, ai-tts 폴더, 원본 설치 앱, PS1 감시기, `%LocalAppData%\Packages\NotificationsPro_*`, 레지스트리 값 직접 변경, UI 문자열 리소스 추출, P1-B(스키마 v6·이관·정책/음성 경로 분리)

## 0. 결론

1. **테스트가 더 이상 실제 `%AppData%`에 쓰지 않는다.** 테스트 어셈블리의 모듈 초기화가 앱 데이터 루트를 `%TEMP%\NotificationsProKR-Mnyang.Tests\<guid>\AppData`로 바꾸고, 종료 시 지운다. `dotnet test` 실행 전·후 모두 `%AppData%\NotificationsPro`와 `%AppData%\NotificationsProKR-Mnyang`가 없었다. 임시 폴더도 남지 않았다.
2. **포크 식별자를 적용했다.** exe `NotificationsProKR-Mnyang.exe`, AppData `%AppData%\NotificationsProKR-Mnyang`, 언패키지 AUMID `rudals252.NotificationsProKR.Mnyang`, mutex `Local\NotificationsProKR-Mnyang.SingleInstance`, 표시명 `Notifications Pro KR-Mnyang`이다. 상수는 새 파일 `Helpers/ForkIdentity.cs` 한 곳에 모았다. 네임스페이스와 `NotificationsPro.csproj` 파일명은 그대로다.
3. **단일 인스턴스가 동작한다(실측).** 두 번째 실행은 175ms 만에 종료 코드 0으로 끝났다. 기존 인스턴스에는 설정창(`Notifications Pro Settings`)이 열렸다. CLI 인자 전달은 P4로 미뤘다.
4. **언패키지 실행이 접근성으로 떨어지지 않고 `WinRT (polling 1s)`로 동작한다(실측).** 트레이 툴팁은 `Listening via WinRT (polling 1s)`였다. 로컬 테스트 토스트 2건을 보내자 `0 captured` → `2 captured`로 바뀌었다. 조회 번호 증가로 1초 주기도 확인했다. 구독 실패(`0x80070490`)는 진단 상태에 기록된다.
5. **빌드 경고 0·오류 0, 테스트 314개 전부 통과**(기준 285 → 314, +29). 아직 실제 Windows에서 확인하지 못한 것: 권한 철회·재허용 분기, 구독 성공(이벤트) 경로, `Unknown` 앱 전달·자기 풍선 필터. 단위 테스트로만 검증했다(§5).

## 1. T0 — 테스트의 실제 AppData 사용 제거

### 1.1 원인 (P0 §9 재확인)

- 인자 없는 `new SettingsManager()`: `QueueManagerTests.CreateSettings()`, `AccessibilityTests`(1곳), `UxPolishTests`(2곳).
- 인자 없는 `ThemeManager`/`ProfileManager`: `SettingsViewModel` 생성자가 `profileManager ?? new ProfileManager()`, `themeManager ?? new ThemeManager()`를 쓴다. `SettingsViewModelRegressionTests` 대부분이 이 경로를 탄다.
- `ManagedAssetPathHelper.GetRoot(...)` 기반 경로: `SettingsManagerTests`, `ThemeTests`, `ProfileManagerTests`, `AppSettingsAssetPathHelperTests`, `IconServiceTests`·`BackgroundImageServiceTests`(관리 폴더와 그 옆 `*-escape` 폴더 생성).

### 1.2 변경

| 파일 | 내용 |
|---|---|
| `src/NotificationsPro/Helpers/ForkIdentity.cs` (신규) | `AppDataRoot` 속성(기본 `%AppData%\NotificationsProKR-Mnyang`). `internal static SetAppDataRootOverrideForTesting(string?)`로 재지정한다. 기존 `InternalsVisibleTo("NotificationsPro.Tests")`를 그대로 쓴다 |
| `Helpers/ManagedAssetPathHelper.cs` | `static readonly AppDataRoot` 제거 → `GetRoot()`가 `ForkIdentity.AppDataRoot`를 그때그때 참조 |
| `Services/SettingsManager.cs`, `ThemeManager.cs`, `ProfileManager.cs` | 인자 없는 생성자의 기본 경로를 `ForkIdentity.AppDataRoot` 기준으로 변경. 경로를 받는 생성자는 그대로 |
| `tests/.../TestAppDataIsolation.cs` (신규) | `[ModuleInitializer]`로 테스트 시작 전에 루트를 `Path.GetTempPath()\NotificationsProKR-Mnyang.Tests\<guid>\AppData`로 재지정한다. `ProcessExit`에서 세션 폴더를 지우고, 상위 폴더가 비면 그것도 지운다. `CreateSettingsManager()`는 고유 임시 폴더에 묶인 `SettingsManager`를 돌려준다 |
| `QueueManagerTests`, `AccessibilityTests`, `UxPolishTests` | `new SettingsManager()` 4곳 → `TestAppDataIsolation.CreateSettingsManager()`. assert는 바꾸지 않았다 |
| `tests/.../TestAppDataIsolationTests.cs` (신규) | 테스트 중 `AppDataRoot`·`GetRoot()`가 임시 폴더 아래이고 실제 `%AppData%` 아래가 아님을 확인 |

설계 결정:

- **생성자 주입을 모든 서비스에 추가하지 않고 정적 재지정 훅 1개를 택했다.** `IconService`·`SoundService`·`BackgroundImageService`는 정적 필드로 루트를 잡는다. `SettingsViewModel`은 기본 매니저를 직접 만든다. 이것들을 모두 주입식으로 바꾸면 변경 범위가 커지고 upstream과 충돌이 늘어난다. 정적 서비스 필드는 지연 초기화되므로, 모듈 초기화가 먼저 실행된 뒤에 재지정된 루트를 읽는다.
- 재지정 훅은 `internal`이라 앱 코드에서 공개 API로 노출되지 않는다.

### 1.3 검증 (자동, 실제 파일 시스템 확인 포함)

실행 전후 확인은 `Environment.GetFolderPath(ApplicationData)` 기준이다(P0 §3.1: 앱이 이 API를 쓰므로 `$env:APPDATA`가 아니라 이 값으로 확인).

```text
# T0 변경 직후(포크 식별자 적용 전 코드 + 격리) 1회차
before: NP=False KR=False
통과!  - 실패:     0, 통과:   286, 건너뜀:     0, 전체:   286, 기간: 2 s - NotificationsPro.Tests.dll (net8.0)
after: NP=False KR=False
(temp 세션 폴더 잔여 없음)

# T1·T2 포함 최종
before: NP=False KR=False
통과!  - 실패:     0, 통과:   314, 건너뜀:     0, 전체:   314, 기간: 1 s - NotificationsPro.Tests.dll (net8.0)
after: NP=False KR=False
temp leftovers: False
```

- NP = `%AppData%\NotificationsPro`, KR = `%AppData%\NotificationsProKR-Mnyang`.
- 참고: P0 보고서 시점에 있던 `%AppData%\NotificationsPro`(테스트값으로 오염된 settings.json)는 **이번 작업 시작 시점에 이미 없었다**(`Test-Path` = False). 이번 작업에서 지우지 않았다. 누가 언제 지웠는지는 확인하지 못했다.

## 2. T1 — 포크 식별자 + 단일 인스턴스

### 2.1 식별자 적용 위치

| 항목 | 원본 | 포크 | 적용 위치 |
|---|---|---|---|
| exe | `NotificationsPro.exe` | `NotificationsProKR-Mnyang.exe` | csproj `<AssemblyName>` |
| RootNamespace | (프로젝트명 기본) | `NotificationsPro` 명시 | csproj. AssemblyName 변경이 리소스·네임스페이스 기본값에 번지지 않게 고정 |
| Product / AssemblyTitle | `Notifications Pro` | `Notifications Pro KR-Mnyang` | csproj |
| AppData | `%AppData%\NotificationsPro` | `%AppData%\NotificationsProKR-Mnyang` | `ForkIdentity.AppDataRoot` ← ManagedAssetPathHelper/SettingsManager/ThemeManager/ProfileManager |
| 언패키지 AUMID | `NotificationsPro.App` | `rudals252.NotificationsProKR.Mnyang` | `App.xaml.cs` `SetCurrentProcessExplicitAppUserModelID` |
| mutex | 없음 | `Local\NotificationsProKR-Mnyang.SingleInstance` | `App.OnStartup` |
| 설정창 요청 이벤트 | 없음 | `Local\NotificationsProKR-Mnyang.ShowSettings` (구현 중 추가한 이름) | `App.OnStartup` |
| 스케줄러 작업 | — | `NotificationsProKR-Mnyang-Logon` | 상수만(등록 안 함, P4) |
| 트레이 이름·툴팁 접두어·첫 실행 풍선 | `Notifications Pro` | `Notifications Pro KR-Mnyang` | `App.xaml.cs` |
| 자기 앱 알림 필터 | 원본 2개 이름 | 원본 2개 + `Notifications Pro KR-Mnyang`, `NotificationsProKR-Mnyang` | `ForkIdentity.SelfAppNames` ← `ShouldIgnoreCapturedNotification` |

- 원저작권: csproj·기존 파일의 원 표기는 그대로 두었다. 신규 파일 `ForkIdentity.cs`에는 upstream 출처와 GPL v3 표기를 넣었다. 기존 파일에는 `@file` 주석을 추가하지 않았다.
- About 대화상자의 `Listener:` 항목은 `IsAccessGranted ? "WinRT" : "Accessibility"` 대신 `ListenerMode`를 쓰게 바꿨다(T2 문구 구분과 일치시키기 위함). 오류 대화상자 제목, "About Notifications Pro" 메뉴 같은 나머지 UI 문자열은 바꾸지 않았다. 문자열 정리는 P3 리소스 추출 때 한다.
- **AUMID에 하이픈을 쓰지 않은 근거:** Microsoft 문서 [Application User Model IDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids)는 형식을 `CompanyName.ProductName.SubProduct.VersionInformation`(각 부분 PascalCase, 최대 128자, 공백 불가)으로 안내한다. 하이픈 허용 여부는 명시하지 않는다. 그래서 문서 형식에 정확히 맞는 점 구분 이름을 썼다. 실측 결과, 첫 실행 풍선 표시 후 Windows가 `HKCU\…\Notifications\Settings\rudals252.NotificationsProKR.Mnyang` 키를 자동 생성했다(`PeriodicNotificationCount=1`). 즉 이 AUMID가 알림 식별자로 등록됐다.

### 2.2 단일 인스턴스 (신규 `Helpers/SingleInstanceGuard.cs`)

- `OnStartup` 맨 앞(`base.OnStartup` 직후, 설정 로드·AUMID 설정·리스너 생성 전)에서 `new Mutex(initiallyOwned: true, name, out createdNew)`를 잡는다.
- 이미 있으면 두 번째 실행이다. 이름 있는 `EventWaitHandle`(AutoReset)을 열어 `Set()`한다. 기존 인스턴스가 이벤트를 만들기 전일 수 있어 100ms 간격으로 최대 10회 재시도한다. 그 뒤 `Shutdown(0)`으로 끝낸다. 설정 파일·리스너·트레이를 건드리지 않는다.
- 주 인스턴스는 `ThreadPool.RegisterWaitForSingleObject`로 이벤트를 기다린다. 신호가 오면 `Dispatcher.BeginInvoke(ShowSettings)`를 호출한다. 등록 시점은 트레이 생성 직후다.
- `OnExit`에서 guard를 정리한다(이벤트 해제, mutex 핸들 닫기).
- **P4로 미룬 것:** 설계 §9.2 "기존 CLI 인자는 기존 인스턴스에 전달해 한 번만 실행"은 구현하지 않았다. 지금은 두 번째 실행에 `--pause` 같은 인자가 있어도 설정창만 열리고 **인자는 버려진다**(§6 K12 영향).
- **R08 설계 메모(미구현):** 두 번째 실행의 종료 코드 0은 "주 인스턴스가 아님"을 뜻한다. 비정상 종료 판정과는 무관하다. R08의 실행 기록은 반드시 mutex를 획득한 뒤에만 쓰고, 보조 실행은 기록을 만들거나 지우지 않아야 한다. 이번 구현은 mutex 획득 실패 시 기록 경로에 도달하기 전에 반환하므로, 이 순서와 충돌하지 않는다.

## 3. T2 — 언패키지 WinRT 리스너 수정

### 3.1 변경 (`Services/NotificationListener.cs`, 신규 `Services/WinRtPollTracker.cs`)

| 항목 | 이전 | 이후 |
|---|---|---|
| 시작 순서 | `_isRunning=true` → `NotificationChanged +=` (예외 시 시드·조회까지 포기, 접근성 전환) → 비동기 시드 | **시드(기준 집합) → 구독 시도(별도 try/catch) → 모드 결정·타이머 시작 → `_isRunning=true`**. `InitializeAsync`가 이 과정을 await한다 |
| 구독 실패 | 접근성 전환 | `SubscriptionState = "Failed COMException 0x80070490"` 형식으로 기록 → **1초 조회 전용 모드** |
| 조회 주기 | 2초 고정 | 구독 성공: 이벤트 + 보조 조회 2초(기존 유지). 구독 실패: 1초 |
| 조회 타임아웃 | 없음 | 5초(`Task.WhenAny` + `operation.Cancel()`). 타임아웃이면 그 틱만 건너뛰고 상태에 횟수를 표시 |
| 중첩 방지 | `_isPolling` | 유지. 시작·중지·전환마다 세션 번호(`_winRtSession`)를 올려, 늦게 끝난 이전 조회가 새 세션 상태를 건드리지 않게 함 |
| 중복 키 | `uint Id`, 5000 초과 시 상위 1000개 | `(AUMID 또는 "Unknown", Id, CreationTime)`. 현재 목록에 없는 키 정리(IntersectWith와 같은 효과). 이벤트·조회가 같은 추적기를 공유 |
| 권한 확인 | 시작 시 1회 | **매 틱 `GetAccessStatus()`**. `Allowed`가 아니면 `WinRT (access revoked)` 상태로 바꾸고 조회를 멈춘다(타이머는 권한 확인만 계속). 다시 `Allowed`가 되면 기준 집합을 새로 시드하고 재개한다(그동안 쌓인 알림은 소급하지 않음) |
| 조회 실패(예외) | 접근성 전환 | 유지(확정 정책: 조회 자체가 실패할 때만 접근성 전환) |
| AppInfo 예외 | `catch {}`로 알림 전체 폐기 | AUMID·표시 이름을 각각 안전하게 읽음. 표시 이름이 없으면 앱 이름 `"Unknown"`으로 전달 |
| 모드 문구 | `WinRT` / `Accessibility` / `Initializing` | `WinRT (events)` / `WinRT (polling 1s)` / `WinRT (access revoked)` / `Accessibility` / `Initializing` (코드 내 문자열, 리소스 추출은 P3) |
| 이벤트 핸들러 해제 | 무조건 `-=` | 구독에 성공한 경우에만 `-=`(try/catch) |

- 이벤트와 조회의 경합 처리: 조회 요청이 진행 중일 때 이벤트로 새 알림이 먼저 처리될 수 있다. 그러면 그 알림이 빠진 오래된 스냅샷이 키를 지워, 다음 조회에서 같은 알림을 다시 처리할 수 있다. 이를 막으려고 `WinRtPollTracker`는 키마다 기록된 스냅샷 세대를 저장한다. 그리고 해당 스냅샷 요청 이후에 기록된 키는 정리하지 않는다(테스트 `ProcessSnapshot_KeepsEventKeyRecordedWhileSnapshotWasInFlight`).
- 자기 앱 필터 보강: 풍선 팁은 AppInfo가 없어 `Unknown`으로 들어온다. 이 포크의 첫 실행 풍선도 마찬가지다. 그래서 앱 이름이 `Unknown`이거나 비어 있으면 **제목**이 자기 앱 이름(원본·포크)인지 확인해 무시한다. 근거는 풍선 제목이 곧 제품명이라는 점이다. 이것 없이 `Unknown` 전달만 하면, 첫 실행 때 자기 풍선이 오버레이에 카드로 뜰 수 있다. 다른 앱이 `Unknown`이 아닌 이름으로 들어오면 제목이 같아도 무시하지 않는다(테스트로 확인).
- 상태 문구에는 개수·조회 번호·HRESULT만 넣는다. 알림 제목·본문은 넣지 않는다. 디스크 기록이나 로그 경로는 추가하지 않았다.

### 3.2 `WinRtPollTracker` (순수 로직, WinRT 객체 비의존)

- `CreateKey(aumid, id, creationTime)`: AUMID가 비거나 null이면 `"Unknown"`.
- `Seed(existing)`: 기준 집합 교체(소급 없음). `Reset()`.
- `TryMarkSeen(key)`: 이벤트 경로. 처음일 때만 true.
- `BeginSnapshot()` / `ProcessSnapshot(token, current)`: 조회 경로. 새 키 반환 + 목록 밖 키 정리(요청 이후 기록된 키는 보존).
- `EvaluateAccess(status, currentlyRevoked)` → `Poll` / `EnterRevoked` / `StayRevoked` / `Resume`.

### 3.3 추가 테스트 (+29, 모두 통과)

| 파일 | 개수 | 내용 |
|---|---|---|
| `WinRtPollTrackerTests.cs` (신규) | 18 | Unknown 키(3), 키 구별, 시드 소급 없음, 신규 1회 판정, 이벤트→조회 1회, 조회→이벤트 1회, 목록 밖 정리, 진행 중 스냅샷 경합, Unknown 앱 보고, Reset, 권한 분기(6) |
| `SingleInstanceGuardTests.cs` (신규) | 4 | mutex 두 번째 획득 실패·해제 후 재획득, 신호 전달 → 콜백 호출, 주 인스턴스 없을 때 false, 식별자 상수·실제 AssemblyName 일치 |
| `NotificationListenerTests.cs` (기존 수정) | +6 | 자기 앱 이름에 포크 이름 2개 추가, Unknown/빈 앱 + 자기 제목 무시(3), Unknown 외부 알림·다른 앱 이름 + 같은 제목은 무시 안 함(1) |
| `TestAppDataIsolationTests.cs` (신규) | 1 | 테스트 AppData 루트가 임시 폴더 아래인지 |

## 4. 실행한 명령과 결과 원문

### 4.1 빌드·테스트 (자동)

```text
> dotnet build src/NotificationsPro/NotificationsPro.csproj --no-incremental
    경고 0개
    오류 0개
  NotificationsPro -> ...\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.dll

> dotnet build tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj --no-incremental
    경고 0개
    오류 0개

> dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj
통과!  - 실패:     0, 통과:   314, 건너뜀:     0, 전체:   314, 기간: 1 s - NotificationsPro.Tests.dll (net8.0)
```

실패 기록: 빌드·테스트 실패는 없었다. 작업 도구 문제(셸 heredoc에 작은따옴표가 섞이면 `unexpected EOF while looking for matching '`가 나는 문제)가 2회 있었다. 편집 스크립트를 파일로 저장해 실행하는 방식으로 바꿔 해결했다. 소스 결과에는 영향이 없다.

### 4.2 실제 Windows 실행 (언패키지 exe)

실행 전 상태(조회만, 종료·변경하지 않음):

```text
Id    ProcessName      Path                                                                                       StartTime            SessionId
46104 NotificationsPro C:\Program Files\WindowsApps\NotificationsPro_1.1.10.37_neutral__h8abfdfjdvdc6\NotificationsPro.exe  2026-10-09 오후 1:35:34  1
PS1 감시기: ProcessId 8252, CreationDate 2026-10-09 오전 8:34:32, SessionId 1
%AppData%\NotificationsPro = False, %AppData%\NotificationsProKR-Mnyang = False
```

1) 실행 + 트레이 상태(`scripts/custom/p0/read-tray-status.ps1 -OpenOverflow`, 기술 상태만 읽음):

```text
started PID=9460 exited=False
name : Notifications Pro KR-Mnyang Notifications Pro KR-Mnyang — Listening via WinRT (polling 1s) || WinRT (polling 1s) — 0 captured, 27 in system (poll #5); events:
name : Notifications Pro Notifications Pro — Listening via WinRT || WinRT — 5 captured, 27 in system (poll #397)
```

(툴팁 127자 제한 때문에 `events: Failed COMException 0x80070490` 뒷부분이 잘렸다. 전체 문구는 트레이 메뉴 상태 항목과 설정창 진단에 표시된다.)

2) 로컬 테스트 토스트 2건(`tools/P0Probe ... probe --seconds 16 --toasts 2 --skip-request --out .p0\p1a-toasts.jsonl`, 무음·일반 기술 문구, 종료 시 자기 토스트만 삭제):

```text
probe exit=0
{"evt":"subscribe","data":{"ok":false,"error":{"type":"COMException","hresult":"0x80070490","message":""}}}
{"evt":"toast-sent","data":{"i":1,...,"notifier_setting":"Enabled"}}   04:49:05.287Z
{"evt":"toast-sent","data":{"i":2,...,"notifier_setting":"Enabled"}}   04:49:10.304Z
summary: polls 16, poll_errors 0, poll_new_from_toast_aumid 2
```

이후 트레이:

```text
name : Notifications Pro KR-Mnyang Notifications Pro KR-Mnyang — Listening via WinRT (polling 1s) || WinRT (polling 1s) — 2 captured, 26 in system (poll #38); events
name : Notifications Pro Notifications Pro — Listening via WinRT || WinRT — 7 captured, 26 in system (poll #414)
```

- 포크 `0 → 2 captured`(원본도 같은 시간에 `5 → 7`). 테스트 토스트 2건 수신으로 판단한다. 같은 시간대에 실제 앱 알림이 섞였을 가능성은 배제하지 못했다. 원본 증가분이 같은 2건이고 프로브의 신규 감지도 테스트 토스트 2건뿐이라 가능성은 낮다.
- 포크 앱의 개별 감지 지연은 앱에 시각 기록이 없어 측정하지 않았다. 같은 시간에 프로브의 1초 조회는 229ms·263ms에 감지했다.

3) 두 번째 인스턴스(mutex):

```text
primary windows before:
  [Window] 'Notifications Pro Overlay' offscreen=False
second PID=42652 exited=True exitCode=0 elapsedMs=175
primary alive: True
primary windows after:
  [Window] 'Notifications Pro Overlay' offscreen=False
  [Window] 'Notifications Pro Settings' offscreen=False
실행 중 포크 프로세스: 9460 하나뿐
```

4) 정리:

```text
경로 확인(F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe) 후 Stop-Process -Id 9460 -Force → stopped: True
원본 46104 실행 중 유지, PS1 감시기 8252 유지
%AppData%\NotificationsPro = False
```

부수 효과(예상된 것):

- `%AppData%\NotificationsProKR-Mnyang\settings.json`(6253 bytes, 13:48:44)이 생성됐다. 포크 자체 데이터 폴더이며 지우지 않았다. 이후 `dotnet test` 전후 확인 때 이 폴더는 이미 존재하는 상태가 기준이 된다.
- 첫 실행 풍선 때문에 Windows가 `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\rudals252.NotificationsProKR.Mnyang`을 자동 생성했다(`LastNotificationAddedTime`, `PeriodicNotificationCount=1`). 직접 쓴 값은 아니다. 테스트 토스트로 PowerShell AUMID 키의 카운터도 올라갔다.
- 강제 종료라 트레이에 빈 아이콘 흔적이 남을 수 있다(마우스를 올리면 사라짐).

## 5. 자동 검증 vs 실제 Windows 검증

| 항목 | 자동(단위 테스트·빌드) | 실제 Windows |
|---|---|---|
| 테스트가 실제 AppData에 쓰지 않음 | 격리 테스트 | `dotnet test` 전후 폴더 부재 확인 |
| exe 이름·AssemblyName | 테스트(AssemblyName = 상수) | 빌드 산출물 `NotificationsProKR-Mnyang.exe` 실행 |
| AUMID | — | Windows 알림 설정 키 자동 생성으로 확인 |
| 단일 인스턴스·설정창 요청 | mutex·신호 테스트 | 두 번째 실행 종료 코드 0, 설정창 열림 |
| 구독 실패 → 1초 조회 유지 | — (WinRT 객체가 필요해 단위 테스트 불가) | 트레이 `WinRT (polling 1s)`, 조회 번호 1초 증가 |
| 새 알림 수신 | 추적기 신규 판정 | 로컬 토스트 2건 수신(`2 captured`) |
| 중복 키·이벤트/조회 1회 처리 | 추적기 테스트 | **미검증**(이 PC 언패키지는 구독이 실패해 이벤트 경로가 없음) |
| 권한 철회/거부 → 조회 중지·재허용 재개 | 분기 테스트 | **미검증**(사용자가 Windows 설정을 바꿔야 함) |
| 5초 타임아웃 건너뛰기 | — | **미검증**(타임아웃 재현 수단 없음) |
| AppInfo 실패 → Unknown 전달, 자기 풍선 무시 | 필터 테스트 | **미검증**. 이번 실행의 포크 첫 실행 풍선은 시드 이전에 표시돼 기준 집합에 들어갔거나 목록에 남지 않았다. 프로브 목록의 `(error 80004001)` 1건은 이전 풍선으로 보이며 기준 집합에 포함됐다 |
| 정상 Quit 경로의 mutex 해제 | dispose 테스트 | 미검증(강제 종료만 시험) |

## 6. 보존 계약 영향

| ID | 영향 | 비고 |
|---|---|---|
| K01 WinRT·접근성·진단·재시도 | **변경(의도)**: 언패키지에서 구독 실패 시 접근성 대신 WinRT 1초 조회. 조회 실패 시 접근성 전환, 강제 접근성 모드, 수동 Retry, 시드는 유지. 권한 철회 시 접근성으로 자동 전환하지 않고 정지·표시 | 패키지(구독 성공) 경로는 기존과 같이 이벤트 + 2초 보조 조회. 중복 키만 강화됨. 실제 구독 성공 환경에서는 미검증 |
| K02 기본 알림 제거 | 유지(`RemoveNotification` 호출 위치 동일) | WinRT 조회 모드에서도 동작해야 하나 실측 안 함 |
| K06 앱 필터 | `Unknown` 앱 이름이 새로 들어올 수 있음. 앱별 규칙에서 "Unknown"이 하나의 앱처럼 보임 | 이전에는 같은 알림이 조용히 버려졌다 |
| K11 테마·프로필·가져오기 | 저장 위치만 포크 폴더로 이동. 원본 폴더의 기존 테마·프로필은 자동으로 보이지 않음 | 이관은 P1-B·P5 |
| K12 트레이·CLI | 트레이 표시명 변경. **두 번째 실행의 CLI 인자는 버려짐**(원본은 두 번째 인스턴스가 통째로 떠서 인자를 자기에게 적용) | 인자 전달은 P4 |
| 그 밖 K03–K05, K07–K10, K13 | 코드 변경 없음 | 기존 테스트 통과 |
| 프라이버시 하드 제약 | 위반 없음. 새 상태 문구는 개수·HRESULT·AUMID 없는 모드명만 포함. 디스크 기록 추가 없음 | 추적기는 키(AUMID·ID·시각)만 RAM에 보관 |

## 7. 미완료·P4 이후로 미룬 것

1. CLI 인자를 기존 인스턴스에 전달(§9.2) → P4.
2. 스케줄러 작업 `NotificationsProKR-Mnyang-Logon` 등록·해제 → P4(상수만 있음).
3. R08 실행 기록(주 인스턴스만 기록, 정상 종료 시 정리) → P4. 위 §2.2 메모 참고.
4. 실제 Windows 확인이 남은 항목: 권한 철회·재허용, 패키지(구독 성공) 경로의 이벤트·조회 중복 1회, 타임아웃, Unknown 전달·자기 풍선 필터, 정상 Quit 후 재실행.
5. MSIX 패키지 프로젝트는 손대지 않았다. `Package.appxmanifest`가 `NotificationsPro.exe`를 참조하므로, **AssemblyName 변경 후에는 패키지 빌드가 맞지 않는다**. MSIX를 다시 채택할 때만 함께 정한다(설계 §9.1·§11.3).
6. `docs/PLAN.md`·`STATUS.md`·`CHANGELOG.md`·`REPO_MAP.md`는 이번 지시 범위(코드·보고서)에 없어 갱신하지 않았다.
7. UI 문자열 리소스 추출은 하지 않았다(P3, 별도 커밋 예정).

## 8. 검토자 결정 요청

1. **권한 철회 시 동작:** 지금은 확정 정책("조회 자체가 실패할 때만 접근성 전환")에 따라, 철회되면 WinRT 조회를 멈추고 `WinRT (access revoked)`를 표시한다. 재허용되면 자동으로 다시 시드해 재개한다. 철회된 동안에는 **수신이 멈춘다.** 철회 시 접근성으로 자동 전환할지 결정이 필요하다. 원본은 시작 시 거부되면 접근성으로 전환한다.
2. **자기 풍선 제목 기반 필터:** `Unknown`이거나 빈 앱 이름이면서 제목이 원본·포크 제품명인 알림을 무시한다. 다른 무명 Win32 앱이 같은 제목을 쓰면 함께 무시된다. 이 범위를 허용할지 결정이 필요하다.
3. **표시 이름이 없고 AUMID만 있는 경우:** 지금은 `Unknown`으로 표시한다. AUMID 문자열(예: `OpenAI.Codex_…!App`)을 앱 이름으로 쓸지 결정이 필요하다. AUMID를 쓰면 R05 앱 매칭에는 유리하지만 표시가 길고 기존 앱별 규칙 이름과 다르다.
4. **두 번째 실행 인자 처리 공백(K12):** P4 전까지 인자를 버리는 상태를 허용할지 결정이 필요하다. 대안은 인자가 있으면 기존 동작처럼 실패 안내 후 종료하는 것이다.
5. **포크 데이터 폴더:** 이번 실측으로 생긴 `%AppData%\NotificationsProKR-Mnyang\settings.json`(기본값)을 둘지 지울지 결정이 필요하다.
