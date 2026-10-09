// @file RealEnginePlaybackTests.cs
// @author: rudals252
// @brief 실제 WinRT 엔진·보관함 게이트·재생 조정기를 함께 돌리는 통합 테스트(무음 합성 WAV만 사용): 동시 알림 3건이 겹치지 않고 순차 재생되는지, 같은 앱 쿨다운이 동작하는지 실제 재생 시각으로 확인한다.

using System.Diagnostics;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class RealEnginePlaybackTests
{
    private sealed class TimingEngine : ISoundEngine
    {
        private readonly ISoundEngine _inner;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _sync = new();

        public TimingEngine(ISoundEngine inner) => _inner = inner;

        public List<(long Start, long End)> Intervals { get; } = new();

        public ISoundPlaybackHandle Play(string fullPath, ISoundPlaybackObserver observer)
            => _inner.Play(fullPath, new Recorder(this, observer));

        private sealed class Recorder : ISoundPlaybackObserver
        {
            private readonly TimingEngine _owner;
            private readonly ISoundPlaybackObserver _inner;
            private long _start = -1;

            public Recorder(TimingEngine owner, ISoundPlaybackObserver inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public void OnStarted()
            {
                _start = _owner._clock.ElapsedMilliseconds;
                _inner.OnStarted();
            }

            public void OnCompleted()
            {
                lock (_owner._sync)
                    _owner.Intervals.Add((_start, _owner._clock.ElapsedMilliseconds));
                _inner.OnCompleted();
            }

            public void OnFailed(SoundEngineFailureKind kind, bool hadStarted) => _inner.OnFailed(kind, hadStarted);
        }
    }

    private static (PlaybackCoordinator Coordinator, TimingEngine Engine) Create(double minInterval)
    {
        var managed = TestAppDataIsolation.CreateUniqueDirectory("real-engine-sounds");
        TestWavFactory.Write(managed, "silence.wav", TestWavFactory.Pcm(seconds: 0.4));
        var settings = TestAppDataIsolation.CreateSettingsManager();
        settings.Settings.SoundEnabled = true;
        settings.Settings.DefaultSoundRule = SoundRule.CreateSingle("sounds/silence.wav");
        settings.Settings.PlaybackPolicy = new PlaybackPolicy { MinIntervalPerAppSeconds = minInterval, MaxQueued = 5, MaxQueueWaitSeconds = 30 };

        var engine = new TimingEngine(new WinRtSoundEngine());
        var library = new SoundLibraryService(() => settings.Settings, managedRoot: managed, trustedWindowsSounds: Array.Empty<string>);
        var coordinator = new PlaybackCoordinator(settings, engine, library, action => ThreadPool.QueueUserWorkItem(_ => action()));
        return (coordinator, engine);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(50);
    }

    [Fact]
    public async Task ThreeSimultaneousNotifications_PlaySequentially_WithoutOverlap()
    {
        var (coordinator, engine) = Create(minInterval: 0);
        using var _ = coordinator;

        var outcomes = new[]
        {
            coordinator.EnqueueNotificationSound("AppA", "A.App"),
            coordinator.EnqueueNotificationSound("AppB", "B.App"),
            coordinator.EnqueueNotificationSound("AppC", null),
        };
        await WaitUntil(() => coordinator.GetDiagnostics().Completed >= 3);

        var diag = coordinator.GetDiagnostics();
        Assert.Equal(new[] { PlaybackRequestOutcome.Started, PlaybackRequestOutcome.Queued, PlaybackRequestOutcome.Queued }, outcomes);
        Assert.Equal(3, diag.Started);
        Assert.Equal(3, diag.Completed);
        Assert.Equal(0, diag.Failed);
        Assert.Equal(1, diag.MaxConcurrentPlaybacks);
        Assert.Equal(PlaybackActivity.Idle, diag.Activity);

        var intervals = engine.Intervals.OrderBy(i => i.Start).ToList();
        Assert.Equal(3, intervals.Count);
        for (var i = 1; i < intervals.Count; i++)
            Assert.True(intervals[i].Start >= intervals[i - 1].End, $"playback {i} started at {intervals[i].Start} ms before the previous one ended at {intervals[i - 1].End} ms");
    }

    [Fact]
    public async Task SameAppWithinCooldown_PlaysOnce_AndSkipsTheRest()
    {
        var (coordinator, _) = Create(minInterval: 4);
        using var __ = coordinator;

        var outcomes = Enumerable.Range(0, 3).Select(_ => coordinator.EnqueueNotificationSound("AppA", "A.App")).ToArray();
        await WaitUntil(() => coordinator.GetDiagnostics().Completed >= 1);

        var diag = coordinator.GetDiagnostics();
        Assert.Equal(new[] { PlaybackRequestOutcome.Started, PlaybackRequestOutcome.SkippedCooldown, PlaybackRequestOutcome.SkippedCooldown }, outcomes);
        Assert.Equal(1, diag.Started);
        Assert.Equal(1, diag.Completed);
        Assert.Equal(2, diag.SkippedCooldown);
    }
}
