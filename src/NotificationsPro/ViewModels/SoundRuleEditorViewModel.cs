// @file SoundRuleEditorViewModel.cs
// @author: rudals252
// @brief 소리 규칙 하나(기본 또는 앱 전용)의 편집 상태: 선택 방식, 연속 중복 방지, 재생 순서가 있는 파일 목록(올리기·내리기·빼기), 보관함 체크 목록. 변경 시 Changed를 알린다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Collections.ObjectModel;
using System.Windows.Input;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.ViewModels;

/// <summary>
/// Edits one <see cref="SoundRule"/>'s mode, repeat avoidance and ordered file list. The play order is
/// the order in <see cref="Files"/> (check order, then Up/Down) — never name or folder order.
/// <see cref="Changed"/> fires after every user edit, never while loading.
/// </summary>
public sealed class SoundRuleEditorViewModel : BaseViewModel
{
    public static IReadOnlyList<OptionItem<SoundSelectionMode>> ModeOptions { get; } = new[]
    {
        new OptionItem<SoundSelectionMode>(SoundSelectionMode.Single, "Single — always the first file"),
        new OptionItem<SoundSelectionMode>(SoundSelectionMode.Random, "Random — any usable file"),
        new OptionItem<SoundSelectionMode>(SoundSelectionMode.Sequential, "Sequential — list order, then wrap"),
    };

    private Func<string, SoundRuleFileItem> _describe = reference => new SoundRuleFileItem(reference, reference, true, string.Empty);
    private SoundSelectionMode _mode = SoundSelectionMode.Single;
    private bool _avoidRepeat;
    private bool _loading;

    public SoundRuleEditorViewModel()
    {
        MoveUpCommand = new RelayCommand(parameter => Move(parameter as SoundRuleFileItem, -1));
        MoveDownCommand = new RelayCommand(parameter => Move(parameter as SoundRuleFileItem, +1));
        RemoveCommand = new RelayCommand(parameter => RemoveItem(parameter as SoundRuleFileItem));
    }

    public event Action? Changed;

    public ObservableCollection<SoundRuleFileItem> Files { get; } = new();

    public ObservableCollection<SoundChoiceItem> Choices { get; } = new();

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand RemoveCommand { get; }

    public SoundSelectionMode Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(value) || !SetProperty(ref _mode, value))
                return;

            OnPropertyChanged(nameof(IsRandom));
            RaiseChanged();
        }
    }

    public bool AvoidRepeat
    {
        get => _avoidRepeat;
        set
        {
            if (SetProperty(ref _avoidRepeat, value))
                RaiseChanged();
        }
    }

    /// <summary>Repeat avoidance only applies to Random.</summary>
    public bool IsRandom => _mode == SoundSelectionMode.Random;

    public bool HasFiles => Files.Count > 0;

    public bool IsEmpty => Files.Count == 0;

    public string ModeLabel => SoundUiText.Mode(_mode);

    /// <summary>Replaces the whole editor state from a stored rule (no <see cref="Changed"/>).</summary>
    public void Load(SoundRule? rule, IEnumerable<SoundLibraryItemViewModel> library, Func<string, SoundRuleFileItem> describe)
    {
        _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        _loading = true;
        try
        {
            var source = rule ?? new SoundRule();
            Mode = Enum.IsDefined(source.Mode) ? source.Mode : SoundSelectionMode.Single;
            AvoidRepeat = source.AvoidRepeat;
            Files.Clear();
            foreach (var reference in (source.Files ?? new List<string>()).Where(file => !string.IsNullOrWhiteSpace(file)))
            {
                if (!Contains(reference))
                    Files.Add(_describe(reference));
            }

            RenumberAndNotify();
            RebuildChoices(library);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Refreshes file descriptions and the checklist after a library rescan (no <see cref="Changed"/>).</summary>
    public void UpdateLibrary(IEnumerable<SoundLibraryItemViewModel> library, Func<string, SoundRuleFileItem> describe)
    {
        _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        var references = Files.Select(item => item.RuleReference).ToList();
        Files.Clear();
        foreach (var reference in references)
            Files.Add(_describe(reference));
        RenumberAndNotify();
        RebuildChoices(library);
    }

    public bool Contains(string reference)
        => Files.Any(item => string.Equals(item.RuleReference, reference, StringComparison.OrdinalIgnoreCase));

    public List<string> GetFiles() => Files.Select(item => item.RuleReference).ToList();

    /// <summary>Writes mode, repeat avoidance and files into <paramref name="rule"/>.</summary>
    public void ApplyTo(SoundRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Mode = _mode;
        rule.AvoidRepeat = _avoidRepeat;
        rule.Files = GetFiles();
    }

    /// <summary>Adds a file at the end of the play order (no-op if present).</summary>
    public void Add(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || Contains(reference))
            return;

        Files.Add(_describe(reference));
        SyncChoice(reference, true);
        RenumberAndNotify();
        RaiseChanged();
    }

    public void Remove(string reference)
    {
        var item = Files.FirstOrDefault(file => string.Equals(file.RuleReference, reference, StringComparison.OrdinalIgnoreCase));
        RemoveItem(item);
    }

    private void RemoveItem(SoundRuleFileItem? item)
    {
        if (item == null || !Files.Remove(item))
            return;

        SyncChoice(item.RuleReference, false);
        RenumberAndNotify();
        RaiseChanged();
    }

    private void Move(SoundRuleFileItem? item, int delta)
    {
        if (item == null)
            return;

        var index = Files.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Files.Count)
            return;

        Files.Move(index, target);
        RenumberAndNotify();
        RaiseChanged();
    }

    private void RebuildChoices(IEnumerable<SoundLibraryItemViewModel> library)
    {
        Choices.Clear();
        foreach (var item in library ?? Enumerable.Empty<SoundLibraryItemViewModel>())
        {
            if (Choices.Any(choice => string.Equals(choice.RuleReference, item.RuleReference, StringComparison.OrdinalIgnoreCase)))
                continue;

            Choices.Add(new SoundChoiceItem(
                item.RuleReference,
                item.DisplayName,
                item.Detail,
                item.IsUsable,
                Contains(item.RuleReference),
                OnChoiceToggled));
        }
    }

    private void OnChoiceToggled(SoundChoiceItem choice, bool selected)
    {
        if (selected)
            Add(choice.RuleReference);
        else
            Remove(choice.RuleReference);
    }

    private void SyncChoice(string reference, bool selected)
    {
        foreach (var choice in Choices.Where(choice => string.Equals(choice.RuleReference, reference, StringComparison.OrdinalIgnoreCase)))
            choice.SetSelectedSilently(selected);
    }

    private void RenumberAndNotify()
    {
        for (var i = 0; i < Files.Count; i++)
            Files[i].Position = i + 1;

        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(ModeLabel));
        if (!_loading)
            Changed?.Invoke();
    }
}
