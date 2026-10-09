// @file ManualMuteConfirmation.cs
// @author: rudals252
// @brief 앱별 원래 알림음 수동 음소거에 대한 사용자 확인 상태(문자열)와 시각. 자동 검증 결과가 아니며 이 PC 전용(프로필·내보내기 제외)이다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>
/// User-reported manual mute state for one app's own notification sound. It records what the
/// user confirmed, not something the app verified, and never travels with profiles or exports.
/// </summary>
public class ManualMuteConfirmation
{
    public const string Unconfirmed = "Unconfirmed";
    public const string SetupNeeded = "SetupNeeded";
    public const string UserConfirmed = "UserConfirmed";
    public const string NotSupported = "NotSupported";

    public string State { get; set; } = Unconfirmed;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ManualMuteConfirmation Clone()
    {
        return new ManualMuteConfirmation { State = State, UpdatedAt = UpdatedAt };
    }
}
