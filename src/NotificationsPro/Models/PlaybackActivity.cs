// @file PlaybackActivity.cs
// @author: rudals252
// @brief 재생 권한을 현재 가진 작업 종류(없음, 알림 WAV, 미리 듣기, 낭독).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum PlaybackActivity
{
    Idle = 0,
    NotificationSound = 1,
    Preview = 2,
    Speech = 3,
}
