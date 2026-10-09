<!-- @file P4-resident-mute.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang P4(로그온 자동 실행·트레이 시작·비정상 종료 안내·CLI 인자 전달·원음 수동 음소거 안내) 구현 보고서: 설계 결정, 명령·결과 원문, 자동/실제 검증 구분, 사용자 수동 검증 체크리스트, 보존 계약 영향, 검토자 결정 요청. -->

# P4 — 상주·로그온 실행·비정상 종료 안내·원음 수동 음소거 안내 보고서

- 기준: HEAD `6040fd8`(P3-B), 브랜치 `custom/base`. **미커밋.**
- 구현: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: rudals252
- 설계 근거: `ai-tts/docs/notifications-pro-integrated-design.md` v1.1 §5.2·§5.3, §8.1·§8.2, §9.1~§9.3, §11.1, §13 P4, §14.1·§14.2. P0 보고서 §4~§6, P1-A 보고서 §2.2, P3 보고서 §2
- 범위 밖(손대지 않음): 사운드 구성표 실험(§8.3), 별도 감시기, `RestartOnFailure`, 레지스트리 쓰기, Discord·카카오톡 설정 파일, 원본 앱·PS1 감시기, `src/NotificationsPro.Package/**`

## 1. 결론

1. **코드 4가지를 구현했고 자동 검증은 통과했다.** 로그온 작업 서비스(Task Scheduler COM), 실행 표식(R08), 이름 있는 파이프를 이용한 CLI 인자 전달(K12), 앱 탭 원래 소리 처리 섹션(R09)이다. 빌드는 경고 0·오류 0이다. 테스트는 **554 → 607**(+53, 검토 반영분 +5 포함)이고 모두 통과했다.
2. **실제 앱 실행 검증은 하지 않았다.** 사용자 포크 인스턴스(PID 7716, `bin\Debug`, 16:16:46 시작)가 실행 중이었다. 지시에 따라 종료하지 않았고 T5 전체(강제 종료→재실행 안내, `--pause` 전달, 임시 스케줄러 작업 등록·XML 확인·삭제)를 **"사용자 인스턴스 실행 중으로 미실시"** 처리했다. 그래서 `ComTaskSchedulerClient`가 실제 COM을 호출하는 경로, `SessionEnding` 정리, 실제 두 프로세스 사이의 파이프 전달은 아직 검증되지 않았다.
3. 대신 실제 빌드 산출물로 **오프스크린 XAML 하네스**를 돌렸다. 숨김 HWND를 썼고 화면 표시·캡처·입력 주입은 없었다. 시스템 탭의 P4 요소 8종과 앱 탭 카드 요소 5종이 AutomationId로 노출된다. 영어·한국어 문구와 상태별 버튼 표시도 맞았고 바인딩 오류는 0이었다(§5.3).
4. **검토 결과를 반영했다(§9).** ① `StartMinimizedToTray` 기본값을 `true`로 바꿔 원본처럼 트레이로 시작한다(K12 보존). 첫 실행은 설계 §5.3대로 설정창을 항상 연다. ② `ManualMuteConfirmations` 저장 키를 "AUMID가 있으면 AUMID, 없으면 표시 이름"으로 바꿨다. 기존 표시 이름 키는 읽기 전용으로만 조회한다. 이 PC 전용 값 지정, Priority 5, 전달 실패 종료 코드 1, 카카오톡 이름 판정은 승인됐다. 단, 사용자의 기존 포크 `settings.json`(v6)에는 이전 빌드가 쓴 `"StartMinimizedToTray": false`가 들어 있다. 그대로 두면 수동 실행 때 설정창이 열린다(§9 추가 결정 A).
5. 실제 사용자 데이터는 바뀌지 않았다. `%AppData%\NotificationsProKR-Mnyang\settings.json`의 SHA-256은 작업 전후 모두 `0C9198D4…CD561A`다. `%AppData%\NotificationsPro`는 생기지 않았다. `run-state.json`도 만들어지지 않았다(앱을 실행하지 않았으므로). 스케줄러 작업 `NotificationsProKR*`는 없다. 레지스트리도 변경하지 않았다.

## 2. T1 — 로그온 작업 (`Services/LogonTaskService.cs` 외)

### 2.1 API 선택

- **Task Scheduler 2.0 COM(`Schedule.Service`)을 late binding(`dynamic`)으로 호출한다.** 추가 패키지나 interop 어셈블리가 필요 없다. `schtasks.exe`·PowerShell `ScheduledTasks` 모듈과 달리 자식 프로세스·출력 파싱·실행 정책에 의존하지 않는다. 오류도 HRESULT로 정확히 받는다.
- 구성: `ITaskSchedulerClient`(조회·XML 등록·삭제·XML 읽기·정책 확인) ← `ComTaskSchedulerClient`(실제) / 테스트용 가짜. 상태 매핑과 XML 생성은 `LogonTaskService`에 둔다.
- 등록: `ITaskFolder.RegisterTask(name, xml, TASK_CREATE_OR_UPDATE=6, null, null, TASK_LOGON_INTERACTIVE_TOKEN=3, null)`. 루트 폴더에 등록하며 주체는 XML의 현재 사용자다.
- 패키지 식별이 있을 때(MSIX)는 기존 `StartupTask` 토글·`SyncStartupTaskState`를 그대로 쓴다. 언패키지일 때 `SyncStartupTaskState`는 바로 반환한다. 설정값으로 OS 작업을 다시 만들지 않는다.

