// @file SettingsSchemaMigrator.cs
// @author: rudals252
// @brief 설정 스키마 v5 이하 → v6 이관 규칙(기존 단일 WAV·앱별 WAV를 1파일 Single 규칙으로 변환, 이관 설정의 AudioDispatchMode=OverlayAccepted, 앱별 최소 간격 0)을 한 곳에 모은다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Schema v6 migration (design §11.2). Must run after
/// <see cref="AppSettingsAssetPathHelper.NormalizeForRuntime(AppSettings)"/> so it sees the same
/// sound values the original app would actually use at runtime.
///
/// Legacy sound semantics confirmed in the upstream code (v5):
/// - <see cref="AppSettings.SoundEnabled"/> is the master switch: false = no sound at all. It is kept as-is.
/// - <see cref="AppSettings.DefaultSound"/> "None"/empty = the default plays nothing.
/// - <see cref="AppSettings.PerAppSounds"/> entries whose value is "None", empty, or not a trusted
///   Windows/managed sound are removed by NormalizeForRuntime, so such an app inherits the default sound.
///   (The per-app "Default" UI choice removes the entry; choosing "None" per app is also normalized away,
///   so the original app has no per-app explicit silence.)
/// Legacy fields are never deleted, so the original app can still read a migrated file.
/// </summary>
public static class SettingsSchemaMigrator
{
    public const int ForkSchemaVersion = 6;

    /// <summary>
    /// Migrates settings loaded from settings.json. Returns true when values changed and should be saved.
    /// New installs (no file) keep model defaults, including <see cref="AudioDispatchMode.PolicyAccepted"/>.
    /// </summary>
    public static bool MigrateLoadedSettings(AppSettings settings, bool hadExistingSettingsFile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!hadExistingSettingsFile)
            return false;

        var fileVersion = settings.SettingsSchemaVersion ?? 0;
        if (fileVersion >= ForkSchemaVersion)
            return false;

        settings.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
        DisablePerAppCooldownForLegacy(settings);
        BuildSoundRulesFromLegacy(settings);
        return true;
    }

    /// <summary>
    /// Upgrades a profile/import snapshot written before v6. Such a file has no opinion about the
    /// audio dispatch path, so the current value is kept instead of silently switching modes.
    /// </summary>
    public static void MigrateLegacySnapshot(AppSettings incoming, AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(current);
        if ((incoming.SettingsSchemaVersion ?? 0) >= ForkSchemaVersion)
            return;

        incoming.AudioDispatchMode = current.AudioDispatchMode;
        DisablePerAppCooldownForLegacy(incoming);
        BuildSoundRulesFromLegacy(incoming);
    }

    /// <summary>
    /// Pre-v6 files never had a per-app minimum interval, so migration must not add a new limit
    /// (design §7.4, reviewer decision P2-A §11-4): <see cref="PlaybackPolicy.MinIntervalPerAppSeconds"/> = 0.
    /// The other playback values keep their defaults. New installs and existing v6 files are untouched.
    /// </summary>
    internal static void DisablePerAppCooldownForLegacy(AppSettings settings)
    {
        settings.PlaybackPolicy ??= new PlaybackPolicy();
        settings.PlaybackPolicy.MinIntervalPerAppSeconds = 0;
    }

    /// <summary>
    /// Converts the legacy default sound and each per-app sound into a one-file Single rule.
    /// Existing app rule keys are not overwritten.
    /// </summary>
    public static void BuildSoundRulesFromLegacy(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var libraryFolders = settings.SoundLibraryFolders ?? new List<string>();

        settings.DefaultSoundRule = SoundRule.CreateSingle(
            AppSettingsAssetPathHelper.ToPortableRuleSoundReferenceOrEmpty(settings.DefaultSound, libraryFolders));

        var rules = AppSettings.CloneSoundRules(settings.AppSoundRules);
        foreach (var (appName, sound) in settings.PerAppSounds ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(appName) || rules.ContainsKey(appName))
                continue;

            var file = AppSettingsAssetPathHelper.ToPortableRuleSoundReferenceOrEmpty(sound, libraryFolders);
            if (string.IsNullOrWhiteSpace(file))
                continue; // "None"/empty/untrusted = inherit default in v5, so no rule

            rules[appName] = SoundRule.CreateSingle(file);
        }

        settings.AppSoundRules = rules;
    }
}
