// @file PlaybackDiagnostics.cs
// @author: rudals252
// @brief 재생 조정기의 진단 상태 스냅샷(현재 작업, 대기 수, 누적 카운터, 동시 재생 최대값). 앱 이름·파일 경로·알림 본문은 담지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>
/// Counters since the coordinator was created (RAM only). <see cref="MaxConcurrentPlaybacks"/> counts
/// engine playbacks plus granted speech turns that were alive at the same time; it must stay ≤ 1.
/// <see cref="LastEvent"/> names the most recent coordinator event (e.g. "Started", "Completed",
/// "SkippedCooldown", "Queued"). Contains no app names, file paths or notification text.
/// </summary>
public sealed record PlaybackDiagnostics(
    PlaybackActivity Activity,
    int QueuedSounds,
    bool PreviewPending,
    bool SpeechWaiting,
    int Requested,
    int Started,
    int Completed,
    int Failed,
    int Cancelled,
    int Expired,
    int SkippedCooldown,
    int SkippedSilent,
    int SkippedNoUsableFile,
    int SkippedBusyOrFull,
    int PreviewsStarted,
    int SpeechGranted,
    int MaxConcurrentPlaybacks,
    DateTime? LastStartedUtc,
    string LastEvent = "None")
{
    public string ToSummary()
    {
        return $"{Activity}; queued {QueuedSounds}{(PreviewPending ? " +preview" : string.Empty)}{(SpeechWaiting ? " +speech" : string.Empty)}; "
            + $"requested {Requested}, started {Started}, completed {Completed}, failed {Failed}, cancelled {Cancelled}, expired {Expired}; "
            + $"skipped cooldown {SkippedCooldown}, silent {SkippedSilent}, no-file {SkippedNoUsableFile}, busy/full {SkippedBusyOrFull}; "
            + $"previews {PreviewsStarted}, speech turns {SpeechGranted}; max concurrent {MaxConcurrentPlaybacks}";
    }
}