### 2.2 작업 XML (P0 `register-temp-task.ps1` 설정 기반, 래퍼 없음)

| 항목 | 값 | 비고 |
|---|---|---|
| 이름 | `NotificationsProKR-Mnyang-Logon` | `ForkIdentity.LogonTaskName` |
| 트리거 | `LogonTrigger` + `UserId`=현재 사용자(`WindowsIdentity.GetCurrent().Name`) | 이 사용자 로그온만 |
| 주체 | `LogonType=InteractiveToken`, `RunLevel=LeastPrivilege` | |
| 설정 | `ExecutionTimeLimit=PT0S`, `MultipleInstancesPolicy=IgnoreNew`, 배터리 조건 해제, `AllowStartOnDemand=true` | |
| 재시작 | **없음**(`RestartOnFailure` 미포함) | P0에서 동작하지 않음 |
| 동작 | `Exec Command`=실행 중인 exe 전체 경로(`Environment.ProcessPath`), `Arguments=--start-minimized`, `WorkingDirectory`=exe 폴더 | powershell 래퍼 없음 |
| 우선순위 | `Priority=5`(NORMAL) | 승인됨. 기본값 7은 BELOW_NORMAL |

### 2.3 상태 매핑

| 관측 | 상태 | 시스템 탭 문구(ko) |
|---|---|---|
| 작업 있음, `Enabled=true` | 등록됨 | 등록됨: Windows에 로그인하면 트레이로 시작합니다. |
| 작업 있음, `Enabled=false` | 등록됨(비활성) | 작업 스케줄러에서 사용 안 함… 다른 자동 실행 방법을 대신 쓰지 않습니다 |
| 작업 없음 | 미등록 | 미등록: 로그인 시 자동으로 시작하지 않습니다. |
| 작업 없음 + 정책 `Task Creation`≠0, 또는 `E_ACCESSDENIED`/`UnauthorizedAccessException` | 정책 차단 | 정책 차단: … (0x80070005 등) |
| 그 밖의 예외(서비스 중지, COM 오류 등) | 확인 실패 | 확인 실패: 작업 스케줄러 상태를 읽지 못했습니다. (0x…) |
| 등록됨/비활성 + `Command`가 현재 exe와 다름 | 위 상태 + 추가 줄 | 작업이 다른 위치의 실행 파일을 시작합니다… |

- 정책 확인은 `HKLM`/`HKCU\SOFTWARE\Policies\Microsoft\Windows\Task Scheduler5.0`의 `Task Creation` 값을 **읽기만** 한다. 이 값 이름은 그룹 정책 "새 작업 만들기 금지"에 해당한다는 문서 지식에 근거한 **추정이며 이 PC에서 확인하지 않았다.**
- 토글은 "등록됨(사용)"일 때만 켜져 보인다. 비활성 작업은 토글이 꺼진 채로 상태 줄에 표시된다. 앱이 스스로 다시 켜거나 다른 방식(Run 키 등)으로 우회하지 않는다. 사용자가 토글을 직접 켜면 `CREATE_OR_UPDATE`로 덮어써 다시 사용 상태로 만든다(명시적 조작). 등록된 상태(비활성 포함)에서는 "작업 등록 해제" 버튼이 보인다. 설계 §9.3의 "EXE 삭제 전 해제"를 위한 것이다.
- 상태 조회(`GetStatus`)는 아무것도 바꾸지 않는다(테스트로 고정). 작업 정보는 설정에 저장하지 않는다. OS 상태가 유일한 기준이다. 그래서 `AutoStartAtLogon`은 추가하지 않았다. 프로필 가져오기로 OS 등록이 바뀌는 경로가 없다.

### 2.4 시스템 탭 UI (모두 `AutomationId` 부여)

`LogonTaskToggle`, `LogonTaskStatusText`, `LogonTaskRefreshButton`, `LogonTaskRemoveButton`, `StartMinimizedToTrayToggle`, `RestartOnCrashToggle`(비활성·꺼짐 고정, HelpText 포함), `RestartOnCrashNote`("이 버전에서는 제공하지 않습니다…"), `PreviousRunStatusText`. 패키지 실행일 때는 기존 `StartWithWindows` 토글만 보인다.

## 3. T2 — 실행 표식 (`Helpers/RunMarker.cs`)

- **파일:** `%AppData%\NotificationsProKR-Mnyang\run-state.json`(`ForkIdentity.RunMarkerPath`). `settings.json`·themes·profiles와 별개인 로컬 운영 메타데이터다. 프로필·내보내기 경로에 들어가지 않는다.
- **내용:** `{"Format":1,"SessionToken":"<guid>","StartedAt":"<ISO 8601>","Version":"1.1.10.37"}`. 이 4개 키만 둔다(테스트로 고정). 알림 내용·경로·PID는 넣지 않는다. 쓰기는 `AtomicFileWriter`(임시 파일 후 교체)로 한다.
- **시작:** `OnStartup`에서 mutex를 획득하고 `--quit` 조기 종료 판정을 한 **뒤에** `BeginSession`을 호출한다. 파일이 남아 있으면 "이전 실행 미완료"로 판정한다(내용이 손상돼도 미완료로 본다). 그 뒤 새 토큰으로 덮어쓴다. 보조 실행은 mutex 실패 시 이 경로에 오기 전에 반환한다. `RunMarker.BeginForPrimaryInstance(false, …)`는 파일을 읽지도 쓰지도 않는다.
- **정리(`EndSession`):** 파일 토큰이 이번 실행의 토큰과 같을 때만 지운다. 이전 실행의 늦은 정리가 현재 표식을 지우지 못하게 하기 위해서다. 호출 위치:
  - `QuitApp` 끝(트레이 Quit, `--quit`)
  - `SessionEnding`(로그오프/Windows 종료. `OnExit` 전에 프로세스가 끝날 수 있어 먼저 정리)
  - `OnExit`(mutex 해제 **전**)
  - 업데이트 경로는 아직 없다(P5). P5 업데이터가 `--quit`으로 정상 종료 경로를 쓰도록 하는 것을 제안한다.
