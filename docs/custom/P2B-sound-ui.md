<!-- @file P2B-sound-ui.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro 커스텀 포크 P2-B(WAV 기능 UI: 소리 탭·앱 탭·트레이, P2-A 후속 수정) 구현·검증 보고서. -->

# P2-B 구현 보고서: WAV 기능 UI (소리 탭 · 앱 탭 · 트레이) + P2-A 후속

- 작성일: 2026-10-09 (Asia/Seoul)
- 구현 담당: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: 사용자(rudals252)
- 기준: 설계서 v1.1 §5 전체, §7.1, §7.3, §10, §14.2 "한국어·영어" 행. `P2A-playback-engine.md` §9·§11(검토자 결정 1~10). `P1B-schema-policy.md` §1.3, §8-1
- 저장소: `F:\dev\notifications-pro-custom`. 시작 HEAD `c6a2a1d`(P2-A), 작업 트리 깨끗함. **commit하지 않았다**
- 손대지 않은 것: `src/NotificationsPro.Package/**`, upstream 문서(`docs/PLAN.md`·`STATUS.md`·`CHANGELOG.md` 등), ai-tts 폴더(사용자 WAV 포함), 원본 앱·PS1·패키지 LocalCache·레지스트리. 새 NuGet·새 로깅 없음. 화면 캡처 없음

## 0. 결론

1. **규칙 UI가 기존 기본/앱별 소리 컨트롤을 대체했다.** 소리 탭에는 기본 규칙 편집기, 음성 보관함, 재생 정책, 조정기 진단 줄을 넣었다. 앱 탭의 "Default/None/파일" 드롭다운은 ‘기본값 상속 / 이 앱 무음 / 이 앱 전용 목록’으로 바꿨다. 원본의 "None" 결함(실제로는 상속)은 ‘이 앱 무음’(`ExplicitSilence`)으로 정확히 표현된다. 트레이에 ‘Stop Playback’·‘Preview Default Sound’를 추가했다.
2. **P2-A 후속(결정 4·8)을 반영했다.** v5 이하 이관 파일·이관 프로필 스냅샷·원본 가져오기는 `MinIntervalPerAppSeconds = 0`이다. 신규 설치는 4초, 이미 v6인 파일은 그대로다. `SoundDisplayNames`(파일 참조 → 표시 이름, 프로필로 이전 가능)를 추가했다.
3. **빌드 경고 0·오류 0, 테스트 521개 전부 통과**(494 → 521, +27). 규칙 저장 경로를 일부러 끊은 변이 2건은 테스트 5개씩이 잡았다.
4. **실제 exe 실행에서 순차 재생·겹침 없음·쿨다운·재생 중지를 UIA 진단 줄로 확인했다.** 무음 WAV 2개를 가져와 기본 규칙에 넣었다. 동시 토스트 3건은 A→B→A 순서로 하나씩 재생됐다(동시 최대 1). 최소 간격 4초에서는 1건 재생·2건 건너뜀이었다. ‘Stop all playback’ 뒤에는 대기 0·Idle이 됐다. 설정은 바이트 단위로 원복했다.
5. **미검증·주의 사항이 있다.** 트레이 메뉴는 실제 클릭으로 확인하지 못했다(마우스 금지). 재생 방식 콤보는 UIA로 조작하지 못해 실제 실행에서는 `settings.json`을 직접 고쳤다. 실행 중 설정창이 두 번 사라졌고, **원본 앱(PID 46104)이 실행 도중 종료돼 있었다.** 둘 다 원인을 확인하지 못했다(§5.4). 원본 앱은 다시 시작하지 않았다.

## 1. T0 — P2-A 후속

### 1.1 이관 파일의 앱별 최소 간격 0 (결정 4)

- `SettingsSchemaMigrator.DisablePerAppCooldownForLegacy`를 추가했다. `MinIntervalPerAppSeconds`만 0으로 둔다. 대기 5개·30초·Queue는 기본값을 유지한다.
- 적용 위치:
  - `MigrateLoadedSettings`: v5 이하 `settings.json`. 스키마 번호가 없는 파일과 손상·1MB 초과 파일도 이 경로를 지나므로 0이 된다(P1-B에서 이미 "기존 사용자"로 분류한 경로).
  - `MigrateLegacySnapshot`: v6 이전 프로필·가져오기 스냅샷 적용 시.
  - `LegacySettingsImporter`: 내부에서 `MigrateLoadedSettings`를 호출하므로 같은 규칙을 탄다. 테스트를 따로 추가했다.
- 신규 설치는 4초다. 이미 v6인 파일(현재 포크 설정 포함)은 바꾸지 않는다. undo/redo 스냅샷은 v6라 영향이 없다.

### 1.2 `SoundDisplayNames` (결정 8)

