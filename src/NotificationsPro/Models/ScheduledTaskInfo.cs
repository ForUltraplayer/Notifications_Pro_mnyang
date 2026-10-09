// @file ScheduledTaskInfo.cs
// @author: rudals252
// @brief 작업 스케줄러에서 읽은 작업 하나의 최소 정보(사용 여부, 실행 파일 경로, 인자). 로그온 작업 상태 판정 입력.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Minimal view of a registered task as read from the Task Scheduler.</summary>
public sealed record ScheduledTaskInfo(bool Enabled, string? ExecutablePath, string? Arguments);
