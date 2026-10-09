// @file SoundRuleResolution.cs
// @author: rudals252
// @brief 앱 식별(AUMID → 표시 이름 → 기본)로 찾은 소리 규칙과 그 규칙의 선택 이력 키. 본문은 담지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Helpers;

namespace NotificationsPro.Models;

/// <summary>
/// Result of <c>SoundRuleSelector.Resolve</c>: which rule applies to an app and under which key its
/// selection history (sequence position, last file) is kept. Holds no notification text.
/// </summary>
public sealed record SoundRuleResolution(
    SoundRule? Rule,
    string? RuleKey,
    string HistoryKey,
    AppIdentityMatchKind MatchKind,
    SoundSilenceReason SilenceReason)
{
    public bool IsSilent => SilenceReason != SoundSilenceReason.None || Rule == null;
}
