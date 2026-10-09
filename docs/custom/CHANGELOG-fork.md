<!-- @file CHANGELOG-fork.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang 포크의 변경 이력. upstream CHANGELOG.md는 cherry-pick 충돌을 줄이려고 수정하지 않고 여기에만 기록한다. -->

# 포크 변경 이력 (Notifications Pro KR-Mnyang)

upstream `CHANGELOG.md`, `docs/PLAN.md`, `docs/STATUS.md`는 수정하지 않는다. 포크 변경은 이 파일과 `docs/custom/` 단계별 보고서에 기록한다.

## P4 — 상주·로그온 실행·비정상 종료 안내·CLI 전달·원음 수동 음소거 안내 (2026-10-09, 미커밋)

상세: `docs/custom/P4-resident-mute.md`

### 추가

- 로그온 자동 실행(언패키지): 작업 스케줄러 작업 `NotificationsProKR-Mnyang-Logon`을 Task Scheduler COM으로 등록·해제·조회한다. 현재 사용자 로그온 트리거, 대화형 토큰, 최소 권한, `PT0S`, `IgnoreNew`, exe 직접 실행 + `--start-minimized`, 재시작 없음으로 구성한다. 시스템 탭에 토글·상태 줄(등록됨 / 등록됨(비활성) / 미등록 / 정책 차단 / 확인 실패, 다른 exe 경고)·상태 새로 고침·작업 등록 해제 버튼을 두었다. 비활성 작업을 다른 방식으로 우회하지 않는다. 패키지 실행은 기존 StartupTask 토글을 그대로 쓴다.
- 비정상 종료 안내(R08): 주 인스턴스가 `run-state.json`(토큰·시작 시각·버전만)을 쓰고, 정상 종료(`QuitApp`)·로그오프/Windows 종료(`SessionEnding`)·`OnExit`에서 자기 토큰일 때만 지운다. 다음 시작 때 표식이 남아 있으면 트레이 풍선과 시스템 탭 "이전 실행" 줄로 알린다. 원인은 확정하지 않는다.
- CLI 인자 전달(K12): 두 번째 실행의 알려진 명령을 현재 사용자 전용·세션별 이름 있는 파이프로 기존 인스턴스에 한 번 전달한다. 시작 중에 도착하면 대기열에 넣는다. 인자가 없거나 모르는 인자뿐이면 기존처럼 설정창을 연다. 종료 코드는 성공 시 0, 전달 실패 시 1이다. 새 인자는 `--start-minimized`, `--quit`이다.
- 트레이로 시작: 시스템 탭 토글(`StartMinimizedToTray`)과 `--start-minimized`가 있으면 설정창을 열지 않는다. 첫 실행은 항상 설정창을 연다. 오버레이·OBS 캡처 상태는 건드리지 않는다.
- 실패 시 다시 시작: "이 버전에서는 제공하지 않음"으로 비활성 표시한다(저장값 불변).
- 앱 탭 원래 소리 처리(R09): AUMID 기반으로 앱 유형(Windows toast 앱 / Discord / 카카오톡 / 미확인)을 판정한다. 유형별 안내문, Windows 알림 설정 열기, "사용자 확인 완료로 표시", "다시 확인 필요" 버튼을 두었다. 확인 상태는 이 PC 전용으로 바로 저장하며 앱이 검증했다고 표시하지 않는다. 저장 키는 AUMID가 있으면 AUMID(같은 AUMID를 쓰는 ChatGPT·Codex는 상태 공유), 없으면 표시 이름이다. 기존 표시 이름 키 값은 읽기 전용 호환으로만 조회한다.

### 변경

- `StartMinimizedToTray`를 이 PC 전용 값에 추가했다(프로필·내보내기 제외, 적용·가져오기·undo·초기화 때 보존).
- `StartMinimizedToTray` 기본값을 `true`로 바꿨다(원본처럼 트레이로 시작, K12). 신규 설치·v5 이관·`settings.example.json`도 true다. 첫 실행은 설정창을 연다. v6 파일에 `false`를 명시했으면 그 값을 유지한다.
- `ProcessCliArguments`가 공용 파서(`CliArguments`)를 쓴다. 기존 인자 의미는 같다.
- 언패키지 실행에서는 `SyncStartupTaskState`가 아무것도 하지 않는다.
- 리소스 키 37개 추가(1,365 → 1,402). 원래 소리 툴팁의 "이후 업데이트" 문장을 지우고, 한국어 "원래 소리 처리:" 뒤 공백을 고쳤다.

