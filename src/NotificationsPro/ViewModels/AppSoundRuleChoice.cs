// @file AppSoundRuleChoice.cs
// @author: rudals252
// @brief 앱 탭 소리 선택 3가지(기본값 상속 = 규칙 없음 / 이 앱 무음 = ExplicitSilence / 이 앱 전용 목록). UI 상태이며 저장값이 아니다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.ViewModels;

public enum AppSoundRuleChoice
{
    /// <summary>No app rule: the default sound rule applies.</summary>
    Inherit = 0,

    /// <summary>App rule with ExplicitSilence: never falls back to the default.</summary>
    Silence = 1,

    /// <summary>App rule with its own mode and file list.</summary>
    Custom = 2,
}
