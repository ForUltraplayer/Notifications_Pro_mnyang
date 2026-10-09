// @file SoundDeleteResult.cs
// @author: rudals252
// @brief 관리 폴더 WAV 실제 삭제 요청 결과(삭제됨, 확인 플래그 없음, 관리 파일 아님, 없음, 실패).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum SoundDeleteResult
{
    Deleted = 0,
    NotConfirmed = 1,
    NotManagedFile = 2,
    NotFound = 3,
    Failed = 4,
}