### 테스트

- 554 → 607 (+53, 검토 반영 +5 포함). 빌드 경고 0·오류 0(실행 중인 포크가 `bin\Debug`를 잠가 출력 경로를 바꿔 확인).
- 작업 XML·상태 매핑·등록/해제 의도(가짜 스케줄러), 실행 표식(정상 종료·강제 종료 시뮬레이션·보조 실행 불변·이전 토큰), 실제 파이프 1회 전달, 수동 음소거 판정·전이·저장·프로필 제외를 확인했다.
- 오프스크린 XAML 하네스: 시스템·앱 탭 P4 요소 UIA 노출, 영어·한국어 문구 확인, 바인딩 오류 0.
- **실제 앱 실행 검증은 미실시**(사용자 인스턴스 실행 중). 로그온·재로그인·강제 종료 후 안내·`--pause` 전달·실제 COM 등록·앱별 원음 무음은 보고서 §10·§11에 남겼다.

## P3 — 한국어/영어 지원 (2026-10-09, 미커밋)

상세: `docs/custom/P3-localization.md`

### 추가

- `.resx` 리소스(`Resources/Strings.resx` 영어 기준 + `Strings.ko-KR.resx`, 키 1,365개)와 `Localization/`(`LocalizationManager`, `{loc:Loc}`·`{loc:LocFormat}`·`{loc:LocOption}` 마크업 확장). 위성 어셈블리 `ko-KR\NotificationsProKR-Mnyang.resources.dll`.
- 시스템 탭 ‘언어: 한국어 / English / OS 기본값’. 선택은 이 PC 전용(`UiLanguage`)이며 즉시 적용된다(XAML 바인딩 갱신, 트레이 메뉴 재생성, 즉시 바뀌지 않는 곳은 안내문). 신규 설치·OS 기본값은 OS UI 문화권이 `ko`로 시작하면 한국어, 그 외 영어.
- 콤보 저장값(글꼴 굵기·정렬·모드·애니메이션·아이콘 프리셋 등)은 표시만 번역하고 저장값은 그대로 둔다.
- `NotificationListener.ListenerModeDisplay`(표시용). `ListenerMode`는 식별자로 유지.

### 변경

- 설정창 전체 탭·오버레이·트레이 메뉴/툴팁/풍선·메시지 상자·VM 상태 문구·리스너 상태·단축키 오류·원본 가져오기·접근성 이름의 영어 문자열을 리소스 호출로 바꿨다. `Helpers/SoundUiText.cs`는 enum→키 연결만 남겼다.
- 전체 초기화(`ResetToDefaults`)가 이 PC 전용 값 4개(UiLanguage, SoundLibraryFolders, ManualMuteConfirmations, RestartOnCrash)를 보존한다.
- 탭 머리글이 번역되므로 탭 이동은 `TabItem.Tag`(영어 탭 ID) 기준이다.
- 시간 표시(`DateTime`)·Voice Access 카드 이름의 날짜 형식은 UI 문화권을 따른다(영어 UI는 기존과 같음). 저장 형식은 변경 없음.
- Help ‘Sounds all sound the same’의 "Browse" 안내를 "Import files"로 정정했다(P2-B 이후 없는 버튼).
- Help 탭 2열 표 레이블에 줄바꿈을 넣었다(한국어 길이).

### 테스트

- 521 → 554 (+33). 빌드 경고 0·오류 0(실행 중인 포크가 `bin\Debug`를 잠가 출력 경로를 바꿔 확인). 그룹 A만 적용한 상태도 빌드 0/0·테스트 551 통과.
- 키 대응·자리표시자·서식 유효성·번역 금지 대상 미포함·기술 토큰 보존·참조 키 존재·미사용 키 없음·누락 시 영어 대체·OS 기본값 결정·초기화 보존·전환 후 재생 대기/설정/undo 유지.
- 실제 XAML을 화면에 띄우지 않고 로드한 검사: 영어·한국어 전 탭 미해결 키 0, 바인딩 오류 0, 열린 창 언어 전환 동작 확인. **실제 앱 실행·트레이·배율·IME 확인은 미실시**(보고서 §6.3, §7).

