// @file SingleInstanceGuard.cs
// @author: rudals252
// @brief 이름 있는 mutex로 단일 인스턴스를 보장하고, 두 번째 실행이 이름 있는 이벤트로 기존 인스턴스에 설정창 표시를 요청하게 한다.

namespace NotificationsPro.Helpers;

/// <summary>
/// Owns the per-session single-instance mutex for the primary instance and listens for
/// "show settings" requests from later launches. CLI argument forwarding is not handled
/// here yet (planned for P4).
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
    }

    /// <summary>
    /// Tries to become the primary instance. Returns null when another instance already owns
    /// the mutex.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string mutexName, string activationEventName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
        return new SingleInstanceGuard(mutex, activationEvent);
    }

    /// <summary>
    /// Asks the primary instance to show itself. Retries briefly because the primary creates
    /// its activation event right after taking the mutex.
    /// </summary>
    public static bool SignalExistingInstance(string activationEventName, int attempts = 10, int delayMs = 100)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (EventWaitHandle.TryOpenExisting(activationEventName, out var existing))
            {
                using (existing)
                    return existing.Set();
            }

            Thread.Sleep(delayMs);
        }

        return false;
    }

    /// <summary>Invokes <paramref name="onActivationRequested"/> (on a thread-pool thread) for each request.</summary>
    public void StartListening(Action onActivationRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registeredWait?.Unregister(null);
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => onActivationRequested(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _registeredWait?.Unregister(null);
        _activationEvent.Dispose();

        // This process holds the only handle; closing it destroys the named mutex,
        // so the next launch sees createdNew == true.
        _mutex.Dispose();
    }
}
