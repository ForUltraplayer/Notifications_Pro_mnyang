// @file LegacySettingsImportResult.cs
// @author: rudals252
// @brief 원본 설정 가져오기 결과(성공 여부, 백업 경로, 복사·건너뜀·충돌 백업한 자산 수, 원본 스키마, 실패 사유)를 담는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

public sealed class LegacySettingsImportResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public LegacySettingsSourceKind? SourceKind { get; init; }
    public int? SourceSchemaVersion { get; init; }

    /// <summary>Copy of the fork's previous settings.json (settings.json.pre-import-&lt;timestamp&gt;), or null if there was none.</summary>
    public string? SettingsBackupPath { get; init; }

    /// <summary>Folder holding fork asset files that were moved aside because the import replaced them, or null.</summary>
    public string? AssetBackupFolder { get; init; }

    public int AssetsCopied { get; init; }
    public int AssetsSkippedIdentical { get; init; }
    public int AssetsBackedUp { get; init; }

    /// <summary>Save outcome after applying the imported settings.</summary>
    public SettingsSaveStatus SaveStatus { get; init; } = SettingsSaveStatus.NotAttempted;

    public static LegacySettingsImportResult Fail(string error, LegacySettingsSourceKind? kind = null, int? schemaVersion = null)
        => new() { Success = false, Error = error, SourceKind = kind, SourceSchemaVersion = schemaVersion };
}
