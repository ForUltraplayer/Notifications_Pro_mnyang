// @file ISoundFileProber.cs
// @author: rudals252
// @brief 실제 재생 엔진으로 파일을 열어만 보고(소리 없음) 재생 가능 여부와 길이를 확인하는 인터페이스.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Services;

public interface ISoundFileProber
{
    Task<SoundProbeResult> ProbeAsync(string fullPath, CancellationToken cancellationToken = default);
}
