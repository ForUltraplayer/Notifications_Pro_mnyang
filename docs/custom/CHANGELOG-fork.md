<!-- @file CHANGELOG-fork.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang 포크의 변경 이력. upstream CHANGELOG.md는 cherry-pick 충돌을 줄이려고 수정하지 않고 여기에만 기록한다. -->

# 포크 변경 이력 (Notifications Pro KR-Mnyang)

upstream `CHANGELOG.md`, `docs/PLAN.md`, `docs/STATUS.md`는 수정하지 않는다. 포크 변경은 이 파일과 `docs/custom/` 단계별 보고서에 기록한다.

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
