// @file ISoundPlaybackHandle.cs
// @author: rudals252
// @brief 진행 중인 재생 하나를 멈추는 핸들. Stop 이후에는 관찰자 콜백이 오지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public interface ISoundPlaybackHandle
{
    /// <summary>Stops playback immediately and releases engine resources. Idempotent; no callbacks follow.</summary>
    void Stop();
}
