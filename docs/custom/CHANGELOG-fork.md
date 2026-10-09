<!-- @file CHANGELOG-fork.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang 포크의 변경 이력. upstream CHANGELOG.md는 cherry-pick 충돌을 줄이려고 수정하지 않고 여기에만 기록한다. -->

# 포크 변경 이력 (Notifications Pro KR-Mnyang)

upstream `CHANGELOG.md`, `docs/PLAN.md`, `docs/STATUS.md`는 수정하지 않는다. 포크 변경은 이 파일과 `docs/custom/` 단계별 보고서에 기록한다.

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
