// @file SoundProbeResult.cs
// @author: rudals252
// @brief 실제 재생 엔진으로 파일을 열어 본(재생하지 않음) 결과: 열기 성공 여부, 길이, 실패 종류.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public sealed record SoundProbeResult(bool Playable, TimeSpan? Duration, SoundEngineFailureKind? Failure);
