// @file SoundChoiceItem.cs
// @author: rudals252
// @brief 규칙 편집기의 보관함 파일 체크 항목. 체크하면 규칙 목록 끝에 추가되고, 해제하면 빠진다(재생 순서는 체크 순서이며 이름 정렬에 의존하지 않는다).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.ViewModels;

public sealed class SoundChoiceItem : BaseViewModel
{
    private readonly Action<SoundChoiceItem, bool> _onToggled;
    private bool _isSelected;

    public SoundChoiceItem(string ruleReference, string displayName, string detail, bool isUsable, bool isSelected, Action<SoundChoiceItem, bool> onToggled)
    {
        RuleReference = ruleReference;
        DisplayName = displayName;
        Detail = detail;
        IsUsable = isUsable;
        _isSelected = isSelected;
        _onToggled = onToggled ?? throw new ArgumentNullException(nameof(onToggled));
    }

    public string RuleReference { get; }

    public string DisplayName { get; }

    public string Detail { get; }

    public bool IsUsable { get; }

    /// <summary>Unusable files can still be unchecked, but not newly added.</summary>
    public bool CanToggle => IsUsable || IsSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
                return;

            OnPropertyChanged(nameof(CanToggle));
            _onToggled(this, value);
        }
    }

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value)
            return;

        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanToggle));
    }
}