- `AppSettings.SoundDisplayNames`: `Dictionary<string,string>`, 키는 대소문자를 구분하지 않는다. `CopyForkExtensionsTo`·`Clone`·`NormalizeForkSettings`에 넣었다.
- 정규화(`AppSettingsAssetPathHelper.NormalizeForRuntime` 안): 키는 규칙 파일과 같은 기준으로 변환한다(관리 `sounds/x.wav`, 신뢰 Windows 소리, 등록 폴더 바로 아래 파일). 허용되지 않는 키는 버린다. 값은 제어 문자를 빼고 trim하며 최대 200자다. 항목은 최대 1024개다.
- 프로필·내보내기: 이전 가능 값이다. 프로필 저장은 외부 폴더 목록을 먼저 비우므로 외부 경로 키는 자동으로 빠진다(테스트로 고정).
- 쓰는 곳: ‘파일 가져오기’에서 이름이 충돌해 고유 내부 이름(`이름-<해시8자>.wav`)이 생겼을 때만 원래 이름을 저장한다. 실제 파일 삭제 시 해당 키를 지운다.
- 가져오기 상한 64MB는 P2-A 값 그대로다(결정 8 확정).
- `settings.example.json`에 `"SoundDisplayNames": {}`를 추가했다.

## 2. T1 — 소리 탭

### 2.1 대체한 기존 컨트롤과 보존한 항목

| 구분 | 항목 | 처리 |
|---|---|---|
| **대체** | "Default Sound" 콤보(`AvailableWindowsSounds`/`SelectedWindowsSound`) | `DefaultSoundRule` 편집기로 대체 |
| **대체** | "Test" 버튼(`TestSoundCommand` → `SoundService.PlaySound("__test__")`, 기존 `DefaultSound` 사용) | ‘Preview default sound’(조정기 `PreviewRule`, 규칙 목록의 첫 사용 가능 파일)로 대체 |
| **대체** | "Browse Custom Sound..."(`BrowseCustomSoundCommand`, 같은 이름이면 **덮어쓰기 복사**) | ‘Import files...’(`SoundLibraryService.Import`, 덮어쓰지 않음, 고유 내부 이름)로 대체 |
| **대체** | 앱 탭 "Sound" 드롭다운(`PerAppSoundOptions`: Default/None/파일) | 앱별 규칙 UI로 대체(§3) |
| 보존 | ‘NOTIFICATION SOUNDS’ 머리글, ‘Notification Sounds’ 토글(`SoundEnabled`, 마스터 스위치) | 그대로. UIA용 `AutomationId="SoundEnabledToggle"`만 추가 |
| 보존 | Windows 소리 선택 | ‘Show Windows sounds’ 체크 시 보관함 목록에 신뢰된 Windows 소리가 나오고 기본·앱 규칙에 넣을 수 있다 |
| 보존 | 다른 탭의 모든 고급 항목 | 변경 없음 |
| 보존(코드만) | `TestSoundCommand`, `BrowseCustomSoundCommand`, `AvailableWindowsSounds`, `SelectedWindowsSound`, `PerAppSoundOptions`, `PerAppConfigEntry.Sound` | XAML 바인딩만 뺐다. upstream 병합 충돌을 줄이려고 VM 코드는 남겼다(§8-4) |

- 원래 소리 탭에 볼륨 항목은 없었다. Help 탭의 "Settings → Sounds → Browse" 안내 문구는 바꾸지 않았다(P3 문자열 작업 때 정리, §7).

### 2.2 새 구성 (위에서 아래로)

1. **DEFAULT SOUND**
   - ‘Use the default sound’ 토글(`DefaultSoundRule.Enabled`). ‘Silent on purpose (explicit silence)’ 체크(`ExplicitSilence`).
   - 재생 방식 콤보(Single/Random/Sequential, 설명 포함). ‘Avoid playing the same file twice in a row’ 체크는 Random일 때만 활성화된다.
   - **선택 파일 목록(재생 순서)**: 번호, 표시 이름, 문제 상태(사용 불가일 때만), Up/Down/Preview/Remove. 순서는 체크한 순서와 Up/Down으로만 정해지고 이름 정렬과 무관하다. Remove는 이 목록에서만 뺀다.
   - 빈 목록이면 "이 목록은 아무것도 재생하지 않는다"를 표시한다. ‘Preview default sound’, ‘Stop preview’.
2. **SOUND LIBRARY**
   - 버튼: ‘Open managed folder’(없으면 만들고 탐색기로 연다), ‘Add folder...’(.NET 8 `OpenFolderDialog`, `ValidateFolderForRegistration` 거부 사유를 문구로 표시), ‘Refresh’, ‘Import files...’(여러 파일 선택 가능, 결과를 파일별로 표시). 가져온 파일은 규칙에 자동으로 넣지 않는다(설계 §7.3).
   - 폴더 목록: 경로, 출처(관리/외부), 상태(사용 가능/폴더 없음/드라이브 분리/허용 안 됨), WAV 수. 외부 폴더에만 ‘Remove reference’가 있다(확인 대화상자, 디스크 파일은 지우지 않음, §8-2).
   - 파일 행: 기본 규칙 체크, 표시 이름, `출처 · 상태 · 길이`(길이는 읽을 수 있을 때만), 문제 상태 강조, Preview/Stop, **‘Remove from lists’**(모든 규칙에서 제외, 파일 유지)와 **‘Delete file...’**(관리 폴더 파일만, 확인 대화상자 필수)은 서로 다른 명령이다.
   - 스캔 시점: 소리 탭·앱 탭에 들어올 때(`TabItem.IsSelected`를 `OneWayToSource`로 VM에 전달)와 Refresh 때. 상시 감시는 없다. 헤더 검사 뒤 실제 엔진 확인(`VerifyWithEngineAsync`)을 비동기로 이어서 상태를 갱신한다. Windows 소리는 엔진 확인에서 뺐다.
