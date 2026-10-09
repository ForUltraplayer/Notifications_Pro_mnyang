// @file AppIdentityMatcher.cs
// @author: rudals252
// @brief 앱 식별 매칭 우선순위(1. AUMID 정확 일치 2. 표시 이름 일치 3. 없음)를 한 곳에서 처리한다. 기존 표시 이름 기준 규칙(PerAppSounds, MutedApps)과 공존한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

public enum AppIdentityMatchKind
{
    None = 0,
    AppId = 1,
    DisplayName = 2,
}

/// <summary>
/// Matches an app against keyed per-app settings (design §7.1):
/// 1) exact AppUserModelId key, 2) display-name key, 3) no match.
/// Keys compare case-insensitively like the existing name-based dictionaries. Existing
/// name-only rules (PerAppSounds, MutedApps) keep working because step 2 uses the display name;
/// nothing here merges a name rule into a different app automatically.
/// </summary>
public static class AppIdentityMatcher
{
    public static AppIdentityMatchKind TryFind<TValue>(
        IReadOnlyDictionary<string, TValue>? map,
        string? appId,
        string? displayName,
        out TValue? value,
        out string? matchedKey)
    {
        value = default;
        matchedKey = null;
        if (map == null || map.Count == 0)
            return AppIdentityMatchKind.None;

        if (TryGetKey(map, appId, out value, out matchedKey))
            return AppIdentityMatchKind.AppId;

        if (TryGetKey(map, displayName, out value, out matchedKey))
            return AppIdentityMatchKind.DisplayName;

        value = default;
        matchedKey = null;
        return AppIdentityMatchKind.None;
    }

    /// <summary>
    /// Finds the app's sound rule in <see cref="AppSettings.AppSoundRules"/> (AUMID first, then name).
    /// Returns None when no rule exists; callers then use the default rule (playback is P2).
    /// </summary>
    public static AppIdentityMatchKind FindAppSoundRule(AppSettings settings, string? appId, string? displayName, out SoundRule? rule)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return TryFind(settings.AppSoundRules, appId, displayName, out rule, out _);
    }

    /// <summary>Name-based list lookup used by MutedApps-style lists (unchanged upstream semantics).</summary>
    public static bool ContainsDisplayName(IEnumerable<string>? names, string? displayName)
    {
        if (names == null || string.IsNullOrWhiteSpace(displayName))
            return false;

        return names.Contains(displayName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetKey<TValue>(IReadOnlyDictionary<string, TValue> map, string? key, out TValue? value, out string? matchedKey)
    {
        value = default;
        matchedKey = null;
        var trimmed = key?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        if (map.TryGetValue(trimmed, out var direct))
        {
            value = direct;
            matchedKey = trimmed;
            return true;
        }

        // Dictionaries deserialized or cloned without a comparer may be case-sensitive.
        foreach (var pair in map)
        {
            if (string.Equals(pair.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                matchedKey = pair.Key;
                return true;
            }
        }

        return false;
    }
}
