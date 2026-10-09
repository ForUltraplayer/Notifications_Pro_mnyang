// @file SoundLibraryItemViewModel.cs
// @author: rudals252
// @brief 음성 보관함 파일 한 행(표시 이름, 출처, 상태, 읽을 수 있을 때의 길이, 기본 규칙 선택 체크). 관리 폴더 파일만 실제 삭제 대상이다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.ViewModels;

public sealed class SoundLibraryItemViewModel : BaseViewModel
{
    private SoundLibraryEntry _entry;
    private SoundChoiceItem? _defaultChoice;

    public SoundLibraryItemViewModel(SoundLibraryEntry entry, string displayName)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? entry.DisplayName : displayName;
    }

    public SoundLibraryEntry Entry => _entry;

    public string RuleReference => _entry.RuleReference;

    public string FullPath => _entry.FullPath;

    public string DisplayName { get; }

    public string FileName => Path.GetFileName(_entry.FullPath);

    public bool IsManaged => _entry.Source == SoundFileSource.Managed;

    public bool IsUsable => _entry.IsUsable;

    public bool NeedsAttention => !_entry.IsUsable;

    public string SourceText => SoundUiText.Source(_entry.Source);

    public string StatusText => SoundUiText.Status(_entry.Status);

    public string DurationText => SoundUiText.Duration(_entry.Duration);

    public string Detail => string.IsNullOrEmpty(DurationText)
        ? $"{SourceText} · {StatusText}"
        : $"{SourceText} · {StatusText} · {DurationText}";

    /// <summary>Checkbox: is this file in the default rule's list.</summary>
    public SoundChoiceItem? DefaultChoice
    {
        get => _defaultChoice;
        internal set => SetProperty(ref _defaultChoice, value);
    }

    internal void UpdateEntry(SoundLibraryEntry entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(IsUsable));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Detail));
    }
}
