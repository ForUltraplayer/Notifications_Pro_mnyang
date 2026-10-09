// @file PlaybackCoordinator.cs
// @author: rudals252
// @brief 알림 WAV·기존 낭독·미리 듣기가 하나의 재생 권한을 공유하도록 조정한다(한 번에 하나, 짧은 대기열, 대기 만료, 앱별 최소 간격, 취소·실패·완료 시 권한 반환, 작업 식별자로 늦은 완료 무시).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text.Json;
using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Single playback turn shared by notification WAVs, narration (through <see cref="IPlaybackGate"/>) and
/// previews (design §7.4).
/// <list type="bullet">
/// <item>Only one engine playback or granted speech turn is alive at a time; the rest wait in FIFO order.
/// A pending preview runs right after the current turn; only one preview waits (a newer one replaces it).</item>
/// <item>Queued WAV entries hold only the rule snapshot, history key, rule key, AUMID and time — never
/// notification text. <see cref="PlaybackPolicy"/> limits the queue (size, wait time, Queue/Skip) and
/// spaces sounds per app (<see cref="PlaybackPolicy.MinIntervalPerAppSeconds"/>, 0 = off).</item>
/// <item>File choice happens when a WAV actually starts; history is committed on the engine's start
/// signal, so previews, cooldown skips, expired entries and failed attempts never consume the order.
/// An engine failure before start tries the next usable file of the same rule.</item>
/// <item>Content-filter / sound-rule / policy changes cancel queued WAVs; sound off or pause also stops
/// the active WAV. Card expiry never touches a WAV (WAV jobs are not tied to cards).</item>
/// <item>Every completion, failure, cancellation and dispose returns the turn. Callbacks carry a job id,
/// so a late completion of an earlier job cannot change the state of the next one.</item>
/// </list>
/// Public members are meant to be called on one thread (the UI dispatcher); engine callbacks and
/// speech grant/revoke callbacks are always delivered through the supplied asynchronous
/// <c>post</c> delegate, never inline. The Windows master volume is never changed.
/// </summary>
public sealed class PlaybackCoordinator : IPlaybackGate, IDisposable
{
    internal const int MaxCooldownEntries = 512;

    private readonly SettingsManager _settingsManager;
    private readonly ISoundEngine _engine;
    private readonly ISoundFileGate _files;
    private readonly SoundRuleSelector _selector;
    private readonly Action<Action> _post;
    private readonly Func<DateTime> _utcNow;
    private readonly object _sync = new();

    private readonly LinkedList<Job> _queue = new();
    private readonly Dictionary<string, DateTime> _lastAcceptedByApp = new(StringComparer.OrdinalIgnoreCase);
    private PreviewJob? _pendingPreview;
    private Job? _active;
    private long _nextJobId;
    private int _alive;
    private string _settingsSignature;
    private bool _stateChangedPending;
    private bool _disposed;

    private int _requested;
    private int _started;
    private int _completed;
    private int _failed;
    private int _cancelled;
    private int _expired;
    private int _skippedCooldown;
    private int _skippedSilent;
    private int _skippedNoFile;
    private int _skippedBusyOrFull;
    private int _previewsStarted;
    private int _speechGranted;
    private int _maxConcurrent;
    private DateTime? _lastStartedUtc;

    /// <param name="post">Queues an action to run later on the coordinator's thread (must not run it inline).</param>
    public PlaybackCoordinator(
        SettingsManager settingsManager,
        ISoundEngine engine,
        ISoundFileGate fileGate,
        Action<Action> post,
        SoundRuleSelector? selector = null,
        Func<DateTime>? utcNow = null)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _files = fileGate ?? throw new ArgumentNullException(nameof(fileGate));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _selector = selector ?? new SoundRuleSelector();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _settingsSignature = ComputeSettingsSignature(_settingsManager.Settings);
        _settingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Raised (posted) after the playback state or counters change.</summary>
    public event Action? StateChanged;

    public SoundRuleSelector Selector => _selector;

