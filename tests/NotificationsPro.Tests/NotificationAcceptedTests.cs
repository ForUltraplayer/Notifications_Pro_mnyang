// @file NotificationAcceptedTests.cs
// @author: rudals252
// @brief QueueManager.NotificationAccepted(정책 통과) 계약 테스트: 오버플로 시 NotificationAdded와의 차이, 기존 Added/Displayed 시점 유지, 모든 정책 거절 시 미발생, 내용 중복 ON/OFF, AUMID 전달, 모드별 단일 구독. P2-A: NotificationAddedWithIdentity 시점·조건.

using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class NotificationAcceptedTests
{
    private static SettingsManager CreateSettings(int maxVisible = 2, Action<AppSettings>? configure = null)
    {
        var manager = TestAppDataIsolation.CreateSettingsManager();
        manager.Settings.NotificationDuration = 60;
        manager.Settings.MaxVisibleNotifications = maxVisible;
        manager.Settings.AnimationDurationMs = 0;
        configure?.Invoke(manager.Settings);
        return manager;
    }

    private sealed class EventLog
    {
        public List<string> Order { get; } = new();
        public List<NotificationAcceptedEventArgs> Accepted { get; } = new();
        public List<string> Added { get; } = new();
        public List<NotificationItem> Displayed { get; } = new();

        public EventLog(QueueManager queue)
        {
            queue.NotificationAccepted += args => { Accepted.Add(args); Order.Add("Accepted:" + args.AppName); };
            queue.NotificationAdded += app => { Added.Add(app); Order.Add("Added:" + app); };
            queue.NotificationDisplayed += item => { Displayed.Add(item); Order.Add("Displayed:" + item.AppName); };
        }
    }

    [Fact]
    public void Overflow_RaisesAcceptedButNotAddedOrDisplayed()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 2));
        var log = new EventLog(queue);

        queue.AddNotification("A", "t1", "b1");
        queue.AddNotification("B", "t2", "b2");
        queue.AddNotification("C", "t3", "b3"); // overflow

        Assert.Equal(new[] { "A", "B", "C" }, log.Accepted.Select(a => a.AppName));
        Assert.Equal(new[] { true, true, false }, log.Accepted.Select(a => a.OverlayAccepted));
        Assert.Equal(new[] { "A", "B" }, log.Added);
        Assert.Equal(new[] { "A", "B" }, log.Displayed.Select(d => d.AppName));
        Assert.Equal(1, queue.OverflowCount);
    }

    [Fact]
    public void AddedAndDisplayed_KeepOriginalTiming_AcceptedComesFirst()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 5));
        var log = new EventLog(queue);

        queue.AddNotification("Slack", "Hi", "There");

        Assert.Equal(new[] { "Accepted:Slack", "Added:Slack", "Displayed:Slack" }, log.Order);
        Assert.Single(queue.VisibleNotifications);
    }

    [Fact]
    public void OverlayAcceptedFlag_MatchesWhetherAddedFires_IncludingReplaceMode()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 1, s => s.ReplaceMode = true));
        var log = new EventLog(queue);

        queue.AddNotification("A", "1", "x");
        queue.AddNotification("B", "2", "y");

        Assert.All(log.Accepted, a => Assert.True(a.OverlayAccepted));
        Assert.Equal(new[] { "A", "B" }, log.Added);
    }

    [Fact]
    public void Accepted_CarriesAppIdButNoContent()
    {
        var queue = new QueueManager(CreateSettings());
        var log = new EventLog(queue);

        queue.AddNotification("Codex", "Done", "Task finished", "OpenAI.Codex_2p2nqsd0c76g0!App");
        queue.AddNotification("Reader", "Line", "Text", null);

        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0!App", log.Accepted[0].AppId);
        Assert.Null(log.Accepted[1].AppId);
        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0!App", queue.VisibleNotifications.Single(n => n.AppName == "Codex").AppId);
        Assert.DoesNotContain(typeof(NotificationAcceptedEventArgs).GetProperties(), p => p.Name is "Title" or "Body");
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("quiet")]
    [InlineData("mutedApp")]
    [InlineData("muteRule")]
    [InlineData("muteKeyword")]
    public void PolicyRejections_RaiseNoAccepted(string policy)
    {
        var manager = CreateSettings(maxVisible: 5, s =>
        {
            switch (policy)
            {
                case "paused":
                    s.NotificationsPaused = true; // focus mode and presentation mode use this flag
                    break;
                case "quiet":
                    s.QuietHoursEnabled = true;
                    s.QuietHoursStart = "00:00";
                    s.QuietHoursEnd = "23:59:59";
                    break;
                case "mutedApp":
                    s.MutedApps.Add("Discord");
                    break;
                case "muteRule":
                    s.MuteRules.Add(new MuteRuleDefinition { Keyword = "secret" });
                    break;
                case "muteKeyword":
                    s.MuteKeywords.Add("secret");
                    break;
            }
        });
        var queue = new QueueManager(manager);
        var log = new EventLog(queue);

        queue.AddNotification("Discord", "a secret", "message", "Discord.App");

        Assert.Empty(log.Accepted);
        Assert.Empty(log.Added);
    }

    [Fact]
    public void BurstLimit_RejectsAcceptedLikeOverlay()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 50, s =>
        {
            s.BurstLimitEnabled = true;
            s.BurstLimitCount = 2;
            s.BurstLimitWindowSeconds = 60;
        }));
        var log = new EventLog(queue);

        queue.AddNotification("A", "1", "x");
        queue.AddNotification("A", "2", "y");
        queue.AddNotification("A", "3", "z");

        Assert.Equal(2, log.Accepted.Count);
        Assert.Equal(2, log.Added.Count);
    }

    [Fact]
    public void VisibleDuplicate_RaisesNoAccepted_WhenDeduplicationOn()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 5, s => s.DeduplicationEnabled = true));
        var log = new EventLog(queue);

        queue.AddNotification("Mail", "Same", "Text");
        queue.AddNotification("Mail", "Same", "Text");

        Assert.Single(log.Accepted);
        Assert.Single(log.Added);
    }

    [Fact]
    public void OverflowDuplicate_IsSuppressedOnPolicyPath_WhenDeduplicationOn_ButOverlayBehaviorUnchanged()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 1, s =>
        {
            s.DeduplicationEnabled = true;
            s.DeduplicationWindowSeconds = 30;
        }));
        var log = new EventLog(queue);

        queue.AddNotification("Fill", "x", "y");
        queue.AddNotification("Mail", "Same", "Text"); // overflow, accepted
        queue.AddNotification("Mail", "Same", "Text"); // overflow duplicate → no second accepted
        queue.AddNotification("Mail", "Other", "Text"); // different content → accepted

        Assert.Equal(new[] { "Fill", "Mail", "Mail" }, log.Accepted.Select(a => a.AppName));
        Assert.Equal(new[] { true, false, false }, log.Accepted.Select(a => a.OverlayAccepted));
        Assert.Equal(3, queue.OverflowCount); // overlay still counts every overflow like upstream
    }

    [Fact]
    public void DeduplicationOff_DoesNotDropSeparateIdenticalNotifications()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 1, s => s.DeduplicationEnabled = false));
        var log = new EventLog(queue);

        queue.AddNotification("Fill", "x", "y");
        queue.AddNotification("Mail", "Same", "Text");
        queue.AddNotification("Mail", "Same", "Text");
        queue.AddNotification("Mail", "Same", "Text");

        Assert.Equal(4, log.Accepted.Count);
        Assert.Equal(3, log.Accepted.Count(a => a.AppName == "Mail"));
    }

    [Fact]
    public void DeduplicationOff_VisibleIdenticalNotifications_AllReachOverlayAndAccepted()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 5, s => s.DeduplicationEnabled = false));
        var log = new EventLog(queue);

        queue.AddNotification("Mail", "Same", "Text");
        queue.AddNotification("Mail", "Same", "Text");

        Assert.Equal(2, log.Accepted.Count);
        Assert.Equal(2, log.Added.Count);
        Assert.Equal(2, queue.VisibleNotifications.Count);
    }

    [Fact]
    public void Preview_RaisesNoAccepted()
    {
        var queue = new QueueManager(CreateSettings());
        var log = new EventLog(queue);

        queue.AddPreviewNotification("Preview", "t", "b");

        Assert.Empty(log.Accepted);
        Assert.Empty(log.Added);
    }

    [Fact]
    public void AppIdentities_RecordsSeveralAumidsForOneDisplayName()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 10));

        queue.AddNotification("Browser", "a", "1", "Chrome.App");
        queue.AddNotification("Browser", "b", "2", "Edge.App");
        queue.AddNotification("Mail", "c", "3", "Mail.App");
        queue.AddNotification("Reader", "d", "4");

        Assert.True(queue.AppIdentities.HasMultipleAppIds("browser"));
        Assert.Equal(new[] { "Browser" }, queue.AppIdentities.GetAmbiguousDisplayNames());
        Assert.False(queue.AppIdentities.HasMultipleAppIds("Mail"));
        Assert.Empty(queue.AppIdentities.GetAppIds("Reader"));
    }

    // ---- AudioDispatchSubscription: exactly one trigger per mode ----

    private static (QueueManager Queue, SettingsManager Settings, List<(string App, string? Id)> Plays, AudioDispatchSubscription Subscription)
        CreateDispatch(AudioDispatchMode mode, int maxVisible = 1)
    {
        var settings = CreateSettings(maxVisible, s => s.AudioDispatchMode = mode);
        var queue = new QueueManager(settings);
        var plays = new List<(string, string?)>();
        var subscription = new AudioDispatchSubscription(queue, settings, (app, id) => plays.Add((app, id)));
        return (queue, settings, plays, subscription);
    }

    [Fact]
    public void Dispatch_OverlayAccepted_PlaysOnlyForOverlayAcceptedNotifications()
    {
        var (queue, _, plays, subscription) = CreateDispatch(AudioDispatchMode.OverlayAccepted);

        queue.AddNotification("A", "1", "x", "A.App");
        queue.AddNotification("B", "2", "y", "B.App"); // overflow

        Assert.Equal(AudioDispatchMode.OverlayAccepted, subscription.ActiveMode);
        // P2-A: the OverlayAccepted path now carries the AUMID too (NotificationAddedWithIdentity).
        Assert.Equal(new[] { ("A", (string?)"A.App") }, plays);
    }

    [Fact]
    public void Dispatch_PolicyAccepted_PlaysForOverflowToo_ExactlyOncePerNotification()
    {
        var (queue, _, plays, subscription) = CreateDispatch(AudioDispatchMode.PolicyAccepted);

        queue.AddNotification("A", "1", "x", "A.App");
        queue.AddNotification("B", "2", "y", "B.App"); // overflow

        Assert.Equal(AudioDispatchMode.PolicyAccepted, subscription.ActiveMode);
        Assert.Equal(new[] { ("A", (string?)"A.App"), ("B", (string?)"B.App") }, plays);
    }

    [Fact]
    public void Dispatch_ModeChange_Resubscribes_WithoutDoublePlay()
    {
        var (queue, settings, plays, subscription) = CreateDispatch(AudioDispatchMode.OverlayAccepted, maxVisible: 10);

        var updated = settings.Settings.Clone();
        updated.AudioDispatchMode = AudioDispatchMode.PolicyAccepted;
        settings.Apply(updated);
        queue.AddNotification("A", "1", "x", "A.App");

        Assert.Equal(AudioDispatchMode.PolicyAccepted, subscription.ActiveMode);
        Assert.Single(plays);
        Assert.Equal("A.App", plays[0].Id);

        updated = settings.Settings.Clone();
        updated.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
        settings.Apply(updated);
        queue.AddNotification("B", "2", "y", "B.App");

        Assert.Equal(AudioDispatchMode.OverlayAccepted, subscription.ActiveMode);
        Assert.Equal(2, plays.Count);
        Assert.Equal("B.App", plays[1].Id); // P2-A: OverlayAccepted now also carries the AUMID
    }

    [Fact]
    public void Dispatch_UnrelatedSettingsChange_DoesNotDuplicateSubscription()
    {
        var (queue, settings, plays, _) = CreateDispatch(AudioDispatchMode.PolicyAccepted, maxVisible: 10);

        for (var i = 0; i < 3; i++)
        {
            var updated = settings.Settings.Clone();
            updated.FontSize = 10 + i;
            settings.Apply(updated);
        }

        queue.AddNotification("A", "1", "x");
        Assert.Single(plays);
    }

    [Fact]
    public void Dispatch_Dispose_StopsPlaying()
    {
        var (queue, _, plays, subscription) = CreateDispatch(AudioDispatchMode.PolicyAccepted, maxVisible: 10);

        subscription.Dispose();
        queue.AddNotification("A", "1", "x");

        Assert.Null(subscription.ActiveMode);
        Assert.Empty(plays);
    }

    // ---- P2-A: NotificationAddedWithIdentity ----

    [Fact]
    public void AddedWithIdentity_FiresRightAfterAdded_BeforeDisplayed_WithAumid()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 5));
        var order = new List<string>();
        var identities = new List<(string App, string? Id)>();
        queue.NotificationAccepted += args => order.Add("Accepted");
        queue.NotificationAdded += _ => order.Add("Added");
        queue.NotificationAddedWithIdentity += (app, id) => { order.Add("AddedWithIdentity"); identities.Add((app, id)); };
        queue.NotificationDisplayed += _ => order.Add("Displayed");

        queue.AddNotification("Slack", "Hi", "There", "Slack.App");
        queue.AddNotification("Reader", "Hi", "There");

        Assert.Equal(new[] { "Accepted", "Added", "AddedWithIdentity", "Displayed", "Accepted", "Added", "AddedWithIdentity", "Displayed" }, order);
        Assert.Equal(new[] { ("Slack", (string?)"Slack.App"), ("Reader", (string?)null) }, identities);
    }

    [Fact]
    public void AddedWithIdentity_FollowsAddedExactly_ForOverflowReplaceAndPreview()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 1));
        var added = new List<string>();
        var withIdentity = new List<string>();
        queue.NotificationAdded += app => added.Add(app);
        queue.NotificationAddedWithIdentity += (app, _) => withIdentity.Add(app);

        queue.AddNotification("A", "1", "x", "A.App");
        queue.AddNotification("B", "2", "y", "B.App"); // overflow: neither event
        queue.AddPreviewNotification("Preview", "t", "b");

        Assert.Equal(added, withIdentity);
        Assert.Equal(new[] { "A" }, withIdentity);
    }

    [Fact]
    public void ExistingAddedSubscribers_StillReceiveOnlyTheAppName()
    {
        var queue = new QueueManager(CreateSettings(maxVisible: 5));
        var received = new List<string>();
        Action<string> legacyHandler = app => received.Add(app);
        queue.NotificationAdded += legacyHandler;

        queue.AddNotification("Slack", "Hi", "There", "Slack.App");

        Assert.Equal(new[] { "Slack" }, received);
    }

    [Fact]
    public void Dispatch_OverlayAccepted_DoesNotDoublePlay_WhenAddedAndAddedWithIdentityBothFire()
    {
        var (queue, settings, plays, subscription) = CreateDispatch(AudioDispatchMode.OverlayAccepted, maxVisible: 5);

        queue.AddNotification("A", "1", "x", "A.App");
        queue.AddNotification("B", "2", "y");

        Assert.Equal(new[] { ("A", (string?)"A.App"), ("B", (string?)null) }, plays);
        subscription.Dispose();
    }
}
