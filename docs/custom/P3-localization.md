<!-- @file P3-localization.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang P3(한국어/영어 지원) 구현 보고서: 리소스 구조, 문자열 추출 범위, 잔존 하드코딩, 검증 결과, 수동 검증 체크리스트, 커밋 그룹 A/B 파일 목록, 보존 계약 영향. -->

# P3 — 한국어/영어 지원 보고서

- 기준: HEAD `64880ce`(P2-B), 브랜치 `custom/base`. **미커밋.**
- 구현: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: rudals252
- 설계 근거: `ai-tts/docs/notifications-pro-integrated-design.md` v1.1 §10, §5.3, §11.1, §13 P3, §14.1·§14.2

## 1. 결론

1. 설정창 전체 탭·오버레이·트레이 메뉴/툴팁/풍선·메시지 상자·VM 상태 문구·리스너 상태·접근성 이름을 `.resx`(영어 기준 + `ko-KR`)로 옮겼다. 리소스 키 **1,365개**(그룹 A 1,357 + 그룹 B 8).
2. 시스템 탭에 ‘언어: 한국어 / English / OS 기본값’ 콤보를 추가했다. 선택은 즉시 적용된다. XAML은 바인딩으로, WinForms 트레이 메뉴는 재생성으로 바뀐다. 즉시 바뀌지 않는 곳은 콤보 아래 안내문으로 알린다. 전환은 설정 `Apply`를 거치지 않아 재생 대기열·다른 설정·undo 기록을 건드리지 않는다(테스트로 고정).
3. `ResetToDefaults`가 이 PC 전용 값 4개(UiLanguage, SoundLibraryFolders, ManualMuteConfirmations, RestartOnCrash)를 보존한다(테스트로 고정).
4. 빌드 경고 0·오류 0, 테스트 **521 → 554**(+33) 전부 통과. 그룹 A만 적용한 상태도 빌드 0/0·테스트 551 통과를 별도 작업 트리에서 확인했다.
5. **실제 앱 실행 검증은 하지 못했다.** 포크 인스턴스(PID 7716, `bin\Debug`, 부모 explorer.exe, 16:16:46 시작)가 떠 있어 단일 인스턴스 규칙상 새 빌드를 띄울 수 없고, 그 프로세스를 끌 권한이 없다. 이 exe는 내가 작업 도중 16:15에 기본 출력 경로로 빌드한 **P3 중간 산출물**(XAML 리소스화까지, 영어 고정)이며 탐색기에서 실행되었다(실행 주체 미확인). 대신 실제 `SettingsWindow`·`OverlayWindow` XAML을 화면에 띄우지 않고 로드하는 검증 하네스로 영어·한국어 전 탭을 확인했다(§6). 트레이·배율·IME는 §7 수동 검증이 필요하다.

## 2. 구조·키 규칙·문체

### 2.1 구조 (새 파일)

| 파일 | 역할 |
|---|---|
| `Localization/LocalizationManager.cs` | 현재 UI 문화권 보관·결정(명시 선택 > OS 기본), `CultureChanged`·`PropertyChanged("Item[]")`, `Get`/`Format`. 조회는 스레드 문화권이 아니라 선택한 문화권으로 한다. `SetCulture`는 호출 스레드(UI)와 `DefaultThreadCurrentUICulture`(새 스레드)의 `CurrentUICulture`만 바꾸고 **`CurrentCulture`(숫자·날짜 형식)는 건드리지 않는다.** `Apply()` 전 기본값은 영어(테스트가 OS 언어에 흔들리지 않음) |
| `Localization/LocExtension.cs` | `{loc:Loc 키}` — 인덱서 OneWay 바인딩. 언어 전환 시 즉시 갱신 |
| `Localization/LocFormatExtension.cs` | `{loc:LocFormat 키, Path=..., OptionPrefix=...}` — 기존 `StringFormat='... {0}'` 대체 |
| `Localization/LocOptionExtension.cs` | 저장값 문자열 목록(글꼴 굵기·정렬·모드 등) 콤보의 **표시만** 번역. 키 `Option_` + 저장값(구분자 제거·단어 첫 글자 대문자). 키가 없으면 저장값 그대로 표시 |
| `Resources/Strings.resx`, `Strings.ko-KR.resx` | 문구. 위성 어셈블리 `ko-KR\NotificationsProKR-Mnyang.resources.dll`로 빌드됨(배포 시 `ko-KR` 폴더 포함 필요) |
| `App.Language.cs` (B) | 시작 시 `UiLanguage` 적용, 전환 시 트레이 메뉴 재생성·툴팁/상태 갱신·VM 문구 갱신 |
| `ViewModels/SettingsViewModel.Language.cs` (B) | 언어 콤보, 저장(이 PC 전용, undo 제외), `RefreshLocalizedText()` |

