<!-- @file README-fork.md -->
<!-- @author: rudals252 -->
<!-- @brief Notifications Pro KR-Mnyang 포크 안내: 원본 대비 변경점, 빌드·테스트, 배포 스크립트, 문서 위치. upstream README.md는 수정하지 않는다. -->

# Notifications Pro KR-Mnyang (포크)

[Notifications Pro](https://github.com/lwytch/Notifications-Pro)(lwytch, GPL-3.0-or-later) `v1.1.10.37`을 기반으로 rudals252가 이 PC 사용 목적에 맞게 수정한 버전입니다. upstream `README.md`·`CHANGELOG.md`는 cherry-pick 충돌을 줄이려고 그대로 두고, 포크 내용은 `docs/custom/`에만 기록합니다. 공개 저장소는 아직 정하지 않았습니다.

## 바꾼 것

- **설치 정체성 분리:** 실행 파일 `NotificationsProKR-Mnyang.exe`, 데이터 `%AppData%\NotificationsProKR-Mnyang`, AUMID `rudals252.NotificationsProKR.Mnyang`, 로그온 작업 `NotificationsProKR-Mnyang-Logon`. 원본 MSIX 앱과 함께 설치할 수 있습니다.
- **언패키지 실행:** WinRT 이벤트 구독이 안 되는 환경에서 1초 주기 조회로 알림을 받습니다.
- **WAV 기능:** 소리 보관함(가져오기·폴더 참조), 기본/앱별 규칙(단일·랜덤·순차, 명시적 무음), 재생 조정(한 번에 하나, 앱별 최소 간격, 대기열).
- **한국어/영어 UI**(시스템 탭 언어 선택).
- **설정 스키마 v6:** 이관, 안전 저장, 손상 파일 보존, 원본 설정 가져오기(프로필 탭).
- **상주:** 로그온 자동 실행(작업 스케줄러), 트레이로 시작, 다음 시작 때 비정상 종료 안내, 두 번째 실행의 CLI 인자 전달(`--quit`, `--start-minimized` 추가).
- **원래 알림음 수동 음소거 안내**(앱 탭). 자동 재시작과 사운드 구성표 변경은 제공하지 않습니다.

단계별 상세: [`CHANGELOG-fork.md`](CHANGELOG-fork.md).

## 빌드·테스트

```powershell
dotnet build src/NotificationsPro/NotificationsPro.csproj
dotnet test tests/NotificationsPro.Tests/NotificationsPro.Tests.csproj
```

포크가 `bin\Debug`에서 실행 중이면 출력이 잠깁니다. 이때는 `-p:OutDir=<다른 폴더>\`를 붙여 빌드·테스트합니다.

## 배포 스크립트 (`scripts/custom/`)

| 스크립트 | 하는 일 |
|---|---|
| `publish.ps1` | Release·win-x64·프레임워크 종속 publish → `dist\NotificationsProKR-Mnyang\`. GPL 고지(`LICENSE`, `THIRD_PARTY_NOTICES.md`, `licenses\`, `NOTICE-fork.md`, `CHANGELOG-fork.md`)와 `settings.example.json`을 복사하고 산출물을 검사합니다. |
| `install.ps1` | `%LocalAppData%\Programs\NotificationsProKR-Mnyang\`에 설치·업데이트, 시작 메뉴 바로가기 1개. 같은 경로 인스턴스에만 `--quit`(최대 10초, 강제 종료 없음). `-SourceDir`, `-WhatIf` 지원. |
| `uninstall.ps1` | 정상 종료 요청 → 로그온 작업 해제 → 프로그램 폴더·바로가기 삭제. 데이터는 `-RemoveData`일 때만 삭제. `-WhatIf` 지원. |
| `p0/` | P0 기반 검증용 도구(배포와 무관). |

실행 환경: .NET 8 Desktop Runtime(x64). 관리자 권한은 필요 없습니다.

## 문서 (`docs/custom/`)

- [`MIGRATION-ko.md`](MIGRATION-ko.md): 설치·전환 가이드와 사용자 검증 체크리스트
- [`NOTICE-fork.md`](NOTICE-fork.md): GPL 수정 고지
- `P0-…` ~ `P5-…`: 단계별 구현·검증 보고서
- 설계서: `C:\Users\Administrator\Desktop\ai-tts\docs\notifications-pro-integrated-design.md`(저장소 밖)

## 라이선스

GPL-3.0-or-later(`LICENSE`). 원저작권 Liam Wytcherley. 사용자 WAV와 설정은 저장소·배포물에 포함하지 않습니다.
