// @file ISoundFileGate.cs
// @author: rudals252
// @brief 규칙에 저장된 파일 참조를 재생 직전에 허용 위치·형식·실패 기록으로 확인해 전체 경로로 바꾸는 인터페이스.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public interface ISoundFileGate
{
    /// <summary>Returns the full path when the rule entry is allowed and currently playable; otherwise null.</summary>
    string? ResolvePlayablePath(string ruleFile);

    /// <summary>Remembers that the engine could not decode this file so the selector skips it until it changes.</summary>
    void MarkUnplayable(string fullPath);
}
