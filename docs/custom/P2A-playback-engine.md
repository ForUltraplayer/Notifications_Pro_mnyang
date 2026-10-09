<!-- @file P2A-playback-engine.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro 커스텀 포크 P2-A(WAV 재생 엔진: 규칙 선택기·재생 조정기·파일 검증·보관함 서비스, 최소 연결) 구현·검증 보고서. -->

# P2-A 구현 보고서: WAV 재생 엔진 (선택기 · 조정기 · 보관함 · 최소 연결)

- 작성일: 2026-10-09 (Asia/Seoul)
- 구현 담당: Claude Opus 5.5 / 검토: Claude Fable 5.1 / 결정: 사용자(rudals252)
- 기준: 설계서 v1.1 §6.2, §7 전체, §12, §14.1("WAV 선택"·"재생"), §17("정확한 재생 엔진"). `P1B-schema-policy.md` §1.1, §3.3, §8(미룬 것 1·2·6)
- 저장소: `F:\dev\notifications-pro-custom`. 시작 HEAD는 `a1b0964`(P1-B)였고 작업 트리는 깨끗했다. **commit하지 않았다**
- 범위 밖(손대지 않음): UI(소리 탭·앱 탭·트레이 메뉴, XAML), 문자열 리소스화, `src/NotificationsPro.Package/**`, upstream 문서, ai-tts 폴더(사용자 WAV 포함, 읽지도 복사하지도 않음), 원본 앱·PS1·패키지 LocalCache·레지스트리. 새 NuGet·새 로깅 없음

## 0. 결론

1. **엔진은 WinRT `Windows.Media.Playback.MediaPlayer`로 정했다**(후보 A·B가 아닌 C, §1). 기존 낭독이 이미 쓰는 엔진이다. 후보 A(`SoundPlayer.PlaySync`)는 **다른 스레드에서 `Stop()`을 불러도 멈추지 않았다**(5초 파일, 0.31초에 Stop → 5.08초에 반환, 3회 동일). C는 `C:\Windows\Media` WAV 70개를 모두 열었다. SoundPlayer가 받아들이는 형식(PCM 8/16/24비트, IEEE float, MS ADPCM)도 모두 재생했다. 중지는 약 3ms에 끝났고 늦은 이벤트는 오지 않았다.
2. **규칙 선택기·재생 조정기·보관함 서비스를 만들고 알림음을 조정기로 옮겼다.** 규칙(`DefaultSoundRule`/`AppSoundRules`)만 읽는다. `SoundEnabled`는 마스터 스위치로 쓴다. 낭독은 같은 재생 권한을 기다린다. 그래서 같은 알림은 WAV 다음에 낭독이 나온다. `OverlayAccepted` 모드도 새 이벤트 `NotificationAddedWithIdentity`로 AUMID 규칙을 쓴다.
3. **빌드 경고 0·오류 0, 테스트 494개 전부 통과**(384 → 494, +110). 핵심 동작을 일부러 망가뜨리는 변이 검사 8건은 모두 테스트가 잡았다(§6.3). 무음 합성 WAV로 실제 엔진·게이트·조정기를 함께 돌렸다. 동시 3건은 겹치지 않고 순차 재생됐고 같은 앱 쿨다운도 동작했다(자동 통합 테스트).
4. **실제 exe 실행은 일부만 했다.** 포크 exe 실행, 규칙 로드, 테스트 토스트 3건 동시 발행(0.2ms 안)까지는 마쳤다. 그러나 진단 상태를 읽지 못했다. 설정창 탭 내용이 UI Automation에 노출되지 않았기 때문이다(upstream 템플릿 결함, §7.2). 확인용 화면 캡처에서 사용자가 전체 화면 앱을 쓰는 중임을 보고 즉시 중단했다. 쿨다운 실측(2회차)은 하지 않았다. 설정·클립보드는 원복했다. `%AppData%\NotificationsPro`는 생기지 않았다. **"실제 실행에서 순차·쿨다운 확인"은 미완료다.**
5. **주의할 동작 변화(검토 필요):** P2-B 전까지 기존 소리 탭에서 고른 기본/앱별 소리는 알림 재생에 반영되지 않는다. 규칙이 단일 진실이고 기존 필드는 동결이기 때문이다. 이관된 설정에도 4초 쿨다운·대기열 정책이 적용된다. 설계 §7.4는 "기존 설정 이관에 새 제한을 강제하지 않는다"고 한다(결정 요청 §11-3·4).

## 1. 재생 엔진 조사 · 근거 · 실측

### 1.1 조사 방법

- `ilspycmd`로 .NET 8 `System.Windows.Extensions.dll`의 `System.Media.SoundPlayer`를 디컴파일해 기존 재생 경로를 확인했다.
  - `PlaySync()`/`Play()`는 `ValidateSoundFile` 다음에 `winmm!PlaySound`를 부른다. `ValidateSoundFile`은 mmio로 RIFF/WAVE·`fmt ` 청크를 읽고 형식 태그 **1(PCM)·2(MS ADPCM)·3(IEEE float)만** 받는다. `PlaySound`의 반환값은 무시한다.
  - `Stop()`은 `PlaySound(NULL, 0, SND_PURGE)`다.
- 세션 임시 폴더에 프로브 콘솔 앱을 만들어 세 후보를 같은 파일로 비교했다. 재생은 **무음 합성 WAV**만 했다. Windows 소리는 "열기만" 했고 재생하지 않았다. 코덱별 무음 파일은 설치된 ffmpeg로 만들었다(anullsrc). 사용자 WAV는 쓰지 않았다.

### 1.2 실측 결과 (원문 요약)

