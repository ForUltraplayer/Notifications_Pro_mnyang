// @file SoundImportStatus.cs
// @author: rudals252
// @brief WAV 가져오기 결과 종류(복사됨, 같은 내용이 이미 있음, 이미 관리 폴더 안, 원본 없음, 미지원·손상, 너무 큼, 실패).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum SoundImportStatus
{
    Copied = 0,
    AlreadyPresent = 1,
    AlreadyManaged = 2,
    SourceMissing = 3,
    NotWav = 4,
    Unsupported = 5,
    TooLarge = 6,
    Failed = 7,
}
