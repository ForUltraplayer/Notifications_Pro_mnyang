// @file ISoundPlaybackObserver.cs
// @author: rudals252
// @brief 재생 엔진이 시작·완료·실패를 알리는 콜백. 엔진 스레드에서 호출될 수 있으므로 구현은 자기 스레드로 넘겨야 한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Receives engine notifications for one playback. Calls may arrive on any thread and must never be
/// processed re-entrantly inside <see cref="ISoundEngine.Play"/>; implementations post them onward.
/// At most one terminal call (<see cref="OnCompleted"/> or <see cref="OnFailed"/>) is made, and none
/// after <see cref="ISoundPlaybackHandle.Stop"/>.
/// </summary>
public interface ISoundPlaybackObserver
{
    void OnStarted();
    void OnCompleted();
    void OnFailed(SoundEngineFailureKind kind, bool hadStarted);
}
