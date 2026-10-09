// @file AudioDispatchMode.cs
// @author: rudals252
// @brief WAV 알림음을 어떤 이벤트 기준으로 재생할지(오버레이 수용 기준 / 공통 정책 통과 기준) 정하는 설정 값.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text.Json.Serialization;
using NotificationsPro.Helpers;

namespace NotificationsPro.Models;

/// <summary>
/// Which queue event drives notification sounds. Stored in JSON as the member name (never translated).
/// The first member is the conservative fallback for unknown stored values.
/// </summary>
[JsonConverter(typeof(SafeEnumJsonConverter<AudioDispatchMode>))]
public enum AudioDispatchMode
{
    /// <summary>Original behavior: play only for notifications the overlay queue accepted (QueueManager.NotificationAdded). Default for migrated settings.</summary>
    OverlayAccepted = 0,

    /// <summary>Play for every notification that passed the shared policy, even if the overlay rejected it (QueueManager.NotificationAccepted). Default for new installs.</summary>
    PolicyAccepted = 1,
}
