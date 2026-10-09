// @file OriginalSoundAppKind.cs
// @author: rudals252
// @brief 원래 알림음 수동 음소거 안내용 앱 유형(미확인 / Windows toast 앱 / Discord / 카카오톡).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>App type used to pick the manual mute guidance (design §8.2, P0 §5–§6).</summary>
public enum OriginalSoundAppKind
{
    /// <summary>No AUMID seen this session and no known name: guidance cannot be chosen.</summary>
    Unknown,

    /// <summary>An app seen with an AUMID through Windows notifications: the per-app Windows sound toggle applies.</summary>
    WindowsToast,

    /// <summary>Discord (Squirrel AUMID): Windows toggle plus Discord's own message sound.</summary>
    Discord,

    /// <summary>KakaoTalk: not a Windows notification app on the tested PC, so no guidance.</summary>
    KakaoTalk,
}
