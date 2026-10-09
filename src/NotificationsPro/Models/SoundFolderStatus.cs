// @file SoundFolderStatus.cs
// @author: rudals252
// @brief 보관함 폴더 상태(사용 가능, 폴더 없음, 드라이브 분리, 허용되지 않음).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public sealed record SoundFolderStatus(string FolderPath, SoundFileSource Source, SoundFileStatus Status, int WavCount);
