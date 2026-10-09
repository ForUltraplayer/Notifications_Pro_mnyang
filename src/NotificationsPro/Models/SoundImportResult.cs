// @file SoundImportResult.cs
// @author: rudals252
// @brief WAV 가져오기 결과(상태, 규칙에 저장할 참조, 고유 내부 파일명, 원래 이름에서 만든 표시 이름).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public sealed record SoundImportResult(
    SoundImportStatus Status,
    string? RuleReference = null,
    string? InternalFileName = null,
    string? DisplayName = null)
{
    public bool Succeeded => Status is SoundImportStatus.Copied or SoundImportStatus.AlreadyPresent or SoundImportStatus.AlreadyManaged;
}
