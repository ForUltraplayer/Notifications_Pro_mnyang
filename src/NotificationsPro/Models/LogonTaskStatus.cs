// @file LogonTaskStatus.cs
// @author: rudals252
// @brief 로그온 작업 상태 조회 결과(상태, 다른 실행 파일을 가리키는지, 실패 시 HRESULT). 시스템 탭 상태 줄 표시에 쓴다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Result of a logon task query or change. Holds no notification data.</summary>
/// <param name="State">Observed state.</param>
/// <param name="PointsToOtherExecutable">True when the registered task starts a different exe than the running one.</param>
/// <param name="ErrorCode">HRESULT of the failure for <see cref="LogonTaskState.PolicyBlocked"/> / <see cref="LogonTaskState.CheckFailed"/> (0 when none).</param>
public sealed record LogonTaskStatus(LogonTaskState State, bool PointsToOtherExecutable = false, int ErrorCode = 0)
{
    public bool IsRegistered => State is LogonTaskState.Registered or LogonTaskState.RegisteredDisabled;
}