```text
== A. SoundPlayer.ValidateSoundFile (리플렉션, 재생 없음) ==
  pcm16/pcm8/pcm24/float32           OK
  extensible_pcm16 (0xFFFE)          REJECT InvalidOperationException
  mp3tag_0x55 / bad_riff / empty     REJECT
  truncated(헤더+16바이트)           OK   ← 검증은 통과하지만 아래 PlaySound는 False
  Windows\Media: 70 files, SoundPlayer OK=70, rejected=0, format tags: 0x1=70

== B. winmm PlaySound SYNC (무음 합성만) ==
  pcm16_1s return=True 1083ms | pcm8 True 1103ms | pcm24 True 1036ms | float32 True 1033ms
  extensible True 1105ms | mp3tag False 30ms | bad_riff False 0ms | empty False | truncated False | missing False

== C. SoundPlayer.PlaySync(작업 스레드) + 다른 스레드 Stop() (5초 무음) ==
  trial 0: stop@310ms, PlaySync returned@5086ms
  trial 1: stop@310ms, PlaySync returned@5082ms
  trial 2: stop@309ms, PlaySync returned@5081ms
  -- winmm PlaySound sync + PlaySound(NULL) 다른 스레드: stop@310ms returned@5077ms   ← 중지 불가

== D. WinRT MediaPlayer ==
  Windows\Media (열기만): opened=70 failed=0
  합성: pcm16/pcm8/pcm24/float32/extensible/mp3tag opened, bad_riff·empty FAILED SourceNotSupported
  play pcm16_1s → MediaEnded@1056ms | play bad_riff → MediaFailed@6ms SourceNotSupported
  ffmpeg 코덱: adpcm_ms(0x2)·adpcm_ima(0x11)·5.1 extensible·alaw·mulaw 모두 opened·MediaEnded(≈1.04~1.08s)
  중지(MTA, 5초 무음, 3회): stop@372 done@375 / stop@326 done@328 / stop@327 done@329, 이후 Ended·Failed 없음

== E. WPF System.Windows.Media.MediaPlayer (디스패처 STA) ==
  Windows\Media opened=70, 합성은 D와 같음(bad/empty는 FileNotFoundException 0x80070002)
  play pcm16_1s → MediaEnded@1006ms
```

### 1.3 선택: C (WinRT MediaPlayer)

| 기준 | A SoundPlayer | B WPF MediaPlayer | **C WinRT MediaPlayer** |
|---|---|---|---|
| 완료 이벤트 | `PlaySync` 반환뿐. 비동기 `Play`는 완료 신호 없음 | `MediaEnded` | `MediaEnded` |
| 실패 이벤트 | 형식 거부는 예외. 장치 실패는 `PlaySound` 반환을 무시해 알 수 없음 | `MediaFailed` | `MediaFailed` + `SourceNotSupported`로 파일 문제 구별 |
| 취소 | **동기 재생은 중지 불가(실측)** | `Stop/Close` | `Pause`+`Source=null`+`Dispose` 약 3ms, 늦은 이벤트 없음 |
| SoundPlayer 수용 형식 재생 | 기준 | 70/70 + 합성 동일 | 70/70 + PCM 8/16/24, float, MS ADPCM 모두 |
| 스레드 | 아무 스레드 | **디스패처 스레드 종속** | 아무 스레드(MTA에서 실측) |
| 볼륨 | 없음(프로세스 볼륨만) | 플레이어별 | 플레이어별(`Volume`, Windows 전체 볼륨 미변경) |
| 프로젝트 의존성 | 기존 | 기존(WPF) | 기존(낭독이 이미 사용, 새 NuGet 없음) |

- A는 이 단계의 필수 조건인 "취소 신뢰성"을 충족하지 못한다. 비동기 `Play`+`Stop`으로 바꾸면 중지는 되지만 완료·실패를 알 수 없다. 헤더로 길이를 계산한 타이머로 추정해야 한다.
- B와 C의 형식 수용은 같았다. C는 디스패처에 묶이지 않아 가짜 엔진 없이도 테스트 호스트에서 실제 재생 테스트가 돌았다. 낭독과 같은 엔진이라 장치·세션 동작도 하나로 맞춰진다.
- **퇴행 확인:** SoundPlayer가 받아들이는 WAV 중 C가 거부한 것은 없었다(Windows\Media 70개, 합성 PCM 8/16/24·float·MS ADPCM). 이 관계는 테스트로 고정했다(`WinRtSoundEngineTests.Probe_OpensEveryWindowsMediaWav_WithoutPlaying`, `WavHeaderInspectorTests`의 SoundPlayer 리플렉션 비교).
- 남는 위험: Windows N/KN 에디션처럼 Media Foundation이 없는 환경은 미검증이다. 이 PC 밖은 확인하지 않았다.

## 2. T1 — 규칙 선택기 `Services/SoundRuleSelector.cs`

- `Resolve(settings, appName, appId)`: `SoundEnabled=false`면 무음(`MasterSwitchOff`). 그다음 `AppIdentityMatcher.TryFind`로 AUMID → 표시 이름 순서로 앱 규칙을 찾는다.
  - `ExplicitSilence`면 무음이고 기본으로 돌아가지 않는다.
  - `Enabled=false`인 앱 규칙은 기본 규칙을 상속한다(`SoundRule` 문서 주석 그대로).
  - 기본 규칙이 `Enabled=false`면 `RuleDisabled`, 목록이 비면 `EmptyList`다.
- `Select(...)`는 후보만 정하고 이력은 바꾸지 않는다. `Commit(...)`은 **엔진이 실제 재생 시작을 알린 때만** 조정기가 부른다.
  - Single: 첫 파일. 쓸 수 없으면 무음(`AllFilesUnavailable`)이다.
  - Random: 쓸 수 있는 파일 중 균등하게 고른다(중복 경로 1회). `AvoidRepeat`면 직전 파일을 빼되, 남는 게 없으면 반복을 허용한다. 난수 함수는 주입할 수 있다.
  - Sequential: 저장 위치부터 다음 쓸 수 있는 파일을 고르고 끝에서 처음으로 순환한다. 확정 시 위치 = 재생한 파일 인덱스 + 1.
- **이력 키(구현 결정):** 앱 규칙은 `app:<매칭 키>`, 기본 규칙은 `default:<AUMID 또는 표시 이름>`. 기본 규칙을 공유하는 앱도 순서를 따로 가진다(설계 "앱별 이력 분리"). 이력은 RAM에만 있고 최대 512개다. 새 인스턴스(재시작)는 첫 파일부터 시작한다.
- 기존 `DefaultSound`/`PerAppSounds`는 읽지 않는다(테스트 `LegacyDefaultSoundAndPerAppSounds_AreNotRead`).