- `OptionItem<T>`: 리소스 키로 만든 항목(`OptionItem.Localized`)은 언어가 바뀌면 `Label` 변경을 약한 이벤트로 알린다. 고정 문구 생성자는 그대로다.
- 탭 이동: 탭 머리글이 번역되므로 `TabItem.Tag`에 영어 탭 ID를 두고 `SettingsTabNavigationHelper`가 Tag도 비교한다(`NavigateToTab("Apps")`, 창 재생성 후 탭 복원 유지). 기존 `GetSelectedTabHeader()`가 StackPanel 머리글을 `"System.Windows.Controls.StackPanel"`로 반환하던 원래 결함도 Tag로 해소된다.
- 표시 문화권: 시간 표시(`DateTime` 방식)와 Voice Access 카드 이름의 날짜 형식은 UI 문화권을 따른다(영어 UI는 기존과 같은 en-US). 저장 형식은 변경 없음.
- 글꼴: `Segoe UI`를 그대로 두고 영문 글꼴 강제를 추가하지 않았다(WPF 글꼴 대체로 한글 표시).

### 2.2 키 규칙

- `영역_의미`. 영역은 탭(`Appearance`, `Behavior`, `Filtering`, `Apps`, `Layout`, `Sounds`, `Streaming`, `Accessibility`, `SettingsWindow`, `System`, `Profiles`, `Help`), 창(`Shell`, `Overlay`, `SoundRule`), 기능(`Tray`, `Listener`, `LegacyImport`, `Diagnostic`, `Archive`, `Hotkey`, `Dialog`, `Time`, `Unit`, `Error`, `About`, `Preview`, `Sound*`, `PlaybackEvent`, `ManualMute`), 공용 `Common_`, 저장값 표시 `Option_`/`ThemeMode_`.
- XAML 키는 스크립트로 영어 첫 단어들에서 만들었다(예: `Appearance_FontFamily`, `Behavior_DedupWindow`). 대문자 섹션 머리글은 `...Header`.
- 같은 영어 문구는 키 하나로 통합했다(XAML 1,049곳 → 967키, 여러 탭에 쓰이면 `Common_`). 같은 영어라도 의미가 다르면 분리했다: `Common_Preview`(미리 보기) / `Common_PreviewSound`(미리 듣기).
- 구분자뿐인 서식(`{0} · {1}`, `{0} — {1}`)은 키로 만들지 않고 코드에 남겼다.

### 2.3 문체

- 메뉴·버튼·레이블: 명사형 간결체(‘설정 열기’, ‘재생 중지’, ‘기본 소리 사용’). 설명·도움말: ‘~합니다/~하세요’.
- 고유명사·기술어 유지: Notifications Pro, WinRT, AUMID, OBS, WAV, JSON, HEX, XAML, DWM, API, Voice Access, Ctrl+…, HH:mm, settings.json, 16진수 색상, 파일 필터 패턴(테스트로 고정).
- 용어: overlay=오버레이, mute=음소거, highlight=강조, narration/read aloud=낭독/소리 내어 읽기, quiet hours=조용한 시간, burst protection=폭주 방지, session archive=세션 보관함, sound library=소리 보관함, Overlay accepted/Policy accepted=화면 수용 기준/정책 통과 기준(설계서 용어), 원래 소리 상태=미확인/설정 필요/사용자 확인 완료/안내 미지원(설계서 §5.2).
- 의미 변경 없음. 예외 1건: Help ‘Sounds all sound the same’의 "Settings → Sounds → Browse"는 P2-B 이후 없는 버튼이라 영어·한국어 모두 "Import files / 파일 가져오기"로 고쳤다(P2-B 보고서 §8에서 P3로 넘긴 항목).

## 3. 추출 범위와 개수

