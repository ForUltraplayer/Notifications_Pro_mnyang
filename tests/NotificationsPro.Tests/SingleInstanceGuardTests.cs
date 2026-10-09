// @file SingleInstanceGuardTests.cs
// @author: rudals252
// @brief SingleInstanceGuard의 mutex 단일 획득·해제 후 재획득·두 번째 실행의 설정창 표시 요청 신호 전달 검증(테스트 전용 고유 이름 사용).

using NotificationsPro.Helpers;

namespace NotificationsPro.Tests;

public class SingleInstanceGuardTests
{
    private static (string Mutex, string Event) UniqueNames()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return ($@"Local\NotificationsProKR-Mnyang.Test.{suffix}.Mutex", $@"Local\NotificationsProKR-Mnyang.Test.{suffix}.Event");
    }

    [Fact]
    public void TryAcquire_SecondAttemptFails_UntilFirstIsDisposed()
    {
        var (mutexName, eventName) = UniqueNames();

        var first = SingleInstanceGuard.TryAcquire(mutexName, eventName);
        Assert.NotNull(first);

        Assert.Null(SingleInstanceGuard.TryAcquire(mutexName, eventName));

        first!.Dispose();
        using var third = SingleInstanceGuard.TryAcquire(mutexName, eventName);
        Assert.NotNull(third);
    }

    [Fact]
    public void SignalExistingInstance_InvokesPrimaryCallback()
    {
        var (mutexName, eventName) = UniqueNames();
        using var primary = SingleInstanceGuard.TryAcquire(mutexName, eventName);
        Assert.NotNull(primary);

        using var signaled = new ManualResetEventSlim(false);
        primary!.StartListening(() => signaled.Set());

        Assert.True(SingleInstanceGuard.SignalExistingInstance(eventName));
        Assert.True(signaled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SignalExistingInstance_ReturnsFalse_WhenNoPrimaryExists()
    {
        var (_, eventName) = UniqueNames();

        Assert.False(SingleInstanceGuard.SignalExistingInstance(eventName, attempts: 2, delayMs: 10));
    }

    [Fact]
    public void ForkIdentity_UsesConfirmedNames()
    {
        Assert.Equal("NotificationsProKR-Mnyang", ForkIdentity.AppDataFolderName);
        Assert.Equal(@"Local\NotificationsProKR-Mnyang.SingleInstance", ForkIdentity.SingleInstanceMutexName);
        Assert.Equal("NotificationsProKR-Mnyang-Logon", ForkIdentity.LogonTaskName);
        Assert.Equal("rudals252.NotificationsProKR.Mnyang", ForkIdentity.UnpackagedAppUserModelId);
        Assert.Equal("Notifications Pro KR-Mnyang", ForkIdentity.ProductDisplayName);
        Assert.Equal("NotificationsProKR-Mnyang.exe", ForkIdentity.ExecutableFileName);
        Assert.Equal(
            ForkIdentity.ExecutableFileName,
            typeof(ForkIdentity).Assembly.GetName().Name + ".exe");
    }
}
