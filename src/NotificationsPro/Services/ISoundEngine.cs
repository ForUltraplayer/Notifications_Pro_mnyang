// @file ISoundEngine.cs
// @author: rudals252
// @brief WAV 재생 엔진 추상화. 재생 조정기는 이 인터페이스만 사용하므로 테스트에서 가짜 엔진으로 완료·실패·취소·지연을 흉내 낼 수 있다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

/// <summary>Plays one file at a time per handle. Never changes the Windows master volume.</summary>
public interface ISoundEngine
{
    /// <summary>Starts playing <paramref name="fullPath"/>. Must not call <paramref name="observer"/> synchronously.</summary>
    ISoundPlaybackHandle Play(string fullPath, ISoundPlaybackObserver observer);
}
