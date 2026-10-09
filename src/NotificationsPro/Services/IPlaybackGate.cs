// @file IPlaybackGate.cs
// @author: rudals252
// @brief 낭독 서비스가 WAV·미리 듣기와 같은 하나의 재생 권한을 요청할 때 쓰는 인터페이스.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public interface IPlaybackGate
{
    /// <summary>
    /// Queues a speech turn behind any playback already active or queued. <paramref name="onGranted"/> runs
    /// (posted, never inline) when the turn starts; <paramref name="onRevoked"/> runs when an explicit
    /// stop-all takes the turn away. The caller must <see cref="PlaybackLease.Release"/> the lease when
    /// speech ends or is cancelled, including before it was granted.
    /// </summary>
    PlaybackLease RequestSpeech(Action onGranted, Action onRevoked);
}
