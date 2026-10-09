// @file SoundLibrarySnapshot.cs
// @author: rudals252
// @brief 보관함 새로고침 결과(파일 항목 목록과 폴더별 상태). 파일 감시 없이 화면 진입·새로고침 때 만든다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public sealed record SoundLibrarySnapshot(IReadOnlyList<SoundLibraryEntry> Entries, IReadOnlyList<SoundFolderStatus> Folders);
