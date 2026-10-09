// @file WinRtSoundEngine.cs
// @author: rudals252
// @brief Windows.Media.Playback.MediaPlayer(기존 낭독과 같은 엔진)로 WAV를 재생한다. 시작·완료·실패를 이벤트로 알리고, 즉시 중지·시간 초과 감시를 제공하며 Windows 전체 볼륨은 바꾸지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace NotificationsPro.Services;

/// <summary>
/// Sound engine built on the WinRT <see cref="MediaPlayer"/> already used for narration. Chosen over
/// <c>System.Media.SoundPlayer</c> because <c>PlaySync</c> cannot be interrupted by <c>Stop()</c>
/// (measured: a 5 s file returned after ~5.08 s although Stop was called at 0.31 s) and asynchronous
/// <c>Play()</c> reports neither completion nor failure. Media Foundation opened all Windows\Media WAVs
/// and every SoundPlayer-accepted format in tests (PCM 8/16/24-bit, IEEE float, MS ADPCM).
/// Volume is per player (1.0); the system master volume is never touched.
/// </summary>
public sealed class WinRtSoundEngine : ISoundEngine, ISoundFileProber
{
    public WinRtSoundEngine()
        : this(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5))
    {
    }

    internal WinRtSoundEngine(TimeSpan openTimeout, TimeSpan endGrace, TimeSpan unknownDurationLimit)
    {
        OpenTimeout = openTimeout;
        EndGrace = endGrace;
        UnknownDurationLimit = unknownDurationLimit;
    }

    public TimeSpan OpenTimeout { get; }
    public TimeSpan EndGrace { get; }
    public TimeSpan UnknownDurationLimit { get; }

    public ISoundPlaybackHandle Play(string fullPath, ISoundPlaybackObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var handle = new PlaybackHandle(this, fullPath, observer);
        handle.Start();
        return handle;
    }

    public async Task<SoundProbeResult> ProbeAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        MediaPlayer? player = null;
        try
        {
            player = CreatePlayer();
            player.Volume = 0;
            var completion = new TaskCompletionSource<SoundProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaOpened += (sender, _) =>
            {
                var duration = sender.PlaybackSession.NaturalDuration;
                completion.TrySetResult(new SoundProbeResult(true, duration > TimeSpan.Zero ? duration : null, null));
            };
            player.MediaFailed += (_, args) => completion.TrySetResult(new SoundProbeResult(false, null, MapError(args.Error)));
            player.Source = MediaSource.CreateFromUri(new Uri(fullPath));

            var finished = await Task.WhenAny(completion.Task, Task.Delay(OpenTimeout, cancellationToken)).ConfigureAwait(false);
            return finished == completion.Task
                ? completion.Task.Result
                : new SoundProbeResult(false, null, SoundEngineFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new SoundProbeResult(false, null, SoundEngineFailureKind.FileUnsupported);
        }
        finally
        {
            if (player != null)
                ReleasePlayer(player);
        }
    }

    private static MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer
        {
            AutoPlay = false,
            Volume = 1.0,
        };
        player.CommandManager.IsEnabled = false;
        player.SystemMediaTransportControls.IsEnabled = false;
        return player;
    }

    private static void ReleasePlayer(MediaPlayer player)
    {
        try { player.Pause(); } catch { }
        try { player.Source = null; } catch { }
        try { player.Dispose(); } catch { }
    }

    private static SoundEngineFailureKind MapError(MediaPlayerError error)
    {
        return error switch
        {
            MediaPlayerError.SourceNotSupported => SoundEngineFailureKind.FileUnsupported,
            MediaPlayerError.DecodingError => SoundEngineFailureKind.FileUnsupported,
            _ => SoundEngineFailureKind.DeviceOrOther,
        };
    }

    private sealed class PlaybackHandle : ISoundPlaybackHandle
    {
        private readonly WinRtSoundEngine _engine;
        private readonly string _fullPath;
        private readonly ISoundPlaybackObserver _observer;
        private readonly object _sync = new();
        private MediaPlayer? _player;
        private System.Threading.Timer? _watchdog;
        private bool _started;
        private bool _finished;

        public PlaybackHandle(WinRtSoundEngine engine, string fullPath, ISoundPlaybackObserver observer)
        {
            _engine = engine;
            _fullPath = fullPath;
            _observer = observer;
        }

        public void Start()
        {
            MediaPlayer player;
            try
            {
                player = CreatePlayer();
                player.MediaOpened += OnMediaOpened;
                player.MediaEnded += OnMediaEnded;
                player.MediaFailed += OnMediaFailed;
                player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            }
            catch
            {
                FinishAsync(() => _observer.OnFailed(SoundEngineFailureKind.DeviceOrOther, hadStarted: false));
                return;
            }

            lock (_sync)
            {
                _player = player;
                _watchdog = new System.Threading.Timer(_ => OnWatchdog(), null, _engine.OpenTimeout, Timeout.InfiniteTimeSpan);
            }

            try
            {
                player.Source = MediaSource.CreateFromUri(new Uri(_fullPath));
            }
            catch
            {
                // Still inside Play(): the observer must not be called synchronously.
                Finish(() => _observer.OnFailed(SoundEngineFailureKind.FileUnsupported, hadStarted: false), deferNotify: true);
            }
        }

        public void Stop()
        {
            MediaPlayer? player;
            lock (_sync)
            {
                if (_finished)
                    return;

                _finished = true;
                player = DetachLocked();
            }

            if (player != null)
                ReleasePlayer(player);
        }

        private void OnMediaOpened(MediaPlayer sender, object args)
        {
            TimeSpan limit;
            lock (_sync)
            {
                if (_finished)
                    return;

                var duration = sender.PlaybackSession.NaturalDuration;
                limit = duration > TimeSpan.Zero
                    ? duration + _engine.EndGrace
                    : _engine.UnknownDurationLimit;
                _watchdog?.Change(limit, Timeout.InfiniteTimeSpan);
            }

            try
            {
                sender.Play();
            }
            catch
            {
                Finish(() => _observer.OnFailed(SoundEngineFailureKind.DeviceOrOther, hadStarted: false));
            }
        }

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            if (sender.PlaybackState != MediaPlaybackState.Playing)
                return;

            lock (_sync)
            {
                if (_finished || _started)
                    return;

                _started = true;
            }

            _observer.OnStarted();
        }

        private void OnMediaEnded(MediaPlayer sender, object args)
        {
            Finish(() => _observer.OnCompleted());
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            var kind = MapError(args.Error);
            Finish(() => _observer.OnFailed(kind, HasStarted()));
        }

        private void OnWatchdog()
        {
            Finish(() => _observer.OnFailed(SoundEngineFailureKind.Timeout, HasStarted()));
        }

        private bool HasStarted()
        {
            lock (_sync)
                return _started;
        }

        private void Finish(Action notify, bool deferNotify = false)
        {
            MediaPlayer? player;
            lock (_sync)
            {
                if (_finished)
                    return;

                _finished = true;
                player = DetachLocked();
            }

            // Release the player off the event thread (disposing inside its own callback is avoided).
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (player != null)
                    ReleasePlayer(player);
                if (deferNotify)
                    notify();
            });

            if (!deferNotify)
                notify();
        }

        private void FinishAsync(Action notify)
        {
            lock (_sync)
            {
                if (_finished)
                    return;

                _finished = true;
            }

            ThreadPool.QueueUserWorkItem(_ => notify());
        }

        private MediaPlayer? DetachLocked()
        {
            _watchdog?.Dispose();
            _watchdog = null;

            var player = _player;
            _player = null;
            if (player != null)
            {
                player.MediaOpened -= OnMediaOpened;
                player.MediaEnded -= OnMediaEnded;
                player.MediaFailed -= OnMediaFailed;
                try { player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged; } catch { }
            }

            return player;
        }
    }
}
