// @file LogonTaskState.cs
// @author: rudals252
// @brief Windows 로그온 자동 실행 작업(작업 스케줄러)의 관측 상태: 등록됨 / 등록됨(비활성) / 미등록 / 정책 차단 / 확인 실패.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Observed state of the fork's Task Scheduler logon task (design §9.1, §9.3).</summary>
public enum LogonTaskState
{
    /// <summary>The task exists and is enabled.</summary>
    Registered,

    /// <summary>The task exists but was disabled (for example in Task Scheduler). Never bypassed.</summary>
    RegisteredDisabled,

    /// <summary>No task with the fork's name exists.</summary>
    NotRegistered,

    /// <summary>The OS refused access (E_ACCESSDENIED) or a policy prohibits creating tasks.</summary>
    PolicyBlocked,

    /// <summary>The Task Scheduler could not be queried (service unavailable, COM error, ...).</summary>
    CheckFailed,
}
