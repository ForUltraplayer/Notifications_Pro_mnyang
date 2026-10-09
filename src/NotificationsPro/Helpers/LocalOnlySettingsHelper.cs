// @file LocalOnlySettingsHelper.cs
// @author: rudals252
// @brief 이 PC 전용 설정(UI 언어, 외부 WAV 폴더 허용 목록, 원음 수동 음소거 확인, 비정상 종료 재시작 의도, 트레이로 시작)을 프로필·내보내기에서 빼고, 프로필·가져오기·undo/redo 적용 시 현재 값을 보존한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text.Json;
using System.Text.Json.Nodes;
using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// App-local settings never travel with profiles/exports and are never overwritten by applying
/// a profile, an imported file, or an undo/redo snapshot:
/// - <see cref="AppSettings.UiLanguage"/>: this PC's language choice (design §10).
/// - <see cref="AppSettings.SoundLibraryFolders"/>: external-folder trust is per PC (design §7.3, §11.2.7).
/// - <see cref="AppSettings.ManualMuteConfirmations"/>: user confirmations of OS/app settings on this PC (design §11.1).
/// - <see cref="AppSettings.RestartOnCrash"/>: OS task registration intent; profiles must not silently change OS auto-run (design §9.3).
/// - <see cref="AppSettings.StartMinimizedToTray"/>: start-up behaviour of this PC, paired with the logon task (P4).
/// The logon task itself is never stored in settings: its OS state is the source of truth.
/// </summary>
public static class LocalOnlySettingsHelper
{
    public static readonly IReadOnlyList<string> LocalOnlyPropertyNames = new[]
    {
        nameof(AppSettings.UiLanguage),
        nameof(AppSettings.SoundLibraryFolders),
        nameof(AppSettings.ManualMuteConfirmations),
        nameof(AppSettings.RestartOnCrash),
        nameof(AppSettings.StartMinimizedToTray),
    };

    /// <summary>Overwrites the local-only values of <paramref name="target"/> with those of <paramref name="source"/>.</summary>
    public static void CopyLocalOnly(AppSettings source, AppSettings target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        target.UiLanguage = source.UiLanguage;
        target.SoundLibraryFolders = new List<string>(source.SoundLibraryFolders ?? new List<string>());
        target.ManualMuteConfirmations = AppSettings.CloneManualMuteConfirmations(source.ManualMuteConfirmations);
        target.RestartOnCrash = source.RestartOnCrash;
        target.StartMinimizedToTray = source.StartMinimizedToTray;
    }

    /// <summary>Resets local-only values to their defaults (used before writing a portable snapshot).</summary>
    public static void ClearLocalOnly(AppSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.UiLanguage = null;
        target.SoundLibraryFolders = new List<string>();
        target.ManualMuteConfirmations = new Dictionary<string, ManualMuteConfirmation>(StringComparer.OrdinalIgnoreCase);
        target.RestartOnCrash = false;
        target.StartMinimizedToTray = true; // model default (the key is removed from portable JSON anyway)
    }

    /// <summary>
    /// Serializes a profile/export snapshot without the local-only properties at all
    /// (not even as empty values), so the JSON carries no trace of this PC's local state.
    /// </summary>
    public static string SerializePortable(AppSettings snapshot, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var node = JsonSerializer.SerializeToNode(snapshot, options) as JsonObject
            ?? throw new JsonException("Settings snapshot did not serialize to a JSON object.");

        foreach (var name in LocalOnlyPropertyNames)
            node.Remove(name);

        return node.ToJsonString(options);
    }
}