3. **PLAYBACK**
   - ‘Sound trigger’ 콤보(`AudioDispatchMode`). 설명 한 줄: "Overlay accepted는 오버레이가 받은 알림만, Policy accepted는 필터를 통과한 모든 알림(오버레이가 가득 차도)".
   - ‘Minimum interval per app’ 슬라이더(0 = "Off"), ‘When another sound is playing’(대기/생략), ‘Maximum waiting sounds’, ‘Maximum wait’.
   - 슬라이더 최대값은 `max(기본 상한, 현재 값)`이다. 손으로 큰 값을 넣은 설정을 슬라이더가 조용히 깎아 저장하지 않게 했다(기본 상한 60초·20개·120초, 저장 범위는 P1-B 정규화 그대로 3600/100/3600).
   - **진단 상태 줄 1개**(`AutomationId="SoundPlaybackStatusLine"`, UIA Text). 형식:
     `Playback: <상태> | Queued: n | Started: n | Completed: n | Interval skips: n | Max at once: n | Last: <마지막 결과> | Last file: <표시 이름>`
     앱 이름·알림 본문·경로는 넣지 않는다. 마지막으로 시작한 파일의 표시 이름만 넣는다(순차 재생 확인용).
   - ‘Stop all playback’ 버튼(트레이 ‘Stop Playback’과 같은 `StopAll`).

### 2.3 UIA 노출 수정 (upstream 접근성 결함)

- `Resources/Theme.xaml`의 `SettingsTabControl` 템플릿 `ContentPresenter`에 `x:Name="PART_SelectedContentHost"`를 붙였다. 이것이 없으면 선택된 탭 내용이 UI Automation·화면 낭독기에 보이지 않는다(P2-A §7.2). 진단 줄을 UIA로 읽으려면 필요했다. 시각 변화는 없다.
- P2-A 때 이 결함을 별도 작업 칩으로 분리했다. 이번 수정으로 그 칩은 중복이 됐다.

## 3. T2 — 앱 탭

- 각 앱 카드의 "Sound" 행을 규칙 블록으로 바꿨다. Read aloud·Icon·Card background·Clear Overrides는 그대로다.
- **소리 선택**: ‘Use the default sound’(규칙 없음 = 상속) / ‘Silent for this app’(`ExplicitSilence`, 기본으로 돌아가지 않음) / ‘This app's own list’(모드·파일·연속 중복 방지).
  - 앱 규칙은 기존 앱별 재정의처럼 **즉시 저장**한다(`OnAppSoundRuleEdited`). 상속으로 바꾸면 규칙 키를 지운다. 무음으로 바꿔도 파일 목록은 규칙에 남는다(다시 전용 목록으로 돌아올 때 복원).
  - 전용 목록에서는 보관함 파일을 체크로 추가하고 같은 Up/Down/Preview/Remove를 쓴다. 빈 전용 목록은 무음이라고 요약에 표시한다.
- **표시 정보**:
  - 요약: 상속 시 기본 규칙의 모드·파일 수(또는 "현재 무음"), 전용 목록의 모드·파일 수, 무음 설명. `SoundEnabled`가 꺼져 있으면 그 사실을 덧붙인다.
  - 수신 경로: 이번 세션에 AUMID가 관찰됐으면 `WinRT (app ID known)`(여러 개면 개수), 이름만 봤으면 `Accessibility (no app ID)`, 이번 세션에 못 봤으면 `Not seen this session`(`AppIdentityRegistry`, RAM).
  - 원래 소리 처리 상태: `ManualMuteConfirmations`를 규칙 키 → 표시 이름 → 관찰된 AUMID 순서로 찾아 읽기 전용으로 표시한다. `Not checked / Setup needed / Confirmed by you (not verified by the app) / Guidance not available`. 변경 UI와 안내 문구는 P4다.
  - 기존 이름 규칙: `PerAppSounds`에 값이 있으면 "원본 앱의 앱별 소리(재생에 쓰지 않음)", `MutedApps`에 있으면 "Filtering에서 음소거됨"을 표시만 한다. 지우거나 규칙에 자동으로 합치지 않는다.
- **같은 표시 이름·여러 AUMID**(설계 §7.1): `HasMultipleAppIds`일 때 ‘이름 기준(모든 앱 ID)’과 각 AUMID 중 규칙 키를 고르는 콤보를 보여준다.
  - 키를 바꾸면 기존 규칙을 새 키로 옮긴다. 새 키에 이미 규칙이 있으면 옮기지 않고 그 규칙을 편집 대상으로 불러온다.
  - 기본 키는 표시 이름이다(WinRT·접근성 경로 모두 매칭). AUMID 키 규칙이 있으면 그 키를 우선 편집한다.
- 목록 구성: 기존 원천(관찰된 앱, `PerAppSounds`/`PerAppIcons`/`PerAppBackgroundImages`/`SpokenMutedApps` 키)에 `AppSoundRules` 키를 더했다. AUMID 키는 이번 세션에 관찰된 표시 이름으로 묶고, 관찰되지 않은 AUMID는 키 그대로 별도 항목이 된다. 앱 이름은 번역·변형하지 않는다.
- ‘Clear Overrides’는 이제 그 앱의 소리 규칙(표시 이름 키, 관찰된 AUMID 키, 현재 편집 키)도 지운다(툴팁 "global sound로 되돌림"과 일치). ‘Only modified’ 필터는 규칙이 있는 앱도 수정된 앱으로 본다.
- `AppIdentityRegistry`에 잠금과 `FindDisplayNameForAppId`를 추가했다.

