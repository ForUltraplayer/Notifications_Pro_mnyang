<!-- @file P1B-schema-policy.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro 커스텀 포크 P1-B(설정 스키마 v6·이관·안전 저장, 공통 정책/음성 경로 분리, 앱 식별 전달) 구현·검증 보고서. -->

# P1-B 구현 보고서: 설정 스키마 v6 + 정책/음성 경로 분리 + 앱 식별 전달

- 작성일: 2026-10-09 (Asia/Seoul)
- 구현 담당: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: 사용자(rudals252)
- 기준: 설계서 v1.1 §6, §7.1, §7.3, §10(마지막 두 항목), §11, §12, §14.1. P0·P1-A 보고서
- 저장소: `F:\dev\notifications-pro-custom`. 시작 HEAD는 `3f002e7`(P1-A)이고 작업 트리는 깨끗했다. **commit하지 않았다**(작업 트리 변경만)
- 범위 밖(손대지 않음): `src/NotificationsPro.Package/**`, upstream `docs/PLAN.md`·`docs/STATUS.md`·`CHANGELOG.md`, ai-tts 폴더, 원본 설치 앱, PS1 감시기, `%LocalAppData%\Packages\NotificationsPro_*`, 레지스트리. WAV 규칙 엔진·재생 조정(P2), UI 문자열 리소스화(P3)

## 0. 결론

1. **스키마 v6과 이관이 동작한다.** v5 이하 파일은 기본 WAV와 앱별 WAV를 파일 1개짜리 `Single` 규칙으로 바꾸고 `AudioDispatchMode = OverlayAccepted`가 된다. 신규 설치는 `PolicyAccepted`다. 기존 필드는 지우지 않는다. 미래 스키마(>6) 파일은 읽기 전용으로 쓰며 덮어쓰지 않는다. 손상 파일은 `settings.json.corrupt-<시각>`으로 먼저 보존한다. 저장은 임시 파일을 쓴 뒤 교체한다.
2. **원본 `PerAppSounds`의 `None`·빈 값은 "무음"이 아니라 "기본 상속"이다**(코드로 확인, §1.3). 그래서 이관 때 이런 항목은 규칙을 만들지 않는다. 원본에는 앱별 명시적 무음이 없다.
3. **`NotificationAccepted`를 추가했다.** 기존 정책 판정(일시정지·집중·발표 모드, 조용한 시간, 앱 음소거, 음소거 규칙·키워드, 연속 알림 제한, 화면 내 내용 중복)을 모두 통과한 직후, 오버레이 대기열에 넣기 직전에 발생한다. 인자의 `OverlayAccepted`로 "정책은 통과했지만 오버레이가 거절함(표시 수 초과)"을 구별한다. `NotificationAdded`·`NotificationDisplayed`의 의미와 시점은 그대로다. 알림음은 모드에 따라 둘 중 한 이벤트만 구독한다.
4. **AUMID를 전달한다.** WinRT 경로는 AUMID를, 접근성 경로는 null을 `QueueManager.AddNotification`에 넘긴다. 값은 `NotificationItem.AppId`(RAM)에 담긴다. `AppIdentityMatcher`는 AUMID → 표시 이름 → 없음 순으로 찾는다. 기존 이름 기준 규칙(`PerAppSounds`, `MutedApps`)은 그대로 동작한다.
5. **빌드 경고 0·오류 0, 테스트 384개 전부 통과**(기준 314 → 384, +70). 실제 실행 2회도 했다. v5였던 포크 설정이 v6으로 바뀌었고(추가 키만 생기고 기존 값 불변), 트레이는 `WinRT (polling 1s)`, 테스트 토스트 2건 수신(`0 → 2 captured`), 오버레이 창이 표시됐다. `%AppData%\NotificationsPro`는 생기지 않았다. **원본 가져오기 버튼은 실제로 누르지 않았다**(§5, §8).

## 1. T1 — AppSettings v6

### 1.1 새 설정 (`Models/AppSettings.cs` 끝부분 + 신규 모델 파일)

| 설정 | 형식·기본값 | 저장·이전 | 이번 단계 |
|---|---|---|---|
| `UiLanguage` | `string?`: `"ko-KR"` / `"en-US"` / null(OS 기본). 그 외 값은 null로 정규화 | **이 PC 전용**. 프로필·내보내기 제외, 적용·가져오기·undo/redo 시 보존 | 모델·보존만. 변경 UI는 P3 |
| `AudioDispatchMode` | enum `OverlayAccepted`(0) / `PolicyAccepted`(1). 모델 기본 `PolicyAccepted` | JSON에 이름 문자열로 저장, 이전 가능 | 구독 전환에 사용 |
| `DefaultSoundRule` | `SoundRule` | 이전 가능 | 모델·이관만 |
| `AppSoundRules` | `Dictionary<string, SoundRule>`(키: AUMID 또는 표시 이름, 대소문자 무시) | 이전 가능 | 모델·이관만 |
| `SoundRule` | `Enabled`(true), `Mode`(`Single`/`Random`/`Sequential`), `Files`, `AvoidRepeat`, `ExplicitSilence` | — | 재생은 P2 |
| `SoundLibraryFolders` | `List<string>`(정규화한 로컬 전체 경로, UNC 거부, 중복 제거) | **이 PC 전용** | 모델·검증만 |
| `PlaybackPolicy` | `MinIntervalPerAppSeconds` 4(0=끔), `MaxQueued` 5, `MaxQueueWaitSeconds` 30, `OverflowBehavior` `Queue`/`Skip` | 이전 가능 | 모델·범위 고정만 |
| `StartMinimizedToTray` | false | 이전 가능 | 모델만(P4) |
| `RestartOnCrash` | false | **이 PC 전용**(구현 결정, §8-1) | 모델만(P4) |
| `ManualMuteConfirmations` | `Dictionary<string, ManualMuteConfirmation{State 문자열, UpdatedAt}>` | **이 PC 전용** | 모델만(P4) |

