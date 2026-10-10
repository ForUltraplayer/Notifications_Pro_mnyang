// @file App.Labs.cs
// @author: rudals252
// @brief [실험실] 카카오톡 팝업 감지기의 시작·중지(설정 연동), 감지 시 소리만 재생(옵션으로 카드도 표시), 설정창 실험실 탭 상태 갱신.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Windows.Threading;
using NotificationsPro.Localization;
using NotificationsPro.Services;

namespace NotificationsPro;

public partial class App
{
    /// <summary>App name used for KakaoTalk popups (an app name, so it is not translated).</summary>
    internal const string KakaoTalkAppName = "카카오톡";

    private KakaoTalkPopupWatcher? _kakaoWatcher;
    private DispatcherTimer? _labsStatusTimer;

    /// <summary>Call once on the UI thread after the queue and settings view model exist.</summary>
    private void InitializeLabs()
    {
        if (_settingsManager == null)
            return;

        _settingsManager.SettingsChanged += ApplyLabsSettings;
        ApplyLabsSettings();
    }

    private void ApplyLabsSettings()
    {
        var enabled = _settingsManager?.Settings.LabsKakaoPopupDetection == true;
        if (enabled && _kakaoWatcher == null)
        {
            _kakaoWatcher = new KakaoTalkPopupWatcher();
            _kakaoWatcher.PopupDetected += OnKakaoPopupDetected;
            _kakaoWatcher.StateChanged += UpdateLabsStatus;
            _kakaoWatcher.Start();

            // KakaoTalk can start or quit at any time; refresh the "running" part of the status.
            _labsStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _labsStatusTimer.Tick += (_, _) => UpdateLabsStatus();
            _labsStatusTimer.Start();
        }
        else if (!enabled && _kakaoWatcher != null)
        {
            DisposeLabs();
        }

        UpdateLabsStatus();
    }

    private void OnKakaoPopupDetected(string? appUserModelId)
    {
        if (_settingsManager?.Settings.LabsKakaoShowCard == true)
        {
            // Normal queue: pause, quiet hours, app mute, de-duplication and sound rules all apply.
            // The popup text cannot be read, so the card only says a message arrived.
            _queueManager?.AddNotification(
                KakaoTalkAppName,
                LocalizationManager.Get("Labs_KakaoCardTitle"),
                string.Empty,
                appUserModelId);
            return;
        }

        // Default, sound only: KakaoTalk already shows its own popup. Pause, quiet hours, app mute and
        // the app's sound rule still apply.
        if (_queueManager?.AcceptSoundOnlyNotification(KakaoTalkAppName, appUserModelId) == true)
            _playbackCoordinator?.EnqueueNotificationSound(KakaoTalkAppName, appUserModelId);
    }

    private void UpdateLabsStatus()
    {
        if (_settingsViewModel == null)
            return;

        string text;
        var watcher = _kakaoWatcher;
        if (watcher == null)
        {
            text = LocalizationManager.Get("Labs_KakaoStatusOff");
        }
        else if (watcher.StartError != null)
        {
            text = LocalizationManager.Format("Labs_KakaoStatusFailed", watcher.StartError);
        }
        else
        {
            var running = KakaoTalkPopupWatcher.IsKakaoTalkRunning()
                ? LocalizationManager.Get("Labs_KakaoRunning")
                : LocalizationManager.Get("Labs_KakaoNotRunning");
            var last = watcher.LastDetectedAt?.ToString("HH:mm:ss") ?? LocalizationManager.Get("Labs_KakaoNever");
            text = LocalizationManager.Format("Labs_KakaoStatusWatching", running, watcher.DetectedCount, last);
            if (watcher.LastRejectReason != null)
                text += Environment.NewLine + LocalizationManager.Format("Labs_KakaoLastReject", watcher.LastRejectReason);
        }

        _settingsViewModel.SetLabsKakaoStatus(text);
    }

    private void DisposeLabs()
    {
        _labsStatusTimer?.Stop();
        _labsStatusTimer = null;
        if (_kakaoWatcher != null)
        {
            _kakaoWatcher.PopupDetected -= OnKakaoPopupDetected;
            _kakaoWatcher.StateChanged -= UpdateLabsStatus;
            _kakaoWatcher.Dispose();
            _kakaoWatcher = null;
        }
    }
}
