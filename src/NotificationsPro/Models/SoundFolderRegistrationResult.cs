// @file SoundFolderRegistrationResult.cs
// @author: rudals252
// @brief 외부 WAV 폴더 등록 검증 결과(정규화한 로컬 전체 경로 또는 거부 사유).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public sealed record SoundFolderRegistrationResult(string? NormalizedPath, SoundFolderRejection Rejection)
{
    public bool Accepted => Rejection == SoundFolderRejection.None && NormalizedPath != null;
}
