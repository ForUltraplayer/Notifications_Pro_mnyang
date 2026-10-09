// @file WinRtPollTracker.cs
// @author: rudals252
// @brief WinRT 알림 조회·이벤트의 순수 로직(중복 키 생성, 시드 기준 집합, 신규 판정, 목록 밖 키 정리, 권한 상태 분기)을 WinRT 의존 없이 테스트 가능하게 분리한다.

using Windows.UI.Notifications.Management;

namespace NotificationsPro.Services;

/// <summary>
/// Event-level de-duplication key for a WinRT notification. The app id falls back to
/// <see cref="WinRtPollTracker.UnknownAppId"/> when AppInfo cannot be read (e.g. tray balloon tips).
/// Only ids and timestamps are kept — never notification text.
/// </summary>
public readonly record struct WinRtNotificationKey(string AppId, uint Id, DateTimeOffset CreationTime);

/// <summary>What the listener should do after checking the access status on a poll tick.</summary>
public enum WinRtAccessDecision
{
    /// <summary>Access is allowed: poll normally.</summary>
    Poll,
    /// <summary>Access just stopped being allowed: stop polling and show a revoked/denied state.</summary>
    EnterRevoked,
    /// <summary>Access is still not allowed: keep polling stopped.</summary>
    StayRevoked,
    /// <summary>Access is allowed again after being revoked: re-seed the baseline, then poll.</summary>
    Resume
}

/// <summary>
/// Tracks which WinRT notifications were already handled. Shared by the
/// NotificationChanged event path and the periodic poll so each notification is processed once.
/// Not thread-safe: use from the dispatcher thread only.
/// </summary>
public sealed class WinRtPollTracker
{
    public const string UnknownAppId = "Unknown";

    // Key -> snapshot generation in which the key was first recorded.
    private readonly Dictionary<WinRtNotificationKey, long> _seen = new();
    private long _generation;

    public int SeenCount => _seen.Count;

    public static WinRtNotificationKey CreateKey(string? appUserModelId, uint id, DateTimeOffset creationTime)
    {
        var appId = string.IsNullOrWhiteSpace(appUserModelId) ? UnknownAppId : appUserModelId.Trim();
        return new WinRtNotificationKey(appId, id, creationTime);
    }

    /// <summary>
    /// Replaces the handled set with the notifications that already exist (start/retry/resume).
    /// These are never forwarded retroactively.
    /// </summary>
    public void Seed(IEnumerable<WinRtNotificationKey> existing)
    {
        _seen.Clear();
        _generation++;
        foreach (var key in existing)
            _seen[key] = _generation;
    }

    public void Reset()
    {
        _seen.Clear();
        _generation = 0;
    }

    public bool Contains(WinRtNotificationKey key) => _seen.ContainsKey(key);

    /// <summary>
    /// Records a key reported by the NotificationChanged event. Returns true only the first time.
    /// </summary>
    public bool TryMarkSeen(WinRtNotificationKey key)
    {
        if (_seen.ContainsKey(key))
            return false;

        _seen[key] = _generation;
        return true;
    }

    /// <summary>
    /// Call right before requesting a poll snapshot. The returned token lets
    /// <see cref="ProcessSnapshot"/> keep keys recorded while the request was in flight.
    /// </summary>
    public long BeginSnapshot()
    {
        _generation++;
        return _generation;
    }

    /// <summary>
    /// Returns the keys in <paramref name="current"/> that were not handled yet (and marks them
    /// handled), then forgets handled keys that are no longer in the system list. Keys recorded
    /// after <paramref name="snapshotToken"/> was issued are kept, because the snapshot may predate them.
    /// </summary>
    public IReadOnlyList<WinRtNotificationKey> ProcessSnapshot(long snapshotToken, IReadOnlyCollection<WinRtNotificationKey> current)
    {
        var newKeys = new List<WinRtNotificationKey>();
        var currentSet = new HashSet<WinRtNotificationKey>(current);

        foreach (var key in current)
        {
            if (_seen.ContainsKey(key))
                continue;

            _seen[key] = snapshotToken;
            newKeys.Add(key);
        }

        // Equivalent of IntersectWith(current), except for keys added after the snapshot began.
        var stale = _seen
            .Where(entry => entry.Value < snapshotToken && !currentSet.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToList();
        foreach (var key in stale)
            _seen.Remove(key);

        return newKeys;
    }

    public static WinRtAccessDecision EvaluateAccess(UserNotificationListenerAccessStatus status, bool currentlyRevoked)
    {
        var allowed = status == UserNotificationListenerAccessStatus.Allowed;
        if (allowed)
            return currentlyRevoked ? WinRtAccessDecision.Resume : WinRtAccessDecision.Poll;

        return currentlyRevoked ? WinRtAccessDecision.StayRevoked : WinRtAccessDecision.EnterRevoked;
    }
}