## 4. T3·T4 — 트레이, ViewModel·저장

### 4.1 트레이

- 기존 항목과 순서는 그대로 두고, ‘Always on Top/Click-Through’ 다음 구분선 뒤에 블록을 끼웠다: ‘Stop Playback’(`StopAll`), ‘Preview Default Sound’(`PreviewRule(저장된 DefaultSoundRule)`), 구분선.
- 툴팁 문구는 바꾸지 않았다.

### 4.2 저장 흐름 (구현 결정)

| 값 | 저장 방식 | undo/redo | 근거 |
|---|---|---|---|
| 기본 규칙, `PlaybackPolicy`, `AudioDispatchMode`, `SoundDisplayNames` | VM 상태 → 기존 debounce `SaveSettings` → `BuildCurrentSettingsSnapshot`이 `CopyForkExtensionsTo` 뒤 `ApplySoundUiStateTo`로 덮어씀 | 됨(다른 설정창 값과 같음) | 기존 흐름 재사용. `LoadFromSettings` 끝에서 `LoadSoundUiFromSettings`가 다시 읽으므로 undo/redo·프로필·가져오기·테마·트레이 프로필 적용 뒤에도 VM이 맞춰진다 |
| 앱별 규칙 | 즉시 `Apply`(기존 앱별 재정의와 같음) | 기존 앱별 값처럼 다음 스냅샷에 포함 | 설정창 저장은 `AppSoundRules`를 현재 설정에서 복사하므로 유실 없음 |
| 외부 폴더 추가 | 즉시 `Apply` | 안 됨(이 PC 전용 값, P1-B 규칙) | — |
| 목록 제외·실제 삭제·폴더 참조 해제 | 대기 중 편집을 먼저 저장 → `Apply` → VM 다시 읽기 | 직전 상태로 돌아갈 수 있음 | 규칙 여러 곳을 한 번에 바꾸므로 |

- 외부 폴더·확인 상태는 프로필에 들어가지 않는다(P1-B 그대로, 기존 테스트 통과).
- 미리 듣기는 조정기 `Preview`/`PreviewRule`만 쓰므로 순서·이력을 소비하지 않는다(UI 경로 테스트 `PreviewFromUi_DoesNotConsumeSequentialOrder`).

### 4.3 조정기 추가 API (P2-A 코드 소폭 변경)

- `PreviewRule(SoundRule?)`: 규칙 목록에서 처음 사용 가능한 파일을 미리 듣는다. 없으면 `SkippedNoUsableFile`.
- `GetLastStartedSoundPath()`: 마지막 알림 WAV의 전체 경로(RAM). UI는 표시 이름으로만 바꿔 보여준다.
- `PlaybackDiagnostics.LastEvent`(기본값 `"None"`): `Started/Completed/Failed/Cancelled/Expired/Queued/PreviewQueued/PreviewStarted/SpeechStarted/Skipped*/StoppedAll`. `ToSummary()`는 바꾸지 않았다(경로·이름 없음 유지).

### 4.4 레이아웃·문자열

- 문자열은 코드에 영문 그대로다. 표시 문자열은 `Helpers/SoundUiText.cs`에 모았고 저장값(enum 이름)과 분리했다.
- 한국어 문구가 1.3~1.5배 길어질 때를 고려했다. 모든 새 `TextBlock`은 `TextWrapping="Wrap"`이다. 행 버튼은 `WrapPanel`에 넣어 좁은 창(최소 폭 620)에서 줄바꿈된다. 고정 폭 열을 새로 만들지 않았고, 앱 카드의 규칙 블록은 기존 150px 라벨 열을 쓰지 않고 두 열을 모두 차지한다.
- **배율 100/150/200%와 한국어 표시 확인은 하지 않았다**(화면 확인 금지, P3에서 문구를 바꾼 뒤 확인 필요).

## 5. 명령·결과 원문

### 5.1 빌드·테스트

```text
# 시작 기준 (HEAD c6a2a1d)
dotnet build src/NotificationsPro/NotificationsPro.csproj   → 경고 0개 / 오류 0개
dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj
통과!  - 실패:     0, 통과:   494, 건너뜀:     0, 전체:   494

# 최종
dotnet build src/NotificationsPro/NotificationsPro.csproj --no-incremental   → 경고 0개 / 오류 0개
dotnet build tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj       → 경고 0개 / 오류 0개
dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj
통과!  - 실패:     0, 통과:   521, 건너뜀:     0, 전체:   521, 기간: 8 s
```

### 5.2 추가 테스트 (+27)

