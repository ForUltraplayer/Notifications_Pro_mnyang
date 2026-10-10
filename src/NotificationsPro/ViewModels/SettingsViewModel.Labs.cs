// @file SettingsViewModel.Labs.cs
// @author: rudals252
// @brief [실험실] 설정창 실험실 탭의 값: 카카오톡 팝업 감지 켜기/끄기(설정 적용으로 앱이 감지기를 시작·중지)와 감지 상태 문구.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.ViewModels;

public partial class SettingsViewModel
{
    private string _labsKakaoStatus = string.Empty;

    /// <summary>Labs: detect KakaoTalk PC message popups. Off by default.</summary>
    public bool LabsKakaoPopupDetection
    {
        get => _settingsManager.Settings.LabsKakaoPopupDetection;
        set
        {
            if (_settingsManager.Settings.LabsKakaoPopupDetection == value)
                return;

            // Apply (not just Save) so SettingsChanged starts/stops the watcher in the app.
            var updated = _settingsManager.Settings.Clone();
            updated.LabsKakaoPopupDetection = value;
            _settingsManager.Apply(updated);
            OnPropertyChanged();
        }
    }

    /// <summary>Technical status line for the Labs tab (no message content).</summary>
    public string LabsKakaoStatus
    {
        get => _labsKakaoStatus;
        private set => SetProperty(ref _labsKakaoStatus, value ?? string.Empty);
    }

    /// <summary>Called by the app whenever the watcher state changes.</summary>
    public void SetLabsKakaoStatus(string text) => LabsKakaoStatus = text;
}
