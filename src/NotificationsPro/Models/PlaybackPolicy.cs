// @file PlaybackPolicy.cs
// @author: rudals252
// @brief WAV 재생 조정 정책(앱별 최소 간격, 대기 상한, 대기 시간 상한, 초과 시 동작) 모델. 이번 단계는 모델만 다룬다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Sound playback coordination values (operational settings, applied in P2).</summary>
public class PlaybackPolicy
{
    /// <summary>Minimum seconds between sounds for the same app. 0 disables the limit.</summary>
    public double MinIntervalPerAppSeconds { get; set; } = 4;

    public int MaxQueued { get; set; } = 5;

    public double MaxQueueWaitSeconds { get; set; } = 30;

    public PlaybackOverflowBehavior OverflowBehavior { get; set; } = PlaybackOverflowBehavior.Queue;

    public PlaybackPolicy Clone()
    {
        return new PlaybackPolicy
        {
            MinIntervalPerAppSeconds = MinIntervalPerAppSeconds,
            MaxQueued = MaxQueued,
            MaxQueueWaitSeconds = MaxQueueWaitSeconds,
            OverflowBehavior = OverflowBehavior,
        };
    }

    public void Normalize()
    {
        MinIntervalPerAppSeconds = double.IsNaN(MinIntervalPerAppSeconds) ? 4 : Math.Clamp(MinIntervalPerAppSeconds, 0, 3600);
        MaxQueued = Math.Clamp(MaxQueued, 0, 100);
        MaxQueueWaitSeconds = double.IsNaN(MaxQueueWaitSeconds) ? 30 : Math.Clamp(MaxQueueWaitSeconds, 0, 3600);
        if (!Enum.IsDefined(OverflowBehavior))
            OverflowBehavior = PlaybackOverflowBehavior.Queue;
    }
}
