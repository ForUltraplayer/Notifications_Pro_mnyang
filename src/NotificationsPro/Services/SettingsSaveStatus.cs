// @file SettingsSaveStatus.cs
// @author: rudals252
// @brief SettingsManager.Save()의 마지막 결과(미시도/저장/새 스키마 읽기 전용으로 생략/실패) 상태 값. 로그 없이 상태로만 보고한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public enum SettingsSaveStatus
{
    NotAttempted = 0,
    Saved = 1,

    /// <summary>settings.json came from a newer schema, so it was deliberately not overwritten.</summary>
    SkippedReadOnly = 2,

    /// <summary>The write failed; the previous settings.json was left in place.</summary>
    Failed = 3,
}
