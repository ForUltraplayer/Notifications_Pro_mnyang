// @file PlaybackLease.cs
// @author: rudals252
// @brief 낭독 한 번의 재생 권한 요청 핸들. 허가 전에는 대기 취소, 허가 후에는 권한 반환으로 동작하며 여러 번 호출해도 안전하다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public sealed class PlaybackLease
{
    private readonly Action<PlaybackLease>? _release;
    private int _released;

    internal PlaybackLease(long id, Action<PlaybackLease>? release)
    {
        Id = id;
        _release = release;
    }

    internal long Id { get; }

    /// <summary>True once the coordinator handed this lease the playback turn.</summary>
    public bool IsGranted { get; internal set; }

    public bool IsReleased => Volatile.Read(ref _released) != 0;

    /// <summary>Cancels a waiting request or returns a granted turn. Idempotent.</summary>
    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        _release?.Invoke(this);
    }

    internal void MarkReleasedWithoutCallback() => Interlocked.Exchange(ref _released, 1);
}