| 파일 | 교체 | 비고 |
|---|---|---|
| `Views/SettingsWindow.xaml` | 속성 1,043곳(`Text`·`Content`·`ToolTip`·`Title`·`AutomationProperties.Name/HelpText`) + `Run "s"` 5 + `StringFormat` 4 + 하이퍼링크 3 + 콤보 표시 템플릿 35 + (B) 언어 섹션 | 탭 12개에 `Tag` 추가, Help 탭 2열 표 TextBlock 31개에 `TextWrapping="Wrap"`(T4) |
| `Views/OverlayWindow.xaml` | 7 | 대기 문구, `+N 표시 안 됨`, 접근성 이름·HelpText, 툴팁 |
| `Views/OverlayWindow.xaml.cs` | 15 | 카드 우클릭 메뉴, 넘침 요약 대화상자 |
| `App.xaml.cs` | 58 참조 | 트레이 메뉴 전 항목·동적 라벨·툴팁·풍선 2·오류창·세션 보관함·정보창 |
| `Helpers/SoundUiText.cs` | 70 참조 | 문구를 리소스로 옮기고 enum→키 연결만 남김. `ReceivePath*` 상수는 속성으로 바뀜 |
| `ViewModels/SettingsViewModel.cs` | 62 참조 | 미리 보기 샘플, 알림 접근 상태, 대화상자·필터, 세션 보관함, 모니터 라벨, 앱별 배경 표시 |
| `ViewModels/SettingsViewModel.SoundUi.cs` | 38 참조 | 보관함 메시지·확인창, 앱 요약, 트리거·초과 옵션 |
| `ViewModels/SettingsViewModel.LegacyImport.cs` | 20 참조 | 원본 앱 가져오기 대화상자 |
| `ViewModels/SettingsViewModel.SettingsAuditPolish.cs` | 12 참조 | 캡처 진단 라벨 |
| `ViewModels/SettingsViewModel.SinglePanelEnhancements.cs` | 6 참조 | 배경 이미지 대화상자 |
| `ViewModels/SoundRuleEditorViewModel.cs`, `AppSoundRuleViewModel.cs`, `SoundFolderItemViewModel.cs`, `OptionItem.cs` | 7 | 재생 방식·앱 소리 선택 옵션, 폴더 WAV 개수 |
| `Services/NotificationListener.cs` | 28 | 상태 문구 + `ListenerModeDisplay`(표시용). `ListenerMode`는 비교용 식별자라 그대로 |
| `Services/HotkeyManager.cs` | 13 | 단축키 등록 오류 |
| `Services/LegacySettingsImporter.cs`, `LegacySettingsCandidate.cs` | 11 | 가져오기 오류·차단 사유 |
| `Services/SpokenNotificationService.cs` | 4 | ‘시스템 기본값’ 음성 이름, 음성 미리 듣기 문장 |
| `Helpers/*Formatter.cs`, `Models/NotificationItem.cs`, `Helpers/SafeErrorDialogHelper.cs`, `Converters/ColorToBrushConverter.cs` | 13 | 상대 시간(방금/N초·분·시간 전), Voice Access 일반 레이블 ‘알림’, 오류 ‘세부 정보’, 음소거 버튼 |
| `Converters/TimestampTextConverter.cs`, `VoiceAccessAutomationNameConverter.cs` | 문화권 인자만 | §2.1 표시 문화권 |

리소스 키 분포(상위): Appearance 217, Help 123, Behavior 113, Filtering 86, Layout 83, Sounds 82, Option 72, Accessibility 65, SettingsWindow 58, System 55, Apps 46, Streaming 43, Common 33, Tray 32, Listener 28.

## 4. 잔존 하드코딩 (grep 결과와 사유)

검사: XAML 표시 속성 정규식 추출, `>텍스트<` 요소, `StringFormat='단어`, C# `"영문 공백 포함"` 리터럴. 오프스크린 하네스에서 ko-KR일 때 한글 없이 영어 단어만 있는 화면 문구는 11개이며 모두 아래 의도된 항목이다.

| 위치 | 문자열 | 사유 |
|---|---|---|
| SettingsWindow 머리글 | `Notifications Pro` | 앱 이름(원본 이름, 번역 금지) |
| SettingsWindow 미리 보기 카드 | `Microsoft Teams` | 샘플 앱 이름 |
| 모양 탭 글꼴 버튼 | `OpenDyslexic`, `Segoe UI` | 글꼴 이름 |
| 단위 Run 21곳 | `px`, `ms`, `°`, `x`, `%` | 기호·단위(초 `s`만 ‘초’로 번역) |
| Help 단축키 표 | `Ctrl+Z / Ctrl+Y`, `Escape`, `--pause, --resume, --theme <name>, --send-test, --hide, --show` | 키 이름·CLI 인자(번역 금지) |
| Help 저장소 링크 | `github.com/lwytch/Notifications-Pro` | URL |
| Help·접근성 | `Microsoft Voice Access`, `Voice Access` | 제품 이름 |
| 콤보 항목 | 테마 이름(`Windows Dark` 등), 글꼴 이름, 설치 음성 이름 | 내부 프리셋 ID·OS 제공 이름. `System`/`Custom` 테마 모드만 표시 번역 |
| 소리 탭 관리 폴더 경로 | `...\sounds` | 경로 |
| C# 상수·기본값 | `Helpers/*Helper.cs` 모드 상수, `AppSettings` 기본값, `ThemePreset` 이름, `SettingsThemeService` 모드, CLI `case "--..."` | 저장값·ID |
| `NotificationListener.ListenerMode` | `Accessibility`, `WinRT (events)` 등 | 호출부가 비교하는 식별자. 화면에는 `ListenerModeDisplay` 사용. 캡처 진단의 `Live listener mode` 값은 기술 값으로 그대로 |
| `NotificationListener.SubscriptionState` | `Not attempted`/`Subscribed`/`Failed {예외} 0x…` | 기술 상태·HRESULT(번역 금지) |
| `PlaybackDiagnostics.ToSummary()` | `requested N, started N…` | 캡처 진단의 기술 카운터 줄 |
| `NotificationListener` 접근성 필터 | `Dismiss`, `Close`, `Notification center`, `Clear all notifications`, `Do not disturb` | 화면 문구가 아니라 Windows 셸 요소 이름과 비교하는 값. **참고:** 한국어 Windows에서는 셸 요소 이름이 한국어라 이 필터가 맞지 않을 수 있다(upstream 기존 동작, P3 범위 밖) |
| 미리 보기 앱 이름 | `Microsoft Teams`, `Slack`, `Outlook Mail`, `Windows Security`, `Discord` | 앱 이름(제목·본문 샘플은 번역) |
| 음성 미리 듣기 제목 | `Notifications Pro` | 앱 이름 |
| `SettingsViewModel.BrowseCustomSound` | (번역함) | P2-B 이후 XAML에서 쓰지 않는 코드지만 문구는 리소스로 옮김 |

