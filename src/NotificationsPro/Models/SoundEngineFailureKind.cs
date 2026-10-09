// @file SoundEngineFailureKind.cs
// @author: rudals252
// @brief 재생 엔진 실패 종류(파일 형식 미지원, 장치·기타 오류, 시간 초과). 파일 문제일 때만 해당 파일을 재생 불가로 기록한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum SoundEngineFailureKind
{
    /// <summary>The engine could not open/decode the file (the file itself is the problem).</summary>
    FileUnsupported = 0,

    /// <summary>Device, driver or other runtime error not attributable to the file.</summary>
    DeviceOrOther = 1,

    /// <summary>Neither completion nor failure was reported in time; the playback was abandoned.</summary>
    Timeout = 2,
}
