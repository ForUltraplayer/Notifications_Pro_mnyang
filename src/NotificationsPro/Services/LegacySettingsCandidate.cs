// @file LegacySettingsCandidate.cs
// @author: rudals252
// @brief 원본 Notifications Pro 설정 가져오기 후보(MSIX 가상화 경로 / 언패키지 경로)의 위치·존재·스키마·수정 시각·자산 폴더 상태를 담는다. 내용(알림)은 없다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public enum LegacySettingsSourceKind
{
    /// <summary>Installed MSIX original: file-system virtualized under %LocalAppData%\Packages\…\LocalCache\Roaming\NotificationsPro.</summary>
    PackagedMsix = 0,

    /// <summary>Unpackaged original run: %AppData%\NotificationsPro.</summary>
    Unpackaged = 1,
}

/// <summary>
/// One place the original app may keep its data. <paramref name="AppVisibleRootPath"/> is the folder
/// path the original app itself saw (MSIX virtualization maps %AppData%\NotificationsPro to
/// <paramref name="RootPath"/>), used to rebase any absolute asset paths stored by older versions.
/// </summary>
public sealed record LegacySettingsSource(LegacySettingsSourceKind Kind, string RootPath, string AppVisibleRootPath);

/// <summary>Detection result for one <see cref="LegacySettingsSource"/>. Read-only inspection, nothing is modified.</summary>
public sealed class LegacySettingsCandidate
{
    public required LegacySettingsSource Source { get; init; }
    public required string SettingsPath { get; init; }
    public bool Exists { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
    public int? SchemaVersion { get; init; }
    public bool IsValidJson { get; init; }
    public bool IsTooLarge { get; init; }
    public bool IsNewerSchema { get; init; }
    public IReadOnlyList<string> AssetFolders { get; init; } = Array.Empty<string>();

    public LegacySettingsSourceKind Kind => Source.Kind;
    public string RootPath => Source.RootPath;

    public bool CanImport => Exists && IsValidJson && !IsTooLarge && !IsNewerSchema;

    /// <summary>Short technical reason when <see cref="CanImport"/> is false (no notification content).</summary>
    public string? BlockReason =>
        !Exists ? "settings.json not found"
        : IsTooLarge ? "settings.json exceeds 1 MB"
        : !IsValidJson ? "settings.json is not valid JSON"
        : IsNewerSchema ? $"settings.json uses a newer schema ({SchemaVersion})"
        : null;
}