- enum은 `SafeEnumJsonConverter<T>`로 **이름 문자열**을 저장한다(번역 없음). 읽을 때 알 수 없는 문자열·숫자는 첫 값(가장 보수적인 값)으로 바뀐다. 오타 하나로 설정 전체가 "손상"으로 기본값 처리되지 않게 하기 위해서다(테스트 `UnknownEnumAndLanguageValues_FallBackSafely_…`).
- `SoundRule.Files` 저장 형식: 관리 자산은 `sounds/<파일>` 상대 경로, 신뢰된 Windows 소리는 절대 경로, `SoundLibraryFolders`에 등록된 폴더의 **바로 아래** `.wav`만 절대 경로로 저장한다. 나머지는 버린다(`AppSettingsAssetPathHelper.NormalizeForRuntime`에서 처리). 재분석 지점(reparse point) 검사는 P2 재생 시점 검증에서 한다.
- `settings.example.json`: 스키마 6과 새 키의 기본값을 반영했다. 실데이터는 없다.

### 1.2 이 PC 전용 값의 보존·제외 (`Helpers/LocalOnlySettingsHelper.cs`)

전체 설정을 clone·apply하는 기존 경로를 모두 찾아 처리했다.

| 경로 | 이전 동작 | 이후 |
|---|---|---|
| `ProfileManager.SaveProfile` | 전체 스냅샷 저장 | 전용 값을 지운 뒤 포터블 스냅샷 생성 → JSON에서 **속성째 제거**(`SerializePortable`). 외부 폴더 신뢰가 빠지므로 외부 WAV 참조도 저장되지 않음 |
| `ThemeManager.ExportSettings` | 동일 | 동일하게 제거 |
| 설정창 프로필 적용 `LoadProfile`, 트레이 프로필 적용 `ApplyProfileFromTray`, 가져오기 `ApplyImportedSettings` | `SettingsManager.Apply(스냅샷)` | **`SettingsManager.ApplySnapshot`**: 현재 전용 값을 스냅샷에 덮어쓴 뒤 적용. v6 이전 스냅샷은 소리 규칙을 변환하고 `AudioDispatchMode`는 현재 값을 유지 |
| undo/redo (`Undo`/`Redo`) | `Apply(스택 스냅샷)` | `ApplySnapshot` → 언어 등 전용 값은 되돌리지 않음 |
| 테마 적용(설정창·트레이·예약 전환) | 현재 설정 `Clone()` + 테마 속성 | 변경 없음. `Clone()`이 v6 값을 깊은 복사하므로 보존됨(테스트로 고정) |
| 설정창 일반 저장 `BuildCurrentSettingsSnapshot` | **`new AppSettings { … }`로 새로 조립** → v6 값이 매 저장마다 기본값으로 초기화되는 구조였음 | `previousSettings.CopyForkExtensionsTo(snapshot)`로 v6 값과 스키마 버전을 이어받음 |

- 위 마지막 항목은 이번에 발견한 위험이다. 처리하지 않았다면 설정창에서 아무 값이나 바꿀 때마다 이관된 사용자의 `AudioDispatchMode`가 `PolicyAccepted`로 바뀌었을 것이다. 테스트 `ViewModel_SaveSettings_CarriesForkValuesInsteadOfResettingThem`으로 고정했다.
- `ResetToDefaults`(전체 초기화)는 바꾸지 않았다. 초기화하면 언어 선택도 OS 기본으로 돌아간다(§8-3).

### 1.3 기존 소리 설정의 실제 의미 (코드 확인 결과)

| 기존 값 | 확인 근거 | 실제 의미 |
|---|---|---|
| `SoundEnabled = false` | `SoundService.PlaySound` 첫 줄 `if (!settings.SoundEnabled) return;` | 전체 소리 끔(앱별 포함). **마스터 스위치로 그대로 유지** |
| `DefaultSound = "None"`/빈 값 | `PlayWav`: `"None"`·빈 값이면 재생 안 함 | 기본 소리 무음 |
| `PerAppSounds[app] = "None"`/빈 값/관리·Windows 소리가 아닌 경로 | `AppSettingsAssetPathHelper.NormalizeForRuntime`이 `IsDefaultSound`(빈 값·"None")이거나 정규화 결과가 "None"인 항목을 **딕셔너리에서 제거**. 로드·`Apply` 때마다 실행 | 항목이 사라지므로 `PlaySound`가 `DefaultSound`를 씀 = **기본 상속** |
| 앱 탭 소리 선택 "Default" | `OnPerAppConfigChanged`: `"Default"`이면 `PerAppSounds.Remove` | 기본 상속 |
| 앱 탭 소리 선택 "None" | `PerAppSoundOptions` = "Default" + "None" + 소리 목록. "None"을 고르면 `PerAppSounds[app]="None"` → `Apply` 정규화에서 제거 | UI는 "None"을 보여주지만 **실제로는 기본 상속**(원본의 잠재 결함). 다시 열면 "Default"로 표시됨 |

