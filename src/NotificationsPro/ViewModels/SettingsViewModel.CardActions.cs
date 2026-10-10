// @file SettingsViewModel.CardActions.cs
// @author: rudals252
// @brief 오버레이 카드 동작 설정(카드 더블클릭으로 앱 열기, 앱을 연 뒤 카드 닫기)을 설정창에 노출한다. 값은 바로 저장한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.ViewModels;

public partial class SettingsViewModel
{
    /// <summary>Double-clicking a card opens the app that sent it (by AUMID). Off by default.</summary>
    public bool OpenAppOnCardDoubleClick
    {
        get => _settingsManager.Settings.OpenAppOnCardDoubleClick;
        set
        {
            if (_settingsManager.Settings.OpenAppOnCardDoubleClick == value)
                return;

            _settingsManager.Settings.OpenAppOnCardDoubleClick = value;
            _settingsManager.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>After the app was opened from a card, dismiss that card. On by default.</summary>
    public bool DismissCardAfterOpeningApp
    {
        get => _settingsManager.Settings.DismissCardAfterOpeningApp;
        set
        {
            if (_settingsManager.Settings.DismissCardAfterOpeningApp == value)
                return;

            _settingsManager.Settings.DismissCardAfterOpeningApp = value;
            _settingsManager.Save();
            OnPropertyChanged();
        }
    }
}
