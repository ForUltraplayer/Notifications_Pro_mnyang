// @file WinRtPollTrackerTests.cs
// @author: rudals252
// @brief WinRtPollTracker의 키 생성, 신규 판정, 이벤트·조회 중복 1회 처리, 목록 밖 키 정리, 권한 비허용 분기, 시드 소급 없음 검증.

using NotificationsPro.Services;
using Windows.UI.Notifications.Management;

namespace NotificationsPro.Tests;

public class WinRtPollTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(9));

    private static WinRtNotificationKey Key(string? aumid, uint id, int secondsOffset = 0)
        => WinRtPollTracker.CreateKey(aumid, id, T0.AddSeconds(secondsOffset));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateKey_UsesUnknown_WhenAppIdIsMissing(string? aumid)
    {
        var key = WinRtPollTracker.CreateKey(aumid, 7, T0);

        Assert.Equal(WinRtPollTracker.UnknownAppId, key.AppId);
        Assert.Equal(7u, key.Id);
        Assert.Equal(T0, key.CreationTime);
    }

    [Fact]
    public void CreateKey_DistinguishesReusedIdsByAppAndCreationTime()
    {
        var a = Key("App.A", 1);
        Assert.NotEqual(a, Key("App.B", 1));
        Assert.NotEqual(a, Key("App.A", 1, secondsOffset: 5));
        Assert.Equal(a, Key("App.A", 1));
    }

    [Fact]
    public void Seed_DoesNotForwardExistingNotificationsRetroactively()
    {
        var tracker = new WinRtPollTracker();
        var existing = new[] { Key("App.A", 1), Key(null, 2) };
        tracker.Seed(existing);

        var newKeys = tracker.ProcessSnapshot(tracker.BeginSnapshot(), existing);

        Assert.Empty(newKeys);
    }

    [Fact]
    public void ProcessSnapshot_ReturnsOnlyNewKeys_AndEachOnlyOnce()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(new[] { Key("App.A", 1) });

        var first = tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { Key("App.A", 1), Key("App.B", 2) });
        var second = tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { Key("App.A", 1), Key("App.B", 2) });

        Assert.Equal(new[] { Key("App.B", 2) }, first);
        Assert.Empty(second);
    }

    [Fact]
    public void EventThenPoll_SameKey_IsHandledOnce()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(Array.Empty<WinRtNotificationKey>());
        var key = Key("App.A", 10);

        Assert.True(tracker.TryMarkSeen(key));
        var fromPoll = tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { key });

        Assert.Empty(fromPoll);
        Assert.False(tracker.TryMarkSeen(key));
    }

    [Fact]
    public void PollThenEvent_SameKey_IsHandledOnce()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(Array.Empty<WinRtNotificationKey>());
        var key = Key("App.A", 10);

        var fromPoll = tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { key });

        Assert.Single(fromPoll);
        Assert.False(tracker.TryMarkSeen(key));
    }

    [Fact]
    public void ProcessSnapshot_PrunesKeysThatLeftTheSystemList()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(new[] { Key("App.A", 1), Key("App.A", 2) });

        tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { Key("App.A", 2) });

        Assert.Equal(1, tracker.SeenCount);
        Assert.False(tracker.Contains(Key("App.A", 1)));
        Assert.True(tracker.Contains(Key("App.A", 2)));
    }

    [Fact]
    public void ProcessSnapshot_KeepsEventKeyRecordedWhileSnapshotWasInFlight()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(Array.Empty<WinRtNotificationKey>());
        var key = Key("App.A", 3);

        // Poll request starts, then the event for a brand-new notification is handled
        // before the (older) snapshot returns without it.
        var token = tracker.BeginSnapshot();
        Assert.True(tracker.TryMarkSeen(key));
        tracker.ProcessSnapshot(token, Array.Empty<WinRtNotificationKey>());

        // The next snapshot contains it: it must not be reported a second time.
        var next = tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { key });
        Assert.Empty(next);
    }

    [Fact]
    public void UnknownAppNotifications_AreStillTrackedAndReported()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(Array.Empty<WinRtNotificationKey>());

        var newKeys = tracker.ProcessSnapshot(tracker.BeginSnapshot(), new[] { Key(null, 27058) });

        var key = Assert.Single(newKeys);
        Assert.Equal(WinRtPollTracker.UnknownAppId, key.AppId);
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        var tracker = new WinRtPollTracker();
        tracker.Seed(new[] { Key("App.A", 1) });

        tracker.Reset();

        Assert.Equal(0, tracker.SeenCount);
    }

    [Theory]
    [InlineData(UserNotificationListenerAccessStatus.Allowed, false, WinRtAccessDecision.Poll)]
    [InlineData(UserNotificationListenerAccessStatus.Denied, false, WinRtAccessDecision.EnterRevoked)]
    [InlineData(UserNotificationListenerAccessStatus.Unspecified, false, WinRtAccessDecision.EnterRevoked)]
    [InlineData(UserNotificationListenerAccessStatus.Denied, true, WinRtAccessDecision.StayRevoked)]
    [InlineData(UserNotificationListenerAccessStatus.Unspecified, true, WinRtAccessDecision.StayRevoked)]
    [InlineData(UserNotificationListenerAccessStatus.Allowed, true, WinRtAccessDecision.Resume)]
    public void EvaluateAccess_StopsPollingUnlessAllowed(
        UserNotificationListenerAccessStatus status,
        bool currentlyRevoked,
        WinRtAccessDecision expected)
    {
        Assert.Equal(expected, WinRtPollTracker.EvaluateAccess(status, currentlyRevoked));
    }
}