- **안내:** 미완료이면 트레이 풍선을 띄운다(8초, 경고 아이콘, 첫 실행·스키마 풍선보다 먼저 띄우므로 그 풍선들이 우선한다). 시스템 탭 `PreviousRunStatusText`에는 "이전 실행(… 시작, 버전 …)이 정상적으로 종료되지 않았습니다. 원인(오류, 강제 종료, 전원 차단 등)은 확인할 수 없습니다."를 표시한다. 정상이면 "정상적으로 끝나지 않은 이전 실행 기록이 없습니다."를 표시한다. 표식 쓰기에 실패하면 예외 형식과 HRESULT만 덧붙여 "다음 시작 때 알리지 못할 수 있음"을 표시한다.
- `RestartOnCrash`는 구현하지 않았다. 저장값은 손대지 않는다(기본 false). 별도 감시기도 없다.

## 4. T3 — CLI 인자 전달

- **전달 수단: 이름 있는 파이프.** 기존 구조(P1-A)는 창 없이 커널 개체(mutex·이벤트)로 신호를 주고받는다. `WM_COPYDATA`는 대상 HWND를 찾아야 하므로 이 구조에 맞지 않는다. 파이프 이름은 `NotificationsProKR-Mnyang.Cli.<세션 ID>`다. 파이프는 머신 전역이라 세션 ID로 `Local\` mutex와 범위를 맞췄다. 양쪽 모두 `PipeOptions.CurrentUserOnly`(현재 사용자 ACL과 서버 소유자 확인)를 쓴다.
- **메시지:** 4바이트 길이 + UTF-8 JSON `{"v":1,"args":[...]}`. 최대 16KB·64개다. 원시 인자 문자열만 보내고, 주 인스턴스가 시작 인자와 같은 파서(`CliArguments.Parse`)로 해석한다.
- **한 번만 실행:** 서버는 연결 하나에서 메시지 하나를 콜백에 한 번 넘긴 뒤 수신 확인 1바이트를 돌려준다. 클라이언트는 연결 전 실패만 기다린다(`Connect(5000)`이 파이프 생성을 기다림). 쓰기를 시작한 뒤에는 재시도하지 않는다.
- **시작 중 도착:** 서버는 mutex 획득 직후 열린다. 시작이 끝나기 전에 받은 인자는 대기열에 넣는다. 시작 인자 처리(`ProcessCliArguments(e.Args)`) 뒤에 도착 순서대로 한 번씩 실행한다.
- **판정(`CliArguments`):** 기존 의미를 유지한다. 대소문자를 무시하고, `--theme`는 다음 값을 받으며, 모르는 인자는 무시한다. 두 번째 실행에 **알려진 명령이 하나라도 있으면** 전달한다. 인자가 없거나 모르는 인자뿐이면 P1-A대로 설정창 이벤트를 보낸다. `--start-minimized`만 있는 두 번째 실행은 전달은 되지만 아무 동작도 하지 않는다(설정창을 띄우지 않음).
- **새 인자(제안, 이번에 추가):** `--start-minimized`(로그온 작업용, 시작 때만 의미 있음), `--quit`(정상 종료 경로 `QuitApp`, 표식 정리). 실행 중인 인스턴스 없이 `--quit`으로 시작하면 아무것도 하지 않고 종료 코드 0으로 끝난다.
- **종료 코드:** 보조 실행이 전달 성공 또는 설정창 요청을 하면 0이다. **전달 실패(주 인스턴스 응답 없음·거부)는 1이다(승인됨).**
- **한계:** `CurrentUserOnly` 확인은 토큰 소유자 SID를 비교한다. 그래서 UAC가 켜진 PC에서 관리자 권한(상승) 실행과 일반 실행이 섞이면 전달이 실패할 수 있다(종료 코드 1). 이 PC는 UAC가 꺼져 있어(P0 §4.2) 해당이 없다. 실측은 하지 않았다.

## 5. T4 — 원래 소리 수동 음소거 안내 (`Helpers/ManualMuteGuidance.cs`, 앱 탭)

### 5.1 앱 유형 판정 (이번 세션 `AppIdentityRegistry`의 AUMID 기준)

| 조건 | 유형 | 기본 상태 |
|---|---|---|
| AUMID가 `\KakaoTalk.exe`로 끝남, 또는 AUMID 없이 표시 이름이 `KakaoTalk`/`카카오톡` | 카카오톡 | 안내 미지원 |
| AUMID가 `com.squirrel.Discord.` / `DiscordCanary.` / `DiscordPTB.`로 시작 | Discord | 설정 필요 |
| 그 밖의 AUMID가 하나 이상 | Windows toast 앱 | 설정 필요 |
| AUMID 없음(접근성 수신·이번 세션 미관찰) | 미확인 | 미확인 |

- 카카오톡 이름 일치는 AUMID 패턴 외에 추가한 규칙이다. P0에서 카카오톡은 Windows 알림 키가 없었으므로 AUMID 없이 들어올 수 있기 때문이다(승인됨).
- 저장된 확인이 `UserConfirmed`이면 카카오톡을 제외하고 "사용자 확인 완료(앱이 검증하지 않음) · 확인 시각"을 표시한다.

### 5.2 UI와 저장

- 카드의 "원래 소리 처리" 상자에는 상태 줄(`OriginalSoundStatusText`, UIA 이름 "원래 소리 처리: …"), 유형별 안내문(`ManualMuteGuidanceText`), "이 앱은 해당 설정을 읽거나 바꾸지 않습니다…" 안내가 들어간다. 버튼은 3개다.
  - **Windows 알림 설정 열기**(`ms-settings:notifications`): Windows toast 앱·Discord
  - **사용자 확인 완료로 표시**: Windows toast 앱·Discord이고 아직 확인하지 않았을 때
  - **다시 확인 필요**: 확인 완료일 때
- 버튼은 `ManualMuteConfirmations[키] = {State, UpdatedAt}`를 현재 설정에 바로 저장한다(`UserConfirmed` / `SetupNeeded`). UiLanguage와 같은 이 PC 전용 경로라 undo/redo·프로필을 거치지 않는다.
- **저장 키(검토 반영, `ManualMuteGuidance.ResolveStorageKey`):** 이번 세션에 본 AUMID가 하나면 그 AUMID를 쓴다. Windows 소리 스위치가 AUMID 단위이기 때문이다. ChatGPT와 Codex처럼 AUMID를 공유하는 카드는 확인 상태 하나를 함께 쓰고, 한쪽에서 누르면 다른 카드도 바로 갱신된다. AUMID가 없으면 표시 이름을 쓴다. 한 이름에 AUMID가 여러 개이면 카드에서 고른 규칙 키가 그중 하나일 때 그 AUMID를 쓰고, 아니면 표시 이름을 쓴다(구현 가정).
- **조회(`FindStored`):** `AppIdentityMatcher`와 같은 우선순위로 저장 키 AUMID → 이번 세션의 다른 AUMID → 표시 이름 순서다. 표시 이름 키로 저장된 기존 값은 읽기 전용 호환으로 조회만 한다. 자동으로 키를 바꾸거나 지우지 않는다. 새로 누르면 AUMID 키에 쓰고, 이후에는 그 값이 우선한다(이름 키 값은 그대로 남는다).
- 한계: AUMID 키로 저장한 확인 상태는 다음 실행에서 그 앱 알림이 한 번 와서 AUMID가 관측될 때까지 보이지 않는다. 그동안은 "미확인"으로 표시된다.
- 안내 경로는 P0 §5.3과 지시 문구를 따랐다. **한국어 Windows 화면의 정확한 토글 문구("알림이 도착하면 소리 재생")와 Discord 실제 메뉴는 이 PC 화면에서 확인하지 않았다**(P0 미완 항목 그대로).
- P1-B의 프로필 제외는 유지된다. 테스트에서 프로필 JSON에 `ManualMuteConfirmations`가 없음을 확인했다.
- P3 잔여 문구 2건을 함께 고쳤다. 툴팁 `Apps_WhetherAppSOwnWindows`의 "변경 기능은 이후 업데이트에서 제공됩니다" 문장을 지웠다(en·ko). ko `Apps_OriginalSoundHandling` 뒤에 공백이 빠져 "처리:미확인"으로 붙어 보이던 것도 고쳤다.

## 6. 트레이로 시작 (`StartMinimizedToTray`)

- **기본값 `true`(검토 반영).** 원본(기준 커밋 `OnStartup`)은 시작 때 첫 실행 풍선만 띄우고 설정창을 열지 않았다. K12를 보존하려고 모델 기본값을 `true`로 바꿨다. 키가 없는 파일(신규 설치, v5 이하 이관)은 트레이로 시작한다. v5 이관 테스트에서 저장 파일에 `"StartMinimizedToTray": true`가 기록됨을 확인했다. `settings.example.json`, UI 토글 기본(켜짐, 하네스에서 `checked=True` 확인), `ClearLocalOnly`의 값도 `true`로 맞췄다. 키에 `false`를 명시한 v6 파일은 그 값을 유지한다(테스트).
- 판정(`CliArguments.ShouldShowSettingsAtStartup`): 설정 파일이 없으면(첫 실행) 항상 설정창을 연다. 그 외에는 설정이 꺼져 있고 `--start-minimized`도 없을 때만 연다. 로그온 작업은 항상 `--start-minimized`를 넘긴다.
- `OverlayVisible`·OBS 캡처 창 상태는 읽지도 쓰지도 않는다. 오버레이 표시는 기존 코드가 저장값대로 처리한다.
- 시스템 탭 토글은 현재 설정에 바로 저장한다. **이 PC 전용 값으로 옮겼다**(`LocalOnlySettingsHelper`에 추가). 그래서 프로필·내보내기에서 빠지고, 프로필 적용·가져오기·undo/redo·전체 초기화 때 현재 값을 유지한다.

## 7. 변경 파일

- 신규(src): `App.Resident.cs`, `Helpers/{RunMarker, CliArguments, CliArgumentMessage, ManualMuteGuidance}.cs`, `Models/{LogonTaskState, LogonTaskStatus, ScheduledTaskInfo, RunMarkerStartResult, CliCommand, CliCommandKind, OriginalSoundAppKind}.cs`, `Services/{ITaskSchedulerClient, ComTaskSchedulerClient, LogonTaskService, CliArgumentServer, CliArgumentClient}.cs`, `ViewModels/SettingsViewModel.Resident.cs`
- 수정(src): `App.xaml.cs`(보조 실행·`--quit`·서버/표식 시작·풍선·시작 설정창·파서 기반 `ProcessCliArguments`·`SyncStartupTaskState` 언패키지 반환·`QuitApp`/`OnExit` 정리), `Helpers/ForkIdentity.cs`(인자·표식·파이프 이름), `Helpers/LocalOnlySettingsHelper.cs`, `Models/AppSettings.cs`(주석), `Services/SettingsManager.cs`(주석), `ViewModels/AppSoundRuleViewModel.cs`, `ViewModels/SettingsViewModel.SoundUi.cs`, `Views/SettingsWindow.xaml`, `Resources/Strings.resx`·`Strings.ko-KR.resx`(새 키 37개, 1,365 → 1,402 + 기존 값 2건 수정)
- 테스트: 신규 `LogonTaskServiceTests`(12), `RunMarkerTests`(10), `CliForwardingTests`(14), `ManualMuteGuidanceTests`(16). 수정 `SettingsSchemaV6Tests`(기본값 true·v5 이관 true·v6 명시 false 유지, +1), `LocalOnlySettingsTests`(StartMinimizedToTray 비기본값 false로 보존 확인), `LocalizationResourceTests`(번역 금지 목록에 `--start-minimized`·`--quit`·`run-state.json`·`ms-settings:` 추가)
- 문서: 이 보고서, `CHANGELOG-fork.md`

## 8. 명령·결과 원문

### 8.1 실행 전 상태 (조회만)

```text
Id 7716  NotificationsProKR-Mnyang  F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe  StartTime 2026-10-09 오후 4:16:46  ParentProcessId 11808
%AppData%\NotificationsPro = False
%AppData%\NotificationsProKR-Mnyang\settings.json  6748 bytes  2026-10-09T14:16:45.8096822+09:00  SHA256 0C9198D409253C24B6551D858F6889A0A5F195D30BD6411509BAFFA378CD561A
Get-ScheduledTask -TaskName 'NotificationsProKR*'  → (없음)
```

설정 파일 바이트 백업: scratchpad `backup\settings.json.bak`. 변경이 없어 원복하지 않았다.

### 8.2 빌드·테스트 (출력 경로 우회: `-p:OutDir=<scratchpad>\out2\`, obj 경로는 그대로)

```text
# 기준(변경 전)
> dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj -p:OutDir=<scratchpad>\out\
통과!  - 실패:     0, 통과:   554, 건너뜀:     0, 전체:   554, 기간: 8 s - NotificationsPro.Tests.dll (net8.0)

