// @file SoundFolderRejection.cs
// @author: rudals252
// @brief 외부 WAV 폴더 등록 거부 사유(빈 값, 전체 경로 아님, 네트워크 경로, 없음, 링크·정션, 관리 폴더, 잘못된 경로).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum SoundFolderRejection
{
    None = 0,
    Empty = 1,
    NotFullyQualified = 2,
    NetworkPath = 3,
    NotFound = 4,
    LinkOrJunction = 5,
    ManagedFolder = 6,
    Invalid = 7,
}