## P2-B — WAV 기능 UI: 소리 탭·앱 탭·트레이, P2-A 후속 (2026-10-09, 미커밋)

상세: `docs/custom/P2B-sound-ui.md`

### 추가

- 소리 탭: 기본 규칙 편집기(사용 여부, 명시적 무음, 단일/랜덤/순차, 연속 중복 방지, 체크 순서와 Up/Down으로 정하는 재생 순서), 음성 보관함(관리 폴더 열기·폴더 추가·새로고침·파일 가져오기, 폴더·파일 상태와 길이, 미리 듣기·중지, ‘목록에서 제외’와 확인 대화상자가 있는 ‘실제 파일 삭제’ 분리, 외부 폴더 참조 해제), 재생 정책(트리거 모드, 앱별 최소 간격 0=끔, 대기/생략, 대기 수, 대기 시간), 조정기 진단 줄(`AutomationId=SoundPlaybackStatusLine`)과 ‘Stop all playback’.
- 앱 탭: ‘기본값 상속 / 이 앱 무음 / 이 앱 전용 목록’ 규칙 UI, 같은 표시 이름에 여러 AUMID가 보이면 규칙 키 선택, 수신 경로·원래 소리 처리 상태(읽기 전용)·기존 이름 규칙(표시만)·요약 표시.
- 트레이: ‘Stop Playback’, ‘Preview Default Sound’(기존 항목·순서 유지).
- 설정 `SoundDisplayNames`(파일 참조 → 표시 이름, 프로필로 이전, 외부 경로 키는 제외). 가져오기 이름 충돌 시 기록한다.
- 조정기 `PreviewRule`, `GetLastStartedSoundPath`, 진단 `LastEvent`. `AppIdentityRegistry` 잠금·`FindDisplayNameForAppId`.

### 변경

- 기존 "Default Sound" 콤보·Test·"Browse Custom Sound..."와 앱별 소리 드롭다운(Default/None/파일)을 규칙 UI로 대체했다. 원본 "None" 결함은 ‘이 앱 무음’(`ExplicitSilence`)으로 정확히 표현된다. 가져오기는 같은 이름 파일을 덮어쓰지 않는다.
- v5 이하 이관 파일·이관 프로필 스냅샷·원본 가져오기는 `MinIntervalPerAppSeconds = 0`(신규 설치 4초, 기존 v6 파일 불변).
- 설정창 탭 템플릿에 `PART_SelectedContentHost`를 붙여 선택 탭 내용을 UI Automation·화면 낭독기에 노출했다.
- ‘Clear Overrides’가 그 앱의 소리 규칙도 지운다.

### 테스트

- 494 → 521 (+27). 빌드 경고 0·오류 0. 규칙 저장 경로 변이 2건을 테스트가 잡았다.
- 실제 exe(무음 WAV, 무음 토스트, UIA 진단 줄): 순차 A→B→A, 동시 최대 1, 간격 4초에서 1건 재생·2건 건너뜀, ‘Stop all playback’ 후 대기 0을 확인했다. 트레이 클릭·앱 탭 조작·재생 방식 콤보 UIA 조작은 미확인이다.

## P2-A — WAV 재생 엔진: 규칙 선택기·재생 조정기·파일 검증·보관함 서비스 (2026-10-09, 미커밋)

상세: `docs/custom/P2A-playback-engine.md`

### 추가

