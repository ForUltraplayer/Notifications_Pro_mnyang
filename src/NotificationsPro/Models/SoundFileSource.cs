// @file SoundFileSource.cs
// @author: rudals252
// @brief 보관함 항목의 출처(관리 폴더, 등록된 외부 폴더, 신뢰된 Windows 소리).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum SoundFileSource
{
    Managed = 0,
    ExternalFolder = 1,
    WindowsSound = 2,
}
