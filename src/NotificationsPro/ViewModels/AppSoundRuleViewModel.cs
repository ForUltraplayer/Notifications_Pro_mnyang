// @file AppSoundRuleViewModel.cs
// @author: rudals252
// @brief 앱 탭 한 앱의 소리 규칙 편집 상태(기본값 상속 / 이 앱 무음 / 이 앱 전용 목록, 규칙 키 AUMID 선택)와 정보(수신 경로, 기존 이름 규칙 표시, 요약), 원래 소리 처리 섹션(앱 유형별 수동 음소거 안내, 사용자 확인 완료 / 다시 확인 필요, P4).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Collections.ObjectModel;
using System.Windows.Input;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

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
        OptionItem<AppSoundRuleChoice>.Localized(AppSoundRuleChoice.Inherit, "Sounds_UseTheDefaultSound"),
        OptionItem<AppSoundRuleChoice>.Localized(AppSoundRuleChoice.Silence, "Apps_ChoiceSilent"),
        OptionItem<AppSoundRuleChoice>.Localized(AppSoundRuleChoice.Custom, "Apps_ChoiceOwnList"),
    };

    private readonly Action<AppSoundRuleViewModel, string?> _onChanged;
    private readonly Action<AppSoundRuleViewModel, bool>? _onManualMuteChanged;
    private OriginalSoundAppKind _originalSoundKind;
    private string _manualMuteState = ManualMuteConfirmation.Unconfirmed;
    private string _manualMuteGuidanceText = string.Empty;
    private AppSoundRuleChoice _choice;
    private string _ruleKey;
    private string _summaryText = string.Empty;
    private string _receivePathText = string.Empty;
    private string _originalSoundStatusText = string.Empty;
    private string _legacyInfoText = string.Empty;
    private bool _loading;

    /// <param name="onChanged">Called after a user edit with (this, previous rule key when the key changed).</param>
    /// <param name="onManualMuteChanged">P4: called with true for "confirmed by me" and false for "needs checking again".</param>
    /// <param name="openWindowsNotificationSettings">P4: opens Windows Settings, System, Notifications.</param>
    public AppSoundRuleViewModel(
        string appName,
        Action<AppSoundRuleViewModel, string?> onChanged,
        Action<AppSoundRuleViewModel, bool>? onManualMuteChanged = null,
        Action? openWindowsNotificationSettings = null)
    {
        AppName = appName ?? string.Empty;
        _ruleKey = AppName;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _onManualMuteChanged = onManualMuteChanged;
        ConfirmManualMuteCommand = new RelayCommand(
            () => _onManualMuteChanged?.Invoke(this, true),
            () => CanConfirmManualMute);
        RequireManualMuteRecheckCommand = new RelayCommand(
            () => _onManualMuteChanged?.Invoke(this, false),
            () => CanRequireManualMuteRecheck);
        OpenWindowsNotificationSettingsCommand = new RelayCommand(() => openWindowsNotificationSettings?.Invoke());
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

    /// <summary>
    /// Original sound handling state text (unconfirmed / setup needed / confirmed by the user / no guidance).
    /// "Confirmed" is what the user reported, never something the app verified (design §5.2).
    /// </summary>
    public string OriginalSoundStatusText
    {
        get => _originalSoundStatusText;
        internal set => SetProperty(ref _originalSoundStatusText, value ?? string.Empty);
    }

    /// <summary>App type picked from this session's AUMIDs (P4).</summary>
    public OriginalSoundAppKind OriginalSoundKind => _originalSoundKind;

    /// <summary>Resolved <see cref="ManualMuteConfirmation"/> state value (P4).</summary>
    public string ManualMuteState => _manualMuteState;

    /// <summary>Where to turn the original sound off for this app type (P4).</summary>
    public string ManualMuteGuidanceText => _manualMuteGuidanceText;

    public bool CanConfirmManualMute => _onManualMuteChanged != null && ManualMuteGuidance.CanConfirm(_originalSoundKind, _manualMuteState);

    public bool CanRequireManualMuteRecheck => _onManualMuteChanged != null && ManualMuteGuidance.CanRequireRecheck(_manualMuteState);

    public bool ShowWindowsNotificationSettingsButton => ManualMuteGuidance.UsesWindowsToggle(_originalSoundKind);

    public ICommand ConfirmManualMuteCommand { get; }

    public ICommand RequireManualMuteRecheckCommand { get; }

    public ICommand OpenWindowsNotificationSettingsCommand { get; }

    /// <summary>Sets the original sound section from settings (no user edit is reported).</summary>
    internal void LoadManualMute(OriginalSoundAppKind kind, string state, string statusText, string guidanceText)
    {
        _originalSoundKind = kind;
        _manualMuteState = string.IsNullOrEmpty(state) ? ManualMuteConfirmation.Unconfirmed : state;
        _manualMuteGuidanceText = guidanceText ?? string.Empty;
        OriginalSoundStatusText = statusText;
        OnPropertyChanged(nameof(OriginalSoundKind));
        OnPropertyChanged(nameof(ManualMuteState));
        OnPropertyChanged(nameof(ManualMuteGuidanceText));
        OnPropertyChanged(nameof(CanConfirmManualMute));
        OnPropertyChanged(nameof(CanRequireManualMuteRecheck));
        OnPropertyChanged(nameof(ShowWindowsNotificationSettingsButton));
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