- `SoundRuleSelector`: AUMID → 표시 이름 → 기본 규칙 순으로 규칙을 찾는다. 단일/랜덤(중복 방지)/순차를 고르며 앱별 이력은 RAM에만 둔다. 후보 결정과 확정(실제 재생 시작 때만)을 분리했다. 명시적 무음은 기본으로 돌아가지 않는다.
- `PlaybackCoordinator`: 알림 WAV·낭독·미리 듣기가 하나의 재생 권한을 쓴다. `PlaybackPolicy`(대기 5·30초, Queue/Skip, 앱별 4초 간격)를 적용한다. 설정 변경 시 대기를 취소하고, 실패하면 다음 파일을 시도하며, 작업 ID로 늦은 완료를 무시한다. `Preview`·`StopPreview`·`StopAll`·`GetDiagnostics` API를 노출했다(UI는 P2-B).
- `WinRtSoundEngine`(`ISoundEngine`): 기존 낭독과 같은 WinRT `MediaPlayer`로 재생한다. 시작·완료·실패 이벤트, 즉시 중지, 시간 초과 감시를 제공한다. 실측 근거: `SoundPlayer.PlaySync`는 `Stop()`으로 멈추지 않았다.
- `WavHeaderInspector`·`SoundLibraryService`: 관리 폴더와 등록 폴더 바로 아래 WAV를 나열하고 헤더와 실제 엔진으로 검증한다. 가져오기는 고유 내부 이름을 쓰고 원본을 바꾸지 않는다. 목록 제외와 실제 삭제(명시 플래그)를 분리했다. 외부 폴더는 UNC·네트워크·링크/정션을 거부한다.
- `QueueManager.NotificationAddedWithIdentity`: `NotificationAdded` 직후 같은 조건에서 `(AppName, AppId)`로 발생한다. `OverlayAccepted` 모드도 AUMID 규칙을 쓴다.
- 기존 "Run Capture Diagnostic"에 `Sound playback:` 카운터 한 줄을 추가했다(앱 이름·경로·본문 없음).

### 변경

- 알림음은 `SoundService.PlaySound` 대신 조정기로 재생한다. 기존 `DefaultSound`/`PerAppSounds`는 읽지 않는다(동결). `SoundEnabled`는 마스터 스위치다. **P2-B 전까지 기존 소리 탭의 기본/앱별 소리 변경은 재생에 반영되지 않는다.**
- 낭독(`SpokenNotificationService`)은 재생 권한을 기다린다. 같은 알림은 WAV 다음에 낭독한다. 카드당 1회, 제거·만료·교체·전체 삭제·음성 끔 시 중지 계약은 그대로다.
- "Test Sound" 버튼(`SoundService.PlayWav`)도 `PlaybackSink`를 거쳐 같은 재생 권한을 쓴다.
- 연속 알림은 upstream처럼 이전 소리를 끊지 않고 대기열로 순서대로 재생한다.

### 테스트

- 384 → 494 (+110). 빌드 경고 0·오류 0. 변이 검사 8건을 모두 테스트가 잡았다.
- 실제 exe 실행은 일부만 했다(토스트 3건 발행까지). 진단 읽기와 쿨다운 실측은 미완료다(보고서 §6.4·§7.2).

## P1-B — 설정 스키마 v6·이관·안전 저장, 정책/음성 경로 분리, 앱 식별 전달 (2026-10-09, 미커밋)

상세: `docs/custom/P1B-schema-policy.md`

### 추가

