// @file SoundFileStatus.cs
// @author: rudals252
// @brief WAV 파일 검증 결과(사용 가능, 결손, 드라이브 분리, 미지원 형식, 손상, 허용되지 않은 위치, 재생 실패 기록).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Playability of one WAV file. Only <see cref="Usable"/> files are offered to the selector.</summary>
public enum SoundFileStatus
{
    Usable = 0,
    Missing = 1,
    DriveUnavailable = 2,
    Unsupported = 3,
    Damaged = 4,
    NotAllowed = 5,
    PlaybackFailed = 6,
}