번역할 수 없는 OS 제공 UI: 메시지 상자 버튼(예/아니요), 파일·폴더 선택 대화상자 틀, 색 선택 대화상자. Windows 표시 언어를 따른다. 원본 가져오기 확인창의 ‘예 = … / 아니요 = …’ 안내는 영어 Windows에서 한국어 UI를 쓰면 버튼(Yes/No)과 표기가 다를 수 있다.

## 5. 명령·결과 원문

포크 인스턴스(PID 7716)가 `bin\Debug\NotificationsProKR-Mnyang.exe`를 잠그고 있어 기본 출력 경로 빌드는 복사 단계에서 실패한다. 컴파일은 성공한다. 검증은 출력 경로만 scratchpad로 바꿔 수행했다(코드·설정 동일).

```text
> dotnet build src/NotificationsPro/NotificationsPro.csproj
error MSB3027: "...\obj\Debug\net8.0-windows10.0.19041.0\apphost.exe"을(를) "bin\Debug\net8.0-windows10.0.19041.0\NotificationsProKR-Mnyang.exe"(으)로 복사할 수 없습니다. 재시도 횟수(10)를 초과하여 작업을 수행하지 못했습니다. 파일이 "Notifications Pro KR-Mnyang (7716)"에 의해 잠겨 있습니다.

> dotnet build src/NotificationsPro/NotificationsPro.csproj -p:OutDir=<scratchpad>\out\app\
    경고 0개
    오류 0개

> dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj -p:OutDir=<scratchpad>\out\test\
통과!  - 실패:     0, 통과:   554, 건너뜀:     0, 전체:   554, 기간: 9 s - NotificationsPro.Tests.dll (net8.0)

(작업 시작 시 기준선, 같은 방식)
통과!  - 실패:     0, 통과:   521, 건너뜀:     0, 전체:   521, 기간: 8 s

(그룹 A만 적용한 임시 작업 트리, 기본 출력 경로)
    경고 0개
    오류 0개
통과!  - 실패:     0, 통과:   551, 건너뜀:     0, 전체:   551, 기간: 8 s
```

`bin\Debug`의 exe·dll·`ko-KR` 폴더(16:15)는 작업 도중 기본 경로로 한 번 성공한 중간 빌드다. PID 7716이 이것을 실행 중이라 최종 코드로 다시 빌드되지 않았다(잠김). 최종 확인 전에 그 인스턴스를 끄고 다시 빌드해야 한다.

## 6. 자동 검증 vs 실제 검증

### 6.1 자동 테스트 (+33)

