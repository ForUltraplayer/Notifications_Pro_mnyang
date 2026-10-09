// @file ComTaskSchedulerClient.cs
// @author: rudals252
// @brief Task Scheduler 2.0 COM(Schedule.Service)을 late-binding으로 호출하는 ITaskSchedulerClient 구현. 추가 패키지·schtasks·PowerShell 없이 루트 폴더의 현재 사용자 작업을 조회·등록·삭제하고, 작업 생성 금지 정책은 레지스트리를 읽기만 한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Runtime.InteropServices;
using Microsoft.Win32;
using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Task Scheduler access through the documented COM API (ITaskService / ITaskFolder / IRegisteredTask)
/// using late binding, so no interop assembly or extra package is needed.
/// </summary>
public sealed class ComTaskSchedulerClient : ITaskSchedulerClient
{
    // https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertask
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskActionExec = 0;
    private const int HResultFileNotFound = unchecked((int)0x80070002);
    private const int HResultPathNotFound = unchecked((int)0x80070003);

    // Group Policy "Prohibit New Task Creation" (read only; never written).
    private const string TaskSchedulerPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\Task Scheduler5.0";
    private const string TaskCreationPolicyValue = "Task Creation";

    public ScheduledTaskInfo? QueryTask(string taskName)
    {
        return WithRootFolder<ScheduledTaskInfo?>(folder =>
        {
            var task = TryGetTask(folder, taskName);
            if (task == null)
                return null;

            try
            {
                bool enabled = task.Enabled;
                string? path = null;
                string? arguments = null;
                dynamic actions = task.Definition.Actions;
                int count = actions.Count;
                for (var i = 1; i <= count; i++)
                {
                    dynamic action = actions.Item(i);
                    if ((int)action.Type != TaskActionExec)
                        continue;

                    path = action.Path;
                    arguments = action.Arguments;
                    break;
                }

                return new ScheduledTaskInfo(enabled, path, arguments);
            }
            finally
            {
                Release(task);
            }
        });
    }

    public void RegisterTask(string taskName, string taskXml)
    {
        WithRootFolder<object?>(folder =>
        {
            // userId/password null: the principal in the XML (current user, interactive token) is used.
            dynamic registered = folder.RegisterTask(taskName, taskXml, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
            Release(registered);
            return null;
        });
    }

    public bool DeleteTask(string taskName)
    {
        return WithRootFolder<bool>(folder =>
        {
            try
            {
                folder.DeleteTask(taskName, 0);
                return true;
            }
            catch (COMException ex) when (IsNotFound(ex.HResult))
            {
                return false;
            }
        });
    }

    public string? GetTaskXml(string taskName)
    {
        return WithRootFolder<string?>(folder =>
        {
            var task = TryGetTask(folder, taskName);
            if (task == null)
                return null;

            try
            {
                return (string)task.Xml;
            }
            finally
            {
                Release(task);
            }
        });
    }

    public bool IsTaskCreationProhibitedByPolicy()
    {
        return IsPolicySet(Registry.LocalMachine) || IsPolicySet(Registry.CurrentUser);
    }

    private static bool IsPolicySet(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(TaskSchedulerPolicyKey, writable: false);
            return key?.GetValue(TaskCreationPolicyValue) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static dynamic? TryGetTask(dynamic folder, string taskName)
    {
        try
        {
            return folder.GetTask(taskName);
        }
        catch (COMException ex) when (IsNotFound(ex.HResult))
        {
            return null;
        }
    }

    private static T WithRootFolder<T>(Func<dynamic, T> action)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        dynamic? folder = null;
        try
        {
            service.Connect();
            folder = service.GetFolder(@"\");
            return action(folder);
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    private static bool IsNotFound(int hresult) => hresult == HResultFileNotFound || hresult == HResultPathNotFound;

    private static void Release(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
            Marshal.ReleaseComObject(comObject);
    }
}
