// @file NarrationPreviewGateTests.cs
// @author: rudals252
// @brief 낭독 미리 듣기가 공통 재생 권한을 기다린 뒤 재생하고(알림 WAV와 겹치지 않음), 끝나면 권한을 돌려주며, '모두 중지'로 취소되는지 검증한다. 실제 음성 합성은 테스트 훅으로 대체한다.

using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

[Collection(nameof(NarrationPreviewGateTests))] // the preview test hook is static
public class NarrationPreviewGateTests
{
    private static (PlaybackCoordinator Coordinator, FakeSoundEngine Engine, ManualPostQueue Posts, SettingsManager Settings) Create()
    {
        var settings = TestAppDataIsolation.CreateSettingsManager();
        settings.Settings.SoundEnabled = true;
        settings.Settings.DefaultSoundRule = new SoundRule { Files = { "sounds/1.wav" } };
        settings.Settings.PlaybackPolicy = new PlaybackPolicy { MinIntervalPerAppSeconds = 0 };
        var engine = new FakeSoundEngine();
        var posts = new ManualPostQueue();
        var coordinator = new PlaybackCoordinator(settings, engine, new FakeSoundFileGate(), posts.Post);
        return (coordinator, engine, posts, settings);
    }

    private static void PumpUntil(ManualPostQueue posts, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            posts.RunAll();
            Thread.Sleep(10);
        }
    }

    [Fact]
    public async Task VoicePreview_WaitsForThePlayingWav_ThenReleasesTheTurn()
    {
        var (coordinator, engine, posts, settings) = Create();
        var previewStarted = 0;
        var wavAliveWhenPreviewStarted = -1;
        SpokenNotificationService.PreviewPlaybackOverrideForTesting = (_, _) =>
        {
            Interlocked.Increment(ref previewStarted);
            wavAliveWhenPreviewStarted = engine.Alive;
            return Task.CompletedTask;
        };
        try
        {
            coordinator.EnqueueNotificationSound("A", null);   // WAV playing
            posts.RunAll();

            var preview = SpokenNotificationService.PlayPreviewAsync(settings.Settings, coordinator);
            posts.RunAll();
            await Task.Delay(50);
            Assert.Equal(0, Volatile.Read(ref previewStarted)); // still waiting behind the WAV

            engine.Current!.Complete();
            PumpUntil(posts, () => preview.IsCompleted);
            await preview;

            Assert.Equal(1, previewStarted);
            Assert.Equal(0, wavAliveWhenPreviewStarted);      // never overlapped the WAV
            PumpUntil(posts, () => coordinator.GetDiagnostics().Activity == PlaybackActivity.Idle);
            Assert.Equal(PlaybackActivity.Idle, coordinator.GetDiagnostics().Activity);
        }
        finally
        {
            SpokenNotificationService.PreviewPlaybackOverrideForTesting = null;
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task StopAll_CancelsARunningVoicePreview()
    {
        var (coordinator, _, posts, settings) = Create();
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        SpokenNotificationService.PreviewPlaybackOverrideForTesting = async (_, token) =>
        {
            Volatile.Write(ref running, 1);
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(true); }
        };
        try
        {
            var preview = SpokenNotificationService.PlayPreviewAsync(settings.Settings, coordinator);
            PumpUntil(posts, () => Volatile.Read(ref running) == 1);   // preview is actually speaking
            Assert.Equal(1, Volatile.Read(ref running));

            coordinator.StopAll();
            PumpUntil(posts, () => preview.IsCompleted);
            await preview;

            Assert.True(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            SpokenNotificationService.PreviewPlaybackOverrideForTesting = null;
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task StopAll_BeforeTheTurnIsGranted_SkipsThePreview()
    {
        var (coordinator, engine, posts, settings) = Create();
        var previewStarted = 0;
        SpokenNotificationService.PreviewPlaybackOverrideForTesting = (_, _) =>
        {
            Interlocked.Increment(ref previewStarted);
            return Task.CompletedTask;
        };
        try
        {
            coordinator.EnqueueNotificationSound("A", null);   // WAV playing, preview waits
            posts.RunAll();
            var preview = SpokenNotificationService.PlayPreviewAsync(settings.Settings, coordinator);
            posts.RunAll();

            coordinator.StopAll();
            PumpUntil(posts, () => preview.IsCompleted);
            await preview;

            Assert.Equal(0, previewStarted);
            Assert.Null(engine.Current);
        }
        finally
        {
            SpokenNotificationService.PreviewPlaybackOverrideForTesting = null;
            coordinator.Dispose();
        }
    }
}
