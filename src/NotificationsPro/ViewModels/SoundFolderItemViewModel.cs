// @file SoundFolderItemViewModel.cs
// @author: rudals252
// @brief 음성 보관함 폴더 한 행(경로, 관리/외부 출처, 상태, WAV 수). 외부 폴더만 참조 해제할 수 있다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.ViewModels;

public sealed class SoundFolderItemViewModel
{
    public SoundFolderItemViewModel(SoundFolderStatus status)
    {
        FolderPath = status.FolderPath;
        IsExternal = status.Source == SoundFileSource.ExternalFolder;
        SourceText = SoundUiText.Source(status.Source);
        StatusText = SoundUiText.FolderStatus(status.Status);
        IsAvailable = status.Status == SoundFileStatus.Usable;
        CountText = $"{status.WavCount} WAV";
    }

    public string FolderPath { get; }

    public bool IsExternal { get; }

    public bool IsAvailable { get; }

    public string SourceText { get; }

    public string StatusText { get; }

    public string CountText { get; }
}
