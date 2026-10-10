// @file PlaybackCoordinatorTests.cs
// @author: rudals252
// @brief 재생 조정기 계약 테스트(설계 §7.4, §14.1 "재생"): 한 번에 하나, 대기열·초과·만료, 앱별 최소 간격, 설정 변경 시 대기 취소, 실패·취소·늦은 완료 처리, 미리 듣기, 낭독 권한 공유, dispose 시 권한 반환. 가짜 엔진으로 완료·실패·지연을 흉내 낸다.

using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class PlaybackCoordinatorTests
{
    private sealed class Harness
    {
        public Harness(Action<AppSettings>? configure = null, Func<int, int>? random = null)
        {
            Settings = TestAppDataIsolation.CreateSettingsManager();
            var settings = Settings.Settings;
            settings.SoundEnabled = true;
            settings.DefaultSoundRule = new SoundRule { Mode = SoundSelectionMode.Sequential, Files = { "sounds/1.wav", "sounds/2.wav", "sounds/3.wav" } };
            settings.PlaybackPolicy = new PlaybackPolicy { MinIntervalPerAppSeconds = 0, MaxQueued = 5, MaxQueueWaitSeconds = 30 };
            configure?.Invoke(settings);

            Selector = new SoundRuleSelector(random);
            Coordinator = new PlaybackCoordinator(Settings, Engine, Files, Posts.Post, Selector, () => Clock.UtcNow);
        }

        public SettingsManager Settings { get; }
        public FakeSoundEngine Engine { get; } = new();
        public FakeSoundFileGate Files { get; } = new();
        public ManualPostQueue Posts { get; } = new();
        public ManualClock Clock { get; } = new();
        public SoundRuleSelector Selector { get; }
        public PlaybackCoordinator Coordinator { get; }

        public PlaybackRequestOutcome Sound(string app = "Slack", string? appId = null)
        {
            var outcome = Coordinator.EnqueueNotificationSound(app, appId);
            Posts.RunAll();
            return outcome;
        }

        public void CompleteCurrent()
        {
            var current = Engine.Current ?? throw new InvalidOperationException("nothing playing");
            current.Complete();
            Posts.RunAll();
        }

        public void ApplySettings(Action<AppSettings> change)
        {
            var updated = Settings.Settings.Clone();
            change(updated);
            Settings.Apply(updated);
            Posts.RunAll();
        }

        public IEnumerable<string> PlayedFiles => Engine.Plays.Select(play => FakeSoundFileGate.FileOf(play.FullPath));

        public PlaybackDiagnostics Diag => Coordinator.GetDiagnostics();
    }

    // ---- one at a time / queue ----

    [Fact]
    public void ConcurrentNotifications_PlayOneAtATime_InArrivalOrder()
    {
        var h = new Harness();

        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("A"));
        Assert.Equal(PlaybackRequestOutcome.Queued, h.Sound("B"));
        Assert.Equal(PlaybackRequestOutcome.Queued, h.Sound("C"));
        Assert.Single(h.Engine.Plays);
        Assert.Equal(2, h.Diag.QueuedSounds);

        h.CompleteCurrent();
        Assert.Equal(2, h.Engine.Plays.Count);
        h.CompleteCurrent();
        h.CompleteCurrent();

        Assert.Equal(3, h.Engine.Plays.Count);
        Assert.Equal(1, h.Engine.MaxAlive);
        Assert.Equal(1, h.Diag.MaxConcurrentPlaybacks);
        Assert.Equal(3, h.Diag.Started);
        Assert.Equal(3, h.Diag.Completed);
        Assert.Equal(PlaybackActivity.Idle, h.Diag.Activity);
    }

    [Fact]
    public void QueueFull_SkipsNewSounds_BeyondMaxQueued()
    {
        var h = new Harness(s => s.PlaybackPolicy.MaxQueued = 2);

        h.Sound("A");
        h.Sound("B");
        h.Sound("C");

        Assert.Equal(PlaybackRequestOutcome.SkippedQueueFull, h.Sound("D"));
        Assert.Equal(2, h.Diag.QueuedSounds);
        Assert.Equal(1, h.Diag.SkippedBusyOrFull);
    }

    [Fact]
    public void OverflowBehaviorSkip_DropsSoundsWhileBusy()
    {
        var h = new Harness(s => s.PlaybackPolicy.OverflowBehavior = PlaybackOverflowBehavior.Skip);

        h.Sound("A");

        Assert.Equal(PlaybackRequestOutcome.SkippedBusy, h.Sound("B"));
        Assert.Equal(0, h.Diag.QueuedSounds);
    }

    [Fact]
    public void QueuedSound_ExpiresAfterMaxWait_WithoutConsumingTheOrder()
    {
        var h = new Harness(s => s.PlaybackPolicy.MaxQueueWaitSeconds = 30);
        h.Sound("A");               // A plays 1.wav
        h.Sound("B");               // queued
        h.Clock.Advance(31);

        h.CompleteCurrent();

        Assert.Single(h.Engine.Plays);
        Assert.Equal(1, h.Diag.Expired);
        Assert.Equal(0, h.Selector.GetNextSequentialIndex("default:B"));

        h.Sound("B");
        Assert.Equal("sounds/1.wav", h.PlayedFiles.Last()); // B's sequence still starts at the first file
    }

    // ---- cooldown ----

    [Fact]
    public void Cooldown_SkipsTheSameAppWithinTheInterval_ButNotOtherApps()
    {
        var h = new Harness(s => s.PlaybackPolicy.MinIntervalPerAppSeconds = 4);

        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("A", "A.App"));
        h.CompleteCurrent();
        h.Clock.Advance(1);
        Assert.Equal(PlaybackRequestOutcome.SkippedCooldown, h.Sound("A", "A.App"));
        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("B"));
        h.CompleteCurrent();
        h.Clock.Advance(3.5);
        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("A", "A.App"));

        Assert.Equal(1, h.Diag.SkippedCooldown);
        Assert.Equal(new[] { "sounds/1.wav", "sounds/1.wav", "sounds/2.wav" }, h.PlayedFiles); // skip did not consume A's order
    }

    [Fact]
    public void Cooldown_Zero_IsOff()
    {
        var h = new Harness(s => s.PlaybackPolicy.MinIntervalPerAppSeconds = 0);

        h.Sound("A");
        h.Sound("A");
        h.Sound("A");

        Assert.Equal(0, h.Diag.SkippedCooldown);
        Assert.Equal(2, h.Diag.QueuedSounds);
    }

    [Fact]
    public void Cooldown_IsReleased_WhenTheSoundFailsBeforeItEverStarted()
    {
        var h = new Harness(s =>
        {
            s.PlaybackPolicy.MinIntervalPerAppSeconds = 4;
            s.DefaultSoundRule = new SoundRule { Files = { "sounds/1.wav" } };
        });
        h.Engine.AutoStart = false;

        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("A", "A.App"));
        h.Engine.Current!.Fail(SoundEngineFailureKind.DeviceOrOther);   // never started, no other file to try
        h.Posts.RunAll();
        h.Clock.Advance(1);

        // The failed sound was never heard, so the next notification of the same app is not skipped.
        h.Engine.AutoStart = true;
        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("A", "A.App"));
        Assert.Equal(0, h.Diag.SkippedCooldown);
    }

    [Fact]
    public void Cooldown_IsReleased_WhenAQueuedSoundIsCancelled()
    {
        var h = new Harness(s => s.PlaybackPolicy.MinIntervalPerAppSeconds = 4);

        h.Sound("X", "X.App");                                   // playing
        Assert.Equal(PlaybackRequestOutcome.Queued, h.Sound("A", "A.App"));
        h.Coordinator.CancelPendingSounds();                     // A never played
        h.Posts.RunAll();
        h.Clock.Advance(1);

        Assert.Equal(PlaybackRequestOutcome.Queued, h.Sound("A", "A.App"));
        Assert.Equal(0, h.Diag.SkippedCooldown);
    }

    [Fact]
    public void Cooldown_StillApplies_AfterASoundThatActuallyPlayed()
    {
        var h = new Harness(s => s.PlaybackPolicy.MinIntervalPerAppSeconds = 4);

        h.Sound("A", "A.App");
        h.Engine.Current!.Fail(SoundEngineFailureKind.DeviceOrOther);   // failed after it had started
        h.Posts.RunAll();
        h.Clock.Advance(1);

        Assert.Equal(PlaybackRequestOutcome.SkippedCooldown, h.Sound("A", "A.App"));
    }

    // ---- silence / pause / settings changes ----

    [Fact]
    public void SilentRule_AndPause_AreSkipped()
    {
        var h = new Harness(s => s.AppSoundRules["Quiet"] = new SoundRule { ExplicitSilence = true });

        Assert.Equal(PlaybackRequestOutcome.SkippedSilent, h.Sound("Quiet"));
        h.Settings.Settings.NotificationsPaused = true;
        Assert.Equal(PlaybackRequestOutcome.SkippedPaused, h.Sound("A"));
        Assert.Empty(h.Engine.Plays);
    }

    [Fact]
    public void SoundRuleChange_CancelsQueuedSounds_ButNotTheActiveOne()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");
        h.Sound("C");

        h.ApplySettings(s => s.DefaultSoundRule.Files.Add("sounds/4.wav"));

        Assert.Equal(0, h.Diag.QueuedSounds);
        Assert.Equal(2, h.Diag.Cancelled);
        Assert.False(h.Engine.Plays[0].Stopped);
        h.CompleteCurrent();
        Assert.Single(h.Engine.Plays);
    }

    [Fact]
    public void ContentFilterChange_CancelsQueuedSounds()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");

        h.ApplySettings(s => s.MuteKeywords.Add("secret"));

        Assert.Equal(0, h.Diag.QueuedSounds);
    }

    [Fact]
    public void UnrelatedSettingChange_KeepsQueuedSounds()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");

        h.ApplySettings(s => s.FontSize += 1);

        Assert.Equal(1, h.Diag.QueuedSounds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SoundOffOrPause_StopsTheActiveSound_AndCancelsTheQueue(bool pause)
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");

        h.ApplySettings(s =>
        {
            if (pause) s.NotificationsPaused = true;
            else s.SoundEnabled = false;
        });

        Assert.True(h.Engine.Plays[0].Stopped);
        Assert.Equal(0, h.Engine.Alive);
        Assert.Equal(PlaybackActivity.Idle, h.Diag.Activity);
        Assert.Equal(0, h.Diag.QueuedSounds);
    }

    // ---- failures / cancellation / late callbacks ----

    [Fact]
    public void FailureBeforeStart_TriesTheNextUsableFile_InTheSameEvent_AndMarksTheFile()
    {
        var h = new Harness();
        h.Engine.AutoStart = false;

        h.Sound("A");
        h.Engine.Plays[0].Fail(SoundEngineFailureKind.FileUnsupported);
        h.Posts.RunAll();

        Assert.Equal(2, h.Engine.Plays.Count);
        Assert.Equal("sounds/2.wav", FakeSoundFileGate.FileOf(h.Engine.Plays[1].FullPath));
        Assert.Contains(@"C:\fake\sounds\1.wav", h.Files.MarkedUnplayable);
        Assert.Equal(0, h.Selector.GetNextSequentialIndex("default:A")); // nothing committed yet

        h.Engine.Plays[1].Start();
        h.Posts.RunAll();
        Assert.Equal(2, h.Selector.GetNextSequentialIndex("default:A"));
        Assert.Equal(1, h.Diag.Failed);
        Assert.Equal(1, h.Diag.Started);
    }

    [Fact]
    public void DeviceFailure_DoesNotMarkTheFileUnplayable()
    {
        var h = new Harness();
        h.Engine.AutoStart = false;

        h.Sound("A");
        h.Engine.Plays[0].Fail(SoundEngineFailureKind.DeviceOrOther);
        h.Posts.RunAll();

        Assert.Empty(h.Files.MarkedUnplayable);
    }

    [Fact]
    public void AllFilesFail_SkipsTheEvent_AndReturnsTheTurn()
    {
        var h = new Harness(s => s.DefaultSoundRule = SoundRule.CreateSingle("sounds/bad.wav"));
        h.Engine.AutoStart = false;
        h.Sound("A");
        h.Sound("B", "B.App");

        h.Engine.Plays[0].Fail();
        h.Posts.RunAll();

        Assert.Single(h.Engine.Plays); // B's only file is now known bad: skipped instead of retried
        Assert.Equal(PlaybackActivity.Idle, h.Diag.Activity);
        Assert.Equal(2, h.Diag.SkippedNoUsableFile);
        Assert.Equal(PlaybackRequestOutcome.SkippedNoUsableFile, h.Sound("C"));
    }

    [Fact]
    public void FailureAfterStart_ReturnsTheTurn_AndPlaysTheNextQueuedSound()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");

        h.Engine.Plays[0].Fail(SoundEngineFailureKind.DeviceOrOther);
        h.Posts.RunAll();

        Assert.Equal(2, h.Engine.Plays.Count);
        Assert.Equal(1, h.Engine.Alive);
    }

    [Fact]
    public void EngineThrowing_DoesNotKeepTheTurn()
    {
        var h = new Harness();
        h.Engine.ThrowOnPlay = true;

        Assert.Equal(PlaybackRequestOutcome.SkippedNoUsableFile, h.Sound("A"));
        Assert.Equal(PlaybackActivity.Idle, h.Diag.Activity);

        h.Engine.ThrowOnPlay = false;
        Assert.Equal(PlaybackRequestOutcome.Started, h.Sound("B"));
    }

    [Fact]
    public void StopAll_StopsActive_ClearsQueue_AndALateCompletionCannotTouchTheNextPlayback()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");
        var first = h.Engine.Plays[0];

        h.Coordinator.StopAll();
        h.Posts.RunAll();
        Assert.True(first.Stopped);
        Assert.Equal(0, h.Diag.QueuedSounds);

        h.Sound("C");
        var second = h.Engine.Plays[1];
        first.LateComplete();
        h.Posts.RunAll();

        Assert.True(second.IsAlive);
        Assert.Equal(PlaybackActivity.NotificationSound, h.Diag.Activity);
        Assert.Equal(0, h.Diag.Completed);
    }

    [Fact]
    public void Dispose_StopsPlayback_ReturnsEverything_AndRejectsNewRequests()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");
        var speechRevoked = false;
        h.Coordinator.RequestSpeech(() => { }, () => speechRevoked = true);

        h.Coordinator.Dispose();
        h.Posts.RunAll();

        Assert.Equal(0, h.Engine.Alive);
        Assert.True(speechRevoked);
        Assert.Equal(PlaybackRequestOutcome.Disposed, h.Coordinator.EnqueueNotificationSound("C", null));
        Assert.Equal(PlaybackRequestOutcome.Disposed, h.Coordinator.Preview("sounds/1.wav"));
        Assert.True(h.Coordinator.RequestSpeech(() => { }, () => { }).IsReleased);
    }

    // ---- preview ----

    [Fact]
    public void Preview_RunsAfterTheActiveSound_BeforeQueuedSounds_AndDoesNotConsumeHistory()
    {
        var h = new Harness();
        h.Sound("A");
        h.Sound("B");

        Assert.Equal(PlaybackRequestOutcome.Queued, h.Coordinator.Preview("sounds/3.wav"));
        h.CompleteCurrent();

        Assert.Equal("sounds/3.wav", FakeSoundFileGate.FileOf(h.Engine.Plays[1].FullPath));
        Assert.Equal(PlaybackActivity.Preview, h.Diag.Activity);
        Assert.Equal(0, h.Selector.GetNextSequentialIndex("default:B"));
        Assert.Equal(1, h.Selector.GetNextSequentialIndex("default:A"));

        h.CompleteCurrent();
        Assert.Equal("sounds/1.wav", FakeSoundFileGate.FileOf(h.Engine.Plays[2].FullPath));
        Assert.Equal(1, h.Diag.PreviewsStarted);
    }

    [Fact]
    public void Preview_OnlyOneWaits_TheNewestReplacesTheOlder()
    {
        var h = new Harness();
        h.Sound("A");

        h.Coordinator.Preview("sounds/2.wav");
        h.Coordinator.Preview("sounds/3.wav");
        h.CompleteCurrent();

        Assert.Equal(2, h.Engine.Plays.Count);
        Assert.Equal("sounds/3.wav", FakeSoundFileGate.FileOf(h.Engine.Plays[1].FullPath));
    }

    [Fact]
    public void StopPreview_StopsTheActivePreview_AndThenQueuedSoundsContinue()
    {
        var h = new Harness();
        h.Coordinator.Preview("sounds/2.wav");
        h.Posts.RunAll();
        h.Sound("A");

        h.Coordinator.StopPreview();
        h.Posts.RunAll();

        Assert.True(h.Engine.Plays[0].Stopped);
        Assert.Equal(2, h.Engine.Plays.Count);
        Assert.Equal(PlaybackActivity.NotificationSound, h.Diag.Activity);
    }

    [Fact]
    public void Preview_WorksWithSoundOff_AndRejectsDisallowedFiles()
    {
        var h = new Harness(s => s.SoundEnabled = false);
        h.Files.Unusable.Add("sounds/missing.wav");

        Assert.Equal(PlaybackRequestOutcome.Started, h.Coordinator.Preview("sounds/1.wav"));
        h.Posts.RunAll();
        h.CompleteCurrent();
        Assert.Equal(PlaybackRequestOutcome.SkippedNoUsableFile, h.Coordinator.Preview("sounds/missing.wav"));
    }

    // ---- speech turn ----

    [Fact]
    public void Speech_WaitsBehindTheActiveSound_AndTheNextSoundWaitsForSpeechRelease()
    {
        var h = new Harness();
        var granted = 0;
        h.Sound("A");

        var lease = h.Coordinator.RequestSpeech(() => granted++, () => { });
        h.Posts.RunAll();
        Assert.Equal(0, granted);
        Assert.True(h.Diag.SpeechWaiting);

        h.Sound("B");
        h.CompleteCurrent();
        Assert.Equal(1, granted);
        Assert.True(lease.IsGranted);
        Assert.Single(h.Engine.Plays); // B waits for speech
        Assert.Equal(PlaybackActivity.Speech, h.Diag.Activity);

        lease.Release();
        h.Posts.RunAll();
        Assert.Equal(2, h.Engine.Plays.Count);
        Assert.Equal(1, h.Diag.MaxConcurrentPlaybacks);
    }

    [Fact]
    public void Speech_ReleasedBeforeGrant_IsRemoved_AndNeverGranted()
    {
        var h = new Harness();
        var granted = false;
        h.Sound("A");
        var lease = h.Coordinator.RequestSpeech(() => granted = true, () => { });

        lease.Release();
        h.CompleteCurrent();

        Assert.False(granted);
        Assert.False(h.Diag.SpeechWaiting);
        Assert.Equal(PlaybackActivity.Idle, h.Diag.Activity);
    }

    [Fact]
    public void Speech_IsGrantedThroughPost_NeverInline()
    {
        var h = new Harness();
        var granted = false;

        h.Coordinator.RequestSpeech(() => granted = true, () => { });

        Assert.False(granted);
        h.Posts.RunAll();
        Assert.True(granted);
    }

    [Fact]
    public void StopAll_RevokesActiveAndWaitingSpeech()
    {
        var h = new Harness();
        var revoked = new List<string>();
        var active = h.Coordinator.RequestSpeech(() => { }, () => revoked.Add("active"));
        h.Posts.RunAll();
        var waiting = h.Coordinator.RequestSpeech(() => { }, () => revoked.Add("waiting"));

        h.Coordinator.StopAll();
        h.Posts.RunAll();

        Assert.Equal(new[] { "waiting", "active" }.OrderBy(x => x), revoked.OrderBy(x => x));
        Assert.True(active.IsReleased);
        Assert.True(waiting.IsReleased);
        Assert.Equal(PlaybackActivity.Idle, h.Diag.Activity);
    }

    [Fact]
    public void SoundRuleChange_DoesNotCancelWaitingSpeech()
    {
        var h = new Harness();
        var granted = false;
        h.Sound("A");
        h.Coordinator.RequestSpeech(() => granted = true, () => { });

        h.ApplySettings(s => s.DefaultSoundRule.Files.Add("sounds/4.wav"));
        h.CompleteCurrent();

        Assert.True(granted);
    }

    // ---- privacy ----

    [Fact]
    public void Diagnostics_ContainNoAppNamesOrPaths()
    {
        var h = new Harness();
        h.Sound("VerySecretAppName", "Secret.App");
        h.Sound("OtherSecret");

        var summary = h.Diag.ToSummary();

        Assert.DoesNotContain("Secret", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sounds", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max concurrent 1", summary);
    }

    // ---- P2-B additions: PreviewRule, LastEvent, last started file ----

    [Fact]
    public void PreviewRule_PlaysFirstUsableFileInListOrder_WithoutHistory()
    {
        var h = new Harness();
        h.Files.Unusable.Add("sounds/1.wav");

        Assert.Equal(PlaybackRequestOutcome.Started, h.Coordinator.PreviewRule(h.Settings.Settings.DefaultSoundRule));
        h.Posts.RunAll();
        Assert.Equal(new[] { "sounds/2.wav" }, h.PlayedFiles);
        Assert.Equal(0, h.Selector.HistoryCount);
        Assert.Equal("PreviewStarted", h.Diag.LastEvent);
        Assert.Null(h.Coordinator.GetLastStartedSoundPath()); // previews are not notification sounds

        h.Files.Unusable.UnionWith(new[] { "sounds/2.wav", "sounds/3.wav" });
        h.CompleteCurrent();
        Assert.Equal(PlaybackRequestOutcome.SkippedNoUsableFile, h.Coordinator.PreviewRule(h.Settings.Settings.DefaultSoundRule));
        Assert.Equal(PlaybackRequestOutcome.SkippedNoUsableFile, h.Coordinator.PreviewRule(null));
        Assert.Equal("SkippedNoUsableFile", h.Diag.LastEvent);
    }

    [Fact]
    public void LastEvent_AndLastStartedFile_FollowTheCoordinator()
    {
        var h = new Harness(s => s.PlaybackPolicy.MinIntervalPerAppSeconds = 4);
        Assert.Equal("None", h.Diag.LastEvent);

        h.Sound("Slack");
        Assert.Equal("Started", h.Diag.LastEvent);
        Assert.Equal(@"C:\fake\sounds\1.wav", h.Coordinator.GetLastStartedSoundPath());

        h.Sound("Slack");
        Assert.Equal("SkippedCooldown", h.Diag.LastEvent);

        h.Sound("Teams");
        Assert.Equal("Queued", h.Diag.LastEvent);

        h.CompleteCurrent();
        Assert.Equal("Started", h.Diag.LastEvent);
        Assert.Equal(@"C:\fake\sounds\1.wav", h.Coordinator.GetLastStartedSoundPath()); // Teams has its own sequence

        h.Coordinator.StopAll();
        h.Posts.RunAll();
        Assert.Equal("StoppedAll", h.Diag.LastEvent);
        Assert.DoesNotContain("1.wav", h.Diag.ToSummary());
    }
}