| 파일 | 수 | 내용 |
|---|---|---|
| `SettingsSchemaV6Tests` | +6 | v5 이관 정책(간격 0, 나머지 기본, 재시작 후 유지), 스키마 없음·손상 파일 0, 신규 4, v6 정책 불변(4·7), 이관 프로필 스냅샷 0·v6 스냅샷 유지 |
| `LegacySettingsImporterTests` | +1 | 원본 가져오기 후 간격 0(재로드 포함) |
| `SoundDisplayNamesTests`(신규) | +4 | 신규 빈 값, 저장·로드 왕복과 무효 키·값 정리, 프로필 이전(외부 경로 제외), Clone 깊은 복사 |
| `SoundUiViewModelTests`(신규) | +14 | 기본 규칙 편집 왕복(체크 순서·Up·모드·중복 방지·무음·사용 여부, 새 VM 재로드, 체크 해제), 무관한 값 저장 시 규칙·정책·모드·표시 이름 비유실, 정책·모드 저장과 undo/redo, 앱별 상속/무음/전용 전환과 무음 비상속, 여러 AUMID 키 선택·이동·재로드, 가져오기 이름 충돌 표시 이름 저장·자동 규칙 추가 없음, 목록 제외 vs 확인 후 실제 삭제, 외부 폴더 파일 삭제 불가, 폴더 추가 거부 사유·참조 등록, 미리 듣기 순서 비소비, 진단 줄(카운터·표시 이름, 앱 이름·경로 없음, 중지), 탭 진입 스캔, Clear Overrides 규칙 제거, AUMID 키 규칙의 항목 묶음과 확인 상태 표시 |
| `PlaybackCoordinatorTests` | +2 | `PreviewRule`(첫 사용 가능 파일, 이력 없음), `LastEvent`·마지막 시작 파일 |

### 5.3 변이 검사

```text
M1 BuildCurrentSettingsSnapshot의 ApplySoundUiStateTo(snapshot) 주석 처리
   → SoundUiViewModelTests 실패 5 / 통과 8 (당시 13개)
M2 LoadFromSettings의 LoadSoundUiFromSettings(s) 주석 처리
   → SoundUiViewModelTests 실패 5 / 통과 8
(둘 다 원복 후 전체 통과 확인)
```

### 5.4 실제 Windows 실행 (언패키지 포크 exe, 15:44~15:57)

실행 전 상태(조회만):

```text
settings: 6748 bytes, 10/09/2026 14:16:45   sha256=0C9198D409253C24   (세션 임시 폴더에 사본)
schema=6 SoundEnabled=False Mode=OverlayAccepted Paused=False OverlayVisible=True
DefaultSoundRule={"Enabled":true,"Mode":"Single","Files":[],...}
PlaybackPolicy={"MinIntervalPerAppSeconds":4,"MaxQueued":5,"MaxQueueWaitSeconds":30,"OverflowBehavior":"Queue"}
NP AppData exists: False      %AppData%\NotificationsProKR-Mnyang\sounds 없음
46104 NotificationsPro C:\Program Files\WindowsApps\NotificationsPro_1.1.10.37_...   ← 원본, 조회만
8252  powershell.exe -NoProfile -ExecutionPolicy By...  (오전 8:34:32)               ← PS1 감시기, 조회만
```

무음 WAV(ffmpeg `anullsrc`, PCM 16비트 44.1kHz 모노, 내용이 달라야 중복 제거를 피하므로 길이를 다르게 함):

```text
a datasize 176400 seconds 2.0 max abs 0
b datasize 220500 seconds 2.5 max abs 0
```

① 실행·설정창·컨트롤 존재(UIA, 두 번째 실행으로 설정창 열기):

```text
fork PID=47204 exited=False / settings window found: True
SoundEnabledToggle ControlType.Button / DefaultRuleEnabledToggle Button "Use the default sound"
DefaultRuleSilenceCheck CheckBox / PreviewDefaultSoundButton / StopSoundPreviewButton
OpenManagedSoundFolderButton / AddSoundFolderButton / RefreshSoundLibraryButton / ImportSoundFilesButton
ShowWindowsSoundsCheck / SoundLibraryFolderList List / SoundLibraryList List
AudioDispatchModeCombo ComboBox / MinIntervalPerAppSlider Slider / OverflowBehaviorCombo ComboBox
MaxQueuedSoundsSlider / MaxQueueWaitSlider / StopAllPlaybackButton
SoundPlaybackStatusLine ControlType.Text "Playback: Idle | Queued: 0 | Started: 0 | Completed: 0 | Interval skips: 0 | Max at once: 0 | Last: none"
```

(`DefaultRuleEditor` AutomationId는 `ContentControl`이라 UIA에 나오지 않는다. 안쪽 콤보·목록은 보인다.)

② 가져오기 2건: ‘Import files...’를 UIA Invoke로 눌렀다. 파일 대화상자의 파일 이름 칸은 UIA 패턴이 노출되지 않았다(Pane, 패턴 없음). 그래서 **Win32 메시지(WM_SETTEXT·BM_CLICK)로 경로를 넣고 열기를 눌렀다**(마우스·키보드 입력 없음). 첫 Invoke가 두 번 처리돼 대화상자가 2개 떴고, 하나씩 a·b로 채웠다.

```text
message: Import — p2b-silent-b.wav: imported
  text: p2b-silent-a / Managed folder · Available · 2.0 s
  text: p2b-silent-b / Managed folder · Available · 2.5 s
open dialogs: 0
```

③ 기본 규칙: 보관함 체크박스 a→b를 UIA Toggle로 켰다. `SoundEnabled` 토글을 켜고 최소 간격 슬라이더를 0으로 설정했다(RangeValue). 저장 결과:

```text
DefaultSoundRule={"Enabled":true,"Mode":"Single","Files":["sounds/p2b-silent-a.wav","sounds/p2b-silent-b.wav"],...}
PlaybackPolicy={"MinIntervalPerAppSeconds":0,...}  SoundEnabled=True
```