# 최종
> dotnet build src/NotificationsPro/NotificationsPro.csproj --no-incremental -p:OutDir=<scratchpad>\out2\
    경고 0개
    오류 0개
> dotnet build tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj --no-incremental -p:OutDir=<scratchpad>\out2\
    경고 0개
    오류 0개
> dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj --no-build -p:OutDir=<scratchpad>\out2\
통과!  - 실패:     0, 통과:   602, 건너뜀:     0, 전체:   602, 기간: 8 s - NotificationsPro.Tests.dll (net8.0)

# 검토 반영 후 (-p:OutDir=<scratchpad>\out3\, --no-incremental, PID 7716 실행 중 그대로)
    경고 0개 / 오류 0개   (앱)
    경고 0개 / 오류 0개   (테스트)
통과!  - 실패:     0, 통과:   607, 건너뜀:     0, 전체:   607, 기간: 8 s - NotificationsPro.Tests.dll (net8.0)
NP=False KR=settings.json
SHA256 0C9198D409253C24B6551D858F6889A0A5F195D30BD6411509BAFFA378CD561A
temp leftovers: False
하네스 재실행: [StartMinimizedToTrayToggle] checked=True (en/ko), binding errors: 0

before: NP=False KR=settings.json
after:  NP=False KR=settings.json
SHA256 0C9198D409253C24B6551D858F6889A0A5F195D30BD6411509BAFFA378CD561A
temp leftovers: False
Id 7716 StartTime 2026-10-09 오후 4:16:46   (그대로 실행 중)
Get-ScheduledTask -TaskName 'NotificationsProKR*' → (없음)
```

새 테스트는 첫 실행에서 모두 통과했다. 빌드 오류는 두 번 있었고 모두 작업 도구 쪽 문제였다. 셸 heredoc에 작은따옴표가 섞이면 `unexpected EOF while looking for matching '`가 나서(P1-A와 같은 증상) 파일 작성 도구로 바꿨다. scratchpad 하네스에서는 `Path`/`TabControl` 모호 참조 오류가 나서 `UseWindowsForms`를 빼 해결했다. 소스 결과에는 영향이 없다.