따라서 이관 규칙은 다음과 같다(`Helpers/SettingsSchemaMigrator.cs`).

- `DefaultSoundRule` = `Single` + `[DefaultSound]`. `None`이면 빈 목록이다(빈 `Single` = 무음, 설계 §7.2). `Enabled = true`. `SoundEnabled`는 마스터 스위치로 따로 유지한다.
- `PerAppSounds`의 유효 항목마다 `AppSoundRules[앱 이름] = Single + [파일]`. `None`·빈 값·비신뢰 경로는 정규화로 이미 빠져 있으므로 규칙을 만들지 않는다(= 상속). `ExplicitSilence`는 이관 때 항상 false다(원본에 해당 의미가 없음).
- 이관은 `NormalizeForRuntime` **뒤에** 실행한다. 원본 앱이 런타임에 실제로 쓰는 값과 같은 기준으로 변환하기 위해서다.
- 기존 키(`SoundEnabled`, `DefaultSound`, `PerAppSounds`)는 삭제하지 않는다. 원본 앱도 이관된 파일을 읽을 수 있다(원본은 모르는 키를 무시).

## 2. T2 — 이관·미래 스키마·손상 파일·안전 저장·원본 가져오기

### 2.1 `SettingsManager.Load()` 순서

1. 파일 없음 → 신규 설치. 모델 기본값(`PolicyAccepted`)을 그대로 쓴다.
2. 1MB 초과 또는 JSON 파싱 실패(`null` 포함) → 기본값으로 시작하고 **원본 파일을 `settings.json.corrupt-yyyyMMdd-HHmmss`로 복사해 보존**한다. 이름이 겹치면 `-1`, `-2`를 붙인다. 이후 동작은 upstream과 같다(기본값으로 저장). 이 경우 기존 사용자로 보고 `OverlayAccepted`로 둔다(§8-4).
3. `SettingsSchemaVersion > 6` → `IsReadOnlyDueToNewerSchema = true`. 읽을 수 있는 값으로 이번 세션을 실행한다. 이관·시작 보정·저장은 하지 않는다. `Save()`는 아무것도 쓰지 않고 `LastSaveStatus = SkippedReadOnly`를 남긴다. 시작 시 트레이 풍선으로 경고한다(영문, P3 대상).
4. 버전이 6 미만(없음 포함) → `SettingsSchemaMigrator.MigrateLoadedSettings` → upstream `StartupSettingsMigrationHelper` → 바뀐 것이 있으면 저장한다.

상태 속성: `IsReadOnlyDueToNewerSchema`, `LoadedFileSchemaVersion`, `PreservedCorruptFilePath`, `LastSaveStatus`(`NotAttempted`/`Saved`/`SkippedReadOnly`/`Failed`). 로그는 추가하지 않았다.

### 2.2 원자적 저장 (`Helpers/AtomicFileWriter.cs`)

- 같은 폴더의 `.<파일명>.<guid>.tmp`에 UTF-8(BOM 없음, upstream `File.WriteAllText`와 같음)로 쓰고 디스크까지 flush한다. 대상이 있으면 `File.Replace`, 없으면 `File.Move`로 교체한다. 실패하면 임시 파일을 지우고 예외를 호출자에게 넘긴다.
- 적용 위치: `SettingsManager.Save`(예외는 기존처럼 삼키고 `LastSaveStatus = Failed`), `ProfileManager.SaveProfile`, `ThemeManager.SaveCustomTheme`, `ThemeManager.ExportSettings`.
- 임시 파일 이름이 `.tmp`로 끝나므로 테마·프로필 목록(`*.json`)에 잡히지 않는다.
- 쓰기 가능한 저장은 이제 **항상 스키마 6으로 기록**한다. upstream은 낮은 버전만 올렸다. 그대로 두면 더 높은 버전의 스냅샷(손으로 만든 프로필 등)을 적용했을 때 다음 시작이 읽기 전용이 된다(테스트 `ApplyingSnapshotWithNewerVersion_…`).

### 2.3 원본 설정 가져오기 (`Services/LegacySettingsImporter.cs`)

- 후보 2개(`GetDefaultSources()`):
  - `PackagedMsix`: `%LocalAppData%\Packages\NotificationsPro_h8abfdfjdvdc6\LocalCache\Roaming\NotificationsPro`. 원본 앱이 보던 경로는 `%AppData%\NotificationsPro`다.
  - `Unpackaged`: `%AppData%\NotificationsPro`.
- `DetectCandidates()`는 읽기만 한다. 존재, 크기, 수정 시각(UTC), 스키마, JSON 유효성, 1MB 초과, 미래 스키마, 자산 폴더 목록을 보고한다. `RequiresUserChoice()`는 둘 다 있으면 true다.
- `Import(candidate)`:
  1. 다시 검사한다. 포크가 읽기 전용이거나, 원본과 대상이 같은 폴더이거나, 원본이 잘못됐으면 **아무것도 바꾸지 않고** 실패를 돌려준다.
  2. 원본 JSON을 먼저 파싱한다. 실패하면 중단한다. 이때 옛 버전이 저장했을 수 있는 원본 폴더 기준 절대 경로를 `sounds/…` 같은 상대 경로로 바꾼다(`RebaseLegacyAssetPaths`).
  3. 포크 `settings.json`이 있으면 `settings.json.pre-import-<시각>`으로 복사한다.
  4. `themes`·`profiles`·`icons`·`sounds`·`backgrounds`를 상대 경로 그대로 복사한다. 재분석 지점은 건너뛴다. 내용이 같은 파일은 건너뛴다. **내용이 다른 포크 파일은 덮어쓰지 않고 `pre-import-<시각>\<폴더>\…`로 옮긴 뒤 복사한다.**
  5. v6 이관(`OverlayAccepted`, 규칙 변환)과 시작 보정을 하고, 포크의 이 PC 전용 값을 유지한 채 `SettingsManager.Apply`로 적용한다. 실행 중인 앱에서도 바로 반영되고 원자적으로 저장된다.
