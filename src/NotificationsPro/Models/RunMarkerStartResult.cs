// @file RunMarkerStartResult.cs
// @author: rudals252
// @brief 주 인스턴스 시작 시 실행 표식 판정 결과(이전 실행이 정상 종료되지 않았는지, 이전 시작 시각·버전, 표식 쓰기 실패 사유). 원인(충돌·강제 종료·전원 차단)은 담지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>
/// What the primary instance found and did with the run marker at start-up (design §9.2).
/// An unclean previous run only means "the marker was still there": the cause is unknown.
/// </summary>
/// <param name="PreviousRunEndedUncleanly">True when a marker from an earlier run was still present.</param>
/// <param name="PreviousStartedAt">Start time stored by that earlier run, when readable.</param>
/// <param name="PreviousVersion">App version stored by that earlier run, when readable.</param>
/// <param name="WriteFailed">True when this run could not write its own marker.</param>
/// <param name="ErrorDescription">Exception type and HRESULT of the write failure (no file content).</param>
public sealed record RunMarkerStartResult(
    bool PreviousRunEndedUncleanly,
    DateTimeOffset? PreviousStartedAt,
    string? PreviousVersion,
    bool WriteFailed,
    string? ErrorDescription);