### 8.3 오프스크린 XAML 하네스 (scratchpad, 저장소 밖, 앱 실행 아님)

실제 산출물 `NotificationsProKR-Mnyang.dll`을 참조했다. AppData는 `%TEMP%\NPKR-P4-Harness-<guid>`로 재지정했다(끝나면 삭제). `SettingsWindow`의 내용을 `WS_VISIBLE`이 없는 숨김 `HwndSource`에 올려 바인딩을 활성화했다. 가짜 스케줄러는 다른 exe를 가리키는 비활성 작업을 돌려주게 했다. 이전 실행은 미완료로 설정했다. 알림은 Discord(AUMID), ChatGPT(AUMID), KakaoTalk(AUMID 없음), Mystery(AUMID 없음) 4건을 RAM에만 넣었다. 발췌:

```text
--- tab System (en-US) ---
[LogonTaskToggle] visible=True checked=False enabled=True name='Run at Windows sign-in'
[LogonTaskStatusText] text='Registered (disabled): the task is turned off in Task Scheduler, so it does not run at sign-in. The app does not use any other auto-start method. Turning the option on enables the task again.
The task starts a copy of the app in another location. Turn the option off and on again to register this copy.'
[RestartOnCrashToggle] visible=True checked=False enabled=False name='Restart after a failure'
[PreviousRunStatusText] text='The previous run (started 10/9/2026 4:14 PM, version 1.1.10.37) did not end normally. The cause (error, forced end, power loss, etc.) cannot be determined.'
--- tab System (ko-KR) ---
[LogonTaskToggle] name='Windows 로그인 시 자동 실행'
[RestartOnCrashNote] text='이 버전에서는 제공하지 않습니다. 시험 결과 작업 스케줄러가 비정상 종료된 앱을 다시 시작하지 않아 이 옵션은 꺼져 있습니다. …'
[PreviousRunStatusText] text='이전 실행(2026. 10. 9. 오후 4:14 시작, 버전 1.1.10.37)이 정상적으로 종료되지 않았습니다. 원인(오류, 강제 종료, 전원 차단 등)은 확인할 수 없습니다.'
--- tab Apps (ko-KR) ---
[OriginalSoundStatusText] (Mystery)   name='원래 소리 처리: 미확인'
[OriginalSoundStatusText] (KakaoTalk) name='원래 소리 처리: 안내 미지원'
[OriginalSoundStatusText] (Discord)   name='원래 소리 처리: 설정 필요'
[ManualMuteGuidanceText] (Discord) text='1) Windows: 설정 > 시스템 > 알림 > Discord > '알림이 도착하면 소리 재생'을 끕니다. 2) Discord: 사용자 설정 > 알림 > 소리에서 'Message(메시지)'를 끕니다. …'
[ManualMuteOpenSettingsButton] (Mystery) visible=False / (KakaoTalk) visible=False / (Discord) visible=True / (ChatGPT) visible=True
===== ko-KR after Discord confirm =====
[OriginalSoundStatusText] (Discord) name='원래 소리 처리: 사용자 확인 완료(앱이 검증하지 않음) · 2026. 10. 9. 오후 5:14'
[ManualMuteConfirmButton] (Discord) visible=False   [ManualMuteRecheckButton] (Discord) visible=True name='Discord의 원래 소리를 다시 확인 필요로 표시'
binding errors: 0
temp removed=True
```

