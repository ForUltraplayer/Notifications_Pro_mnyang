// @file SettingsViewModel.Language.cs
// @author: rudals252
// @brief 시스템 탭의 UI 언어 선택(OS 기본값 / 한국어 / English). UiLanguage는 이 PC 전용 값이라 undo/redo·프로필 경로를 거치지 않고 현재 설정에 바로 저장하며, 다른 설정·재생 대기 상태는 바꾸지 않는다. 언어가 바뀐 뒤 계산 문구를 다시 읽게 한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Localization;
using NotificationsPro.Services;

namespace NotificationsPro.ViewModels;

public partial class SettingsViewModel
{
    /// <summary>Combo value for "follow the OS language" (stored as null).</summary>
    public const string UiLanguageOsDefault = "";

    public static IReadOnlyList<OptionItem<string>> UiLanguageOptions { get; } = new[]
    {
        OptionItem<string>.Localized(LocalizationManager.KoreanCultureName, "System_LanguageKorean"),
        OptionItem<string>.Localized(LocalizationManager.EnglishCultureName, "System_LanguageEnglish"),
        OptionItem<string>.Localized(UiLanguageOsDefault, "System_LanguageOsDefault"),
    };

    /// <summary>"" = OS default, "ko-KR" or "en-US".</summary>
    public string SelectedUiLanguage
    {
        get => _settingsManager.Settings.UiLanguage ?? UiLanguageOsDefault;
        set
        {
            var normalized = SettingsManager.NormalizeUiLanguage(string.IsNullOrEmpty(value) ? null : value);
            if (string.Equals(_settingsManager.Settings.UiLanguage, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            ApplyUiLanguage(normalized);
        }
    }

    private void ApplyUiLanguage(string? uiLanguage)
    {
        // App-local value (design §10/§11.1): written straight to the current settings and saved.
        // It never goes through the undo/redo snapshot path, and no other setting or the playback
        // queue is touched. Pending debounced saves carry it over (CopyForkExtensionsTo).
        _settingsManager.Settings.UiLanguage = uiLanguage;
        _settingsManager.Save();
        OnPropertyChanged(nameof(SelectedUiLanguage));
        LocalizationManager.Instance.Apply(uiLanguage);
    }

    /// <summary>
    /// Re-reads display text after a UI language change: rebuilds list rows whose text is computed
    /// once (muted apps, per-app cards, sound library rows, playback status) and asks every binding
    /// to refresh. Settings values, undo/redo history and the playback queue are left unchanged.
    /// Messages already shown (e.g. the last library action result) keep their language until the next action.
    /// </summary>
    internal void RefreshLocalizedText()
    {
        RefreshMutedAppEntries();
        RefreshPerAppSoundRules();
        foreach (var entry in PerAppConfigEntries)
            entry.RefreshDisplayText();
        if (_soundUiLoaded)
            RefreshSoundLibrary();
        UpdatePlaybackStatus();
        OnPropertyChanged(string.Empty);
    }
}
