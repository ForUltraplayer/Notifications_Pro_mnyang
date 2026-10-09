// @file WinRtSoundEngineTests.cs
// @author: rudals252
// @brief 실제 WinRT 재생 엔진 테스트: Windows\Media WAV 전수 열기(재생 없음), 합성 무음 WAV 재생의 시작·완료 이벤트, 즉시 중지 후 콜백 없음, 잘못된 파일의 실패 이벤트. 소리가 나지 않는 무음 파일만 재생한다.

using System.IO;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class WinRtSoundEngineTests
{
    private sealed class RecordingObserver : ISoundPlaybackObserver
    {
        public TaskCompletionSource<string> Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartedCount;
        public int TerminalCount;
        public SoundEngineFailureKind? FailureKind;

        public void OnStarted() => Interlocked.Increment(ref StartedCount);

        public void OnCompleted()
        {
            Interlocked.Increment(ref TerminalCount);
            Terminal.TrySetResult("completed");
        }

        public void OnFailed(SoundEngineFailureKind kind, bool hadStarted)
        {
            Interlocked.Increment(ref TerminalCount);
            FailureKind = kind;
            Terminal.TrySetResult("failed");
        }
    }

    [Fact]
    public async Task Probe_OpensEveryWindowsMediaWav_WithoutPlaying()
    {
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        if (!Directory.Exists(media))
            return;

        var engine = new WinRtSoundEngine();
        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(media, "*.wav"))
        {
            var result = await engine.ProbeAsync(file);
            if (!result.Playable)
                failures.Add($"{Path.GetFileName(file)}:{result.Failure}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Probe_RejectsABrokenFile()
    {
        var folder = TestAppDataIsolation.CreateUniqueDirectory("engine-probe");
        var bad = TestWavFactory.Write(folder, "bad.wav", TestWavFactory.BadHeader());

        var result = await new WinRtSoundEngine().ProbeAsync(bad);

        Assert.False(result.Playable);
    }

    [Fact]
    public async Task Play_SilentPcm_ReportsStartedThenCompletedOnce()
    {
        var folder = TestAppDataIsolation.CreateUniqueDirectory("engine-play");
        var path = TestWavFactory.Write(folder, "silence.wav", TestWavFactory.Pcm(seconds: 0.3));
        var observer = new RecordingObserver();

        new WinRtSoundEngine().Play(path, observer);
        var finished = await Task.WhenAny(observer.Terminal.Task, Task.Delay(8000));

        Assert.Same(observer.Terminal.Task, finished);
        Assert.Equal("completed", await observer.Terminal.Task);
        await Task.Delay(200);
        Assert.Equal(1, observer.StartedCount);
        Assert.Equal(1, observer.TerminalCount);
    }

    [Fact]
    public async Task Stop_EndsPlaybackImmediately_AndNoCallbackFollows()
    {
        var folder = TestAppDataIsolation.CreateUniqueDirectory("engine-stop");
        var path = TestWavFactory.Write(folder, "long-silence.wav", TestWavFactory.Pcm(seconds: 3));
        var observer = new RecordingObserver();

        var handle = new WinRtSoundEngine().Play(path, observer);
        await Task.Delay(400);
        handle.Stop();
        handle.Stop(); // idempotent
        await Task.Delay(3500);

        Assert.Equal(0, observer.TerminalCount);
    }

    [Fact]
    public async Task Play_BrokenFile_ReportsFileFailure()
    {
        var folder = TestAppDataIsolation.CreateUniqueDirectory("engine-fail");
        var bad = TestWavFactory.Write(folder, "bad.wav", TestWavFactory.BadHeader());
        var observer = new RecordingObserver();

        new WinRtSoundEngine().Play(bad, observer);
        var finished = await Task.WhenAny(observer.Terminal.Task, Task.Delay(8000));

        Assert.Same(observer.Terminal.Task, finished);
        Assert.Equal("failed", await observer.Terminal.Task);
        Assert.Equal(SoundEngineFailureKind.FileUnsupported, observer.FailureKind);
        Assert.Equal(0, observer.StartedCount);
    }
}
