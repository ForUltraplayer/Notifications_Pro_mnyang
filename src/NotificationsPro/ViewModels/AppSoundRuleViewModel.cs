// @file AppSoundRuleViewModel.cs
// @author: rudals252
// @brief 앱 탭 한 앱의 소리 규칙 편집 상태(기본값 상속 / 이 앱 무음 / 이 앱 전용 목록, 규칙 키 AUMID 선택)와 읽기 전용 정보(수신 경로, 원래 소리 처리 상태, 기존 이름 규칙 표시, 요약).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Collections.ObjectModel;

namespace NotificationsPro.ViewModels;

/// <summary>
/// Sound rule state for one app card in the Apps tab. The app name is shown exactly as received.
/// <list type="bullet">
/// <item><see cref="AppSoundRuleChoice.Inherit"/>: no app rule (the default rule applies).</item>
/// <item><see cref="AppSoundRuleChoice.Silence"/>: ExplicitSilence — never falls back to the default.</item>
/// <item><see cref="AppSoundRuleChoice.Custom"/>: the app's own mode and file list (<see cref="Editor"/>).</item>
/// </list>
/// <see cref="RuleKey"/> is the AppSoundRules key being edited: the display name by default, or one
/// AUMID chosen by the user when several AUMIDs were seen for this name. Existing name-based legacy
/// values (PerAppSounds, MutedApps) are only shown, never removed or merged.
/// </summary>
public sealed class AppSoundRuleViewModel : BaseViewModel
{
    public static IReadOnlyList<OptionItem<AppSoundRuleChoice>> ChoiceOptions { get; } = new[]
    {
        new OptionItem<AppSoundRuleChoice>(AppSoundRuleChoice.Inherit, "Use the default sound"),
        new OptionItem<AppSoundRuleChoice>(AppSoundRuleChoice.Silence, "Silent for this app"),
        new OptionItem<AppSoundRuleChoice>(AppSoundRuleChoice.Custom, "This app's own list"),
    };

    private readonly Action<AppSoundRuleViewModel, string?> _onChanged;
    private AppSoundRuleChoice _choice;
    private string _ruleKey;
    private string _summaryText = string.Empty;
    private string _receivePathText = string.Empty;
    private string _originalSoundStatusText = string.Empty;
    private string _legacyInfoText = string.Empty;
    private bool _loading;

    /// <param name="onChanged">Called after a user edit with (this, previous rule key when the key changed).</param>
    public AppSoundRuleViewModel(string appName, Action<AppSoundRuleViewModel, string?> onChanged)
    {
        AppName = appName ?? string.Empty;
        _ruleKey = AppName;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        Editor.Changed += () =>
        {
            if (!_loading)
                _onChanged(this, null);
        };
    }

    public string AppName { get; }

    public SoundRuleEditorViewModel Editor { get; } = new();

    public AppSoundRuleChoice Choice
    {
        get => _choice;
        set
        {
            if (!Enum.IsDefined(value) || !SetProperty(ref _choice, value))
                return;

            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(HasRule));
            if (!_loading)
                _onChanged(this, null);
        }
    }

    public bool IsCustom => _choice == AppSoundRuleChoice.Custom;

    /// <summary>True when this app has its own rule (silence or a custom list).</summary>
    public bool HasRule => _choice != AppSoundRuleChoice.Inherit;

    /// <summary>The AppSoundRules key this card edits (display name or one AUMID).</summary>
    public string RuleKey => _ruleKey;

    /// <summary>Key choices, shown only when several AUMIDs were seen for this display name.</summary>
    public ObservableCollection<OptionItem<string>> RuleKeyOptions { get; } = new();

    public bool HasMultipleAppIds => RuleKeyOptions.Count > 1;

    public string SelectedRuleKey
    {
        get => _ruleKey;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(_ruleKey, value, StringComparison.Ordinal))
                return;

            var previous = _ruleKey;
            _ruleKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RuleKey));
            if (!_loading)
                _onChanged(this, previous);
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        internal set => SetProperty(ref _summaryText, value ?? string.Empty);
    }

    public string ReceivePathText
    {
        get => _receivePathText;
        internal set => SetProperty(ref _receivePathText, value ?? string.Empty);
    }

    /// <summary>Read-only ManualMuteConfirmations state (changing it arrives in P4).</summary>
    public string OriginalSoundStatusText
    {
        get => _originalSoundStatusText;
        internal set => SetProperty(ref _originalSoundStatusText, value ?? string.Empty);
    }

    /// <summary>Legacy name-based values shown for information only (empty when none).</summary>
    public string LegacyInfoText
    {
        get => _legacyInfoText;
        internal set
        {
            if (SetProperty(ref _legacyInfoText, value ?? string.Empty))
                OnPropertyChanged(nameof(HasLegacyInfo));
        }
    }

    public bool HasLegacyInfo => !string.IsNullOrEmpty(_legacyInfoText);

    /// <summary>Sets state from settings without reporting a user edit.</summary>
    internal void LoadState(AppSoundRuleChoice choice, string ruleKey, IEnumerable<OptionItem<string>> keyOptions)
    {
        _loading = true;
        try
        {
            Choice = choice;
            _ruleKey = string.IsNullOrWhiteSpace(ruleKey) ? AppName : ruleKey;
            RuleKeyOptions.Clear();
            foreach (var option in keyOptions ?? Enumerable.Empty<OptionItem<string>>())
                RuleKeyOptions.Add(option);
            OnPropertyChanged(nameof(RuleKey));
            OnPropertyChanged(nameof(SelectedRuleKey));
            OnPropertyChanged(nameof(HasMultipleAppIds));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Runs <paramref name="action"/> (e.g. loading the editor) without reporting user edits.</summary>
    internal void WithoutNotifications(Action action)
    {
        var previous = _loading;
        _loading = true;
        try
        {
            action();
        }
        finally
        {
            _loading = previous;
        }
    }
}