## 9. 검토자 결정 (반영 상태)

| # | 항목 | 결정 | 반영 |
|---|---|---|---|
| 1 | 트레이로 시작 기본값(K12) | **수정 지시: 기본값 `true`.** 첫 실행은 설정창 표시, `--start-minimized`는 강제 | 반영: 모델 기본값·`settings.example.json`·`ClearLocalOnly`·UI 기본(켜짐)·테스트(신규 설치 true, v5 이관 true, v6 명시 false 유지) |
| 2 | `StartMinimizedToTray` 이 PC 전용 | **승인**(P1-B 결정 번복) | 변경 없음(이미 구현) |
| 3 | 작업 `Priority=5` | **승인** | 변경 없음 |
| 4 | 전달 실패 종료 코드 1 | **승인** | 변경 없음 |
| 5 | 카카오톡 표시 이름 판정 | **승인** | 변경 없음 |
| 6 | 확인 저장 키 | **수정 지시: AUMID가 있으면 AUMID, 없으면 표시 이름.** 읽기도 같은 우선순위, 기존 표시 이름 키는 읽기 전용 호환, 자동 재키 금지 | 반영: `ResolveStorageKey`·`FindStored`, 같은 AUMID 카드 동시 갱신, 테스트 +4(키 결정, 조회 우선순위, ChatGPT/Codex 공유, 기존 이름 키 불변) |
| 7 | 비활성 작업에서 토글 켜기 = 덮어써 다시 사용 | 회신 없음 | 현재 동작 유지(확인 대화상자 없음) |
| 8 | `--quit` 공개 여부 | 회신 없음 | 현재 내부용(문서는 이 보고서와 포크 변경 이력만) |
| 9 | 실제 실행 검증 일정 | 회신 없음(PID 7716 종료 금지 유지) | §10 그대로 미실시 |

추가 결정 요청:

