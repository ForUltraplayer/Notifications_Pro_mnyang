// @file LogonTaskServiceTests.cs
// @author: rudals252
// @brief P4 로그온 작업: 상태 매핑(등록됨/비활성/미등록/정책 차단/확인 실패, 다른 exe), 작업 XML(로그온 트리거·대화형·최소 권한·PT0S·IgnoreNew·exe 직접 실행·--start-minimized·재시작 없음), 등록·해제 의도(가짜 스케줄러), 상태 조회의 무변경, 설정창 토글·상태 줄 연결을 검증한다.

using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class LogonTaskServiceTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string ExePath = @"D:\Apps\Notifications & Pro\NotificationsProKR-Mnyang.exe";
    private const string User = @"PC\tester";

    internal sealed class FakeTaskScheduler : ITaskSchedulerClient
    {
        public ScheduledTaskInfo? Task { get; set; }
        public bool CreationProhibited { get; set; }
        public Exception? QueryError { get; set; }
        public Exception? RegisterError { get; set; }
        public Exception? DeleteError { get; set; }
        public List<(string Name, string Xml)> Registered { get; } = new();
        public List<string> Deleted { get; } = new();
        public int Queries { get; private set; }

        public ScheduledTaskInfo? QueryTask(string taskName)
        {
            Queries++;
            if (QueryError != null)
                throw QueryError;
            return Task;
        }

        public void RegisterTask(string taskName, string taskXml)
        {
            if (RegisterError != null)
                throw RegisterError;
            Registered.Add((taskName, taskXml));
            var exec = XDocument.Parse(taskXml).Descendants(Ns + "Exec").Single();
            Task = new ScheduledTaskInfo(true, (string?)exec.Element(Ns + "Command"), (string?)exec.Element(Ns + "Arguments"));
        }

        public bool DeleteTask(string taskName)
        {
            if (DeleteError != null)
                throw DeleteError;
            Deleted.Add(taskName);
            var existed = Task != null;
            Task = null;
            return existed;
        }

        public string? GetTaskXml(string taskName) => Registered.LastOrDefault().Xml;

        public bool IsTaskCreationProhibitedByPolicy() => CreationProhibited;
    }

    private static LogonTaskService Create(FakeTaskScheduler fake, string taskName = ForkIdentity.LogonTaskName)
        => new(fake, taskName, ExePath, User);

    [Fact]
    public void MapQuery_CoversAllStates()
    {
        Assert.Equal(LogonTaskState.NotRegistered, LogonTaskService.MapQuery(null, false, ExePath).State);
        Assert.Equal(LogonTaskState.PolicyBlocked, LogonTaskService.MapQuery(null, true, ExePath).State);

        var enabled = LogonTaskService.MapQuery(new ScheduledTaskInfo(true, ExePath, "--start-minimized"), true, ExePath);
        Assert.Equal(LogonTaskState.Registered, enabled.State);
        Assert.False(enabled.PointsToOtherExecutable);
        Assert.True(enabled.IsRegistered);

        var disabled = LogonTaskService.MapQuery(new ScheduledTaskInfo(false, ExePath, null), false, ExePath);
        Assert.Equal(LogonTaskState.RegisteredDisabled, disabled.State);
        Assert.True(disabled.IsRegistered);

        var quoted = LogonTaskService.MapQuery(new ScheduledTaskInfo(true, "\"" + ExePath.ToUpperInvariant() + "\"", null), false, ExePath);
        Assert.False(quoted.PointsToOtherExecutable);

        var other = LogonTaskService.MapQuery(new ScheduledTaskInfo(true, @"E:\Old\NotificationsProKR-Mnyang.exe", null), false, ExePath);
        Assert.Equal(LogonTaskState.Registered, other.State);
        Assert.True(other.PointsToOtherExecutable);
    }

    [Fact]
    public void MapException_AccessDeniedIsPolicyBlocked_OthersAreCheckFailed()
    {
        Assert.Equal(LogonTaskState.PolicyBlocked, LogonTaskService.MapException(new UnauthorizedAccessException()).State);

        var denied = LogonTaskService.MapException(new COMException("denied", LogonTaskService.HResultAccessDenied));
        Assert.Equal(LogonTaskState.PolicyBlocked, denied.State);
        Assert.Equal(LogonTaskService.HResultAccessDenied, denied.ErrorCode);

        var serviceDown = LogonTaskService.MapException(new COMException("not running", unchecked((int)0x80041315)));
        Assert.Equal(LogonTaskState.CheckFailed, serviceDown.State);
        Assert.Equal(unchecked((int)0x80041315), serviceDown.ErrorCode);
    }

    [Fact]
    public void GetStatus_QueryFailure_IsCheckFailed_AndChangesNothing()
    {
        var fake = new FakeTaskScheduler { QueryError = new COMException("x", unchecked((int)0x80040154)) };
        var status = Create(fake).GetStatus();

        Assert.Equal(LogonTaskState.CheckFailed, status.State);
        Assert.Empty(fake.Registered);
        Assert.Empty(fake.Deleted);
    }

    [Fact]
    public void GetStatus_NeverRegistersOrDeletes_EvenWhenTaskIsDisabled()
    {
        var fake = new FakeTaskScheduler { Task = new ScheduledTaskInfo(false, ExePath, "--start-minimized") };
        var service = Create(fake);

        Assert.Equal(LogonTaskState.RegisteredDisabled, service.GetStatus().State);
        Assert.Equal(LogonTaskState.RegisteredDisabled, service.GetStatus().State);
        Assert.Empty(fake.Registered);
        Assert.Empty(fake.Deleted);
    }

    [Fact]
    public void BuildTaskXml_FollowsP0SettingsWithoutWrapper()
    {
        var xml = XDocument.Parse(Create(new FakeTaskScheduler()).BuildTaskXml());
        var task = xml.Root!;

        var trigger = task.Element(Ns + "Triggers")!.Elements().Single();
        Assert.Equal("LogonTrigger", trigger.Name.LocalName);
        Assert.Equal(User, (string?)trigger.Element(Ns + "UserId"));
        Assert.Equal("true", (string?)trigger.Element(Ns + "Enabled"));

        var principal = task.Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal(User, (string?)principal.Element(Ns + "UserId"));
        Assert.Equal("InteractiveToken", (string?)principal.Element(Ns + "LogonType"));
        Assert.Equal("LeastPrivilege", (string?)principal.Element(Ns + "RunLevel"));

        var settings = task.Element(Ns + "Settings")!;
        Assert.Equal("PT0S", (string?)settings.Element(Ns + "ExecutionTimeLimit"));
        Assert.Equal("IgnoreNew", (string?)settings.Element(Ns + "MultipleInstancesPolicy"));
        Assert.Equal("false", (string?)settings.Element(Ns + "DisallowStartIfOnBatteries"));
        Assert.Equal("false", (string?)settings.Element(Ns + "StopIfGoingOnBatteries"));
        Assert.Equal("true", (string?)settings.Element(Ns + "Enabled"));
        Assert.Equal(LogonTaskService.TaskPriority.ToString(), (string?)settings.Element(Ns + "Priority"));
        Assert.Null(settings.Element(Ns + "RestartOnFailure"));

        var exec = task.Element(Ns + "Actions")!.Elements().Single();
        Assert.Equal("Exec", exec.Name.LocalName);
        Assert.Equal(ExePath, (string?)exec.Element(Ns + "Command"));
        Assert.Equal(ForkIdentity.StartMinimizedArgument, (string?)exec.Element(Ns + "Arguments"));
        Assert.Equal(Path.GetDirectoryName(ExePath), (string?)exec.Element(Ns + "WorkingDirectory"));
        Assert.DoesNotContain("powershell", xml.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_WritesTheTaskOnce_AndReportsRegistered()
    {
        var fake = new FakeTaskScheduler();
        var status = Create(fake).Register();

        Assert.Equal(LogonTaskState.Registered, status.State);
        var (name, xml) = Assert.Single(fake.Registered);
        Assert.Equal(ForkIdentity.LogonTaskName, name);
        Assert.Contains("LogonTrigger", xml);
        Assert.Empty(fake.Deleted);
    }

    [Fact]
    public void Register_WhenPolicyProhibitsCreation_DoesNotRegister()
    {
        var fake = new FakeTaskScheduler { CreationProhibited = true };
        var status = Create(fake).Register();

        Assert.Equal(LogonTaskState.PolicyBlocked, status.State);
        Assert.Empty(fake.Registered);
    }

    [Fact]
    public void Register_AccessDenied_IsPolicyBlocked()
    {
        var fake = new FakeTaskScheduler { RegisterError = new UnauthorizedAccessException() };
        Assert.Equal(LogonTaskState.PolicyBlocked, Create(fake).Register().State);
    }

    [Fact]
    public void Register_OnExplicitRequest_ReplacesADisabledTaskWithAnEnabledOne()
    {
        var fake = new FakeTaskScheduler { Task = new ScheduledTaskInfo(false, @"E:\Old\x.exe", null) };
        var status = Create(fake).Register();

        Assert.Equal(LogonTaskState.Registered, status.State);
        Assert.False(status.PointsToOtherExecutable);
        Assert.Single(fake.Registered);
    }

    [Fact]
    public void Unregister_DeletesOnlyTheNamedTask_AndReportsNotRegistered()
    {
        var fake = new FakeTaskScheduler { Task = new ScheduledTaskInfo(true, ExePath, null) };
        var status = Create(fake).Unregister();

        Assert.Equal(LogonTaskState.NotRegistered, status.State);
        Assert.Equal(new[] { ForkIdentity.LogonTaskName }, fake.Deleted);
        Assert.Empty(fake.Registered);
    }

    [Fact]
    public void Unregister_Failure_IsReported()
    {
        var fake = new FakeTaskScheduler
        {
            Task = new ScheduledTaskInfo(true, ExePath, null),
            DeleteError = new COMException("denied", LogonTaskService.HResultAccessDenied),
        };
        Assert.Equal(LogonTaskState.PolicyBlocked, Create(fake).Unregister().State);
    }

    [Fact]
    public void SettingsViewModel_ToggleRegistersAndRemoves_AndShowsEachState()
    {
        StaThreadTestHelper.Run(() =>
        {
            var root = TestAppDataIsolation.CreateUniqueDirectory("logon-vm");
            try
            {
                var manager = new SettingsManager(Path.Combine(root, "settings"));
                manager.Load();
                var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(root, "profiles")));
                Assert.True(viewModel.UsesPackagedStartupTask);

                var fake = new FakeTaskScheduler();
                viewModel.ConfigureLogonTask(Create(fake));
                Assert.True(viewModel.UsesLogonTask);
                Assert.False(viewModel.UsesPackagedStartupTask);
                Assert.False(viewModel.LogonTaskEnabled);
                Assert.StartsWith("Not registered", viewModel.LogonTaskStatusText);

                viewModel.LogonTaskEnabled = true;
                Assert.Single(fake.Registered);
                Assert.True(viewModel.LogonTaskEnabled);
                Assert.StartsWith("Registered:", viewModel.LogonTaskStatusText);

                // Disabled outside the app: shown as is, toggle off, nothing re-registered.
                fake.Task = fake.Task! with { Enabled = false };
                viewModel.RefreshLogonTaskStatusCommand.Execute(null);
                Assert.False(viewModel.LogonTaskEnabled);
                Assert.StartsWith("Registered (disabled)", viewModel.LogonTaskStatusText);
                Assert.True(viewModel.CanUnregisterLogonTask);
                Assert.Single(fake.Registered);

                viewModel.UnregisterLogonTaskCommand.Execute(null);
                Assert.Single(fake.Deleted);
                Assert.StartsWith("Not registered", viewModel.LogonTaskStatusText);

                fake.CreationProhibited = true;
                viewModel.LogonTaskEnabled = true;
                Assert.False(viewModel.LogonTaskEnabled);
                Assert.StartsWith("Blocked by policy", viewModel.LogonTaskStatusText);

                fake.QueryError = new COMException("x", unchecked((int)0x80041315));
                viewModel.RefreshLogonTaskStatus();
                Assert.Equal("Check failed: the Task Scheduler state could not be read. (0x80041315)", viewModel.LogonTaskStatusText);

                // The logon task is OS state only: nothing about it is written to settings.
                Assert.False(manager.Settings.StartWithWindows);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        });
    }
}