## 3. T2 — 재생 조정기 `Services/PlaybackCoordinator.cs`

### 3.1 구조

- 재생 권한은 하나다. 엔진 재생(알림 WAV·미리 듣기)과 허가된 낭독 차례를 합쳐 동시에 살아 있는 수를 센다. 진단 `MaxConcurrentPlaybacks`는 1을 넘지 않아야 한다.
- 대기열은 FIFO다. 알림 WAV 작업에는 규칙 스냅샷(파일 경로 목록), 이력 키, 규칙 키, AUMID, 시각만 있다. **본문·제목은 없다.** 낭독 차례도 같은 FIFO에 들어간다. 미리 듣기는 별도 칸 하나이며, 현재 재생이 끝나면 대기 중인 WAV보다 먼저 실행된다.
- 공개 메서드는 UI 디스패처에서 호출한다는 전제다. 엔진 콜백과 낭독 허가·회수 콜백은 **항상 `post`(앱에서는 `Dispatcher.BeginInvoke`)로 넘긴다.** 그 자리에서 바로 실행하지 않는다. 모든 콜백에는 작업 ID가 붙어 있어서, 끝난 작업의 늦은 완료는 무시된다.

### 3.2 정책 (`PlaybackPolicy`)

| 항목 | 동작 |
|---|---|
| `MinIntervalPerAppSeconds`(4, 0=끔) | 키는 AUMID, 없으면 표시 이름. 같은 앱이 마지막으로 **수락**(즉시 시작 또는 대기열 진입)된 뒤 간격 안에 오면 건너뛴다. 건너뛴 요청은 시각도 순서도 바꾸지 않는다 |
| `OverflowBehavior` | `Queue`: 바쁘면 대기. `Skip` 또는 `MaxQueued=0`: 바쁘면 건너뜀 |
| `MaxQueued`(5) | 대기 WAV 수 상한. 넘으면 새 요청을 건너뛴다 |
| `MaxQueueWaitSeconds`(30) | 꺼낼 때와 새 요청 때 검사해서 넘긴 대기는 만료시킨다(순서 미소비). **0 = 대기 불가(기다린 항목은 만료)**로 해석했다 |

### 3.3 취소·실패·완료

- 설정 변경 처리:
  - 소리 끔(`SoundEnabled=false`)·일시 정지: 활성 알림 WAV를 멈추고 대기 WAV를 취소한다.
  - 내용 필터(앱 음소거·음소거 규칙·키워드·조용한 시간·중복 설정), 소리 규칙, 정책, 모드, 외부 폴더 변경: 대기 WAV만 취소한다. 서명 비교로 판단한다.
  - 관계없는 설정 저장은 대기를 유지한다.
  - 낭독 대기는 낭독 서비스가 스스로 처리한다.
- 엔진 실패:
  - 시작 전 `FileUnsupported`: 그 파일을 "재생 실패"로 기록한다(파일 크기·수정 시각이 바뀌거나 새로고침하면 해제). 같은 알림에서 다음 쓸 수 있는 파일을 바로 시도한다.
  - `DeviceOrOther`·`Timeout`: 파일 기록 없이 다음 파일을 시도한다.
  - 모든 파일이 불가하면 그 알림을 건너뛴다.
  - 시작 후 실패: 권한을 반환하고 다음 작업으로 넘어간다.
- 엔진 감시: 열기 5초, 열린 뒤 (길이 + 5초), 길이를 모르면 5분이 지나면 `Timeout`으로 끝내고 권한을 반환한다.
- `StopAll()`: 활성(WAV·미리 듣기·낭독)과 대기 WAV·대기 미리 듣기·대기 낭독을 모두 취소하고, 낭독 쪽에 회수를 알린다. `StopPreview()`, `CancelPendingSounds()`도 노출했다.
- `Dispose()`: 모든 것을 멈추고 반환한다. 이후 요청은 `Disposed`다.
- Windows 전체 볼륨은 바꾸지 않는다(플레이어 볼륨 1.0).

### 3.4 낭독 통합 (`SpokenNotificationService`)

- 생성자에 `IPlaybackGate`를 받는 오버로드를 추가했다. 기존 3인자 생성자는 게이트 없이 upstream과 같게 동작한다(테스트 `WithoutGate_SpeechStartsImmediately_LikeUpstream`).
- 말할 항목을 꺼내면 `RequestSpeech`로 차례를 기다린다. 기다리는 동안 그 항목이 "현재 항목"이다. 그래서 기존 정지 경로가 그대로 대기를 취소한다: 카드 제거·만료·교체·전체 삭제(`OnVisibleNotificationsChanged`), 음성 끔·일시 정지, 설정 재평가.
  - `StopCurrentPlayback`과 `TryStartNext`(유휴일 때)에서 차례를 반환한다. 합성 실패 같은 모든 조기 종료 경로가 `TryStartNext`를 지나므로 누수가 없다(변이 M3·M7로 확인).
- 이벤트 순서는 `Accepted → Added → AddedWithIdentity → Displayed`이고 낭독은 디스패처를 한 번 거친다. 그래서 같은 알림의 WAV가 먼저 대기열에 들어가고 낭독은 그 뒤에 선다.
- 활성 WAV는 카드와 묶이지 않는다. 카드가 만료·닫혀도 WAV는 계속된다.
- `StopAll` 회수를 받으면 현재·대기 항목을 "읽음"으로 표시하고 멈춘다. 이후 설정 변경으로 다시 읽지 않는다(구현 결정, §11-6). 회수 콜백은 해당 차례에 묶여 있어서 이미 교체된 차례를 끊지 않는다.
- 테스트 전용 내부 훅 `SpeakOverrideForTesting`(false 반환 = 합성 실패와 같은 경로), `CompleteCurrentSpeechForTesting`을 추가했다(internal, `InternalsVisibleTo` 범위).

### 3.5 `QueueManager.NotificationAddedWithIdentity`