- 설정 스키마 **v6**(`SettingsManager.CurrentSettingsSchemaVersion = 6`). 새 설정 `UiLanguage`, `AudioDispatchMode`, `DefaultSoundRule`, `AppSoundRules`, `SoundLibraryFolders`, `PlaybackPolicy`, `StartMinimizedToTray`, `RestartOnCrash`, `ManualMuteConfirmations`. enum은 이름 문자열로 저장하고, 알 수 없는 값은 안전한 첫 값으로 읽는다.
- v5 이하 → v6 이관: 기존 기본 WAV와 앱별 WAV를 파일 1개짜리 `Single` 규칙으로 변환한다. 기존 필드는 지우지 않는다. 이관된 설정은 `AudioDispatchMode = OverlayAccepted`, 신규 설치는 `PolicyAccepted`.
- 미래 스키마(>6) `settings.json`은 읽기 전용으로 처리한다. 저장하지 않고 트레이 풍선으로 알린다.
- 손상·1MB 초과 `settings.json`은 기본값으로 덮어쓰기 전에 `settings.json.corrupt-<시각>`으로 보존한다.
- 원자적 저장(`AtomicFileWriter`): 설정·테마·프로필·내보내기 JSON을 임시 파일에 쓴 뒤 교체한다. 실패하면 기존 파일이 그대로 남는다.
- 원본 설정 가져오기(`LegacySettingsImporter`): MSIX 가상화 경로와 언패키지 경로를 탐지한다(존재·스키마·수정 시각). 원본은 읽기만 하고 복사한다. 기존 포크 설정은 `settings.json.pre-import-<시각>`으로 백업한다. 프로필 탭 Import/Export 옆에 `Import from Original App` 버튼을 추가했다(영문 문구, P3에서 리소스화).
- `QueueManager.NotificationAccepted`: 공통 정책을 모두 통과한 직후 발생한다. 오버레이 수용 여부와 무관하며, 인자에 `OverlayAccepted`가 들어 있다. 제목·본문은 담지 않는다.
- `AudioDispatchSubscription`: 모드에 따라 `NotificationAdded`나 `NotificationAccepted` 중 **하나만** 구독한다. 모드가 바뀌면 다시 구독한다.
- 앱 식별: WinRT AUMID를 `QueueManager`와 `NotificationItem.AppId`(RAM)로 전달한다. `AppIdentityMatcher`(AUMID > 표시 이름 > 없음), `AppIdentityRegistry`(같은 표시 이름에 여러 AUMID가 관찰되면 상태로 노출)를 추가했다.

### 변경

- 이 PC 전용 값(`UiLanguage`, `SoundLibraryFolders`, `ManualMuteConfirmations`, `RestartOnCrash`)은 프로필 저장·설정 내보내기 JSON에서 속성째 빠진다. 프로필 적용(설정창·트레이), 설정 가져오기, undo/redo를 해도 현재 값을 유지한다.
- 설정창 일반 저장(`BuildCurrentSettingsSnapshot`)이 v6 값을 기본값으로 되돌리지 않고 이어받는다.
- 쓰기 가능한 저장은 항상 현재 스키마(6)로 기록한다.

### 유지(회귀 테스트로 고정)

- `NotificationAdded`·`NotificationDisplayed`의 의미와 발생 시점, 낭독(`SpokenNotificationService`) 경로, 단일 파일 WAV 재생 방식(규칙 엔진은 P2).
- `DeduplicationEnabled` 내용 중복 의미. 끄면 같은 문구의 별개 알림을 지우지 않는다.

### 테스트

- 314 → 384 (+70). 빌드 경고 0·오류 0.

## P1-A — 테스트 AppData 격리, 포크 식별자·단일 인스턴스, 언패키지 WinRT 리스너 (2026-10-09, 커밋 `3f002e7`)

상세: `docs/custom/P1A-identity-listener.md`

### 추가

- 테스트 AppData 격리(`TestAppDataIsolation`): 테스트가 실제 `%AppData%`에 쓰지 않는다.
- 포크 식별자(`ForkIdentity`): exe `NotificationsProKR-Mnyang.exe`, AppData `%AppData%\NotificationsProKR-Mnyang`, AUMID `rudals252.NotificationsProKR.Mnyang`, mutex `Local\NotificationsProKR-Mnyang.SingleInstance`.
- 단일 인스턴스(`SingleInstanceGuard`): 두 번째 실행은 기존 인스턴스의 설정창을 열고 종료한다. CLI 인자 전달은 P4로 미뤘다.
- `WinRtPollTracker`: 이벤트 중복 키 (AUMID 또는 `Unknown`, Id, 생성 시각), 시드 기준 집합, 권한 상태 분기.

### 변경

- 언패키지 실행에서 WinRT 구독이 실패하면 접근성 대신 **1초 조회**(`WinRT (polling 1s)`)로 동작한다. 조회 타임아웃은 5초, 매 틱 권한을 확인한다.
- AppInfo를 읽지 못한 알림을 버리지 않고 앱 이름 `Unknown`으로 전달한다. 자기 앱 풍선은 제목으로 걸러낸다.

### 테스트

- 285 → 314 (+29).
