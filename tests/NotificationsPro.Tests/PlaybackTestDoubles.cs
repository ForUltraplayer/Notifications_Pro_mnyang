// @file PlaybackTestDoubles.cs
// @author: rudals252
// @brief 재생 조정기 테스트용 가짜 엔진(시작·완료·실패·지연 수동 제어, 동시 재생 수 기록), 가짜 파일 게이트, 수동 post 대기열, 수동 시계.

using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

internal sealed class ManualPostQueue
{
    private readonly Queue<Action> _actions = new();

    public void Post(Action action) => _actions.Enqueue(action);

    public int Pending => _actions.Count;

    /// <summary>Runs queued actions (including ones they enqueue) until the queue is empty.</summary>
    public void RunAll()
    {
        var guard = 0;
        while (_actions.Count > 0)
        {
            if (++guard > 10_000)
                throw new InvalidOperationException("Post queue did not drain.");
            _actions.Dequeue()();
        }
    }
}

internal sealed class ManualClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}

internal sealed class FakeSoundEngine : ISoundEngine
{
    private int _alive;

    public List<FakePlayback> Plays { get; } = new();
    public int MaxAlive { get; private set; }
    public int Alive => _alive;
    public bool ThrowOnPlay { get; set; }

    /// <summary>When true, each play reports OnStarted immediately (still asynchronously, through the observer).</summary>
    public bool AutoStart { get; set; } = true;

    public FakePlayback? Current => Plays.LastOrDefault(play => play.IsAlive);

    public ISoundPlaybackHandle Play(string fullPath, ISoundPlaybackObserver observer)
    {
        if (ThrowOnPlay)
            throw new InvalidOperationException("engine unavailable");

        var playback = new FakePlayback(this, fullPath, observer);
        Plays.Add(playback);
        _alive++;
        MaxAlive = Math.Max(MaxAlive, _alive);
        if (AutoStart)
            playback.Start();
        return playback;
    }

    internal void OnEnded() => _alive--;
}

internal sealed class FakePlayback : ISoundPlaybackHandle
{
    private readonly FakeSoundEngine _engine;
    private readonly ISoundPlaybackObserver _observer;
    private bool _started;

    public FakePlayback(FakeSoundEngine engine, string fullPath, ISoundPlaybackObserver observer)
    {
        _engine = engine;
        FullPath = fullPath;
        _observer = observer;
    }

    public string FullPath { get; }
    public bool IsAlive { get; private set; } = true;
    public bool Stopped { get; private set; }

    public void Start()
    {
        _started = true;
        _observer.OnStarted();
    }

    public void Complete()
    {
        End();
        _observer.OnCompleted();
    }

    public void Fail(SoundEngineFailureKind kind = SoundEngineFailureKind.FileUnsupported)
    {
        End();
        _observer.OnFailed(kind, _started);
    }

    /// <summary>Delivers a completion even after Stop (simulates a late engine callback).</summary>
    public void LateComplete() => _observer.OnCompleted();

    public void Stop()
    {
        if (!IsAlive)
            return;
        Stopped = true;
        End();
    }

    private void End()
    {
        if (!IsAlive)
            return;
        IsAlive = false;
        _engine.OnEnded();
    }
}

internal sealed class FakeSoundFileGate : ISoundFileGate
{
    public HashSet<string> Unusable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> MarkedUnplayable { get; } = new();

    public string? ResolvePlayablePath(string ruleFile)
    {
        if (string.IsNullOrWhiteSpace(ruleFile) || Unusable.Contains(ruleFile))
            return null;
        return @"C:\fake\" + ruleFile.Replace('/', '\\');
    }

    public void MarkUnplayable(string fullPath)
    {
        MarkedUnplayable.Add(fullPath);
        var ruleFile = fullPath.Replace(@"C:\fake\", string.Empty).Replace('\\', '/');
        Unusable.Add(ruleFile);
    }

    public static string FileOf(string fullPath) => fullPath.Replace(@"C:\fake\", string.Empty).Replace('\\', '/');
}
