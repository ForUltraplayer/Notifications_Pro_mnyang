// @file ITaskSchedulerClient.cs
// @author: rudals252
// @brief 작업 스케줄러 접근 추상화(조회·XML 등록·삭제·XML 내보내기·작업 생성 금지 정책 확인). 실제 구현은 COM(Schedule.Service), 테스트는 가짜 구현을 쓴다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Thin Task Scheduler access used by <see cref="LogonTaskService"/>. Methods throw on failure
/// (COMException / UnauthorizedAccessException); the service maps them to <see cref="LogonTaskState"/>.
/// All tasks live in the root folder and belong to the current user.
/// </summary>
public interface ITaskSchedulerClient
{
    /// <summary>Returns the task, or null when no task with that name exists.</summary>
    ScheduledTaskInfo? QueryTask(string taskName);

    /// <summary>Creates or replaces the task from Task Scheduler XML, running as the interactive user.</summary>
    void RegisterTask(string taskName, string taskXml);

    /// <summary>Deletes the task. Returns false when it did not exist.</summary>
    bool DeleteTask(string taskName);

    /// <summary>Registered XML of the task, or null when it does not exist (used for verification).</summary>
    string? GetTaskXml(string taskName);

    /// <summary>True when a read-only policy check says new task creation is prohibited.</summary>
    bool IsTaskCreationProhibitedByPolicy();
}
