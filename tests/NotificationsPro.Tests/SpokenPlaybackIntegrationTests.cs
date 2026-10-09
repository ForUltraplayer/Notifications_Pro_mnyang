// @file SpokenPlaybackIntegrationTests.cs
// @author: rudals252
// @brief 낭독 서비스와 재생 조정기 직렬화 테스트: 같은 알림의 WAV 후 낭독, WAV 뒤 대기 중 카드 닫힘·전체 삭제·교체·음성 끄기 시 낭독 취소, 활성 WAV는 카드 제거로 취소되지 않음, 낭독 완료·중지 시 권한 반환, 카드당 한 번 낭독 유지.

using System.Windows.Threading;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class SpokenPlaybackIntegrationTests
{
    private sealed class Rig : IDisposable
    {
        public Rig(Action<AppSettings>? configure = null)
        {
            Settings = TestAppDataIsolation.CreateSettingsManager();
            var s = Settings.Settings;
            s.NotificationDuration = 60;
            s.MaxVisibleNotifications = 5;
            s.AnimationDurationMs = 0;
            s.SoundEnabled = true;
            s.DefaultSoundRule = SoundRule.CreateSingle("sounds/a.wav");
            s.PlaybackPolicy = new PlaybackPolicy { MinIntervalPerAppSeconds = 0 };
            s.ReadNotificationsAloudEnabled = true;
            s.ReadNotificationsAloudVolume = 0;
            s.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
            configure?.Invoke(s);

            Queue = new QueueManager(Settings);
            Coordinator = new PlaybackCoordinator(Settings, Engine, Files, Posts.Post);
            Speech = new SpokenNotificationService(Queue, Settings, Dispatcher.CurrentDispatcher, Coordinator)
            {
                SpeakOverrideForTesting = item =>
                {
                    Spoken.Add(item);
                    return !FailNextSpeech;
                },
            };
            Dispatch = new AudioDispatchSubscription(Queue, Settings, (app, id) => Coordinator.EnqueueNotificationSound(app, id));
        }

        public SettingsManager Settings { get; }
        public QueueManager Queue { get; }
        public FakeSoundEngine Engine { get; } = new();
        public FakeSoundFileGate Files { get; } = new();
        public ManualPostQueue Posts { get; } = new();
        public PlaybackCoordinator Coordinator { get; }
        public SpokenNotificationService Speech { get; }
        public AudioDispatchSubscription Dispatch { get; }
        public List<NotificationItem> Spoken { get; } = new();
        public bool FailNextSpeech { get; set; }

        public PlaybackActivity Activity => Coordinator.GetDiagnostics().Activity;

        public NotificationItem Add(string app, string title = "Title", string body = "Body")
        {
            Queue.AddNotification(app, title, body, app + ".App");
            Drain();
            return Queue.VisibleNotifications.First(item => item.AppName == app && item.Title == title);
        }

        public void CompleteWav()
        {
            Engine.Current!.Complete();
            Drain();
        }

        public void CompleteSpeech()
        {
            Speech.CompleteCurrentSpeechForTesting();
            Drain();
        }

        public void ApplySettings(Action<AppSettings> change)
        {
            var updated = Settings.Settings.Clone();
            change(updated);
            Settings.Apply(updated);
            Drain();
        }

        public void Drain()
        {
            for (var i = 0; i < 20; i++)
            {
                Posts.RunAll();
                PumpDispatcher();
                if (Posts.Pending == 0)
                    return;
            }
        }

        public void Dispose()
        {
            Dispatch.Dispose();
            Speech.Dispose();
            Coordinator.Dispose();
        }

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }), null);
            Dispatcher.PushFrame(frame);
        }
    }

    [Fact]
    public void SameNotification_PlaysWavFirst_ThenSpeech()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig();

            var item = rig.Add("Slack");

            Assert.Single(rig.Engine.Plays);
            Assert.Empty(rig.Spoken);
            Assert.True(rig.Speech.IsAwaitingPlaybackTurnForTesting);
            Assert.Same(item, rig.Speech.CurrentItemForTesting);

            rig.CompleteWav();
            Assert.Equal(new[] { item }, rig.Spoken);
            Assert.Equal(PlaybackActivity.Speech, rig.Activity);

            rig.CompleteSpeech();
            Assert.Equal(PlaybackActivity.Idle, rig.Activity);
            Assert.Equal(1, rig.Coordinator.GetDiagnostics().MaxConcurrentPlaybacks);
        });
    }

    [Fact]
    public void PolicyAcceptedMode_AlsoPlaysWavBeforeSpeech()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig(s => s.AudioDispatchMode = AudioDispatchMode.PolicyAccepted);

            var item = rig.Add("Slack");
            Assert.Single(rig.Engine.Plays);
            Assert.Empty(rig.Spoken);

            rig.CompleteWav();
            Assert.Equal(new[] { item }, rig.Spoken);
        });
    }

    [Fact]
    public void TwoNotifications_AreSerialized_WavSpeechWavSpeech()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig();
            var first = rig.Add("A");
            var second = rig.Add("B");
            Assert.Single(rig.Engine.Plays);

            rig.CompleteWav();                    // A wav -> A speech (B's wav was queued after A's speech request)
            Assert.Equal(PlaybackActivity.Speech, rig.Activity);
            Assert.Equal(new[] { first }, rig.Spoken);

            rig.CompleteSpeech();                 // -> B wav
            Assert.Equal(2, rig.Engine.Plays.Count);
            Assert.Equal(PlaybackActivity.NotificationSound, rig.Activity);

            rig.CompleteWav();                    // -> B speech
            Assert.Equal(new[] { first, second }, rig.Spoken);
            Assert.Equal(1, rig.Engine.MaxAlive);
            Assert.Equal(1, rig.Coordinator.GetDiagnostics().MaxConcurrentPlaybacks);
        });
    }

    [Fact]
    public void CardClosedWhileSpeechWaitsBehindWav_CancelsThatSpeech_ButNotTheWav()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig();
            var item = rig.Add("Slack");

            rig.Queue.DismissNotification(item);
            rig.Drain();

            Assert.False(rig.Speech.IsAwaitingPlaybackTurnForTesting);
            Assert.False(rig.Coordinator.GetDiagnostics().SpeechWaiting);
            Assert.False(rig.Engine.Plays[0].Stopped);
            Assert.Equal(PlaybackActivity.NotificationSound, rig.Activity);

            rig.CompleteWav();
            Assert.Empty(rig.Spoken);
            Assert.Equal(PlaybackActivity.Idle, rig.Activity);
        });
    }

    [Fact]
    public void ClearAllWhileSpeechWaits_CancelsSpeech()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig();
            rig.Add("Slack");

            rig.Queue.ClearAll();
            rig.Drain();
            rig.CompleteWav();

            Assert.Empty(rig.Spoken);
            Assert.Equal(PlaybackActivity.Idle, rig.Activity);
        });
    }

    [Fact]
    public void ReplacedCardWhileSpeechWaits_CancelsTheOldSpeech()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig(s => s.ReplaceMode = true);
            var old = rig.Add("A");
            var replacement = rig.Add("B");

            Assert.DoesNotContain(old, rig.Queue.VisibleNotifications);
            rig.CompleteWav();                    // A wav done; A speech cancelled; B wav next
            Assert.DoesNotContain(old, rig.Spoken);
            rig.CompleteWav();                    // B wav -> B speech
            Assert.Equal(new[] { replacement }, rig.Spoken);
        });
    }

    [Fact]
    public void ActiveWav_IsNotCancelledWhenItsCardGoesAway()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig(s => s.ReadNotificationsAloudEnabled = false);
            var item = rig.Add("Slack");

            rig.Queue.DismissNotification(item);
            rig.Drain();

            Assert.True(rig.Engine.Plays[0].IsAlive);
            Assert.False(rig.Engine.Plays[0].Stopped);
        });
    }

    [Fact]
    public void SpeechDisabledWhileWaiting_ReleasesTheTurn_SoTheNextWavPlays()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig();
            rig.Add("A");
            rig.Add("B");

            rig.ApplySettings(s => s.ReadNotificationsAloudEnabled = false);
            rig.CompleteWav();

            Assert.Empty(rig.Spoken);
            Assert.Equal(2, rig.Engine.Plays.Count);
            Assert.Equal(PlaybackActivity.NotificationSound, rig.Activity);
        });
    }

    [Fact]
    public void StopAll_StopsSpeech_AndDoesNotReReadThoseCardsLater()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig(s => s.SoundEnabled = false);
            var item = rig.Add("Slack");
            Assert.Equal(new[] { item }, rig.Spoken);
            Assert.Equal(PlaybackActivity.Speech, rig.Activity);

            rig.Coordinator.StopAll();
            rig.Drain();
            Assert.Null(rig.Speech.CurrentItemForTesting);
            Assert.Equal(PlaybackActivity.Idle, rig.Activity);

            rig.ApplySettings(s => s.FontSize += 1); // settings re-evaluation must not re-queue the stopped card
            Assert.Single(rig.Spoken);
        });
    }

    [Fact]
    public void SpokenCard_IsReadOnlyOnce_AcrossSettingsChanges()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig(s => s.SoundEnabled = false);
            var item = rig.Add("Slack");
            rig.CompleteSpeech();

            rig.ApplySettings(s => s.FontSize += 1);

            Assert.Equal(new[] { item }, rig.Spoken);
            Assert.Equal(PlaybackActivity.Idle, rig.Activity);
        });
    }

    [Fact]
    public void WithoutGate_SpeechStartsImmediately_LikeUpstream()
    {
        StaThreadTestHelper.Run(() =>
        {
            var settings = TestAppDataIsolation.CreateSettingsManager();
            settings.Settings.NotificationDuration = 60;
            settings.Settings.AnimationDurationMs = 0;
            settings.Settings.ReadNotificationsAloudEnabled = true;
            settings.Settings.ReadNotificationsAloudVolume = 0;
            var queue = new QueueManager(settings);
            var spoken = new List<NotificationItem>();
            using var speech = new SpokenNotificationService(queue, settings, Dispatcher.CurrentDispatcher)
            {
                SpeakOverrideForTesting = item => { spoken.Add(item); return true; },
            };

            queue.AddNotification("Slack", "Hi", "There");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(_ => { frame.Continue = false; return null; }), null);
            Dispatcher.PushFrame(frame);

            Assert.Single(spoken);
            Assert.False(speech.IsAwaitingPlaybackTurnForTesting);
        });
    }

    [Fact]
    public void SpeechThatFailsToStart_ReturnsTheTurn_SoTheNextWavPlays()
    {
        StaThreadTestHelper.Run(() =>
        {
            using var rig = new Rig();
            rig.Add("A");
            rig.Add("B");
            rig.FailNextSpeech = true;

            rig.CompleteWav(); // A wav done -> A speech granted -> fails to start

            Assert.Single(rig.Spoken);
            Assert.Equal("B", rig.Speech.CurrentItemForTesting?.AppName); // B's speech now waits behind B's wav
            Assert.True(rig.Speech.IsAwaitingPlaybackTurnForTesting);
            Assert.Equal(2, rig.Engine.Plays.Count);
            Assert.Equal(PlaybackActivity.NotificationSound, rig.Activity);
        });
    }
}
