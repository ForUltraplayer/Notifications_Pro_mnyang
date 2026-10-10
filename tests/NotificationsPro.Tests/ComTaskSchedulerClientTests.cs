// @file ComTaskSchedulerClientTests.cs
// @author: rudals252
// @brief 실제 Task Scheduler COM에 읽기 전용으로 접근해, 존재하지 않는 작업 이름이 예외("확인 실패")가 아니라 null("미등록")로 돌아오는지 검증한다. 작업을 만들거나 지우지 않는다.

using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class ComTaskSchedulerClientTests
{
    // Unique name so the test can never collide with a real task.
    private static readonly string MissingTaskName = "NotificationsProKR-Mnyang.Tests.Missing." + Guid.NewGuid().ToString("N");

    [Fact]
    public void QueryTask_MissingTask_ReturnsNull_NotException()
    {
        // Regression: COM reports a missing task as HRESULT 0x80070002, which the runtime surfaces as
        // FileNotFoundException (not COMException). This used to show "check failed (0x80070002)".
        var client = new ComTaskSchedulerClient();

        var info = client.QueryTask(MissingTaskName);

        Assert.Null(info);
    }

    [Fact]
    public void GetTaskXml_MissingTask_ReturnsNull()
    {
        var client = new ComTaskSchedulerClient();

        Assert.Null(client.GetTaskXml(MissingTaskName));
    }

    [Fact]
    public void GetStatus_MissingTask_IsNotRegistered()
    {
        var service = new LogonTaskService(new ComTaskSchedulerClient(), MissingTaskName, @"C:\x\app.exe", @"PC\user");

        var status = service.GetStatus();

        Assert.True(
            status.State is Models.LogonTaskState.NotRegistered or Models.LogonTaskState.PolicyBlocked,
            $"unexpected state {status.State} (0x{status.ErrorCode:X8})");
    }
}
