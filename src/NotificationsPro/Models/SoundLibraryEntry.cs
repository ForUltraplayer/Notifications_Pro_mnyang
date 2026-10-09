// @file SoundLibraryEntry.cs
// @author: rudals252
// @brief 보관함 목록의 WAV 한 개(전체 경로, 규칙에 저장할 참조, 표시 이름, 출처, 상태, 형식, 읽을 수 있을 때의 길이).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <param name="RuleReference">What a <see cref="SoundRule"/> stores for this file ("sounds/x.wav" or an absolute path).</param>
public sealed record SoundLibraryEntry(
    string FullPath,
    string RuleReference,
    string DisplayName,
    SoundFileSource Source,
    string FolderPath,
    SoundFileStatus Status,
    string FormatDescription,
    TimeSpan? Duration)
{
    public bool IsUsable => Status == SoundFileStatus.Usable;
}
