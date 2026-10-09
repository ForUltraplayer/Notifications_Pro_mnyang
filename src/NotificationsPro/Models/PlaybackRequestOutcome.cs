// @file PlaybackRequestOutcome.cs
// @author: rudals252
// @brief 알림음·미리 듣기 요청을 재생 조정기가 처리한 결과(시작, 대기, 각종 건너뜀 사유).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum PlaybackRequestOutcome
{
    Started = 0,
    Queued = 1,
    SkippedSilent = 2,
    SkippedNoUsableFile = 3,
    SkippedCooldown = 4,
    SkippedBusy = 5,
    SkippedQueueFull = 6,
    SkippedPaused = 7,
    Disposed = 8,
}
