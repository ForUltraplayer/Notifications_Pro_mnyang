// @file SoundUiText.cs
// @author: rudals252
// @brief 소리 탭·앱 탭·트레이의 WAV 관련 표시 문구(상태·출처·거부 사유·모드·재생 방식·원래 소리 처리 상태)를 한곳에 모은다. 저장값(enum 이름)과 분리된 표시 문자열이며 P3에서 리소스로 옮긴다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Display strings for the sound UI. Kept in English in code for now (design §10: resources arrive in P3).
/// Stored values (enum member names, JSON keys) are never produced from these strings.
/// </summary>
public static class SoundUiText
{
    public static string Status(SoundFileStatus status) => status switch
    {
        SoundFileStatus.Usable => "Available",
        SoundFileStatus.Missing => "Missing",
        SoundFileStatus.DriveUnavailable => "Drive disconnected",
        SoundFileStatus.Unsupported => "Unsupported format",
        SoundFileStatus.Damaged => "Damaged file",
        SoundFileStatus.NotAllowed => "Location not allowed",
        SoundFileStatus.PlaybackFailed => "Playback failed",
        _ => status.ToString(),
    };

    public static string Source(SoundFileSource source) => source switch
    {
        SoundFileSource.Managed => "Managed folder",
        SoundFileSource.ExternalFolder => "External folder",
        SoundFileSource.WindowsSound => "Windows sound",
        _ => source.ToString(),
    };

    public static string FolderStatus(SoundFileStatus status) => status switch
    {
        SoundFileStatus.Usable => "Available",
        SoundFileStatus.Missing => "Folder missing",
        SoundFileStatus.DriveUnavailable => "Drive disconnected",
        SoundFileStatus.NotAllowed => "Not allowed (network path or link)",
        _ => Status(status),
    };

    public static string FolderRejection(SoundFolderRejection rejection) => rejection switch
    {
        SoundFolderRejection.None => "Added.",
        SoundFolderRejection.Empty => "No folder was chosen.",
        SoundFolderRejection.NotFullyQualified => "The folder path is not a full local path.",
        SoundFolderRejection.NetworkPath => "Network folders cannot be added. Choose a folder on this PC.",
        SoundFolderRejection.NotFound => "The folder does not exist.",
        SoundFolderRejection.LinkOrJunction => "Folders that are links or junctions (or inside one) cannot be added.",
        SoundFolderRejection.ManagedFolder => "This is the managed sound folder; it is always included.",
        SoundFolderRejection.Invalid => "The folder path is not valid.",
        _ => rejection.ToString(),
    };

    public static string ImportStatus(SoundImportStatus status) => status switch
    {
        SoundImportStatus.Copied => "imported",
        SoundImportStatus.AlreadyPresent => "already in the managed folder (same content)",
        SoundImportStatus.AlreadyManaged => "already in the managed folder",
        SoundImportStatus.SourceMissing => "file not found",
        SoundImportStatus.NotWav => "not a .wav file",
        SoundImportStatus.Unsupported => "not a supported WAV file",
        SoundImportStatus.TooLarge => "larger than 64 MB",
        SoundImportStatus.Failed => "could not be copied",
        _ => status.ToString(),
    };

    public static string DeleteResult(SoundDeleteResult result) => result switch
    {
        SoundDeleteResult.Deleted => "File deleted and removed from all sound lists.",
        SoundDeleteResult.NotConfirmed => "Delete was not confirmed.",
        SoundDeleteResult.NotManagedFile => "Only files in the managed folder can be deleted.",
        SoundDeleteResult.NotFound => "The file no longer exists; it was removed from all sound lists.",
        SoundDeleteResult.Failed => "The file could not be deleted.",
        _ => result.ToString(),
    };

    public static string Mode(SoundSelectionMode mode) => mode switch
    {
        SoundSelectionMode.Single => "Single",
        SoundSelectionMode.Random => "Random",
        SoundSelectionMode.Sequential => "Sequential",
        _ => mode.ToString(),
    };

    public static string DispatchMode(AudioDispatchMode mode) => mode switch
    {
        AudioDispatchMode.OverlayAccepted => "Overlay accepted",
        AudioDispatchMode.PolicyAccepted => "Policy accepted",
        _ => mode.ToString(),
    };

    public static string Overflow(PlaybackOverflowBehavior behavior) => behavior switch
    {
        PlaybackOverflowBehavior.Queue => "Wait in queue",
        PlaybackOverflowBehavior.Skip => "Skip the new sound",
        _ => behavior.ToString(),
    };

    public static string Activity(PlaybackActivity activity) => activity switch
    {
        PlaybackActivity.Idle => "Idle",
        PlaybackActivity.NotificationSound => "Notification sound",
        PlaybackActivity.Preview => "Preview",
        PlaybackActivity.Speech => "Speech",
        _ => activity.ToString(),
    };

    public static string ManualMuteState(string? state) => state switch
    {
        ManualMuteConfirmation.SetupNeeded => "Setup needed",
        ManualMuteConfirmation.UserConfirmed => "Confirmed by you (not verified by the app)",
        ManualMuteConfirmation.NotSupported => "Guidance not available",
        _ => "Not checked",
    };

    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } value || value <= TimeSpan.Zero)
            return string.Empty;

        return value.TotalSeconds < 60
            ? $"{value.TotalSeconds:0.0} s"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    public static string LastEvent(string? lastEvent) => lastEvent switch
    {
        "Started" => "started",
        "Completed" => "completed",
        "Failed" => "failed",
        "Cancelled" => "cancelled",
        "Expired" => "expired in queue",
        "Queued" => "queued",
        "PreviewQueued" => "preview queued",
        "PreviewStarted" => "preview started",
        "SpeechStarted" => "speech started",
        "SkippedCooldown" => "skipped (minimum interval)",
        "SkippedSilent" => "skipped (silent or paused)",
        "SkippedNoUsableFile" => "skipped (no usable file)",
        "SkippedBusyOrFull" => "skipped (busy or queue full)",
        "StoppedAll" => "stopped",
        null or "" or "None" => "none",
        _ => lastEvent,
    };

    /// <summary>
    /// Sounds-tab status line. Counters and state only, plus the last started file's display name;
    /// never app names, paths or notification text.
    /// </summary>
    public static string PlaybackStatus(PlaybackDiagnostics diagnostics, string? lastFileDisplayName)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var text = $"Playback: {Activity(diagnostics.Activity)} | Queued: {diagnostics.QueuedSounds} | "
            + $"Started: {diagnostics.Started} | Completed: {diagnostics.Completed} | "
            + $"Interval skips: {diagnostics.SkippedCooldown} | Max at once: {diagnostics.MaxConcurrentPlaybacks} | "
            + $"Last: {LastEvent(diagnostics.LastEvent)}";
        return string.IsNullOrWhiteSpace(lastFileDisplayName) ? text : $"{text} | Last file: {lastFileDisplayName}";
    }

    public const string ReceivePathWinRt = "WinRT (app ID known)";
    public const string ReceivePathAccessibility = "Accessibility (no app ID)";
    public const string ReceivePathNotSeen = "Not seen this session";
}
