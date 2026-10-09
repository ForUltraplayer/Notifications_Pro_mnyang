// @file SoundSelection.cs
// @author: rudals252
// @brief 선택기가 고른 후보 파일(규칙에 저장된 형태)과 목록 위치, 또는 무음 사유. 확정(Commit) 전에는 이력을 바꾸지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>
/// A candidate chosen by <c>SoundRuleSelector.Select</c>. <see cref="File"/> is the rule entry as stored
/// (for example "sounds/a.wav"); <see cref="Index"/> is its position in the rule's list. Choosing a
/// candidate does not change history — only <c>Commit</c> does, once playback has actually started.
/// </summary>
public sealed record SoundSelection(string? File, int Index, SoundSilenceReason SilenceReason)
{
    public bool HasFile => File != null && SilenceReason == SoundSilenceReason.None;

    public static SoundSelection Silent(SoundSilenceReason reason) => new(null, -1, reason);
}