- 원본 폴더는 열어 읽기만 한다. 테스트에서 원본 트리의 내용 해시와 수정 시각이 가져오기 전후로 같음을 확인했다.
- UI(최소 진입점): 설정창 **Profiles 탭 IMPORT / EXPORT의 `Import from Original App` 버튼**(`SettingsViewModel.LegacyImport.cs`). 후보가 1개면 예/아니오로 확인한다. 2개면 예=첫째(MSIX), 아니오=둘째(언패키지), 취소로 고른다. 결과(복사 수, 백업 경로)는 메시지 상자로 보여준다. 문구는 영문 코드 문자열이다(P3 리소스화). 선택 UI를 정식으로 다듬는 것은 P5다.

## 3. T3 — 공통 정책 / 음성 경로 분리

### 3.1 `NotificationAccepted` 발생 지점 (`Services/QueueManager.cs`)

```
AddNotification(app, title, body, appId)
  ├ 일시정지(집중·발표 모드 포함) → return
  ├ 빈 제목·본문 → return
  ├ AppIdentities.Observe(app, appId)        ← 신규(RAM)
  ├ 조용한 시간 / 앱 음소거 / 음소거 규칙·키워드 / 연속 제한 / 화면 내 내용 중복 → return   (기존 그대로)
  ├ 연속 제한 기록, NotificationItem 생성(AppId 포함), 시각·낭독·강조 규칙 적용   (기존 그대로)
  ├ RaiseNotificationAccepted(item)          ← 신규: 여기서 NotificationAccepted
  └ EnqueueNotification(...)                 (기존 그대로: 오버플로면 count만, 아니면 표시 → NotificationAdded → NotificationDisplayed)
```

- 정책 판정 코드는 복제하지 않았다. 기존 `AddNotification`의 판정이 모두 끝난 같은 위치에서 이벤트를 낸다. 따라서 새 경로가 필터를 우회할 수 없다.
- 순서는 `Accepted → Added → Displayed`다. 설계 §7.4의 "WAV 후 낭독" 직렬화를 P2에서 하기 쉽게 하려는 선택이다. `Added`·`Displayed` 자체의 발생 조건·순서·인자는 바꾸지 않았다(회귀 테스트 `AddedAndDisplayed_KeepOriginalTiming_AcceptedComesFirst`).
- `OverlayAccepted`는 `CanOverlayAccept`로 계산한다. 교체 모드에 표시 중인 카드가 있으면 true, 아니면 `표시 수 < 최대`다. `EnqueueNotification`의 판정과 같은 조건이며, 테스트로 "`OverlayAccepted`가 true인 알림만 `Added`가 발생"함을 확인했다(일반·오버플로·교체 모드).
- 미리보기(`AddPreviewNotification`)는 `Accepted`를 내지 않는다(설계 §6.2 "테스트는 실제 수신과 분리").
- 인자 `NotificationAcceptedEventArgs`에는 `AppName`, `AppId`, `OverlayAccepted`, `AcceptedAt`만 있다. 제목·본문은 없다.

### 3.2 이벤트 중복 vs 내용 중복

- 이벤트 중복(같은 WinRT 알림을 이벤트·조회가 반복 보고)은 P1-A `WinRtPollTracker`가 그대로 처리한다. 사용자 옵션과 무관하다.
- 내용 중복(`DeduplicationEnabled`)의 기존 의미("표시 중인 카드와 같은 앱·제목·본문이 시간창 안에 있으면 버림")는 그대로다.
- 보완: 오버레이가 **받지 않은**(오버플로) 알림은 표시 목록에 없어서 기존 판정이 보지 못한다. 그래서 RAM 지문 캐시 `ContentFingerprintCache`를 추가했다. 지문은 (앱, 제목, 본문)의 SHA-256 앞 128비트이며 본문을 보관하지 않는다. 최대 256개, 사용자 시간창 TTL로 제한한다.
  - 오버플로된 알림만 기록한다. 같은 지문이 시간창 안에 다시 오면 `NotificationAccepted`만 생략한다.
  - **오버레이 동작(오버플로 count 증가 등)은 바꾸지 않는다.** `OverlayAccepted` 모드의 기존 결과도 그대로다.
  - 표시된 카드는 기록하지 않는다. 사용자가 카드를 닫은 뒤 같은 문구가 오면 원본처럼 새 알림으로 처리한다.
  - `DeduplicationEnabled = false`면 캐시를 비우고 아무것도 거르지 않는다. 같은 문구의 별개 알림이 모두 `Accepted`된다(테스트 2개).

### 3.3 알림음 구독 (`Services/AudioDispatchSubscription.cs`, `App.xaml.cs`)

