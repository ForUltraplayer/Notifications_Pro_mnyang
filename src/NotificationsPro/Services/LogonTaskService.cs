// @file LogonTaskService.cs
// @author: rudals252
// @brief Windows 로그온 자동 실행 작업(NotificationsProKR-Mnyang-Logon)의 등록·해제·상태 조회. 작업 XML(현재 사용자 로그온 트리거, 대화형 토큰, 최소 권한, 실행 시간 제한 없음, IgnoreNew, exe 직접 실행 + --start-minimized)을 만들고, 조회 결과·예외를 5가지 상태로 매핑한다. 비활성화된 작업을 다른 등록 방식으로 우회하지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.Security.Principal;
using System.Xml.Linq;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Registers, removes and inspects the fork's Task Scheduler logon task (design §9.1).
/// <list type="bullet">
/// <item>Logon trigger for the current user only; interactive token; least privilege.</item>
/// <item>ExecutionTimeLimit PT0S (no 72-hour stop); MultipleInstancesPolicy IgnoreNew.</item>
/// <item>The exe is started directly (no wrapper, P0 §4.3) with <see cref="ForkIdentity.StartMinimizedArgument"/>,
/// working directory = exe folder.</item>
/// <item>No RestartOnFailure: it did not restart the app in P0, so RestartOnCrash is not offered in this version.</item>
/// </list>
/// The task's own state is the source of truth: a task disabled by the user stays disabled until the
/// user explicitly turns the option on again in this app, and no other auto-start mechanism is used.
/// </summary>
public sealed class LogonTaskService
{
    public const int HResultAccessDenied = unchecked((int)0x80070005);

    /// <summary>Task priority 5 = NORMAL_PRIORITY_CLASS (the Task Scheduler default 7 is below normal).</summary>
    public const int TaskPriority = 5;

    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly ITaskSchedulerClient _client;

    public LogonTaskService(ITaskSchedulerClient client, string taskName, string executablePath, string userId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        TaskName = taskName;
        ExecutablePath = Path.GetFullPath(executablePath);
        UserId = userId;
    }

    public string TaskName { get; }

    public string ExecutablePath { get; }

    public string UserId { get; }

    public string WorkingDirectory => Path.GetDirectoryName(ExecutablePath) ?? ExecutablePath;

    /// <summary>Production instance: COM client, the running exe and the current Windows user.</summary>
    public static LogonTaskService CreateForCurrentProcess(string taskName = ForkIdentity.LogonTaskName)
    {
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, ForkIdentity.ExecutableFileName);
        using var identity = WindowsIdentity.GetCurrent();
        return new LogonTaskService(new ComTaskSchedulerClient(), taskName, exePath, identity.Name);
    }

    public LogonTaskStatus GetStatus()
    {
        try
        {
            var info = _client.QueryTask(TaskName);
            var prohibited = info == null && SafeIsCreationProhibited();
            return MapQuery(info, prohibited, ExecutablePath);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    /// <summary>
    /// Creates (or replaces, re-enabling) the task. Called only from an explicit user action.
    /// Nothing is registered when a policy prohibits task creation.
    /// </summary>
    public LogonTaskStatus Register()
    {
        try
        {
            if (_client.QueryTask(TaskName) == null && SafeIsCreationProhibited())
                return new LogonTaskStatus(LogonTaskState.PolicyBlocked);

            _client.RegisterTask(TaskName, BuildTaskXml());
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }

        return GetStatus();
    }

    /// <summary>Deletes the task (a missing task is not an error).</summary>
    public LogonTaskStatus Unregister()
    {
        try
        {
            _client.DeleteTask(TaskName);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }

        return GetStatus();
    }

    /// <summary>Task Scheduler XML for the logon task (exe started directly, no wrapper).</summary>
    public string BuildTaskXml()
    {
        var ns = TaskNamespace;
        var document = new XDocument(
            new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Author", UserId),
                    new XElement(ns + "Description",
                        "Starts " + ForkIdentity.ProductDisplayName + " in the notification area at sign-in. "
                        + "Registered from the app's System tab; turn it off there before deleting the app.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", UserId))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", UserId),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "false"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", TaskPriority)),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", ExecutablePath),
                        new XElement(ns + "Arguments", ForkIdentity.StartMinimizedArgument),
                        new XElement(ns + "WorkingDirectory", WorkingDirectory)))));

        return document.ToString(SaveOptions.None);
    }

    /// <summary>Maps a query result to a status. <paramref name="creationProhibited"/> only matters when no task exists.</summary>
    public static LogonTaskStatus MapQuery(ScheduledTaskInfo? info, bool creationProhibited, string expectedExecutablePath)
    {
        if (info == null)
            return new LogonTaskStatus(creationProhibited ? LogonTaskState.PolicyBlocked : LogonTaskState.NotRegistered);

        var otherExe = !SameExecutable(info.ExecutablePath, expectedExecutablePath);
        return new LogonTaskStatus(info.Enabled ? LogonTaskState.Registered : LogonTaskState.RegisteredDisabled, otherExe);
    }

    /// <summary>Access denied → PolicyBlocked; any other failure → CheckFailed (with its HRESULT).</summary>
    public static LogonTaskStatus MapException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var hresult = ex.HResult;
        if (ex is UnauthorizedAccessException || hresult == HResultAccessDenied)
            return new LogonTaskStatus(LogonTaskState.PolicyBlocked, ErrorCode: hresult);

        return new LogonTaskStatus(LogonTaskState.CheckFailed, ErrorCode: hresult);
    }

    internal static bool SameExecutable(string? registered, string expected)
    {
        if (string.IsNullOrWhiteSpace(registered))
            return false;

        try
        {
            var normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(registered.Trim().Trim('"')));
            return string.Equals(normalized, Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool SafeIsCreationProhibited()
    {
        try
        {
            return _client.IsTaskCreationProhibitedByPolicy();
        }
        catch
        {
            return false;
        }
    }
}