- **A. 기존 v6 포크 설정의 `false`:** 이 PC의 `%AppData%\NotificationsProKR-Mnyang\settings.json`(v6)에는 P1-B~P3 빌드가 기본값으로 쓴 `"StartMinimizedToTray": false`가 들어 있다(읽기만 확인). UI가 없던 때의 값이라 사용자가 고른 것이 아니다. 하지만 새 기본값은 키가 없는 파일에만 적용되므로, 이 PC는 수동 실행 때 설정창이 계속 열린다. 선택지는 두 가지다. ① 사용자가 시스템 탭에서 한 번 켠다(코드 변경 없음, 현재 상태). ② 스키마 표식을 추가해 기존 v6 파일의 `false`를 한 번만 `true`로 바꾼다(스키마 변경이라 별도 승인 필요). 정해 달라.
- **B. 한 이름에 AUMID가 여러 개일 때의 저장 키:** 카드에서 고른 규칙 키가 그 AUMID 중 하나면 그 AUMID를 쓰고, 아니면 표시 이름을 쓰도록 구현했다(구현 가정). 이 규칙으로 확정할지 정해 달라.

## 10. 자동 검증 vs 실제 검증

| 항목 | 자동(단위·하네스) | 실제 Windows |
|---|---|---|
| 작업 XML·상태 매핑·등록/해제 의도·조회 무변경 | 통과(가짜 스케줄러 12건) | **미실시**: 실제 COM 등록·XML 내보내기·삭제(임시 이름 `NotificationsProKR-Mnyang-P4-Temp`), 로그온 실행 |
| 실행 표식: 정상 종료 오탐 없음·강제 종료 안내·보조 실행 불변·이전 토큰 무영향·손상·쓰기 실패 | 통과(10건, 실제 mutex 포함) | **미실시**: `Stop-Process` 후 재실행 안내, `--quit` 후 오탐 없음, 로그오프·Windows 종료 |
| CLI 해석·전달 판정·메시지·파이프 1회 전달·쓰레기 거부 | 통과(14건, 실제 파이프 포함, 한 프로세스 안) | **미실시**: 두 번째 exe의 `--pause` 전달→기존 인스턴스 일시 정지(툴팁) |
| 수동 음소거 판정·상태 전이·저장·프로필 제외 | 통과(12건) | **미실시**: 버튼 조작, `ms-settings:notifications` 열기, 실제 원음 무음 |
| 시스템·앱 탭 UIA·문구·바인딩 | 하네스 통과(binding errors 0) | **미실시**: 실제 창 렌더링·잘림·배율 |
| 트레이로 시작 판정 | 통과(5조합) | **미실시** |

T5 재실행 순서(인스턴스가 없을 때): 기본 경로 재빌드 → 포크 실행 → 경로 확인 후 `Stop-Process` → 재실행 → `PreviousRunStatusText`(UIA)에서 미완료 안내 확인 → `NotificationsProKR-Mnyang.exe --quit`(종료 코드 0, 프로세스 종료, `run-state.json` 없음 확인) → 재실행 시 "기록 없음" 확인 → 두 번째 실행 `--pause` → 트레이 툴팁의 일시 정지 접미사 확인 → 임시 작업 등록(트리거 없음 XML로 별도 하네스)·`Export-ScheduledTask`로 XML 확인·삭제.

## 11. 사용자 수동 검증 체크리스트

### 11.1 로그온 자동 실행 (R07)
- [ ] 포크를 실행하고 시스템 탭에서 "Windows 로그인 시 자동 실행"을 켠다. 상태 줄이 "등록됨"인지 본다.
- [ ] 작업 스케줄러(`taskschd.msc`) 루트에서 `NotificationsProKR-Mnyang-Logon`을 확인한다. 동작이 exe 직접 실행이고 인자가 `--start-minimized`인지, "사용자가 로그온할 때만 실행", 최고 권한 아님, "작업 중지 시간" 없음인지 확인한다.
- [ ] 로그아웃 후 다시 로그인한다. 설정창 없이 트레이 아이콘만 나오는지, 툴팁이 `Listening via WinRT (polling 1s)`인지, 오버레이가 저장된 표시 상태대로인지 확인한다.
- [ ] 재부팅 후에도 같은 결과인지 확인한다.
- [ ] 작업 스케줄러에서 작업을 "사용 안 함"으로 바꾼다. 시스템 탭에서 "상태 새로 고침"을 누르면 "등록됨(비활성)"이 표시되고 토글이 꺼져 있는지 본다. 앱이 작업을 다시 켜지 않는지 확인한다.
- [ ] "작업 등록 해제"를 누르면 "미등록"이 되고 작업 스케줄러에서도 작업이 사라지는지 확인한다.

### 11.2 비정상 종료 안내 (R08)
- [ ] 실행 중인 포크를 작업 관리자에서 "작업 끝내기"로 종료한 뒤 다시 실행한다. 트레이 풍선과 시스템 탭 "이전 실행" 줄의 안내를 확인한다.
- [ ] 트레이 "종료"로 끝낸 뒤 다시 실행한다. "정상적으로 끝나지 않은 이전 실행 기록이 없습니다"가 나오는지 확인한다.
- [ ] 포크를 실행한 채 로그아웃하거나 Windows를 다시 시작한다. 다음 실행에서 오탐이 없는지 확인한다.