    /// <summary>Requests the notification sound for one accepted notification.</summary>
    public PlaybackRequestOutcome EnqueueNotificationSound(string? appName, string? appId)
    {
        lock (_sync)
        {
            if (_disposed)
                return PlaybackRequestOutcome.Disposed;

            _requested++;
            var settings = _settingsManager.Settings;
            if (settings.NotificationsPaused)
            {
                _skippedSilent++;
                NotifyStateChangedLocked();
                return PlaybackRequestOutcome.SkippedPaused;
            }

            var resolution = _selector.Resolve(settings, appName, appId);
            if (resolution.IsSilent)
            {
                _skippedSilent++;
                NotifyStateChangedLocked();
                return PlaybackRequestOutcome.SkippedSilent;
            }

            var now = _utcNow();
            var policy = GetPolicy(settings);
            PruneExpiredLocked(now, policy);

            var appKey = (string.IsNullOrWhiteSpace(appId) ? appName : appId)?.Trim() ?? string.Empty;
            if (policy.MinIntervalPerAppSeconds > 0
                && _lastAcceptedByApp.TryGetValue(appKey, out var lastAccepted)
                && (now - lastAccepted).TotalSeconds < policy.MinIntervalPerAppSeconds)
            {
                _skippedCooldown++;
                NotifyStateChangedLocked();
                return PlaybackRequestOutcome.SkippedCooldown;
            }

            var busy = _active != null || _queue.Count > 0 || _pendingPreview != null;
            if (busy)
            {
                if (policy.OverflowBehavior == PlaybackOverflowBehavior.Skip || policy.MaxQueued <= 0)
                {
                    _skippedBusyOrFull++;
                    NotifyStateChangedLocked();
                    return PlaybackRequestOutcome.SkippedBusy;
                }

                if (CountQueuedSoundsLocked() >= policy.MaxQueued)
                {
                    _skippedBusyOrFull++;
                    NotifyStateChangedLocked();
                    return PlaybackRequestOutcome.SkippedQueueFull;
                }
            }

            var job = new SoundJob(
                ++_nextJobId,
                resolution.Rule!.Clone(),
                resolution.HistoryKey,
                resolution.RuleKey,
                string.IsNullOrWhiteSpace(appId) ? null : appId.Trim(),
                now);

            if (busy)
            {
                RecordAcceptedLocked(appKey, now);
                _queue.AddLast(job);
                NotifyStateChangedLocked();
                return PlaybackRequestOutcome.Queued;
            }

            var started = StartSoundLocked(job);
            if (started)
                RecordAcceptedLocked(appKey, now);
            NotifyStateChangedLocked();
            return started ? PlaybackRequestOutcome.Started : PlaybackRequestOutcome.SkippedNoUsableFile;
        }
    }

    /// <summary>
    /// Plays one allowed file as a preview after the current turn. Does not use rules or history and
    /// ignores the master switch, pause and cooldown. Only one preview waits; a newer one replaces it.
    /// </summary>
    public PlaybackRequestOutcome Preview(string ruleFileOrPath)
    {
        lock (_sync)
        {
            if (_disposed)
                return PlaybackRequestOutcome.Disposed;

            var fullPath = string.IsNullOrWhiteSpace(ruleFileOrPath) ? null : _files.ResolvePlayablePath(ruleFileOrPath);
            if (fullPath == null)
            {
                _skippedNoFile++;
                NotifyStateChangedLocked();
                return PlaybackRequestOutcome.SkippedNoUsableFile;
            }

            var job = new PreviewJob(++_nextJobId, fullPath);
            if (_active == null && _pendingPreview == null)
            {
                var started = StartEngineJobLocked(job, fullPath);
                if (!started)
                    _failed++;
                NotifyStateChangedLocked();
                return started ? PlaybackRequestOutcome.Started : PlaybackRequestOutcome.SkippedNoUsableFile;
            }

            if (_pendingPreview != null)
                _cancelled++;
            _pendingPreview = job;
            NotifyStateChangedLocked();
            return PlaybackRequestOutcome.Queued;
        }
    }

    /// <summary>Stops the active preview (if any) and drops the waiting preview.</summary>
    public void StopPreview()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            if (_pendingPreview != null)
            {
                _pendingPreview = null;
                _cancelled++;
            }

            if (_active is PreviewJob)
                StopActiveLocked();

