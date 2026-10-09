// @file SoundSilenceReason.cs
// @author: rudals252
// @brief 알림음 규칙 선택 결과가 무음일 때의 사유(마스터 끔, 규칙 비활성, 명시적 무음, 빈 목록, 모든 파일 사용 불가).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Why a sound rule produced no file to play. <see cref="None"/> means a file was chosen.</summary>
public enum SoundSilenceReason
{
    None = 0,
    MasterSwitchOff = 1,
    RuleDisabled = 2,
    ExplicitSilence = 3,
    EmptyList = 4,
    AllFilesUnavailable = 5,
}