| 테스트 | 확인 내용 | 그룹 |
|---|---|---|
| `LocalizationResourceTests.KoreanResources_ContainExactlyTheEnglishKeys` | ko-KR 키 = en 키 | A |
| `…AllValues_AreNonEmpty` | 빈 값 없음 | A |
| `…Placeholders_MatchBetweenEnglishAndKorean` | `{n[:형식]}` 토큰 집합 일치 | A |
| `…FormatStrings_AreValidInBothLanguages` | 두 언어 모두 `string.Format` 가능 | A |
| `…Values_NeverContainForkIdentityCliArgumentsOrPaths` | 포크 표시명·AppData 폴더명·EXE명·AUMID·mutex/이벤트·작업 이름·CLI 인자·`%AppData%`·`HKEY_`·`C:\` 미포함 | A |
| `…TechnicalTokens_StayUntranslatedInKorean` | 영어에 있는 기술 토큰·16진수 색·파일 패턴이 한국어에도 그대로 | A |
| `…DialogFilters_KeepPatternSegments` | 파일 필터의 패턴 부분 불변 | A |
| `…EveryKeyReferencedInXamlAndCode_Exists` | XAML `{loc:Loc}`·C# 키 리터럴이 모두 리소스에 있음(오타 검출) | A |
| `…EveryResourceKey_IsUsed` | 쓰지 않는 키 없음(`Option_`/`ThemeMode_` 제외) | A |
| `…StoredOptionValues_HaveDisplayKeys` | 콤보 저장값 전부 표시 키 존재 | A |
| `LocalizationManagerTests`(19) | 기본 영어, ko 조회, **ko 키 누락 시 영어 대체**(테스트 전용 resx), 전체 누락 시 키 반환, **OS 기본값 결정**(ko→한국어, 그 외 영어, 명시 선택 우선), 옵션 키 규칙 | A |
| `SettingsTabNavigationHelperTests.FindTabIndexByHeader_MatchesInvariantTag_WhenHeaderIsLocalized` | 번역된 머리글에서도 Tag로 탭 이동 | A |
| `UiLanguageResetTests.ResetToDefaults_KeepsTheFourLocalOnlyValues_AndResetsTheRest` | **초기화 보존** | B |
| `UiLanguageSwitchTests.SwitchingLanguage_KeepsQueuedSoundsOtherSettingsAndUndoHistory` | **전환 후 상태 유지**: 재생 중 항목·대기 2건 그대로, UiLanguage 외 설정 JSON 동일, undo 가능 여부 동일, 디스크에 즉시 저장, 리소스 문구·옵션 라벨이 한국어로 바뀜, OS 기본값은 null 저장 | B |
| `UiLanguageSwitchTests.LocalizedOptionLabel_RaisesLabelChangedWhenCultureChanges` | 옵션 라벨 변경 알림 | B |

전역 문화권을 바꾸는 테스트는 `DisableParallelization` 컬렉션에서 돌리고 끝나면 영어로 되돌린다. 기존 테스트의 영어 문구 단정은 기본 문화권이 영어라 수정 없이 통과한다(테스트 수정 0건, 탭 이동 테스트 1건 추가만).

### 6.2 오프스크린 XAML 로드 하네스 (scratchpad, 저장소 밖)

실제 빌드 산출물의 `SettingsWindow`·`OverlayWindow`를 `Show()` 없이 생성해 탭 12개를 차례로 선택하고 시각·논리 트리의 `TextBlock.Text`, 문자열 `Content`, `ToolTip`, `AutomationProperties.Name/HelpText`를 모았다. AppData는 임시 폴더로 재지정했다. 화면 표시·캡처·입력 주입 없음.

```text
== en-US: 947 distinct texts, unresolved keys: 0
== ko-KR: 935 distinct texts, unresolved keys: 0
== ko-KR texts without Hangul but with English words: 11   (§4의 의도된 항목)
== runtime switch on open window: before has 'LANGUAGE'=True, after has '언어'=True, after still has 'LANGUAGE'=False
   window title after switch: Notifications Pro — 설정
   language combo: name='언어', items=3, selected='OS 기본값'