- `NotificationAdded` 바로 다음, 같은 조건에서 `(AppName, AppId)`로 발생한다. 제목·본문은 없다. 기존 `NotificationAdded(string)` 시그니처와 구독자는 그대로다.
- `AudioDispatchSubscription`은 `OverlayAccepted`에서 이 이벤트를, `PolicyAccepted`에서 `NotificationAccepted`를 구독한다. 둘을 동시에 구독하지 않는다.
- **의도한 테스트 변경 2건:** P1-B 테스트 `Dispatch_OverlayAccepted_PlaysOnlyForOverlayAcceptedNotifications`와 `Dispatch_ModeChange_Resubscribes_WithoutDoublePlay`는 "OverlayAccepted 경로의 AUMID = null"을 고정하고 있었다. 이번 결정에 따라 AUMID가 전달되도록 기대값을 바꿨다.

## 4. T3 — 파일 검증 · 보관함 `Services/SoundLibraryService.cs`, `Helpers/WavHeaderInspector.cs`

- **나열**(`Refresh`): 관리 폴더 `sounds/`와 `SoundLibraryFolders` 각 폴더의 **바로 아래** `*.wav`만 본다. 재귀·파일 감시는 없다. 새로고침 때 헤더 캐시와 재생 실패 기록을 비운다. 폴더 상태는 사용 가능/없음/드라이브 분리/허용 안 됨(링크·네트워크)으로 나온다.
- **검증:**
  - 1단계: `WavHeaderInspector`가 RIFF/WAVE·`fmt `·`data`를 읽는다. 형식 태그 1·2·3과 Extensible(PCM·float 하위 형식)을 받는다. 다른 코덱(IMA ADPCM·A-law·μ-law·MP3 in WAV)은 `Unsupported`다. 데이터가 없으면 `Damaged`, 길이는 계산할 수 있을 때만 낸다. RIFF 크기 필드는 강제하지 않는다(잘못 쓰는 도구가 많음). SoundPlayer보다 엄격하지 않게 맞췄다.
  - 2단계: `RefreshAsync(verifyWithEngine:true)`는 1단계를 통과한 파일을 **실제 엔진으로 열어 본다**(소리 없음). 열지 못하면 `Unsupported`다.
  - 런타임 게이트(`ResolvePlayablePath`)는 허용 위치 + 헤더 + 실패 기록으로 판단한다. 엔진 실제 재생 자체가 최종 확인이다.
  - 상태: `Usable`/`Missing`/`DriveUnavailable`/`Unsupported`/`Damaged`/`NotAllowed`/`PlaybackFailed`.
- **허용 위치**(런타임마다 다시 확인):
  - 관리 폴더(`AppSettingsAssetPathHelper`·`ManagedAssetPathHelper` 기존 규칙 그대로. 관리 폴더 하위 폴더 경로도 upstream처럼 재생은 허용하고, 나열만 바로 아래로 제한한다).
  - 신뢰된 Windows 소리(레지스트리 구성표).
  - 등록 폴더 **바로 아래** `.wav`.
  - UNC·`\\?\`·매핑된 네트워크 드라이브, 링크/정션(파일, 등록 폴더와 그 상위 폴더)은 거부한다. OneDrive 같은 클라우드 재분석 태그는 링크가 아니므로 막지 않는다(`LinkTarget` 기준).
  - 프로필에서 온 외부 경로를 자동 신뢰하지 않는 P1-B 규칙은 그대로다(외부 폴더 목록은 이 PC 전용).
- **가져오기**(`Import`):
  - 받는 파일: `.wav`, 헤더 유효, 64MB 이하. 원본은 읽기만 한다.
  - 같은 내용(SHA-256)이 이미 있으면 그 파일을 재사용한다(`AlreadyPresent`).
  - 이름이 충돌하고 내용이 다르면 `이름-<해시8자>.wav` 고유 내부 이름을 쓴다. 표시 이름은 원래 이름이다.
  - 임시 파일에 쓴 뒤 `Move(overwrite:false)`로 옮긴다. 덮어쓰지 않는다.
- **제외 vs 삭제:**
  - `RemoveFromRules(settings, file)`: 설정 사본에서만 뺀다. 디스크는 건드리지 않는다.
  - `DeleteManagedFile(file, deletePhysicalFile)`: 플래그가 true일 때만, 관리 폴더 바로 아래 파일만 지운다. 외부·Windows 소리는 지우지 않는다.
- **외부 폴더 등록**(`ValidateFolderForRegistration`): 정규화한 로컬 전체 경로만 받는다. 빈 값·상대 경로·UNC·네트워크·없는 폴더·링크/정션·관리 폴더는 사유와 함께 거부한다.

## 5. T4 — 연결 (최소)

- `App.xaml.cs`:
  - `WinRtSoundEngine` → `SoundLibraryService`(게이트·프로버) → `PlaybackCoordinator(post = Dispatcher.BeginInvoke)` 순서로 만든다.
  - 낭독 서비스에 게이트를 넘긴다. 알림음 콜백은 `EnqueueNotificationSound(appName, appId)`다.
  - 종료 시 조정기를 dispose하고 sink를 해제한다.
- `SoundService`: Windows 소리 목록(신뢰 목록)은 그대로다. `PlaybackSink`를 추가했고, 앱은 이것을 조정기 `Preview`로 연결한다. 그래서 기존 "Test Sound" 버튼(`PlaySound("__test__")` → `PlayWav`)도 같은 재생 권한을 쓴다. sink가 없으면 upstream `SoundPlayer` 동작 그대로다.
- `SettingsViewModel`: 기존 "Run Capture Diagnostic" 내용에 `Sound playback: <요약>` 한 줄을 추가했다(카운터만, 앱 이름·경로·본문 없음). XAML은 바꾸지 않았다.
- `StopAll()`·`Preview(path)`·`StopPreview()`·`GetDiagnostics()`는 공개 API로 노출만 했다(트레이 메뉴·UI는 P2-B).

## 6. 실행한 명령과 결과 원문

### 6.1 빌드·테스트

```text
> dotnet build ... / dotnet test ...        # 작업 시작 기준
    경고 0개 / 오류 0개