- `OverlayAccepted` → `NotificationAdded`만, `PolicyAccepted` → `NotificationAccepted`만 구독한다. 구독하기 전에 항상 두 핸들러를 모두 해제하므로 동시 구독이 생기지 않는다.
- `SettingsChanged`가 오면 모드가 바뀐 경우에만 다시 구독한다. 테스트로 모드 전환 후와 관련 없는 설정을 여러 번 저장한 후에도 알림 1건당 재생 1회임을 확인했다. `Dispose` 후에는 재생하지 않는다.
- 재생 함수는 기존 `SoundService.PlaySound(appName, settings)` 그대로다(단일 파일). AUMID는 `PolicyAccepted` 경로에서만 콜백에 전달된다. upstream `NotificationAdded`는 이름만 넘기므로 `OverlayAccepted` 경로의 AUMID는 null이다(§7-2).
- 낭독(`SpokenNotificationService`)은 변경하지 않았다(`NotificationDisplayed` 기반).

## 4. T4 — 앱 식별 전달

- `NotificationListener.ExtractAndForwardWinRT`: `AddNotification(appName, title, body, TryReadAppUserModelId(notification))`. 표시 이름 규칙은 P1-A와 같다(없으면 `Unknown`). 접근성 경로는 기존 3인자 호출이므로 AppId는 null이다.
- `QueueManager.AddNotification(app, title, body, string? appId)` 오버로드를 추가했다. 기존 2·3인자 시그니처는 유지한다. `NotificationItem.AppId { get; init; }`(RAM)를 추가했다.
- `AppIdentityMatcher`(`Helpers/AppIdentityMatcher.cs`): `TryFind(map, appId, displayName)` → `AppId` / `DisplayName` / `None`. 대소문자를 구별하지 않는다(대소문자 구별 딕셔너리도 보정). `FindAppSoundRule`은 `AppSoundRules` 조회용이다(재생 적용은 P2). `ContainsDisplayName`은 `MutedApps` 같은 이름 목록용이며 기존 의미 그대로다.
- `AppIdentityRegistry`(`QueueManager.AppIdentities`): 세션 RAM에만 표시 이름 → AUMID 집합을 기록한다(최대 512개 이름 × 16개 ID). `HasMultipleAppIds`와 `GetAmbiguousDisplayNames`로 상태를 노출한다. UI는 P2다.
- 기존 `PerAppSounds`·`MutedApps`를 제거하거나 다른 앱에 자동으로 묶지 않았다. 매처는 표시 이름 키도 그대로 찾는다.
- ChatGPT와 Codex는 같은 AUMID(`OpenAI.Codex_2p2nqsd0c76g0!App`)이므로 AUMID 규칙은 둘에 함께 적용된다. 별도로 처리하지 않았다(확정 사항).
- 저장·로그 계약: AppId는 앱 식별자이며 본문이 아니다. 저장하는 곳은 사용자가 만든 `AppSoundRules`·`ManualMuteConfirmations` 키뿐이다. 수신 알림의 AppId를 디스크에 쓰는 코드는 없다.

## 5. 실행한 명령과 결과 원문

### 5.1 빌드·테스트 (자동)

```text
> dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj      # 작업 시작 기준
통과!  - 실패:     0, 통과:   314, 건너뜀:     0, 전체:   314, 기간: 1 s - NotificationsPro.Tests.dll (net8.0)

> dotnet build src/NotificationsPro/NotificationsPro.csproj --no-incremental
    경고 0개
    오류 0개
> dotnet build tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj --no-incremental
    경고 0개
    오류 0개

# 최종 (실제 %AppData% 전후 확인 포함)
before: NP=False KR_files=settings.json KR_settings_mtime=14:16:45 temp_tests=False
통과!  - 실패:     0, 통과:   384, 건너뜀:     0, 전체:   384, 기간: 1 s - NotificationsPro.Tests.dll (net8.0)
after: NP=False KR_files=settings.json KR_settings_mtime=14:16:45 temp_tests=False
```

NP = `%AppData%\NotificationsPro`, KR = `%AppData%\NotificationsProKR-Mnyang`(포크 자체 폴더, 테스트가 건드리지 않음 = 수정 시각 불변), temp_tests = 테스트 임시 루트 잔여 여부.

실패·재시도 기록:
- 첫 빌드에서 `MessageBox`가 WPF·WinForms 사이에서 모호하다는 오류 5건(CS0104)이 났다. `System.Windows.MessageBox`로 바꿔 해결했다.
- 셸 heredoc에 작은따옴표가 들어가면 `unexpected EOF`가 나는 문제가 1회 있었다(P1-A와 같은 원인). 새 파일은 파일 쓰기 도구로 만들었다.
- 테스트 실패는 없었다.

**변이 검사(테스트가 실제로 잡는지 확인):** 다음 세 가지를 임시로 되돌리고 테스트를 돌렸다. ① 일반 저장의 v6 값 이어받기 제거 ② undo를 `Apply`로 되돌림 ③ 오버플로 지문 중복 판정 끔. 결과는 `실패! - 실패: 4, 통과: 379`였고, 해당 테스트 4개가 실패했다(SaveSettings·UndoRedo·ApplyTheme·OverflowDuplicate). 원복 후 다시 전부 통과했다.

### 5.2 추가 테스트 (+70)

