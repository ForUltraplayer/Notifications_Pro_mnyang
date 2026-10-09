// @file AudioDispatchSubscription.cs
// @author: rudals252
// @brief AudioDispatchMode에 따라 QueueManager의 NotificationAddedWithIdentity(오버레이 수용, NotificationAdded와 같은 조건) 또는 NotificationAccepted(정책 통과) 중 하나에만 알림음 재생을 연결하고, 모드 변경 시 다시 연결한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Wires the sound trigger to exactly one queue event (design §6.2), never both:
/// - <see cref="AudioDispatchMode.OverlayAccepted"/> → <see cref="QueueManager.NotificationAddedWithIdentity"/>
///   (raised right after the upstream <see cref="QueueManager.NotificationAdded"/>, same condition).
/// - <see cref="AudioDispatchMode.PolicyAccepted"/> → <see cref="QueueManager.NotificationAccepted"/>.
/// Re-subscribes when the saved mode changes. The play callback receives the app display name and
/// the AUMID when known (null for accessibility capture) in both modes.
/// </summary>
public sealed class AudioDispatchSubscription : IDisposable
{
    private readonly QueueManager _queueManager;
    private readonly SettingsManager _settingsManager;
    private readonly Action<string, string?> _playSound;
    private bool _disposed;

    public AudioDispatchSubscription(QueueManager queueManager, SettingsManager settingsManager, Action<string, string?> playSound)
    {
        _queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _playSound = playSound ?? throw new ArgumentNullException(nameof(playSound));

        _settingsManager.SettingsChanged += OnSettingsChanged;
        Subscribe(_settingsManager.Settings.AudioDispatchMode);
    }

    /// <summary>The event currently subscribed, or null after dispose.</summary>
    public AudioDispatchMode? ActiveMode { get; private set; }

    private void OnSettingsChanged()
    {
        if (_disposed)
            return;

        var mode = _settingsManager.Settings.AudioDispatchMode;
        if (mode != ActiveMode)
            Subscribe(mode);
    }

    private void Subscribe(AudioDispatchMode mode)
    {
        Unsubscribe();
        if (mode == AudioDispatchMode.PolicyAccepted)
            _queueManager.NotificationAccepted += OnNotificationAccepted;
        else
            _queueManager.NotificationAddedWithIdentity += OnNotificationAdded;

        ActiveMode = mode == AudioDispatchMode.PolicyAccepted
            ? AudioDispatchMode.PolicyAccepted
            : AudioDispatchMode.OverlayAccepted;
    }

    private void Unsubscribe()
    {
        // Removing a handler that is not attached is a no-op, so both are always detached.
        _queueManager.NotificationAddedWithIdentity -= OnNotificationAdded;
        _queueManager.NotificationAccepted -= OnNotificationAccepted;
        ActiveMode = null;
    }

    private void OnNotificationAdded(string appName, string? appId) => _playSound(appName, appId);

    private void OnNotificationAccepted(NotificationAcceptedEventArgs args) => _playSound(args.AppName, args.AppId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _settingsManager.SettingsChanged -= OnSettingsChanged;
        Unsubscribe();
    }
}