통과!  - 실패:     0, 통과:   384, 건너뜀:     0, 전체:   384, 기간: 1 s - NotificationsPro.Tests.dll (net8.0)

> dotnet build src/NotificationsPro/NotificationsPro.csproj --no-incremental
    경고 0개
    오류 0개
> dotnet build tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj --no-incremental
    경고 0개
    오류 0개

# 최종 (실제 %AppData% 전후 확인 포함)
before: NP=False KR_mtime=2026-10-09 14:16:45
통과!  - 실패:     0, 통과:   494, 건너뜀:     0, 전체:   494, 기간: 8 s - NotificationsPro.Tests.dll (net8.0)
after: NP=False KR_mtime=2026-10-09 14:16:45 temp_tests=False
```

같은 테스트를 3회 더 반복했고 모두 `통과! 494`였다(실제 엔진 타이밍 테스트의 불안정 여부 확인).

실패·재시도 기록:
- 빌드 오류 1건: `Timer`가 WinForms·Threading 사이에서 모호했다(CS0104). `System.Threading.Timer`로 바꿔 해결했다.
- 테스트 실패 1건: P1-B의 AUMID null 기대 테스트였다. 의도한 변경이라 기대값을 바꿨다(§3.5).
- 변이 검사 스크립트가 원본을 되돌릴 때 수정 시각이 빌드보다 오래되어 MSBuild가 **변이된 DLL을 그대로 재사용**했다. 그 결과 새 테스트 1건이 거짓 실패했다. 원인을 확인하고 원복 후 `touch`와 `--no-incremental` 재빌드를 넣어 해결했다. 최종 결과는 깨끗한 재빌드 기준이다.
- 정션 테스트가 세션 임시 폴더 정리를 막아 빈 폴더가 남았다. 테스트 끝에 정션 자체를 지우도록 고쳤다. 남은 테스트 임시 폴더(이번 세션 생성분)는 지웠다. 최종 `temp_tests=False`다.

### 6.2 추가 테스트 (+110)

| 파일 | 개수 | 내용 |
|---|---|---|
| `SoundRuleSelectorTests.cs` | 26 | 단일(반복·첫 파일만·불가 시 무음), 빈 목록 3모드, 랜덤 1개 반복·다수 균등(주입 난수 상한 확인)·기본 난수 범위·중복 방지·중복 허용·직전만 남을 때 반복·불가 파일 건너뜀, 순차 순환·결손 건너뜀, 모두 불가 시 이력 불변, 실패 후 순서, 미확정 선택 비소비, 재시작 시 처음부터, 앱별·기본 규칙 앱별 이력, 이력 상한, AUMID>이름>기본, 명시적 무음 비상속, 비활성 앱 규칙 상속, 비활성 기본 규칙, 마스터 끔, 기존 필드 미사용 |
| `PlaybackCoordinatorTests.cs` | 29 | 한 번에 하나·도착 순서, 대기 초과, Skip 모드, 대기 만료 비소비, 쿨다운(앱별·0=끔·순서 비소비), 무음·일시 정지, 규칙/필터 변경 시 대기 취소·무관 저장 유지·소리 끔/일시 정지 시 활성 중지, 시작 전 실패 시 다음 파일·파일 기록·장치 실패 비기록·전부 실패 시 건너뜀·시작 후 실패·엔진 예외, StopAll과 늦은 완료 무시, Dispose, 미리 듣기(순서·비소비·1개 대기·중지·소리 꺼도 재생·불허 파일), 낭독 차례(대기·해제 후 다음·허가 전 취소·post 경유·StopAll 회수·규칙 변경에도 유지), 진단에 앱 이름·경로 없음 |
| `SpokenPlaybackIntegrationTests.cs` | 12 | 실제 `SpokenNotificationService`+`QueueManager`+조정기(가짜 엔진): WAV→낭독(두 모드), 두 알림 WAV·낭독·WAV·낭독 직렬, WAV 뒤 대기 중 카드 닫힘·전체 삭제·교체 시 낭독 취소, 활성 WAV는 카드 제거로 미취소, 대기 중 음성 끄면 권한 반환, StopAll 후 재낭독 없음, 카드당 1회, 게이트 없을 때 upstream 동작, 합성 실패 시 권한 반환 |
| `SoundLibraryServiceTests.cs` | 20 | 바로 아래만 나열, 헤더 기반 상태, 엔진 확인, 폴더 없음·드라이브 분리, 결손 규칙 파일, 허용 위치(관리·Windows·등록 폴더만, 하위·외부·UNC·None·탈출 거부), 손상 거부·실패 기록·파일 교체/새로고침 시 해제, 가져오기(원본 해시·시각 불변·임시 파일 없음)·충돌 고유 이름·동일 내용 재사용·거부 3종, 목록 제외(디스크 불변)·삭제 플래그·외부 삭제 거부, 폴더 등록 정규화·UNC/`\\?\`/상대/빈 값/없음/관리 폴더 거부, 정션 거부(등록·런타임) |
| `WavHeaderInspectorTests.cs` | 12 | PCM 8/16/24 길이, float·Extensible, MS ADPCM, 기타 코덱 미지원 3종, 빈 파일·잘못된 헤더·데이터 없음·없는 파일, 잘못된 RIFF 크기 허용, **SoundPlayer 검증(리플렉션)이 받는 합성 WAV를 거부하지 않음**, Windows\Media 전수 |
| `WinRtSoundEngineTests.cs` | 5 | 실제 엔진: Windows\Media 전수 열기(재생 없음), 손상 파일 열기 실패, 무음 재생 시작·완료 1회, 즉시 중지 후 콜백 없음, 손상 파일 재생 실패 이벤트 |
| `RealEnginePlaybackTests.cs` | 2 | 실제 엔진+보관함 게이트+조정기(무음 0.4초 WAV): 동시 3건 `Started/Queued/Queued` → 실제 재생 구간이 겹치지 않음·최대 동시 1, 같은 앱 3건(쿨다운 4초) → 1회 재생·2회 건너뜀 |
| `NotificationAcceptedTests.cs` | +4 | `AddedWithIdentity` 순서(Added 직후·Displayed 전)·AUMID 전달, 오버플로·미리보기에서 Added와 정확히 같은 조건, 기존 Added 구독자 영향 없음, OverlayAccepted 중복 재생 없음 |

모든 파일 테스트는 `TestAppDataIsolation` 임시 루트 아래에서 돈다. 실제 엔진 재생 테스트는 무음 파일만 재생한다. Windows 소리는 열기만 한다.

### 6.3 변이 검사 (테스트가 실제로 잡는지 확인)

```text
[M1 선택 시점에 확정]                 실패 1  FailureBeforeStart_TriesTheNextUsableFile_...
[M2 늦은 완료의 작업 ID 확인 제거]     실패 1  StopAll_StopsActive_..._ALateCompletionCannotTouchTheNextPlayback
[M3 TryStartNext의 차례 반환 제거]    실패 1  SpeechThatFailsToStart_ReturnsTheTurn_SoTheNextWavPlays
[M4 Overlay 경로를 기존 Added로]      실패 3  Dispatch_* (AUMID 전달)
[M5 쿨다운 끔]                        실패 1  Cooldown_SkipsTheSameAppWithinTheInterval_ButNotOtherApps
[M6 MS ADPCM 거부]                    실패 2  WavHeaderInspectorTests (SoundPlayer 호환)
[M7 StopCurrentPlayback의 반환 제거]  실패 1  SpeechDisabledWhileWaiting_ReleasesTheTurn_SoTheNextWavPlays
[M8 규칙 변경 시 대기 유지]           실패 2  SoundRuleChange_.../ContentFilterChange_...
```

8건 모두 실패로 잡혔다. 원복과 깨끗한 재빌드 후 다시 494개가 모두 통과했다. 첫 M3 시도에서는 살아남았다. 이것을 보고 합성 실패 경로를 흉내 내는 테스트 훅과 테스트를 추가했다.

### 6.4 실제 Windows 실행 (언패키지 포크 exe) — 일부만 수행

실행 전 상태(조회만):

```text
NP exists=False  KR exists=True   settings.json 6748  2026-10-09 오후 2:16:45
schema=6 SoundEnabled=False Mode=OverlayAccepted Speech=False MaxVisible=40 Paused=False
DefaultSoundRule={"Enabled":true,"Mode":"Single","Files":[],...}  PlaybackPolicy={"MinIntervalPerAppSeconds":4,"MaxQueued":5,"MaxQueueWaitSeconds":30,"OverflowBehavior":"Queue"}
hash=0C9198D409253C24   (세션 임시 폴더에 사본 보관)
46104 NotificationsPro C:\Program Files\WindowsApps\NotificationsPro_1.1.10.37_...   ← 원본, 조회만
8252  powershell.exe -NoProfile -ExecutionPoli…  (2026-10-09 오전 8:34:32)            ← PS1 감시기, 조회만
trusted registry events for target: Notification.Default   ← Windows Notify System Generic.wav가 신뢰 목록에 있음
target bytes=248092 tag=1 ch=2 rate=48000 avg=192000       ← PCM, 약 1.29초
```

1회차(쿨다운 0, 15:03):

```text
[runA] settings: SoundEnabled=True Files=C:\Windows\Media\Windows Notify System Generic.wav MinInterval=0 Mode=OverlayAccepted
[runA] started PID=49872 exited=False
[runA] probes exit codes: 0,0,0
  toast-sent ×3: 06:03:32.98468 / 06:03:32.98469 / 06:03:32.98484 UTC (P0Probe 3개 동시 실행, 0.2ms 안)