| 파일 | 개수 | 내용 |
|---|---|---|
| `SettingsSchemaV6Tests.cs` | 19 | 스키마 6, 신규 설치 기본값(`PolicyAccepted`·무음 기본 규칙·정책 기본값), v5 이관(유효/`None`/빈 값/외부 경로), 기본 `None`·소리 끔, 신뢰 Windows 소리, 버전 없는 파일, v6 보존, 알 수 없는 enum·언어 대체, 미래 스키마 읽기 전용(Save·Apply 후에도 파일 바이트 동일), 높은 버전 스냅샷 적용 후 6으로 저장, 손상 보존·중복 백업 이름·1MB 초과 보존·정상 파일 백업 없음, 교체 실패 시 원본 보존·임시 파일 없음, AtomicFileWriter 실패·생성·교체, v6 왕복, 규칙 파일 경로(관리 상대·외부 거부·로컬 폴더 허용·하위 폴더 거부) |
| `LocalOnlySettingsTests.cs` | 9 | 프로필·내보내기 JSON에서 전용 속성 제거(외부 WAV 참조 포함), `ApplySnapshot` 전용 값 보존·외부 폴더 신뢰 미이전, 옛 스냅샷의 현재 모드 유지·규칙 변환, VM 프로필 적용·가져오기·테마 적용·일반 저장·undo/redo에서 `UiLanguage` 등 보존 |
| `NotificationAcceptedTests.cs` | 21 | 오버플로 시 Accepted만 발생, Accepted→Added→Displayed 순서, 교체 모드 플래그, AppId 전달·본문 미포함, 정책 거절 5종(일시정지·조용한 시간·앱 음소거·음소거 규칙·키워드) + 연속 제한 + 화면 내 중복 시 미발생, 오버플로 중복 ON 억제(오버레이 count 불변), OFF 비억제 2종, 미리보기 미발생, 여러 AUMID 관찰, 구독 모드별 단일 재생·모드 전환·무관 저장·Dispose |
| `AppIdentityAndFingerprintTests.cs` | 10 | AUMID 우선, 이름 대체, 없음, 대소문자, 규칙 조회와 이름 목록 공존, 레지스트리 상한, 지문 안정성·민감도, TTL, 크기 상한, 문자열 필드 없음 |
| `LegacySettingsImporterTests.cs` | 11 | 기본 후보 경로 구성(경로 문자열만, 읽지 않음), 탐지(존재·스키마·수정 시각·자산), 둘 다 있으면 선택 필요, 잘못된·미래 원본 표시, 가져오기(자산 6개 복사·v6 이관·포크 설정 백업·**원본 해시/시각 불변**), 전용 값 보존, 충돌 자산 이동·동일 자산 건너뜀, 포크 설정 없을 때 백업 없음, 절대 경로 재기준, 잘못된 원본이면 포크 불변, 포크 읽기 전용이면 거부 |

모든 파일 테스트는 `TestAppDataIsolation` 임시 루트 아래에서만 돈다. 이관 테스트는 임시 폴더에 `Packages\NotificationsPro_h8abfdfjdvdc6\LocalCache\Roaming\NotificationsPro`와 `Roaming\NotificationsPro` 모양의 가짜 원본을 만든다. 실제 `%AppData%`·`%LocalAppData%\Packages\NotificationsPro_*`는 테스트에서 읽지 않는다.

### 5.3 실제 Windows 실행 (언패키지 포크 exe)

실행 전 상태(조회만):

```text
NP exists=False  KR exists=True
settings.json   6253 2026-10-09 오후 1:48:44          ← P1-A 실측 때 생긴 포크 설정(스키마 5)
fork settings: schema=5 SoundEnabled=False OverlayVisible=True AudioDispatchMode=
46104 NotificationsPro C:\Program Files\WindowsApps\NotificationsPro_1.1.10.37_neutral__h8abfdfjdvdc6\NotificationsPro.exe 2026-10-09 오후 1:35:34
PS1 감시기: ProcessId 8252, CreationDate 2026-10-09 오전 8:34:32
```

1회차(14:16, 이 보고서의 `Save` 한 줄 변경 전 빌드):

```text
started PID=46272 exited=False
name : Notifications Pro KR-Mnyang Notifications Pro KR-Mnyang — Listening via WinRT (polling 1s) || WinRT (polling 1s) — 0 captured, 26 in system (poll #7); events:
settings.json   6748 2026-10-09 오후 2:16:45
schema=6 AudioDispatchMode=OverlayAccepted SoundEnabled=False DefaultSound=None OverlayVisible=True UiLanguage=
DefaultSoundRule={"Enabled":true,"Mode":"Single","Files":[],"AvoidRepeat":false,"ExplicitSilence":false} AppSoundRules={} PlaybackPolicy={"MinIntervalPerAppSeconds":4,"MaxQueued":5,"MaxQueueWaitSeconds":30,"OverflowBehavior":"Queue"}
StartMinimizedToTray=False RestartOnCrash=False SoundLibraryFolders= ManualMuteConfirmations={}

# 이관 전후 키 비교
added keys: AudioDispatchMode, DefaultSoundRule, AppSoundRules, SoundLibraryFolders, PlaybackPolicy, StartMinimizedToTray, RestartOnCrash, ManualMuteConfirmations
removed keys:
changed values: SettingsSchemaVersion : 5 -> 6

# 로컬 테스트 토스트 2건 (tools/P0Probe probe --seconds 14 --toasts 2 --skip-request --out .p0\p1b-toasts.jsonl)
probe exit=0, subscribe_ok=false (COMException 0x80070490), poll_new_from_toast_aumid=2
name : Notifications Pro KR-Mnyang … — WinRT (polling 1s) — 2 captured, 26 in system (poll #36)
[Window] 'Notifications Pro Overlay' offscreen=False
경로 확인 후 Stop-Process -Id 46272 -Force → stopped: True
```

