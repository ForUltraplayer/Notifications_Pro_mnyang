// @file NotificationAcceptedEventArgs.cs
// @author: rudals252
// @brief 공통 정책을 통과한 알림의 기술 정보(앱 표시 이름, AUMID, 수락 시각, 오버레이 수용 여부)만 담는 이벤트 인자. 제목·본문은 담지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>
/// Raised by QueueManager.NotificationAccepted. Deliberately carries no title/body text: only the
/// app identity, the acceptance time, and whether the overlay queue is taking the notification.
/// </summary>
public sealed class NotificationAcceptedEventArgs : EventArgs
{
    public NotificationAcceptedEventArgs(string appName, string? appId, bool overlayAccepted, DateTime acceptedAt)
    {
        AppName = appName ?? string.Empty;
        AppId = string.IsNullOrWhiteSpace(appId) ? null : appId;
        OverlayAccepted = overlayAccepted;
        AcceptedAt = acceptedAt;
    }

    /// <summary>App display name used by the existing name-based rules (may be "Unknown").</summary>
    public string AppName { get; }

    /// <summary>WinRT AppUserModelId when known; null for accessibility capture or unreadable AppInfo.</summary>
    public string? AppId { get; }

    /// <summary>
    /// True when the overlay queue takes this notification (NotificationAdded/NotificationDisplayed follow);
    /// false when it passed policy but the overlay rejected it (visible limit reached → overflow count only).
    /// </summary>
    public bool OverlayAccepted { get; }

    public DateTime AcceptedAt { get; }
}