[runA] tray:            ← 작업 표시줄 UIA 트리에서 트레이 아이콘을 찾지 못함
진단 읽기 실패: 설정창 System 탭을 선택해도 탭 내용(버튼)이 UIA 트리에 없음 (raw view 24개 = 탭 머리글뿐)
실행 중 설정 파일: SoundEnabled=True Files=C:\Windows\media\Windows Notify System Generic.wav (앱이 신뢰 목록 경로로 정규화해 다시 저장)
```

- 진단 줄을 읽으려고 설정창 영역을 캡처했다. 그 결과 **사용자가 전체 화면 앱을 쓰는 중**임을 확인했다. 마우스·키보드 입력이 필요한 방법(좌표 클릭)은 사용자 작업을 방해하므로 쓰지 않았다. 캡처 파일은 즉시 지웠다. 포크 프로세스는 경로를 확인한 뒤 종료했다(`Stop-Process -Force`). **2회차(쿨다운 4초)는 실행하지 않았다.**
- 1회차에서 알림음은 최대 3번(약 1.3초씩) 사용자 스피커로 재생됐을 수 있다. 청취·진단으로 확인하지는 못했다.
- 원복과 확인:

```text
restored hash=0C9198D409253C24 (before 0C9198D409253C24)    ← 바이트 동일
clipboard unchanged: True                                   ← 진단 버튼을 누르지 않아 클립보드 변경 없음(저장 사본은 삭제)
NP exists=False
original 46104 alive=True  PS1 8252 alive=True
fork running: False
```

- 증거로 남은 파일: `.p0\p2a-runA-1..3.jsonl`(P0Probe 기술 로그, gitignore 대상, 토스트 문구는 일반 시험 문구).

## 7. 자동 검증 vs 실제 Windows 검증

### 7.1 표

| 항목 | 자동 | 실제 Windows |
|---|---|---|
| 엔진 선택 근거(중지 불가·형식 수용·이벤트) | 엔진 테스트 5 | **프로브 실측**(§1.2) |
| SoundPlayer 수용 WAV 비퇴행 | 리플렉션 비교 + Windows\Media 전수 열기 | 프로브 70/70 |
| 선택 모드·이력·결손·실패 후 순서 | 26 | — |
| 한 번에 하나·대기·만료·쿨다운·취소·늦은 완료 | 29(가짜 엔진) + 2(실제 엔진, 무음) | **exe 미확인**(진단 읽기 실패) |
| WAV→낭독 직렬·카드 만료/닫힘/교체/삭제 | 12(실제 낭독 서비스, 합성은 훅) | 미확인(낭독 꺼진 설정) |
| 실제 TTS 합성 경로와 차례 반환 | 훅으로 같은 코드 경로 | **미확인**(실제 합성 미실행) |
| 보관함 나열·검증·가져오기·삭제 분리·UNC/정션 | 20 | 임시 폴더 정션 실생성으로 확인(테스트 안) |
| 규칙 파일 로드(신뢰 Windows 소리) | 스키마 테스트(P1-B) | **확인**(앱이 정규화해 다시 저장) |
| 토스트 3건 동시 발행 | — | 확인(0.2ms 안, 종료 코드 0) |
| 포크가 3건을 수신·재생·순차 처리 | — | **미확인** |
| 쿨다운 실측 | 실제 엔진 테스트 | **미실행** |
| 트레이 상태 | — | **미확인**(UIA에서 아이콘 미발견) |
| `%AppData%\NotificationsPro` 미생성 | 테스트 전후 | 실행 전후 확인 |
| 원본 앱·PS1 무변경 | — | 조회만, 실행 유지 |

### 7.2 실제 실행 진단 읽기가 막힌 원인

- `Resources/Theme.xaml`의 `SettingsTabControl` 템플릿에 있는 `ContentPresenter`에는 `x:Name="PART_SelectedContentHost"`가 없다. WPF는 이 이름의 프레젠터로만 선택된 탭의 내용을 자동화 트리에 노출한다. 그래서 **설정창의 모든 탭 내용이 UI Automation과 화면 낭독기에 보이지 않는다.**
- upstream 접근성 결함이며 이번 범위(XAML 변경 금지) 밖이다. 별도 작업 칩으로 분리했다.

## 8. 보존 계약 영향

| ID | 영향 | 근거 |
|---|---|---|
| **K08** Windows 소리·사용자 WAV·앱별 소리 | **변경(의도), 확인 필요 사항 있음.** ① 엔진이 `SoundPlayer` → WinRT MediaPlayer로 바뀌었다. SoundPlayer가 받던 WAV는 계속 받는다(실측·테스트). ② 재생 근거가 규칙으로 바뀌었다. 이관된 설정은 이관 시점 값이 `Single` 규칙이라 같은 소리가 난다. 그러나 **P2-B 전까지 기존 소리 탭에서 바꾼 기본/앱별 소리는 반영되지 않는다**(동결 결정의 결과). ③ upstream은 연속 알림이 오면 이전 소리를 끊고 새 소리를 냈다(`PlaySound` 교체). 이제는 대기열(최대 5·30초)로 순서대로 낸다. ④ 이관 설정에도 4초 쿨다운이 적용된다(§11-4). ⑤ Windows 소리 목록·신뢰 경로·Test Sound 버튼은 유지된다(Test Sound는 이제 같은 재생 권한을 씀). ⑥ 현재 포크 설정은 `SoundEnabled=false`라 지금 체감 변화는 없다 | 선택기·조정기·엔진 테스트, 프로브 |
| **K09** 음성 낭독 | **유지 + 직렬화.** 카드당 1회, 제거·만료·교체·전체 삭제 시 중지와 대기 제거, 음성 끔·일시 정지 시 중지, 설정 재평가는 기존 코드 경로 그대로다(새 테스트 12개로 고정). 바뀐 점: WAV가 켜져 있으면 같은 알림의 낭독이 WAV가 끝난 뒤 시작된다(WAV 길이만큼 지연). 게이트 없이 만들면 upstream과 같다. **낭독 미리 듣기(`PlayPreviewAsync`)는 아직 공유 권한 밖이다**(P2-B, §10-2) | `SpokenPlaybackIntegrationTests` |
| **K07** 중복·연속 제한·조용한 시간·집중·발표 | **변경 없음.** 판정 코드는 손대지 않았다. 새 이벤트는 `NotificationAdded`와 같은 조건이다. 앱별 최소 간격은 재생 단계의 별도 정책이며 알림 수락·표시에는 영향이 없다. 필터·조용한 시간 설정이 바뀌면 대기 WAV를 취소한다(본문 재평가 불가, 설계 §7.4) | 기존 테스트 전부 통과, 필터 변경 취소 테스트 |
| 프라이버시 하드 제약 | **위반 없음.** 대기 항목·진단·이력에 본문이 없다(규칙 경로·이력 키·AUMID·시각·카운터만). 새 디스크 쓰기는 사용자가 요청한 가져오기(관리 `sounds/` 폴더)와 명시 플래그 삭제뿐이다. 새 로그는 없다. 진단 요약에 앱 이름·경로가 없음을 테스트로 고정했다 | `Diagnostics_ContainNoAppNamesOrPaths`, 코드 검토 |

## 9. P2-B에 넘길 UI 요구 · API 표면

### 9.1 조정기 `PlaybackCoordinator`

| API | 용도 |
|---|---|
| `Preview(string ruleFileOrPath) → PlaybackRequestOutcome` | 소리 탭·앱 탭 미리 듣기. 순서·이력 비소비. 대기 1개(최신이 교체) |
| `StopPreview()` | 미리 듣기 "중지" 버튼 |
| `StopAll()` | 트레이 "재생 중지". 낭독 포함 전부 중지·대기 취소 |
| `CancelPendingSounds()` | 필요 시 대기만 비우기 |
| `GetDiagnostics() → PlaybackDiagnostics`, `ToSummary()` | 상태 표시(현재 작업·대기 수·카운터·최대 동시) |
| `event StateChanged` | 상태 표시 갱신(post로 발생) |
| `Selector`(`SoundRuleSelector`) | `GetLastPlayed`·`GetNextSequentialIndex`로 "다음 순서" 표시 가능 |

### 9.2 보관함 `SoundLibraryService`

| API | 용도 |
|---|---|
| `Refresh(includeWindowsSounds)` / `RefreshAsync(verifyWithEngine, …)` | 화면 진입·새로고침. 항목별 상태·형식·길이, 폴더별 상태 |
| `CheckRuleFile(file)` | 규칙 목록의 결손·비활성 파일 표시 |
| `Import(path) → SoundImportResult` | "가져오기"(관리 폴더로 복사). 표시 이름은 결과에만 있음(§11-8) |
| `RemoveFromRules(settings, file)` | "목록에서 제외"(설정만) |
| `DeleteManagedFile(file, deletePhysicalFile:true)` | "실제 파일 삭제"(확인 대화상자 뒤에만) |
| `ValidateFolderForRegistration(folder)` | "외부 폴더 참조" 등록 전 검증(파일 선택기로 고른 폴더) |

### 9.3 UI 요구 목록

1. 소리 탭: 규칙 편집(모드·파일 목록·순서·중복 방지·사용/무음). 보관함 목록(상태·형식·길이·출처). 가져오기·외부 폴더 참조·제외·삭제 분리. 미리 듣기·중지. 재생 정책(쿨다운·대기 수·대기 시간·Queue/Skip). 재생 상태 한 줄. 상태 줄은 UIA로 읽을 수 있어야 한다(§7.2 결함 수정 필요).
2. 앱 탭: 앱별 규칙(상속/사용/명시적 무음). 같은 표시 이름에 여러 AUMID가 있으면 선택하게 한다(`AppIdentityRegistry`, P1-B §8-6). 기존 "None" 표시 처리(P1-B 결정 요청 6).
3. 트레이: "재생 중지" → `StopAll()`.
4. 기존 `DefaultSound`/`PerAppSounds` 편집 UI를 규칙 UI로 교체하거나 숨긴다(지금은 편집해도 재생에 반영되지 않음).
5. 낭독 미리 듣기를 공유 권한으로 옮긴다. 조정기에 음성 미리 듣기 경로를 추가해야 한다.
6. 모든 새 문자열은 P3 리소스화 대상이다.

## 10. 미완료 · 다음 단계로 넘긴 것

1. **실제 exe에서 순차 재생·쿨다운·트레이 상태 확인**은 미완료다(§6.4, §7.2). 대신 자동 통합 테스트(실제 엔진, 무음)로 순차·쿨다운을 확인했다.
2. 낭독 미리 듣기 공유 권한 연결(VM 변경 필요) → P2-B.
3. 장치 변경(재생 중 기본 장치 바뀜) 실측은 하지 않았다. 엔진은 기본 장치를 따르고, 감시 타이머가 권한 반환을 보장한다(시간 초과 경로는 코드 검토만 했고 실측하지 않았다).
4. 실제 TTS 합성(WinRT·SAPI)과 차례 반환을 실제로 실행하지 않았다(훅으로 같은 코드 경로만 시험).
5. upstream `docs/PLAN.md`·`STATUS.md`·`CHANGELOG.md`는 지시대로 수정하지 않았다. 포크 이력은 `CHANGELOG-fork.md`에 있다.

## 11. 검토자 결정 요청

1. **엔진 C(WinRT MediaPlayer) 채택:** 지시의 후보는 A·B였다. A는 중지 불가를 실측했고, B는 C와 형식이 같으나 디스패처 종속이라 C를 골랐다. 확정해 달라.
2. **형식 정책:** PCM·MS ADPCM·IEEE float·Extensible(PCM/float)만 "사용 가능"으로 둔다. 엔진은 IMA ADPCM·A-law·μ-law·MP3 in WAV도 열었지만 "PCM 우선" 원칙에 따라 `Unsupported`로 둔다. 넓힐지 정해 달라.
3. **P2-A~P2-B 사이 동작:** 기존 소리 탭의 기본/앱별 소리 변경이 재생에 반영되지 않는다(동결 결정의 결과). P2-B까지 이대로 둘지, 임시로 기존 UI 변경을 규칙에 반영하는 다리를 둘지 정해 달라.
4. **이관 설정의 재생 정책:** 설계 §7.4는 "기존 설정 이관에 새 제한을 강제하지 않는다"고 한다. P1-B 이관은 기본 정책(쿨다운 4초·대기 5·30초)을 넣었고, 이번 단계부터 실제로 적용된다. 이관 파일(v5 이하)의 `MinIntervalPerAppSeconds`를 0으로 둘지 정해 달라(이미 v6가 된 포크 설정 처리 포함). 대기열 자체는 "한 번에 하나" 원칙상 끌 수 없다. upstream의 "끊고 새로 재생"은 재현하지 않았다.
5. **쿨다운·대기 해석:** 쿨다운 키는 AUMID 또는 표시 이름이다. 기준 시각은 마지막 수락(시작·대기 진입)이다. `MaxQueueWaitSeconds=0`은 "대기 불가"로 해석했다. 기본 규칙 이력은 앱별(`default:<AUMID|이름>`)로 나눴다. 확정해 달라.
6. **`StopAll`과 낭독:** 중지한 카드는 "읽음" 처리해 설정 변경 뒤에도 다시 읽지 않는다. 이 동작이 맞는지 확인해 달라.
7. **설정 변경 시 대기 취소 범위:** 소리·정책·모드·외부 폴더, 앱 음소거·음소거 규칙·키워드, 조용한 시간, 중복 설정이 바뀌면 대기 WAV를 취소한다. 소리 끔·일시 정지는 활성 WAV도 멈춘다. 미리 듣기는 영향받지 않는다. 범위가 맞는지 확인해 달라.
8. **가져오기 표시 이름 저장:** 스키마에 표시 이름 필드가 없어 결과로만 돌려준다. P2-B에서 `SoundDisplayNames` 같은 설정을 추가할지(스키마 v6 내 선택 필드) 정해 달라. 가져오기 상한 64MB도 확인해 달라.
9. **실제 실행 재시도:** 사용자가 데스크톱을 쓰지 않는 시간에, 사용자 허락을 받고 다시 실행하자. 진단은 ① P2-B의 UIA 노출 상태 줄, ② §7.2 결함 수정 뒤 기존 진단 버튼, ③ 트레이 툴팁 요약(127자 제한·트레이 UI 변경) 중 무엇으로 읽을지 정해 달라. 1회차에서 알림음이 사용자 사용 중에 재생됐을 수 있다는 점도 알린다.
10. **테스트 전용 내부 훅:** `SpokenNotificationService`에 internal 훅 2개를 추가했다(실제 합성을 대체). 이 방식을 허용할지 확인해 달라.