2회차(14:19, 최종 빌드 — dll 14:18:41 > SettingsManager.cs 14:18:28):

```text
started PID=43496 exited=False
name : Notifications Pro KR-Mnyang … — Listening via WinRT (polling 1s) || WinRT (polling 1s) — 0 captured, 26 in system (poll #7)
schema=6 AudioDispatchMode=OverlayAccepted SoundEnabled=False OverlayVisible=True mtime=10/09/2026 14:16:45   ← v6 파일은 다시 쓰지 않음
stopped: True
original 46104 alive: True; PS1 8252 alive: True; NP exists=False
```

- 원본 앱(46104)과 PS1 감시기(8252)는 조회만 했다. 계속 실행 중이었다.
- 포크 설정은 **P1-A가 만든 v5 파일이 있었기 때문에 "이관"으로 처리됐다**(`OverlayAccepted`). 신규 설치(`PolicyAccepted`) 생성 경로는 실제 실행으로 확인하지 않았다. 확인하려면 포크 `settings.json`을 지우거나 옮겨야 해서 하지 않았다(단위 테스트로만 확인, §8-5).
- 강제 종료라 트레이 아이콘 흔적이 남을 수 있다. 소리 OFF 기본값이라 알림음은 재생 경로까지 가지 않는다(`SoundEnabled=false`).

## 6. 자동 검증 vs 실제 Windows 검증

| 항목 | 자동 | 실제 Windows |
|---|---|---|
| v5 → v6 이관(추가 키만, 값 불변, OverlayAccepted) | 테스트 | **확인**(P1-A 포크 설정) |
| 신규 설치 = PolicyAccepted | 테스트 | 미확인(§8-5) |
| v6 파일 재시작 시 다시 쓰지 않음 | — | 확인(수정 시각 불변) |
| 미래 스키마 읽기 전용·풍선 경고 | 읽기 전용 테스트 | 미확인(풍선 표시 미확인) |
| 손상 파일 보존 | 테스트 | 미확인 |
| 원자적 저장 실패 시 원본 보존 | 잠금 상태 교체 실패 테스트 | 미확인 |
| 원본 가져오기(서비스) | 가짜 원본 구조 테스트 | **미실행**: 실제 원본 경로 탐지·가져오기 버튼은 누르지 않음(포크 설정을 실제 원본으로 바꾸는 동작이라 검토자 결정 대기) |
| 가져오기 버튼 표시 | 빌드(XAML 컴파일) | 미확인(설정창 화면 확인 안 함) |
| NotificationAccepted / Added 차이(오버플로) | 테스트 | 미확인(표시 40개를 넘기는 실측 안 함) |
| 모드별 단일 구독 | 테스트 | 소리 OFF라 재생 경로 미실측 |
| WinRT 1초 조회 유지·토스트 수신·오버레이 | — | 확인(`0 → 2 captured`, 오버레이 창 표시) |
| AUMID 전달 | 큐 수준 테스트 | 간접 확인만(수신 2건). AppId 값 자체는 노출 경로가 없어 실측하지 않음 |
| UiLanguage 보존(프로필·가져오기·테마·undo/redo) | VM 테스트 | 미확인(언어 UI 없음, P3) |
| `%AppData%\NotificationsPro` 미생성 | 테스트 전후 | 실행 전후 확인 |

## 7. 보존 계약 영향

| ID | 영향 | 근거 |
|---|---|---|
| K06 앱·키워드·정규식·범위 필터 | **유지.** 판정 코드는 바뀌지 않았다. `NotificationAccepted`는 그 판정이 끝난 뒤에만 발생하므로 음소거·규칙이 새 음성 경로에도 그대로 적용된다. 이름 기준 `MutedApps`·`PerAppSounds`는 그대로이며 AUMID는 추가 정보일 뿐이다 | 정책 거절 테스트 6종, 매처 공존 테스트 |
| K07 중복·연속 제한·조용한 시간·집중·발표 | **유지 + 보완.** 위 정책은 두 모드 모두 같다. 내용 중복은 기존 의미 그대로이고, `PolicyAccepted`에서만 오버플로 알림을 RAM 지문으로 보완 판정한다. OFF면 거르지 않는다. 이벤트 중복(P1-A)은 별개로 유지된다 | 중복 ON/OFF 테스트 |
| K08 Windows 소리·사용자 WAV·앱별 소리 | **유지.** 재생 방식은 단일 파일 그대로다. 이관된 설정은 `OverlayAccepted`라 재생 시점도 원본과 같다. 기존 필드를 유지하고 규칙은 추가만 한다. 신규 설치는 `PolicyAccepted`라 표시 수 초과 알림도 소리가 난다(설계 의도) | 이관 테스트, 실측 키 비교 |
| K09 음성 낭독 | **변경 없음.** `NotificationDisplayed`의 시점과 인자가 같다. `Accepted`가 그보다 먼저 발생하지만 낭독은 구독하지 않는다 | 순서 테스트 |
| K11 테마·프로필·가져오기/내보내기 | **변경(의도).** 프로필·내보내기 JSON에 v6 이전 가능 값이 추가되고 이 PC 전용 4개 속성은 빠진다. 적용·가져오기·undo/redo는 전용 값을 보존한다. v5 프로필·내보내기 파일을 적용하면 소리를 규칙으로 변환하고 현재 `AudioDispatchMode`를 유지한다. 저장은 원자적으로 바뀌었다. 원본 앱 자산은 가져오기 서비스로 복사할 수 있다 | LocalOnly·Importer 테스트 |
| 그 밖 | K01(리스너)은 AUMID 인자만 추가했다. K12에 Profiles 탭 버튼 1개를 추가했다. 나머지는 코드 변경이 없다 | 기존 314 테스트 통과 |
| 프라이버시 하드 제약 | 위반 없음. 새 디스크 쓰기는 설정·테마·프로필·백업·자산 복사뿐이다. 지문 캐시·식별 레지스트리는 RAM에만 있고 본문을 보관하지 않는다(지문만). 새 로그 없음 | 코드 검토, `FingerprintCache_StoresNoText` |