- **재생 방식 콤보는 UIA로 바꾸지 못했다.** 펼친 뒤 항목이 콤보·프로세스 아래 어디에서도 찾히지 않았다(2회 시도 후 중단). 그래서 포크를 종료하고 `settings.json`의 `DefaultSoundRule.Mode`만 `Sequential`로 고친 뒤 다시 실행했다. 다시 연 화면은 `play mode (UI): Sequential — list order, then wrap`, 선택 목록 `1. p2b-silent-a / 2. p2b-silent-b`였다.

④ 토스트와 진단 줄(`tools/P0Probe`, 무음 토스트 `<audio silent="true"/>`, 진단 줄을 100~120ms 간격으로 UIA 조회):

```text
# run A: P0Probe 1개(--seconds 10 --toasts 3) → 실제로는 약 5초 간격 2건 발행
  4.82s  Playback: Notification sound | Queued: 0 | Started: 1 | ... | Max at once: 1 | Last: started | Last file: p2b-silent-a
  6.95s  Playback: Idle | ... | Completed: 1 | ... | Last: completed | Last file: p2b-silent-a
  9.83s  Playback: Notification sound | Queued: 0 | Started: 2 | ... | Last: started | Last file: p2b-silent-b
 12.41s  Playback: Idle | ... | Completed: 2 | ... | Last file: p2b-silent-b

# run B: 간격 0, P0Probe 3개 동시(각 1건)
  4.32s  Playback: Notification sound | Queued: 2 | Started: 3 | Completed: 2 | Interval skips: 0 | Max at once: 1 | Last: started | Last file: p2b-silent-a
  6.49s  Playback: Notification sound | Queued: 1 | Started: 4 | Completed: 3 | ... | Last file: p2b-silent-b
  9.30s  Playback: Notification sound | Queued: 0 | Started: 5 | Completed: 4 | ... | Last file: p2b-silent-a
 11.30s  Playback: Idle | Queued: 0 | Started: 5 | Completed: 5 | Interval skips: 0 | Max at once: 1 | Last: completed | Last file: p2b-silent-a
 13.83s  (no window)        ← 한 번만 읽기 실패, 다음 조회에서 정상
# run C: 간격 4초(슬라이더), 3개 동시
  4.94s  Playback: Notification sound | Queued: 0 | Started: 6 | Completed: 5 | Interval skips: 2 | Max at once: 1 | Last: started | Last file: p2b-silent-b
  7.70s  Playback: Idle | ... | Completed: 6 | Interval skips: 2 | ...
# run D: 간격 0, 3개 동시 → 대기 생기자 ‘Stop all playback’ Invoke
  4.26s  Playback: Notification sound | Queued: 2 | Started: 7 | Completed: 6 | Interval skips: 2 | Max at once: 1 | Last: started | Last file: p2b-silent-a
        >>> Stop all playback invoked
  4.88s  Playback: Idle | Queued: 0 | Started: 7 | Completed: 6 | Interval skips: 2 | Max at once: 1 | Last: stopped | Last file: p2b-silent-a
```

- 순차: A → B(run A), A → B → A(run B, 순환 포함), 이어서 B(run C) → A(run D). 기본 규칙 순서 이력이 실행 내내 이어졌다.
- 겹침 없음: 모든 줄에서 `Max at once: 1`.
- 쿨다운: 4초에서 같은 AUMID(PowerShell) 3건 중 1건 재생·2건 건너뜀.
- 재생 중지: 활성 1건 중지·대기 2건 취소 → `Idle | Queued: 0 | Last: stopped`.

⑤ 종료·원복:

```text
fork running: False   (Stop-Process -Force, 이름 NotificationsProKR-Mnyang 한정)
restored sha256=0C9198D409253C24 (before 0C9198D409253C24)   ← 바이트 동일
sounds 폴더(가져오기로 생긴 p2b-silent-a/b.wav) 삭제 → %AppData%\NotificationsProKR-Mnyang 에는 settings.json(6748)만 남음
NP AppData exists: False
original 46104 alive=False  PS1 8252 alive=True
```

- **원본 앱(PID 46104)이 종료돼 있었다.** 실행 전에는 살아 있었다. 다른 PID의 `NotificationsPro` 프로세스도 없었다. 지난 60분 응용 프로그램 로그에 오류·WER 이벤트는 없었다. 이번 작업의 명령은 프로세스 이름 `NotificationsProKR-Mnyang`과 포크 PID로만 대상을 골랐고, UIA·Win32 메시지도 포크 PID의 창에만 보냈다. 원인은 확인하지 못했다. **원본 앱은 다시 시작하지 않았다**(원본 앱은 조회만 한다는 제약). 사용자가 확인해 주기 바란다.
- **설정창이 두 번 사라졌다**(가져오기 직후 약 1분 뒤, 그리고 설정 변경 1.5초 뒤). `PopupAutoClose=False`였다. 설정창은 `Esc`로 닫히고, 열 때마다 포커스를 가져간다. 그래서 사용자가 다른 작업 중 누른 키에 닫혔을 가능성이 있지만 확인하지 못했다. 두 번째 실행으로 다시 열어 진행했다.
- 알림음은 무음 WAV, 토스트는 무음이라 이번 검증에서 가청 소리를 내지 않았다. 다만 원본 앱·PS1 감시기가 테스트 토스트에 반응했는지는 확인하지 않았다(조회만). 토스트 배너와 설정창은 화면에 나타났다.
- 증거 파일: `.p0\p2b-runA.jsonl`, `.p0\p2b-run{B,C,D}-{1,2,3}.jsonl`(P0Probe 기술 로그, gitignore 대상, 토스트 문구는 일반 시험 문구).