            TryStartNextLocked();
            NotifyStateChangedLocked();
        }
    }

    /// <summary>
    /// Explicit "stop playback": stops whatever holds the turn (WAV, preview or speech), clears queued
    /// WAVs, the waiting preview and waiting speech turns. Speech owners get their revoke callback.
    /// </summary>
    public void StopAll()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            CancelQueueLocked(includeSpeech: true);
            if (_pendingPreview != null)
            {
                _pendingPreview = null;
                _cancelled++;
            }

            StopActiveLocked();
            NotifyStateChangedLocked();
        }
    }

    /// <summary>Cancels queued notification WAVs (the active one keeps playing).</summary>
    public void CancelPendingSounds()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            CancelQueueLocked(includeSpeech: false);
            NotifyStateChangedLocked();
        }
    }

    public PlaybackLease RequestSpeech(Action onGranted, Action onRevoked)
    {
        ArgumentNullException.ThrowIfNull(onGranted);
        ArgumentNullException.ThrowIfNull(onRevoked);

        lock (_sync)
        {
            if (_disposed)
            {
                var dead = new PlaybackLease(0, null);
                dead.MarkReleasedWithoutCallback();
                return dead;
            }

            var id = ++_nextJobId;
            var lease = new PlaybackLease(id, ReleaseSpeech);
            _queue.AddLast(new SpeechJob(id, lease, onGranted, onRevoked));
            TryStartNextLocked();
            NotifyStateChangedLocked();
            return lease;
        }
    }

    public PlaybackDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            var activity = _active switch
            {
                SoundJob => PlaybackActivity.NotificationSound,
                PreviewJob => PlaybackActivity.Preview,
                SpeechJob => PlaybackActivity.Speech,
                _ => PlaybackActivity.Idle,
            };

            return new PlaybackDiagnostics(
                activity,
                CountQueuedSoundsLocked(),
                _pendingPreview != null,
                _queue.Any(job => job is SpeechJob),
                _requested,
                _started,
                _completed,
                _failed,
                _cancelled,
                _expired,
                _skippedCooldown,
                _skippedSilent,
                _skippedNoFile,
                _skippedBusyOrFull,
                _previewsStarted,
                _speechGranted,
                _maxConcurrent,
                _lastStartedUtc);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _settingsManager.SettingsChanged -= OnSettingsChanged;
            CancelQueueLocked(includeSpeech: true);
            _pendingPreview = null;
            StopActiveLocked();
            _disposed = true;
        }
    }

    // ---- settings ----

    private void OnSettingsChanged()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            var settings = _settingsManager.Settings;
            var signature = ComputeSettingsSignature(settings);
            if (!settings.SoundEnabled || settings.NotificationsPaused)
            {
                CancelQueueLocked(includeSpeech: false);
                if (_active is SoundJob)
                    StopActiveLocked();
            }
            else if (!string.Equals(signature, _settingsSignature, StringComparison.Ordinal))
            {
                // Queued entries keep no text, so filters cannot be re-evaluated: drop them (design §7.4).
                CancelQueueLocked(includeSpeech: false);
            }

            _settingsSignature = signature;
            TryStartNextLocked();
            NotifyStateChangedLocked();
        }
    }

    internal static string ComputeSettingsSignature(AppSettings settings)
    {
        try
        {
            return JsonSerializer.Serialize(new
            {
                settings.SoundEnabled,
                settings.AudioDispatchMode,
                settings.DefaultSoundRule,
                settings.AppSoundRules,
                settings.PlaybackPolicy,
                settings.SoundLibraryFolders,
                settings.MutedApps,
                settings.MuteKeywords,
                settings.MuteKeywordRegexFlags,
                settings.MuteRules,
                settings.QuietHoursEnabled,
                settings.QuietHoursStart,
                settings.QuietHoursEnd,
                settings.DeduplicationEnabled,
            });
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    // ---- scheduling (all *Locked members require _sync) ----

    private void TryStartNextLocked()
    {
        while (!_disposed && _active == null)
        {
            if (_pendingPreview != null)
            {
                var preview = _pendingPreview;
                _pendingPreview = null;
                if (StartEngineJobLocked(preview, preview.PreviewPath))
                    return;

                _failed++;
                continue;
            }

            var first = _queue.First;
            if (first == null)
                return;

            _queue.RemoveFirst();
            switch (first.Value)
            {
                case SpeechJob speech:
                    _active = speech;
                    speech.Lease.IsGranted = true;
                    speech.Alive = true;
                    IncrementAliveLocked();
                    _speechGranted++;
                    Post(speech.OnGranted);
                    return;

                case SoundJob sound:
                    if (IsExpired(sound, _utcNow(), GetPolicy(_settingsManager.Settings)))
                    {
                        _expired++;
                        continue;
                    }

                    if (StartSoundLocked(sound))
                        return;
                    continue;
            }
        }
    }

    private bool StartSoundLocked(SoundJob job)
    {
        while (true)
        {
            var selection = _selector.Select(job.Rule, job.HistoryKey,
                file => !job.Excluded.Contains(file) && _files.ResolvePlayablePath(file) != null);
            if (!selection.HasFile)
            {
                _skippedNoFile++;
                return false;
            }

            var fullPath = _files.ResolvePlayablePath(selection.File!);
            if (fullPath == null)
            {
                job.Excluded.Add(selection.File!);
                continue;
            }

            job.Selection = selection;
            if (StartEngineJobLocked(job, fullPath))
                return true;

            _failed++;
            job.Excluded.Add(selection.File!);
        }
    }

    private bool StartEngineJobLocked(EngineJob job, string fullPath)
    {
        job.FullPath = fullPath;
        _active = job;
        job.Alive = true;
        IncrementAliveLocked();
        try
        {
            job.Handle = _engine.Play(fullPath, new EngineObserver(this, job.Id));
            return true;
        }
        catch
        {
            _active = null;
            job.Alive = false;
            _alive--;
            return false;
        }
    }

    private void OnEngineStarted(long jobId)
    {
        lock (_sync)
        {
            if (_disposed || _active is not EngineJob job || job.Id != jobId || job.Started)
                return;

            job.Started = true;
            if (job is SoundJob sound && sound.Selection != null)
            {
                _selector.Commit(sound.HistoryKey, sound.Selection);
                _started++;
                _lastStartedUtc = _utcNow();
            }
            else if (job is PreviewJob)
            {
                _previewsStarted++;
            }

            NotifyStateChangedLocked();
        }
    }

    private void OnEngineCompleted(long jobId)
    {
        lock (_sync)
        {
            if (_disposed || _active is not EngineJob job || job.Id != jobId)
                return;

            _completed++;
            FinishActiveLocked();
            TryStartNextLocked();
            NotifyStateChangedLocked();
        }
    }

    private void OnEngineFailed(long jobId, SoundEngineFailureKind kind, bool hadStarted)
    {
        lock (_sync)
        {
            if (_disposed || _active is not EngineJob job || job.Id != jobId)
                return;

            _failed++;
            var started = hadStarted || job.Started;
            FinishActiveLocked();

            if (job is SoundJob sound && !started && sound.Selection?.File != null)
            {
                if (kind == SoundEngineFailureKind.FileUnsupported && job.FullPath != null)
                    _files.MarkUnplayable(job.FullPath);

                sound.Excluded.Add(sound.Selection.File);
                if (StartSoundLocked(sound))
                {
                    NotifyStateChangedLocked();
                    return;
                }
            }

            TryStartNextLocked();
            NotifyStateChangedLocked();
        }
    }

    private void ReleaseSpeech(PlaybackLease lease)
    {
        lock (_sync)
        {
            if (_active is SpeechJob active && active.Id == lease.Id)
            {
                FinishActiveLocked();
                TryStartNextLocked();
                NotifyStateChangedLocked();
                return;
            }

            for (var node = _queue.First; node != null; node = node.Next)
            {
                if (node.Value is SpeechJob waiting && waiting.Id == lease.Id)
                {
                    _queue.Remove(node);
                    NotifyStateChangedLocked();
                    return;
                }
            }
        }
    }

    private void StopActiveLocked()
    {
        switch (_active)
        {
            case EngineJob engineJob:
                var handle = engineJob.Handle;
                FinishActiveLocked();
                _cancelled++;
                try { handle?.Stop(); } catch { }
                break;

            case SpeechJob speech:
                speech.Lease.MarkReleasedWithoutCallback();
                FinishActiveLocked();
                _cancelled++;
                Post(speech.OnRevoked);
                break;
        }
    }

    private void FinishActiveLocked()
    {
        switch (_active)
        {
            case EngineJob engineJob:
                engineJob.Handle = null;
                if (engineJob.Alive)
                {
                    engineJob.Alive = false;
                    _alive--;
                }
                break;

            case SpeechJob speech:
                if (speech.Alive)
                {
                    speech.Alive = false;
                    _alive--;
                }
                break;
        }

        _active = null;
    }

    private void CancelQueueLocked(bool includeSpeech)
    {
        for (var node = _queue.First; node != null;)
        {
            var next = node.Next;
            switch (node.Value)
            {
                case SoundJob:
                    _queue.Remove(node);
                    _cancelled++;
                    break;

                case SpeechJob speech when includeSpeech:
                    _queue.Remove(node);
                    speech.Lease.MarkReleasedWithoutCallback();
                    _cancelled++;
                    Post(speech.OnRevoked);
                    break;
            }

            node = next;
        }
    }

    private void PruneExpiredLocked(DateTime now, PlaybackPolicy policy)
    {
        for (var node = _queue.First; node != null;)
        {
            var next = node.Next;
            if (node.Value is SoundJob sound && IsExpired(sound, now, policy))
            {
                _queue.Remove(node);
                _expired++;
            }

            node = next;
        }
    }

    private static bool IsExpired(SoundJob job, DateTime now, PlaybackPolicy policy)
    {
        // MaxQueueWaitSeconds = 0 means "no waiting": anything that had to wait expires.
        return (now - job.EnqueuedUtc).TotalSeconds > policy.MaxQueueWaitSeconds;
    }

    private int CountQueuedSoundsLocked() => _queue.Count(job => job is SoundJob);

    private void RecordAcceptedLocked(string appKey, DateTime now)
    {
        if (!_lastAcceptedByApp.ContainsKey(appKey) && _lastAcceptedByApp.Count >= MaxCooldownEntries)
        {
            var oldest = _lastAcceptedByApp.MinBy(pair => pair.Value).Key;
            _lastAcceptedByApp.Remove(oldest);
        }

        _lastAcceptedByApp[appKey] = now;
    }

    private void IncrementAliveLocked()
    {
        _alive++;
        if (_alive > _maxConcurrent)
            _maxConcurrent = _alive;
    }

    private static PlaybackPolicy GetPolicy(AppSettings settings)
    {
        var policy = (settings.PlaybackPolicy ?? new PlaybackPolicy()).Clone();
        policy.Normalize();
        return policy;
    }

    private void NotifyStateChangedLocked()
    {
        if (_stateChangedPending || StateChanged == null)
            return;

        _stateChangedPending = true;
        Post(() =>
        {
            lock (_sync)
                _stateChangedPending = false;
            StateChanged?.Invoke();
        });
    }

    private void Post(Action action)
    {
        try
        {
            _post(() =>
            {
                try { action(); } catch { /* a faulty callback must not break the coordinator */ }
            });
        }
        catch
        {
            // Posting failed (shutting down); nothing else to do.
        }
    }

    private abstract class Job
    {
        protected Job(long id) => Id = id;
        public long Id { get; }
    }

    private abstract class EngineJob : Job
    {
        protected EngineJob(long id) : base(id) { }
        public string? FullPath { get; set; }
        public ISoundPlaybackHandle? Handle { get; set; }
        public bool Alive { get; set; }
        public bool Started { get; set; }
    }

    private sealed class SoundJob : EngineJob
    {
        public SoundJob(long id, SoundRule rule, string historyKey, string? ruleKey, string? appId, DateTime enqueuedUtc)
            : base(id)
        {
            Rule = rule;
            HistoryKey = historyKey;
            RuleKey = ruleKey;
            AppId = appId;
            EnqueuedUtc = enqueuedUtc;
        }

        public SoundRule Rule { get; }
        public string HistoryKey { get; }
        public string? RuleKey { get; }
        public string? AppId { get; }
        public DateTime EnqueuedUtc { get; }
        public HashSet<string> Excluded { get; } = new(StringComparer.OrdinalIgnoreCase);
        public SoundSelection? Selection { get; set; }
    }

    private sealed class PreviewJob : EngineJob
    {
        public PreviewJob(long id, string fullPath) : base(id) => PreviewPath = fullPath;
        public string PreviewPath { get; }
    }

    private sealed class SpeechJob : Job
    {
        public SpeechJob(long id, PlaybackLease lease, Action onGranted, Action onRevoked)
            : base(id)
        {
            Lease = lease;
            OnGranted = onGranted;
            OnRevoked = onRevoked;
        }

        public PlaybackLease Lease { get; }
        public Action OnGranted { get; }
        public Action OnRevoked { get; }
        public bool Alive { get; set; }
    }

    private sealed class EngineObserver : ISoundPlaybackObserver
    {
        private readonly PlaybackCoordinator _owner;
        private readonly long _jobId;

        public EngineObserver(PlaybackCoordinator owner, long jobId)
        {
            _owner = owner;
            _jobId = jobId;
        }

        public void OnStarted() => _owner.Post(() => _owner.OnEngineStarted(_jobId));

        public void OnCompleted() => _owner.Post(() => _owner.OnEngineCompleted(_jobId));

        public void OnFailed(SoundEngineFailureKind kind, bool hadStarted) =>
            _owner.Post(() => _owner.OnEngineFailed(_jobId, kind, hadStarted));
    }
}