## 8. 미완료 · P2/P3/P4/P5로 미룬 것

1. 규칙 기반 재생(`Single`/`Random`/`Sequential`, `AvoidRepeat`, `ExplicitSilence`, `PlaybackPolicy` 적용, 재생 조정기, 외부 폴더 재분석 지점 검사, 원래 소리 수동 음소거 UI) → P2·P4. 지금 소리는 기존 `SoundEnabled`/`DefaultSound`/`PerAppSounds`로만 난다. UI에서 기존 값을 바꿔도 `AppSoundRules`는 다시 만들어지지 않는다(규칙과 기존 필드 동기화는 P2에서 결정 필요).
2. `OverlayAccepted` 경로는 upstream `NotificationAdded(string)`가 이름만 넘겨 AUMID를 모른다. P2에서 AUMID 규칙을 `OverlayAccepted` 모드에도 적용하려면 이벤트 인자 확장이나 추가 이벤트가 필요하다.
3. 언어 선택 UI·적용 → P3. 새 영문 문구(가져오기 버튼·툴팁·대화상자, 읽기 전용 풍선)도 P3 리소스화 대상이다.
4. `StartMinimizedToTray`·`RestartOnCrash`·`ManualMuteConfirmations` 동작 → P4.
5. 원본 가져오기의 정식 선택 UI, 되돌리기(백업 복원) 명령, 실제 원본 데이터로의 가져오기 실측 → P5.
6. 같은 표시 이름·여러 AUMID 선택 UI → P2(상태만 노출).
7. upstream `docs/PLAN.md`·`STATUS.md`·`CHANGELOG.md`·`REPO_MAP.md`·`docs/ARCHITECTURE.md`는 지시대로 수정하지 않았다. 포크 이력은 `docs/custom/CHANGELOG-fork.md`에 있다.

## 9. 검토자 결정 요청

1. **`RestartOnCrash`를 이 PC 전용으로 분류한 것:** 설계 §9.3·§11.2.7("프로필로 OS 자동 실행을 조용히 바꾸지 않는다")에 근거해 프로필·내보내기에서 뺐다. 반대로 `StartMinimizedToTray`는 OS 등록과 무관해서 이전 가능으로 두었다. 이 분류를 확정해 달라.
2. **원본 가져오기 실측 여부:** 버튼을 누르면 실제 MSIX 원본(`…\LocalCache\Roaming\NotificationsPro`)을 읽고, 포크 설정을 백업한 뒤 원본 값으로 바꾼다. 원본 폴더는 바뀌지 않는다. 지금 실측할지, P5 전환 때 할지 정해 달라.
3. **`ResetToDefaults`의 언어 처리:** 지금은 전체 초기화 때 `UiLanguage`·외부 폴더·확인 상태도 기본값으로 돌아가고 모드는 `PolicyAccepted`가 된다. 초기화해도 이 PC 전용 값은 유지할지 정해 달라.
4. **손상 파일 복구 시 모드:** 손상·1MB 초과로 기본값이 된 경우를 "기존 사용자"로 보고 `OverlayAccepted`로 두었다. 신규 기본값(`PolicyAccepted`)이 맞는지 정해 달라. 소리 기본값이 꺼짐이라 당장 체감 차이는 없다.
5. **포크 설정 상태:** P1-A의 v5 포크 설정이 이번 실행으로 v6(`OverlayAccepted`)이 됐다. 이 파일을 계속 둘지, 지우고 신규 설치(`PolicyAccepted`) 경로를 실측할지 정해 달라. 실행 전 사본이 세션 임시 폴더에 있다(`…\scratchpad\fork-settings-before-p1b.json`, 커밋 대상 아님).
6. **원본 UI "None" 결함 처리:** 원본 앱 탭에서 앱별 "None"을 골라도 기본 소리가 상속된다(§1.3). 이관은 실제 동작(상속)을 보존했다. P2에서 앱별 "무음"을 `ExplicitSilence`로 제공할 때 기존 "None" 표시를 어떻게 다룰지 정해 달라.
7. **오버플로 내용 중복 보완 범위:** `PolicyAccepted`에서는 오버레이 밖 중복을 지문으로 거른다. 반면 표시됐다가 닫힌 카드와 같은 문구는 원본처럼 새 알림으로 본다. 이 범위로 충분한지 확인해 달라.