## 6. 자동 검증 vs 실제 Windows 검증

| 항목 | 자동 | 실제 Windows |
|---|---|---|
| 이관 간격 0·신규 4·v6 불변·가져오기 0 | 7 | 현재 포크 v6 설정이 4초 그대로임을 UIA 슬라이더 값(4)으로 확인 |
| `SoundDisplayNames` 저장·정리·프로필 이전 | 4 + VM 2 | 미확인(이름 충돌 가져오기를 실제로 하지 않음) |
| 소리 탭 컨트롤 존재(UIA) | — | **확인**(①) |
| 보관함 나열·상태·길이 | VM 2 | **확인**(②) |
| 가져오기(복사, 덮어쓰지 않음) | VM 3 | **확인**(대화상자 입력은 Win32 메시지, ②) |
| 기본 규칙 체크 → 순서대로 저장 | VM 1 | **확인**(③) |
| 재생 방식 콤보 조작 | VM 1 | **미확인**(UIA로 항목 선택 불가, JSON으로 설정) |
| 순차 재생·겹침 없음 | P2-A 테스트 + VM | **확인**(④ run A·B) |
| 최소 간격(쿨다운) | P2-A 테스트 + VM | **확인**(④ run C, 슬라이더로 4 설정) |
| ‘Stop all playback’ | VM 1 | **확인**(④ run D) |
| 트레이 ‘Stop Playback’·‘Preview Default Sound’ | 빌드만(같은 조정기 메서드) | **미확인**(트레이 메뉴는 마우스 없이 열 수 없음) |
| 트레이 툴팁 | 변경 없음 | 미조회 |
| 앱 탭 상속/무음/전용·AUMID 선택 | VM 5 | **미확인**(실제 실행에서 앱 탭 조작 안 함) |
| 목록 제외 vs 실제 삭제·확인 대화상자 | VM 2 | 미확인(삭제 대화상자 실제 표시 안 함) |
| 외부 폴더 추가(폴더 선택기)·거부 사유 | VM 2 | 미확인 |
| 미리 듣기 순서 비소비 | VM 1 + P2-A | 미확인 |
| 한국어 문구 길이·150/200% 배율 | — | **미확인**(화면 확인 금지, P3 이후) |
| `%AppData%\NotificationsPro` 미생성 | 테스트 격리 | 실행 전후 확인 |
| 설정 원복 | — | 바이트 동일 확인 |

## 7. 보존 계약 영향

| ID | 영향 | 근거 |
|---|---|---|
| **K05** 글꼴·색상·배경·아이콘·테두리·애니메이션·강조 | **변경 없음.** 앱 카드의 Icon·Card background 행, 오버레이 렌더링 코드는 손대지 않았다. `Theme.xaml` 변경은 탭 템플릿의 이름 부여 1곳(시각 변화 없음) | 기존 테스트 통과 |
| **K08** Windows 소리·사용자 WAV·앱별 소리 | **변경(의도).** ① 기존 기본 소리 콤보·Test·Browse와 앱별 드롭다운을 규칙 UI로 대체했다. P2-A 이후 생긴 "기존 UI 변경이 재생에 반영되지 않는" 공백이 사라졌다. ② Windows 소리는 ‘Show Windows sounds’로 계속 고를 수 있다. ③ 사용자 WAV 가져오기는 이제 같은 이름 파일을 **덮어쓰지 않는다**(upstream Browse는 덮어씀). ④ 원본 "None"은 새 UI에서 ‘Silent for this app’으로 실제 무음이 된다. 이관된 "None" 항목은 P1-B대로 상속(규칙 없음)으로 보이고, 원본 값은 앱 카드에 읽기 전용으로 표시된다. ⑤ 이관 파일의 앱별 간격은 0이라 연속 알림이 대기열로 모두 재생된다(upstream의 "끊고 새로 재생"은 재현하지 않음, P2-A 결정) | VM·스키마 테스트, 실제 실행 ③④ |
| **K11** 테마·프로필·가져오기/내보내기·주야간 | **추가(이전 가능 값 1개).** `SoundDisplayNames`가 프로필·내보내기에 들어간다(외부 경로 키 제외). 이관 프로필 적용 시 간격 0. 기본 규칙·정책·모드는 기존 저장 흐름으로 프로필 적용·undo/redo·테마 적용 후 VM에 다시 읽힌다. 이 PC 전용 값 처리는 그대로 | 표시 이름·이관·LocalOnly 테스트 |
| **K12** 트레이·단축키·CLI·설정창 스타일·undo/redo | **추가.** 트레이에 2항목+구분선(기존 항목·순서 유지). 설정창 소리 탭 구성이 바뀌었고 앱 카드 "Sound" 행이 규칙 블록으로 바뀌었다(탭 구조 유지). 기본 규칙·정책 편집은 undo/redo 대상이고, 앱별 규칙은 기존 앱별 재정의처럼 즉시 저장된다. 선택 탭 내용이 UIA에 노출된다(접근성 개선) | undo/redo 테스트, 실제 실행 ① |
| 프라이버시 하드 제약 | **위반 없음.** 진단 줄은 상태·카운터·파일 표시 이름만 담고 앱 이름·본문·경로를 넣지 않는다(테스트로 고정). 새 디스크 쓰기는 사용자가 요청한 가져오기(관리 `sounds/`), 확인 뒤 삭제, 설정 저장뿐이다. 새 로그 없음 | `StatusLine_ShowsStateCountersAndLastFile_ButNoAppName` |