### 11.3 CLI (K12)
- [ ] 실행 중일 때 명령 창에서 `NotificationsProKR-Mnyang.exe --pause`를 실행한다. 기존 인스턴스가 한 번만 일시 정지되는지(트레이 아이콘 흐림), 새 트레이 아이콘이 생기지 않는지, `echo %errorlevel%`이 0인지 확인한다. `--resume`, `--theme <이름>`, `--hide`/`--show`, `--send-test`도 같은 방식으로 확인한다.
- [ ] 인자 없이 두 번째로 실행하면 설정창만 열리는지 확인한다.

### 11.4 원래 소리 수동 음소거 (R09)
- [ ] ChatGPT/Codex·Claude: 알림을 한 번 받은 뒤 앱 탭에서 "설정 필요"와 안내를 확인한다. "Windows 알림 설정 열기"로 해당 앱의 "알림이 도착하면 소리 재생"을 끄고 실제 알림을 받아 본다. 원래 소리 없이 WAV만 나는지, 배너·알림 센터는 유지되는지 확인한다. 그 뒤 "사용자 확인 완료로 표시"를 누르고 앱을 다시 시작해도 유지되는지 확인한다. 한국어 Windows 토글의 실제 문구가 안내문과 같은지도 확인한다.
- [ ] Discord: 위 Windows 토글을 끄고 Discord 사용자 설정 > 알림 > 소리에서 Message를 끈다. 다른 사람이 보낸 실제 메시지로 원래 소리가 없는지, 배너가 유지되는지, 통화 소리는 정상인지 확인한다. 메시지를 보내는 시험은 사용자가 직접 한다. Discord 메뉴 이름이 안내문과 같은지도 확인한다.
- [ ] 카카오톡: 앱 탭에 나타나면 "안내 미지원"으로 보이는지 확인한다. 카카오톡 알림이 실제로 이 앱에 수신되는지(P0 판정: 수신 안 될 가능성 높음)도 기록한다.
- [ ] "다시 확인 필요"를 누르면 "설정 필요"로 돌아가는지 확인한다.
- [ ] ChatGPT와 Codex 카드가 둘 다 보이면, 한쪽에서 확인 완료를 눌렀을 때 다른 카드도 같은 상태가 되는지 확인한다(AUMID 공유).
- [ ] 이 PC의 기존 설정은 `StartMinimizedToTray=false`라 수동 실행 때 설정창이 열린다. 시스템 탭에서 "트레이로 시작"을 켠 뒤 다시 실행하면 트레이로만 시작하는지 확인한다(§9 A 결정 전까지).

## 12. 보존 계약 영향

| 계약 | 영향 | 근거 |
|---|---|---|
| **K01** WinRT·접근성 수신, 진단·권한 재시도 | **변경 없음.** `NotificationListener`·조회 주기·권한 확인 코드는 손대지 않았다. 리스너 생성은 주 인스턴스에서만 일어나고, 보조 실행은 리스너 전에 끝난다(P1-A와 같음). 전달된 명령은 리스너를 다시 만들지 않는다 | 코드 검토, 기존 리스너 테스트 통과 |
| **K12** 트레이·단축키·CLI·설정창·undo/redo | **CLI:** 기존 6개 인자의 의미(대소문자 무시, `--theme` 다음 값, 모르는 인자 무시, 순서대로 실행)를 파서 테스트로 고정했다. 두 번째 실행의 인자가 이제 버려지지 않고 한 번 실행된다(P1-A 이월 해소). `--start-minimized`·`--quit`을 추가했다. **트레이:** 항목·순서 변경 없음, 풍선 1종 추가. **설정창:** 시스템 탭 "시작" 섹션에 항목을 추가했고(패키지 실행은 기존 토글 그대로), 앱 탭 원래 소리 줄을 상자로 확장했다. 새 값은 undo/redo 대상이 아니다. **시작 동작:** 기본값 `true`라 원본처럼 트레이로 시작한다(검토 반영). 사용자가 끄면 수동 실행 때 설정창을 연다. 첫 실행은 설정창을 연다(설계 §5.3). 기존 v6 파일의 `false`는 §9 A | 단위 테스트, 하네스 |
| **K11** 프로필·가져오기/내보내기 | `StartMinimizedToTray`가 프로필·내보내기에서 빠지고 적용·가져오기·undo·초기화 때 보존된다(승인됨). `ManualMuteConfirmations` 키가 AUMID 우선으로 바뀌었다(이 PC 전용이라 프로필에는 영향 없음). 나머지 변화는 없다 | `LocalOnlySettingsTests` |
| 프라이버시 하드 제약 | 위반 없음. 새 디스크 쓰기는 `run-state.json`(토큰·시각·버전)과 사용자 조작 시의 `settings.json` 저장뿐이다. 파이프로는 명령줄 인자만 오간다. 로그·알림 내용 저장은 추가하지 않았다. AGENTS.md 규칙 6의 저장 대상 목록에 `run-state.json`이 없으므로 설계 §11.1(로컬 운영 메타데이터)을 근거로 둔다 | 코드 검토, 표식 내용 테스트 |

## 13. 미완료

- T5 실제 실행 검증 전부(§10). 실제 COM 등록·로그온·재로그인·`SessionEnding`·두 프로세스 간 전달은 아직 검증되지 않았다.
- 앱별 실제 원음 무음 확인과 한국어 Windows·Discord 메뉴 문구 확인(§11.4)
- 업데이트 경로의 표식 정리(P5, `--quit` 사용 제안)
- `RestartOnCrash`(이번 버전 미제공, 결정대로)
