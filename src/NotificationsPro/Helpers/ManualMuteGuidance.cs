// @file ManualMuteGuidance.cs
// @author: rudals252
// @brief 원래 알림음 수동 음소거 안내 로직: 이번 세션에 본 AUMID와 알려진 패턴으로 앱 유형을 판정하고, 저장된 사용자 확인과 앱 유형으로 표시 상태(미확인/설정 필요/사용자 확인 완료/안내 미지원)를 정하며, 확인·다시 확인 필요 전이를 만든다. 앱이 직접 검증한 결과는 만들지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Manual mute support for R09 (design §8.2, confirmed 2026-10-09). The app never reads or changes the
/// Windows or app setting; it only shows where to change it and remembers what the user confirmed.
/// </summary>
public static class ManualMuteGuidance
{
    /// <summary>Settings page with the per-app "Play a sound when a notification arrives" toggles.</summary>
    public const string WindowsNotificationSettingsUri = "ms-settings:notifications";

    private static readonly string[] DiscordAppIdPrefixes =
    {
        "com.squirrel.Discord.",
        "com.squirrel.DiscordCanary.",
        "com.squirrel.DiscordPTB.",
    };

    private static readonly string[] KakaoTalkDisplayNames = { "KakaoTalk", "카카오톡" };

    /// <summary>
    /// App type from the AUMIDs observed this session (AppIdentityRegistry) and known patterns.
    /// No AUMID and no known name → <see cref="OriginalSoundAppKind.Unknown"/>.
    /// </summary>
    public static OriginalSoundAppKind DetectKind(string? displayName, IReadOnlyCollection<string>? appIds)
    {
        var ids = (appIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        if (ids.Any(IsKakaoTalkAppId) || IsKakaoTalkName(displayName))
            return OriginalSoundAppKind.KakaoTalk;

        if (ids.Any(IsDiscordAppId))
            return OriginalSoundAppKind.Discord;

        return ids.Count > 0 ? OriginalSoundAppKind.WindowsToast : OriginalSoundAppKind.Unknown;
    }

    public static bool IsDiscordAppId(string? appId)
        => !string.IsNullOrWhiteSpace(appId)
           && DiscordAppIdPrefixes.Any(prefix => appId.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Desktop KakaoTalk AUMID seen in P0: "{FOLDERID}\Kakao\KakaoTalk\KakaoTalk.exe".</summary>
    public static bool IsKakaoTalkAppId(string? appId)
        => !string.IsNullOrWhiteSpace(appId)
           && appId.Trim().EndsWith(@"\KakaoTalk.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsKakaoTalkName(string? displayName)
        => !string.IsNullOrWhiteSpace(displayName)
           && KakaoTalkDisplayNames.Any(name => string.Equals(name, displayName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// State shown to the user (values of <see cref="ManualMuteConfirmation"/>):
    /// KakaoTalk → NotSupported; a stored UserConfirmed → UserConfirmed; Windows toast / Discord → SetupNeeded;
    /// otherwise Unconfirmed.
    /// </summary>
    public static string ResolveState(OriginalSoundAppKind kind, ManualMuteConfirmation? stored)
    {
        if (kind == OriginalSoundAppKind.KakaoTalk)
            return ManualMuteConfirmation.NotSupported;

        if (string.Equals(stored?.State, ManualMuteConfirmation.UserConfirmed, StringComparison.Ordinal))
            return ManualMuteConfirmation.UserConfirmed;

        return kind is OriginalSoundAppKind.WindowsToast or OriginalSoundAppKind.Discord
            ? ManualMuteConfirmation.SetupNeeded
            : ManualMuteConfirmation.Unconfirmed;
    }

    /// <summary>"Mark as confirmed" is offered when guidance exists and the user has not confirmed yet.</summary>
    public static bool CanConfirm(OriginalSoundAppKind kind, string resolvedState)
        => kind is OriginalSoundAppKind.WindowsToast or OriginalSoundAppKind.Discord
           && !string.Equals(resolvedState, ManualMuteConfirmation.UserConfirmed, StringComparison.Ordinal);

    /// <summary>"Needs checking again" is offered while a user confirmation is shown.</summary>
    public static bool CanRequireRecheck(string resolvedState)
        => string.Equals(resolvedState, ManualMuteConfirmation.UserConfirmed, StringComparison.Ordinal);

    /// <summary>The Windows settings button applies to every app that uses Windows notifications.</summary>
    public static bool UsesWindowsToggle(OriginalSoundAppKind kind)
        => kind is OriginalSoundAppKind.WindowsToast or OriginalSoundAppKind.Discord;

    public static ManualMuteConfirmation Confirm(DateTimeOffset now)
        => new() { State = ManualMuteConfirmation.UserConfirmed, UpdatedAt = now };

    public static ManualMuteConfirmation RequireRecheck(DateTimeOffset now)
        => new() { State = ManualMuteConfirmation.SetupNeeded, UpdatedAt = now };

    /// <summary>
    /// Key under which a confirmation is written: the app's AUMID when one is known (the Windows sound
    /// switch is per AUMID, so apps sharing one AUMID such as ChatGPT and Codex share one confirmation),
    /// otherwise the display name. With several AUMIDs for one name, the card's selected rule key is used
    /// when it is one of them; otherwise the display name.
    /// </summary>
    public static string? ResolveStorageKey(string? displayName, IReadOnlyCollection<string>? appIds, string? ruleKey)
    {
        var ids = NormalizeIds(appIds);
        if (ids.Count == 1)
            return ids[0];

        if (ids.Count > 1 && !string.IsNullOrWhiteSpace(ruleKey))
        {
            var selected = ids.FirstOrDefault(id => string.Equals(id, ruleKey.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selected != null)
                return selected;
        }

        return string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    /// <summary>
    /// Reads a confirmation with the AppIdentityMatcher priority: AUMID keys first (the storage key, then the
    /// other AUMIDs seen this session), then the display name. A display-name entry written before AUMID keys
    /// were used is only read for compatibility; it is never re-keyed or removed automatically.
    /// </summary>
    public static ManualMuteConfirmation? FindStored(
        IReadOnlyDictionary<string, ManualMuteConfirmation>? map,
        string? displayName,
        IReadOnlyCollection<string>? appIds,
        string? ruleKey,
        out string? matchedKey)
    {
        matchedKey = null;
        if (map == null || map.Count == 0)
            return null;

        var ids = NormalizeIds(appIds);
        var storageKey = ResolveStorageKey(displayName, ids, ruleKey);
        var orderedIds = ids
            .OrderBy(id => string.Equals(id, storageKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        foreach (var id in orderedIds)
        {
            if (AppIdentityMatcher.TryFind(map, id, null, out var byId, out matchedKey) != AppIdentityMatchKind.None)
                return byId;
        }

        if (AppIdentityMatcher.TryFind(map, null, displayName, out var byName, out matchedKey) != AppIdentityMatchKind.None)
            return byName;

        matchedKey = null;
        return null;
    }

    private static List<string> NormalizeIds(IReadOnlyCollection<string>? appIds)
        => (appIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Resource key of the guidance text for <paramref name="kind"/>.</summary>
    public static string GuidanceResourceKey(OriginalSoundAppKind kind) => kind switch
    {
        OriginalSoundAppKind.WindowsToast => "ManualMute_GuidanceWindowsToast",
        OriginalSoundAppKind.Discord => "ManualMute_GuidanceDiscord",
        OriginalSoundAppKind.KakaoTalk => "ManualMute_GuidanceKakaoTalk",
        _ => "ManualMute_GuidanceUnknown",
    };
}
