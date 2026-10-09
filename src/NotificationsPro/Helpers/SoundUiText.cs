// @file SoundUiText.cs
// @author: rudals252
// @brief 소리 탭·앱 탭·트레이의 WAV 관련 표시 문구(상태·출처·거부 사유·모드·재생 방식·원래 소리 처리 상태)를 enum 값에서 리소스 키로 연결한다. 저장값(enum 이름)과 분리된 표시 문자열이며 문구 자체는 Resources/Strings*.resx에 있다(P3).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Localization;
using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Display strings for the sound UI, resolved from the UI-language resources.
/// Stored values (enum member names, JSON keys) are never produced from these strings.
/// </summary>
public static class SoundUiText
{
    private static string L(string key) => LocalizationManager.Get(key);

    public static string Status(SoundFileStatus status) => status switch
    {
        SoundFileStatus.Usable => L("SoundStatus_Available"),
        SoundFileStatus.Missing => L("SoundStatus_Missing"),
        SoundFileStatus.DriveUnavailable => L("SoundStatus_DriveDisconnected"),
        SoundFileStatus.Unsupported => L("SoundStatus_UnsupportedFormat"),
        SoundFileStatus.Damaged => L("SoundStatus_DamagedFile"),
        SoundFileStatus.NotAllowed => L("SoundStatus_LocationNotAllowed"),
        SoundFileStatus.PlaybackFailed => L("SoundStatus_PlaybackFailed"),
        _ => status.ToString(),
    };

    public static string Source(SoundFileSource source) => source switch
    {
        SoundFileSource.Managed => L("SoundSource_ManagedFolder"),
        SoundFileSource.ExternalFolder => L("SoundSource_ExternalFolder"),
        SoundFileSource.WindowsSound => L("SoundSource_WindowsSound"),
        _ => source.ToString(),
    };

    public static string FolderStatus(SoundFileStatus status) => status switch
    {
        SoundFileStatus.Usable => L("SoundStatus_Available"),
        SoundFileStatus.Missing => L("SoundFolderStatus_FolderMissing"),
        SoundFileStatus.DriveUnavailable => L("SoundStatus_DriveDisconnected"),
        SoundFileStatus.NotAllowed => L("SoundFolderStatus_NotAllowed"),
        _ => Status(status),
    };

    public static string FolderRejection(SoundFolderRejection rejection) => rejection switch
    {
        SoundFolderRejection.None => L("SoundFolderRejection_Added"),
        SoundFolderRejection.Empty => L("SoundFolderRejection_Empty"),
        SoundFolderRejection.NotFullyQualified => L("SoundFolderRejection_NotFullyQualified"),
        SoundFolderRejection.NetworkPath => L("SoundFolderRejection_NetworkPath"),
        SoundFolderRejection.NotFound => L("SoundFolderRejection_NotFound"),
        SoundFolderRejection.LinkOrJunction => L("SoundFolderRejection_LinkOrJunction"),
        SoundFolderRejection.ManagedFolder => L("SoundFolderRejection_ManagedFolder"),
        SoundFolderRejection.Invalid => L("SoundFolderRejection_Invalid"),
        _ => rejection.ToString(),
    };

    public static string ImportStatus(SoundImportStatus status) => status switch
    {
        SoundImportStatus.Copied => L("SoundImport_Copied"),
        SoundImportStatus.AlreadyPresent => L("SoundImport_AlreadyPresent"),
        SoundImportStatus.AlreadyManaged => L("SoundImport_AlreadyManaged"),
        SoundImportStatus.SourceMissing => L("SoundImport_SourceMissing"),
        SoundImportStatus.NotWav => L("SoundImport_NotWav"),
        SoundImportStatus.Unsupported => L("SoundImport_Unsupported"),
        SoundImportStatus.TooLarge => L("SoundImport_TooLarge"),
        SoundImportStatus.Failed => L("SoundImport_Failed"),
        _ => status.ToString(),
    };

    public static string DeleteResult(SoundDeleteResult result) => result switch
    {
        SoundDeleteResult.Deleted => L("SoundDelete_Deleted"),
        SoundDeleteResult.NotConfirmed => L("SoundDelete_NotConfirmed"),
        SoundDeleteResult.NotManagedFile => L("SoundDelete_NotManagedFile"),
        SoundDeleteResult.NotFound => L("SoundDelete_NotFound"),
        SoundDeleteResult.Failed => L("SoundDelete_Failed"),
        _ => result.ToString(),
    };

