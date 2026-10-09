// @file SoundRuleFileItem.cs
// @author: rudals252
// @brief 규칙의 선택 파일 목록 한 행(재생 순서 번호, 표시 이름, 사용자 행동이 필요한 상태만 표시).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.ViewModels;

public sealed class SoundRuleFileItem : BaseViewModel
{
    private int _position;

    public SoundRuleFileItem(string ruleReference, string displayName, bool isUsable, string problemText)
    {
        RuleReference = ruleReference;
        DisplayName = displayName;
        IsUsable = isUsable;
        ProblemText = problemText ?? string.Empty;
    }

    public string RuleReference { get; }

    public string DisplayName { get; }

    public bool IsUsable { get; }

    /// <summary>Empty when the file is usable (only states that need user action are shown).</summary>
    public string ProblemText { get; }

    public bool HasProblem => !string.IsNullOrEmpty(ProblemText);

    /// <summary>1-based play order.</summary>
    public int Position
    {
        get => _position;
        internal set => SetProperty(ref _position, value);
    }
}