== binding errors: 0
```

한계: 데이터가 있어야 생기는 항목 템플릿(규칙 카드·앱 카드·보관함 행)과 콤보 팝업 항목은 생성되지 않았다. 픽셀 잘림은 측정하지 않았다.

### 6.3 실제 실행 — 미실시

지시 시나리오(포크 실행 → UIA로 영어 확인 → `UiLanguage`를 ko-KR로 → 재실행 → 컨트롤 이름·트레이 툴팁 한국어 확인 → 원복)는 **하지 않았다.**

- 사용자 포크 인스턴스 PID 7716(`F:\dev\notifications-pro-custom\src\NotificationsPro\bin\Debug\...\NotificationsProKR-Mnyang.exe`, 부모 explorer.exe, 시작 2026-10-09 16:16:46)이 실행 중이다. 실행 파일은 16:15에 만든 P3 중간 빌드다. 새 빌드를 띄우면 단일 인스턴스 규칙으로 이 인스턴스의 설정창만 열리고 종료된다. 이 프로세스를 끄는 것은 권한 밖이다.
- 같은 이유로 `%AppData%\NotificationsProKR-Mnyang\settings.json`을 수정·백업하지 않았다(실행 중인 인스턴스가 덮어씀).
- 조회만 한 프로세스: PS1 감시기 PID 8252(실행 중), 원본 앱 PID 28700(조회 시 없음). 아무것도 종료하지 않았다.
- `%AppData%\NotificationsPro`(원본 폴더): 없음 확인.

## 7. 사용자 수동 검증 체크리스트

포크를 종료한 뒤 새 빌드로 실행한다. 설정창 → 시스템 탭 → ‘언어’에서 바꾼다(재시작 불필요).

### 7.1 공통 (각 배율 100% / 150% / 200%, 한국어·English 각각)

- [ ] 언어 콤보: 한국어 / English / OS 기본값 3개, 선택 즉시 설정창 전체가 바뀜. 콤보 아래 안내문 표시
- [ ] 바꾼 직후 재생 중이던 소리·대기 중인 소리가 끊기지 않음, 다른 설정이 그대로, Ctrl+Z가 언어를 되돌리지 않음
- [ ] 트레이 우클릭 메뉴 전 항목(상태 줄, 개인 정보 > 알림 열기, 접근 확인 다시 시도, 오버레이 표시/숨기기, 알림 일시 정지, 항상 위, 클릭 통과, 재생 중지, 기본 소리 미리 듣기, 집중 모드 하위, 빠른 앱 음소거/테마/프로필 하위, 모두 지우기, 세션 보관함 복사, 설정, 정보, 종료)과 트레이 툴팁이 새 언어
- [ ] 집중 모드 진행 중 언어 변경 → 남은 시간 표시가 다음 초에 새 언어로
- [ ] 앱 재시작 후 같은 언어로 시작. ‘OS 기본값’이면 한국어 Windows에서 한국어
- [ ] 설정창 제목, 왼쪽 탭 이름, 하단 ‘기본값으로 초기화’·‘테스트 알림 보내기’·‘변경 사항은 자동으로 저장됩니다’
- [ ] 기본값으로 초기화 후 언어·외부 소리 폴더가 유지됨
- [ ] 한국어 IME로 각 입력란(테마 이름, 프로필 이름, 규칙 키워드, 앱 필터, 프로세스 이름, 단축키, 시각 HH:mm)에 한글 입력·조합 중 표시 정상

### 7.2 탭별 잘림·줄바꿈 (각 배율, 좁은 창 모드 포함)

- [ ] 모양: 섹션 머리글, 글꼴 프리셋 버튼, 밀도 버튼(좁게/보통/넓게), ‘현재 프리셋: …’, 색 ‘선택’ 버튼, 콤보 표시값(굵기·정렬·시간 방식·배경 방식·이미지 맞춤/범위/세로 초점·그룹 스타일·기본 아이콘)
- [ ] 동작: 토글 행 레이블 줄바꿈, 애니메이션 스타일/방향/가속 곡선 콤보, 값 옆 ‘초’ 단위
- [ ] 필터: 음소거 목록의 ‘음소거/음소거 해제’ 버튼(고정 폭 84), 강조 규칙 카드 레이블, ‘정규식(.*)’, 일치 범위·애니메이션·테두리 방식 콤보
- [ ] 앱: 앱 카드 왼쪽 150px 열(소리 내어 읽기·소리·아이콘·카드 배경), 규칙 선택 콤보, 요약·수신 경로·원래 소리 처리 문구
- [ ] 배치: 빠른 위치 9개 버튼, 너비/높이 프리셋, 모니터 콤보(‘모니터 1: …(주 모니터)’)
- [ ] 소리: 재생 방식 콤보, 보관함 행 버튼(미리 듣기·중지·목록에서 제거·파일 삭제), 재생 상태 줄, ‘다른 소리가 재생 중일 때’ 콤보
- [ ] 스트리밍: OBS 프리셋 버튼, 크로마 색 버튼(초록/파랑/마젠타/선택...), 감시할 앱 ‘제거’(고정 폭 72)·‘앱 추가’
- [ ] 접근성: 낭독 조건·낭독 내용·음성 콤보(‘시스템 기본값(…)’), Voice Access 카드 레이블 콤보, 단축키 입력 행
- [ ] 설정창: 표시 방식 콤보(창/팝업), UI 테마 프리셋 콤보(시스템/사용자 지정), 색 항목 9개
- [ ] 시스템: 언어 섹션, 알림 접근 상태 문구, 버튼 3개 줄바꿈, 캡처 방식 콤보
- [ ] 프로필: 저장/삭제 버튼, 가져오기·내보내기·원본 앱에서 가져오기
- [ ] 도움말: 2열 표(180px 왼쪽 열, 이번에 줄바꿈 추가) — 오버레이 조작, 문제 해결, 단축키, 설정 개요
- [ ] 오버레이: ‘알림을 기다리는 중...’, ‘+N 개 표시 안 됨’ 배지, 카드 우클릭 메뉴, 넘침 요약 대화상자
- [ ] 대화상자: 초기화 확인, 소리 파일 삭제 확인, 폴더 참조 해제 확인, 세션 보관함, 캡처 진단, 원본 앱 가져오기, 단축키 오류 문구

## 8. 커밋 그룹 A/B 파일 목록

검증한 분할 패치(HEAD에 A → B 순서로 적용하면 현재 작업 트리와 같음, 줄 끝 제외):

- `C:\Users\Administrator\AppData\Local\Temp\claude\C--Users-Administrator-Desktop-windows\0ce51541-c1a6-4ded-89d7-451db1bce6ff\scratchpad\p3\P3-groupA.patch`
- `...\scratchpad\p3\P3-groupB.patch`

예: `git apply --cached P3-groupA.patch` → 커밋 A → 나머지(`git add -A` 후 B 파일 확인) → 커밋 B. 보고서·변경 이력(`docs/custom/P3-localization.md`, `docs/custom/CHANGELOG-fork.md`)은 패치에 없으므로 원하는 커밋에 넣는다.

### 그룹 A — 리소스 기반·문자열 추출 (단독 빌드 0/0, 테스트 551 통과 확인)

새 파일:
- `src/NotificationsPro/Localization/LocalizationManager.cs`, `LocExtension.cs`, `LocFormatExtension.cs`, `LocOptionExtension.cs`
- `src/NotificationsPro/Resources/Strings.resx`, `Strings.ko-KR.resx` (**혼합**, 아래)
- `tests/NotificationsPro.Tests/LocalizationResourceTests.cs`, `LocalizationManagerTests.cs`
- `tests/NotificationsPro.Tests/TestResources/FallbackStrings.resx`, `FallbackStrings.ko-KR.resx`

수정 (전부 A):
- `Converters/ColorToBrushConverter.cs`, `TimestampTextConverter.cs`, `VoiceAccessAutomationNameConverter.cs`
- `Helpers/SafeErrorDialogHelper.cs`, `SettingsTabNavigationHelper.cs`, `SoundUiText.cs`, `SpokenNotificationTextFormatter.cs`, `TimestampTextFormatter.cs`, `VoiceAccessTextFormatter.cs`
- `Models/NotificationItem.cs`
- `Services/HotkeyManager.cs`, `LegacySettingsCandidate.cs`, `LegacySettingsImporter.cs`, `NotificationListener.cs`, `SpokenNotificationService.cs`
- `ViewModels/AppSoundRuleViewModel.cs`, `OptionItem.cs`, `SettingsViewModel.LegacyImport.cs`, `SettingsViewModel.SettingsAuditPolish.cs`, `SettingsViewModel.SinglePanelEnhancements.cs`, `SettingsViewModel.SoundUi.cs`, `SoundFolderItemViewModel.cs`, `SoundRuleEditorViewModel.cs`
- `Views/OverlayWindow.xaml`, `OverlayWindow.xaml.cs`, `SettingsWindow.xaml.cs`
- `tests/NotificationsPro.Tests/SettingsTabNavigationHelperTests.cs`

### 그룹 B — 언어 선택 UI·OS 기본·초기화 보존

새 파일: `src/NotificationsPro/App.Language.cs`, `src/NotificationsPro/ViewModels/SettingsViewModel.Language.cs`, `tests/NotificationsPro.Tests/UiLanguageTests.cs`

수정 (전부 B): `src/NotificationsPro/Services/SettingsManager.cs`(`ResetToDefaults` 보존)

### 혼합 파일 (A와 B가 한 파일에 있음)

| 파일 | A | B |
|---|---|---|
| `App.xaml.cs` | `using`, 오류창·풍선·트레이 항목·라벨·툴팁·세션 보관함·정보창 문자열 | `OnStartup`의 `InitializeUiLanguage();` 한 줄, `SetupTrayIcon`을 `SetupTrayIcon` + `CreateTrayContextMenu()`로 나눔(트레이 아이콘 생성 블록을 위로 옮기고 끝에 `return contextMenu;`). **첫 B hunk가 A의 `_showHideItem` 줄과 붙어 있어 `git add -p`의 s로는 안 나뉘고 e(편집)가 필요하다** — 패치 사용 권장 |
| `Views/SettingsWindow.xaml` | 나머지 전부(`xmlns:loc`, 1,043곳, 템플릿, Tag, Help 줄바꿈) | 시스템 탭 맨 위 ‘언어’ 섹션 19줄(`<!-- KR-Mnyang fork (P3): UI language` 주석부터). 다음 A 변경과 사이에 바뀌지 않은 줄이 있어 s로 분리 가능 |
| `ViewModels/SettingsViewModel.cs` | 나머지 전부 | `PerAppConfigEntry.RefreshDisplayText()` 7줄(별도 hunk) |
| `Resources/Strings.resx`, `Strings.ko-KR.resx` | 1,357키 | `System_Language*` 8키(정렬상 연속 블록). 새 파일이라 hunk 분리가 어렵다. A 전용 사본: `...\scratchpad\p3\groupA-resx\` |

## 9. 보존 계약 영향

| 계약 | 영향 | 근거 |
|---|---|---|
| **K05** 글꼴·색상·배경·아이콘·테두리·애니메이션·강조 | **시각 변경 없음(영어 UI).** 오버레이 렌더링·스타일·`Theme.xaml` 미변경. 표시 문구와 콤보 표시값만 번역. 콤보에 표시 템플릿을 붙였지만 저장값·`DataTrigger` 비교값은 그대로. 글꼴 강제 없음. 한국어 UI에서 시간 표시(`DateTime`)는 ko-KR 날짜 형식으로 보인다 | 하네스 binding error 0, 옵션 키 테스트 |
| **K11** 테마·프로필·가져오기/내보내기·주야간 | 저장값·JSON 키·프리셋 이름 불변(테마 이름은 번역하지 않음). `UiLanguage`는 P1-B대로 프로필·내보내기 제외, 적용·가져오기·undo/redo 보존. **변경:** 전체 초기화도 이 PC 전용 값 4개를 보존한다 | P1-B `LocalOnlySettingsTests` 통과, 새 초기화 테스트 |
| **K12** 트레이·단축키·CLI·설정창 스타일·undo/redo | 트레이 항목·순서 그대로(문구만 번역, 언어 전환 시 메뉴 재생성). CLI 인자·단축키 형식 불변. 설정창 탭 구조 유지, 시스템 탭 맨 위에 언어 섹션 1개 추가. 언어 변경은 undo/redo 대상이 아님. 탭 이동은 Tag 기준 | 탭 이동 테스트, 전환 테스트(undo 불변) |
| 프라이버시 하드 제약 | 위반 없음. 새 디스크 쓰기는 사용자가 언어를 고를 때의 `settings.json` 저장뿐. 새 로그 없음. 알림 내용은 리소스·저장 경로에 들어가지 않음 | 코드 검토 |

## 10. 검토자 결정 요청

1. **실제 실행 검증:** PID 7716(16:15 중간 빌드를 탐색기에서 실행한 포크)을 사용자가 종료해 줄지, 그 뒤 기본 경로 재빌드와 §6.3 시나리오를 다시 맡길지 정해 달라. 현재 상태로는 트레이·실제 렌더링 확인이 없고 `bin\Debug`는 최종 코드가 아니다.
2. **커밋 분할 방식:** `App.xaml.cs` 첫 hunk와 새 resx 파일은 `git add -p`로 깔끔히 나뉘지 않는다. 제공한 패치(`P3-groupA.patch`/`P3-groupB.patch`)로 나눌지, `CreateTrayContextMenu` 분리를 A로 옮길지 정해 달라.
3. **콤보 저장값 표시 번역 범위:** 글꼴 굵기·정렬·애니메이션·아이콘 프리셋(종·별 등)·가속 곡선(감속·튕김·탄성·선형)까지 표시를 번역했다. 테마 이름·글꼴·음성 이름은 그대로다. 이 범위를 확정해 달라.
4. **미리 보기 샘플 문구 번역:** 테스트 알림·강조 미리 보기의 제목/본문(‘Sarah님이 메시지를 보냈습니다’ 등)과 음성 미리 듣기 문장을 번역했다(앱 이름은 유지). 유지할지 정해 달라.
5. **낭독 상대 시간:** ‘본문 + 시간’ 낭독 모드의 ‘방금/N분 전’이 UI 언어를 따른다(선택한 음성 언어가 아니라). 그대로 둘지 정해 달라.
6. **Help 문구 정정:** "Settings → Sounds → Browse"를 영어 원문에서도 "Import files"로 고쳤다(의미 정정 1건). 허용할지 정해 달라.
7. **트레이 초기 라벨:** 생성 직후 `UpdateMenuLabels()`가 덮어써 화면에 나오지 않던 ‘Enable Click-Through’/‘Disable Always on Top’ 초기 문자열은 별도 키를 만들지 않고 최종 라벨 키(‘클릭 통과’/‘항상 위’)를 썼다. 화면 동작 차이는 없다.
8. **수신 상태 문구:** `NotificationListener.StatusMessage`는 만들 때의 언어로 저장되고, 조회 주기(WinRT 1~2초, 접근성 5초)마다 새 언어로 다시 쓰인다. 즉시 재계산 구조(지연 서식)는 넣지 않았다. 이 수준으로 충분한지 정해 달라.
9. **한국어 Windows의 접근성 필터:** `NotificationListener`가 영어 셸 요소 이름(`Notification center` 등)으로 비알림 항목을 거르는 기존 로직은 한국어 Windows에서 맞지 않을 수 있다(P3 범위 밖, 미검증). 별도 과제로 둘지 정해 달라.