    public static string Mode(SoundSelectionMode mode) => mode switch
    {
        SoundSelectionMode.Single => L("SoundMode_Single"),
        SoundSelectionMode.Random => L("SoundMode_Random"),
        SoundSelectionMode.Sequential => L("SoundMode_Sequential"),
        _ => mode.ToString(),
    };

    public static string DispatchMode(AudioDispatchMode mode) => mode switch
    {
        AudioDispatchMode.OverlayAccepted => L("SoundDispatch_OverlayAccepted"),
        AudioDispatchMode.PolicyAccepted => L("SoundDispatch_PolicyAccepted"),
        _ => mode.ToString(),
    };

    public static string Overflow(PlaybackOverflowBehavior behavior) => behavior switch
    {
        PlaybackOverflowBehavior.Queue => L("SoundOverflow_Queue"),
        PlaybackOverflowBehavior.Skip => L("SoundOverflow_Skip"),
        _ => behavior.ToString(),
    };

    public static string Activity(PlaybackActivity activity) => activity switch
    {
        PlaybackActivity.Idle => L("PlaybackActivity_Idle"),
        PlaybackActivity.NotificationSound => L("PlaybackActivity_NotificationSound"),
        PlaybackActivity.Preview => L("PlaybackActivity_Preview"),
        PlaybackActivity.Speech => L("PlaybackActivity_Speech"),
        _ => activity.ToString(),
    };

    public static string ManualMuteState(string? state) => state switch
    {
        ManualMuteConfirmation.SetupNeeded => L("ManualMute_SetupNeeded"),
        ManualMuteConfirmation.UserConfirmed => L("ManualMute_UserConfirmed"),
        ManualMuteConfirmation.NotSupported => L("ManualMute_NotSupported"),
        _ => L("ManualMute_NotChecked"),
    };

    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } value || value <= TimeSpan.Zero)
            return string.Empty;

        return value.TotalSeconds < 60
            ? LocalizationManager.Format("Unit_SecondsOneDecimal", value.TotalSeconds)
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    public static string LastEvent(string? lastEvent) => lastEvent switch
    {
        "Started" => L("PlaybackEvent_Started"),
        "Completed" => L("PlaybackEvent_Completed"),
        "Failed" => L("PlaybackEvent_Failed"),
        "Cancelled" => L("PlaybackEvent_Cancelled"),
        "Expired" => L("PlaybackEvent_Expired"),
        "Queued" => L("PlaybackEvent_Queued"),
        "PreviewQueued" => L("PlaybackEvent_PreviewQueued"),
        "PreviewStarted" => L("PlaybackEvent_PreviewStarted"),
        "SpeechStarted" => L("PlaybackEvent_SpeechStarted"),
        "SkippedCooldown" => L("PlaybackEvent_SkippedCooldown"),
        "SkippedSilent" => L("PlaybackEvent_SkippedSilent"),
        "SkippedNoUsableFile" => L("PlaybackEvent_SkippedNoUsableFile"),
        "SkippedBusyOrFull" => L("PlaybackEvent_SkippedBusyOrFull"),
        "StoppedAll" => L("PlaybackEvent_StoppedAll"),
        null or "" or "None" => L("PlaybackEvent_None"),
        _ => lastEvent,
    };

    /// <summary>
    /// Sounds-tab status line. Counters and state only, plus the last started file's display name;
    /// never app names, paths or notification text.
    /// </summary>
    public static string PlaybackStatus(PlaybackDiagnostics diagnostics, string? lastFileDisplayName)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var text = LocalizationManager.Format(
            "Sounds_PlaybackStatusLine",
            Activity(diagnostics.Activity),
            diagnostics.QueuedSounds,
            diagnostics.Started,
            diagnostics.Completed,
            diagnostics.SkippedCooldown,
            diagnostics.MaxConcurrentPlaybacks,
            LastEvent(diagnostics.LastEvent));
        return string.IsNullOrWhiteSpace(lastFileDisplayName)
            ? text
            : LocalizationManager.Format("Sounds_PlaybackStatusLastFile", text, lastFileDisplayName);
    }

    public static string ReceivePathWinRt => L("Apps_ReceivePathWinRt");
    public static string ReceivePathAccessibility => L("Apps_ReceivePathAccessibility");
    public static string ReceivePathNotSeen => L("Apps_ReceivePathNotSeen");
}