## 8. P3에 넘길 문자열 목록 위치

| 파일 | 대략 개수 | 내용 |
|---|---|---|
| `src/NotificationsPro/Helpers/SoundUiText.cs` | 약 70 | 파일·폴더 상태, 출처, 폴더 거부 사유, 가져오기·삭제 결과, 모드·트리거·초과 동작·재생 상태·마지막 결과, 원래 소리 상태, 수신 경로, 진단 줄 틀 |
| `src/NotificationsPro/Views/SettingsWindow.xaml`(P2-B 추가분) | 약 95 | 소리 탭·앱 탭의 `Text`·`Content`·`ToolTip`·`AutomationProperties.Name`·`Run` |
| `src/NotificationsPro/ViewModels/SettingsViewModel.SoundUi.cs` | 약 40 | 보관함 메시지, 확인 대화상자 제목·본문, 앱 요약·기존 이름 규칙 안내, 대화상자 제목·필터, 트리거 옵션 2개 |
| `src/NotificationsPro/ViewModels/SoundRuleEditorViewModel.cs`, `AppSoundRuleViewModel.cs` | 6 | 재생 방식 3개, 앱 소리 선택 3개 |
| `src/NotificationsPro/App.xaml.cs` | 2 | 트레이 ‘Stop Playback’, ‘Preview Default Sound’ |
| (기존) Help 탭 | 1~2 | "Settings → Sounds → Browse" 안내가 이제 ‘Import files’와 맞지 않음 |

## 9. 미완료 · 다음 단계

1. 트레이 두 항목의 실제 클릭, 앱 탭·삭제 대화상자·폴더 선택기의 실제 조작은 하지 않았다(§6).
2. 재생 방식 콤보의 UIA 조작이 안 된다. WPF `ComboBox` 항목이 UIA에서 선택되지 않는 원인은 조사하지 않았다(실사용 마우스·키보드 조작에는 영향 없을 것으로 보이나 미확인).
3. 낭독 미리 듣기(`TestNarration` → `SpokenNotificationService.PlayPreviewAsync`)는 여전히 공유 재생 권한 밖이다(P2-A §9.3-5). 이번 지시 범위(T0~T4)에 없어 하지 않았다.
4. 한국어·배율 레이아웃 확인은 P3 문자열 작업 뒤에 해야 한다.
5. 원본 앱 종료·설정창 사라짐의 원인 미확인(§5.4).

## 10. 검토자 결정 요청

1. **원본 앱 종료:** 실제 실행 도중 원본 앱(PID 46104)이 종료돼 있었다(원인 미확인, 다시 시작하지 않음). 사용자에게 알리고 재시작 여부를 맡길지 정해 달라.
2. **외부 폴더 ‘Remove reference’ 추가:** 지시 목록에는 없지만, 잘못 추가한 폴더를 되돌릴 방법이 없어 넣었다. 참조만 해제하고 디스크 파일은 지우지 않는다. 그 폴더 파일은 모든 규칙에서 빠진다(확인 대화상자). 유지할지 정해 달라.
3. **저장 방식 분리:** 기본 규칙·정책·모드·표시 이름은 debounce 저장(undo 대상), 앱별 규칙은 즉시 저장(기존 앱별 재정의와 같음)으로 나눴다. 이 분리를 확정해 달라.
4. **기존 VM 코드 잔존:** XAML에서 뺀 `TestSoundCommand`, `BrowseCustomSoundCommand`, `AvailableWindowsSounds`, `SelectedWindowsSound`, `PerAppSoundOptions`, `PerAppConfigEntry.Sound`를 upstream 병합 편의를 위해 남겼다. `DefaultSound`/`PerAppSounds` 기존 필드도 계속 저장된다(값은 더 이상 UI로 바뀌지 않음). 제거할지, P3까지 둘지 정해 달라.
5. **진단 줄에 파일 표시 이름 포함:** 순차 재생 확인을 위해 마지막 시작 파일의 표시 이름을 넣었다(앱 이름·경로·본문은 없음). 허용할지 정해 달라.
6. **‘Clear Overrides’가 소리 규칙도 지움:** 표시 이름 키·관찰된 AUMID 키 규칙을 함께 지운다. 이 범위가 맞는지 확인해 달라.
7. **`Theme.xaml` 수정:** UIA 노출을 위해 upstream 템플릿에 `PART_SelectedContentHost` 이름을 붙였다(P2-A에서 분리했던 작업 칩과 중복). 이번 커밋에 포함할지, 별도 커밋으로 나눌지 정해 달라.
8. **실제 실행 중 `settings.json` 직접 수정:** 재생 방식 콤보를 UIA로 조작하지 못해 포크 종료 상태에서 `Mode`만 JSON으로 고쳤다(종료 후 바이트 원복). 이 검증 방식을 인정할지 정해 달라.
