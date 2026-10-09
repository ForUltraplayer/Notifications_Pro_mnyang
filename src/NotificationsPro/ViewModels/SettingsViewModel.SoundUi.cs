// @file SettingsViewModel.SoundUi.cs
// @author: rudals252
// @brief 소리 탭(기본 규칙 편집, 음성 보관함, 재생 정책, 조정기 진단 줄)과 앱 탭(앱별 규칙: 상속/무음/전용 목록, AUMID 키 선택, 수신 경로·원래 소리 상태 표시)의 ViewModel 부분. 기본 규칙·정책·표시 이름은 기존 저장(undo/redo) 흐름으로, 앱별 규칙은 기존 앱별 재정의처럼 즉시 저장한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.ViewModels;

/// <summary>
/// KR-Mnyang fork (P2-B) sound UI.
/// <list type="bullet">
/// <item>The default rule, playback policy, <see cref="AudioDispatchMode"/> and sound display names are
/// view-model state saved through the existing debounced <c>SaveSettings</c> (so undo/redo, profiles and
/// themes keep working): <see cref="ApplySoundUiStateTo"/> writes them into every snapshot and
/// <see cref="LoadSoundUiFromSettings"/> reloads them whenever the view model reloads.</item>
/// <item>App rules are written immediately, like the existing per-app overrides.</item>
/// <item>Library actions that change rules (remove from lists, delete file, remove folder) first flush
/// pending edits, then apply, then reload.</item>
/// <item>The library is scanned when the Sounds/Apps tab is entered and on Refresh (no file watching).</item>
/// <item>The status line shows coordinator state and counters plus the last started file's display
/// name; never app names or notification text.</item>
/// </list>
/// </summary>
public partial class SettingsViewModel
{
    public static IReadOnlyList<OptionItem<AudioDispatchMode>> AudioDispatchModeOptions { get; } = new[]
    {
        new OptionItem<AudioDispatchMode>(AudioDispatchMode.OverlayAccepted, "Overlay accepted — only notifications the overlay shows"),
        new OptionItem<AudioDispatchMode>(AudioDispatchMode.PolicyAccepted, "Policy accepted — every notification that passes filters"),
    };

    public static IReadOnlyList<OptionItem<PlaybackOverflowBehavior>> OverflowBehaviorOptions { get; } = new[]
    {
        new OptionItem<PlaybackOverflowBehavior>(PlaybackOverflowBehavior.Queue, SoundUiText.Overflow(PlaybackOverflowBehavior.Queue)),
        new OptionItem<PlaybackOverflowBehavior>(PlaybackOverflowBehavior.Skip, SoundUiText.Overflow(PlaybackOverflowBehavior.Skip)),
    };

    private SoundLibraryService? _soundLibrary;
    private PlaybackCoordinator? _playbackCoordinator;
    private List<SoundLibraryEntry> _libraryEntries = new();
    private Dictionary<string, string> _soundDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private int _libraryRefreshGeneration;

    private bool _soundUiLoaded;
    private bool _suppressSoundUiSave;
    private bool _defaultRuleEnabled = true;
    private bool _defaultRuleExplicitSilence;
    private AudioDispatchMode _audioDispatchMode = AudioDispatchMode.PolicyAccepted;
    private double _minIntervalPerAppSeconds = 4;
    private int _maxQueuedSounds = 5;
    private double _maxQueueWaitSeconds = 30;
    private PlaybackOverflowBehavior _overflowBehavior = PlaybackOverflowBehavior.Queue;
    private bool _showWindowsSounds;
    private bool _isSoundsTabSelected;
    private bool _isAppsTabSelected;
    private string _soundLibraryMessage = string.Empty;
    private string _playbackStatusText = "Playback: unavailable";

    public SoundRuleEditorViewModel DefaultRuleEditor { get; } = new();

    public ObservableCollection<SoundLibraryItemViewModel> SoundLibraryItems { get; } = new();

    public ObservableCollection<SoundFolderItemViewModel> SoundLibraryFolderItems { get; } = new();

    public ICommand OpenManagedSoundFolderCommand { get; private set; } = null!;
    public ICommand AddSoundFolderCommand { get; private set; } = null!;
    public ICommand RemoveSoundFolderCommand { get; private set; } = null!;
    public ICommand RefreshSoundLibraryCommand { get; private set; } = null!;
    public ICommand ImportSoundFilesCommand { get; private set; } = null!;
    public ICommand PreviewSoundCommand { get; private set; } = null!;
    public ICommand StopSoundPreviewCommand { get; private set; } = null!;
    public ICommand RemoveSoundFromListsCommand { get; private set; } = null!;
    public ICommand DeleteSoundFileCommand { get; private set; } = null!;
    public ICommand PreviewDefaultSoundCommand { get; private set; } = null!;
    public ICommand StopAllPlaybackCommand { get; private set; } = null!;

    // ---- test hooks (dialogs and shell) ----

    internal Func<string?> PickSoundFolderAction { get; set; } = DefaultPickSoundFolder;
    internal Func<string[]?> PickSoundFilesAction { get; set; } = DefaultPickSoundFiles;
    internal Func<string, string, bool> ConfirmSoundAction { get; set; } = DefaultConfirmSoundAction;
    internal Action<string> OpenFolderAction { get; set; } = DefaultOpenFolder;

    // ---- default rule ----

    public bool DefaultRuleEnabled
    {
        get => _defaultRuleEnabled;
        set
        {
            if (SetProperty(ref _defaultRuleEnabled, value))
                OnSoundUiEdited(defaultRuleChanged: true);
        }
    }

    public bool DefaultRuleExplicitSilence
    {
        get => _defaultRuleExplicitSilence;
        set
        {
            if (SetProperty(ref _defaultRuleExplicitSilence, value))
                OnSoundUiEdited(defaultRuleChanged: true);
        }
    }

    // ---- playback policy ----

    public AudioDispatchMode SoundAudioDispatchMode
    {
        get => _audioDispatchMode;
        set
        {
            if (Enum.IsDefined(value) && SetProperty(ref _audioDispatchMode, value))
                OnSoundUiEdited(defaultRuleChanged: false);
        }
    }

    /// <summary>0 = off.</summary>
    public double MinIntervalPerAppSeconds
    {
        get => _minIntervalPerAppSeconds;
        set
        {
            var normalized = double.IsNaN(value) ? 0 : Math.Clamp(Math.Round(value), 0, 3600);
            if (!SetProperty(ref _minIntervalPerAppSeconds, normalized))
                return;

            OnPropertyChanged(nameof(MinIntervalPerAppText));
            OnPropertyChanged(nameof(MinIntervalSliderMaximum));
            OnSoundUiEdited(defaultRuleChanged: false);
        }
    }

    public string MinIntervalPerAppText => _minIntervalPerAppSeconds <= 0 ? "Off" : $"{_minIntervalPerAppSeconds:0} s";

    public double MinIntervalSliderMaximum => Math.Max(60, _minIntervalPerAppSeconds);

    public int MaxQueuedSounds
    {
        get => _maxQueuedSounds;
        set
        {
            if (!SetProperty(ref _maxQueuedSounds, Math.Clamp(value, 0, 100)))
                return;

            OnPropertyChanged(nameof(MaxQueuedSliderMaximum));
            OnSoundUiEdited(defaultRuleChanged: false);
        }
    }

    public double MaxQueuedSliderMaximum => Math.Max(20, _maxQueuedSounds);

    public double MaxQueueWaitSeconds
    {
        get => _maxQueueWaitSeconds;
        set
        {
            var normalized = double.IsNaN(value) ? 30 : Math.Clamp(Math.Round(value), 0, 3600);
            if (!SetProperty(ref _maxQueueWaitSeconds, normalized))
                return;

            OnPropertyChanged(nameof(MaxQueueWaitSliderMaximum));
            OnSoundUiEdited(defaultRuleChanged: false);
        }
    }

    public double MaxQueueWaitSliderMaximum => Math.Max(120, _maxQueueWaitSeconds);

    public PlaybackOverflowBehavior SoundOverflowBehavior
    {
        get => _overflowBehavior;
        set
        {
            if (Enum.IsDefined(value) && SetProperty(ref _overflowBehavior, value))
                OnSoundUiEdited(defaultRuleChanged: false);
        }
    }

    // ---- library ----

    /// <summary>Session-only: include trusted Windows sounds in the library list.</summary>
    public bool ShowWindowsSounds
    {
        get => _showWindowsSounds;
        set
        {
            if (SetProperty(ref _showWindowsSounds, value))
                RefreshSoundLibrary();
        }
    }

    public string ManagedSoundFolderPath => _soundLibrary?.ManagedRoot ?? ManagedAssetPathHelper.GetRoot(ManagedAssetPathHelper.SoundsFolderName);

    public string SoundLibraryMessage
    {
        get => _soundLibraryMessage;
        private set
        {
            if (SetProperty(ref _soundLibraryMessage, value ?? string.Empty))
                OnPropertyChanged(nameof(HasSoundLibraryMessage));
        }
    }

    public bool HasSoundLibraryMessage => !string.IsNullOrEmpty(_soundLibraryMessage);

    public bool HasNoSoundLibraryItems => SoundLibraryItems.Count == 0;

    /// <summary>One status line for the Sounds tab (exposed to UI Automation).</summary>
    public string PlaybackStatusText
    {
        get => _playbackStatusText;
        private set => SetProperty(ref _playbackStatusText, value ?? string.Empty);
    }

    /// <summary>Bound one-way-to-source from the Sounds tab: entering the tab rescans the library.</summary>
    public bool IsSoundsTabSelected
    {
        get => _isSoundsTabSelected;
        set
        {
            if (SetProperty(ref _isSoundsTabSelected, value) && value)
            {
                RefreshSoundLibrary();
                UpdatePlaybackStatus();
            }
        }
    }

    /// <summary>Bound one-way-to-source from the Apps tab: entering the tab rescans and reloads app rules.</summary>
    public bool IsAppsTabSelected
    {
        get => _isAppsTabSelected;
        set
        {
            if (SetProperty(ref _isAppsTabSelected, value) && value)
            {
                RefreshSoundLibrary();
                RefreshPerAppSoundRules();
            }
        }
    }

    // ---- wiring ----

    private void InitializeSoundUi()
    {
        DefaultRuleEditor.Changed += () => OnSoundUiEdited(defaultRuleChanged: true);
        OpenManagedSoundFolderCommand = new RelayCommand(_ => OpenManagedSoundFolder());
        AddSoundFolderCommand = new RelayCommand(_ => AddSoundFolder());
        RemoveSoundFolderCommand = new RelayCommand(parameter => RemoveSoundFolder(parameter as SoundFolderItemViewModel));
        RefreshSoundLibraryCommand = new RelayCommand(_ => RefreshSoundLibrary());
        ImportSoundFilesCommand = new RelayCommand(_ => ImportSoundFiles());
        PreviewSoundCommand = new RelayCommand(PreviewSound);
        StopSoundPreviewCommand = new RelayCommand(_ => _playbackCoordinator?.StopPreview());
        RemoveSoundFromListsCommand = new RelayCommand(parameter => RemoveSoundFromLists(ReferenceOf(parameter)));
        DeleteSoundFileCommand = new RelayCommand(parameter => DeleteSoundFile(parameter as SoundLibraryItemViewModel));
        PreviewDefaultSoundCommand = new RelayCommand(_ => PreviewDefaultSound());
        StopAllPlaybackCommand = new RelayCommand(_ => _playbackCoordinator?.StopAll());
    }

    /// <summary>Connects the shared playback coordinator and sound library (called by the app; repeatable).</summary>
    public void ConfigureSoundPlayback(SoundLibraryService? library, PlaybackCoordinator? coordinator)
    {
        if (_playbackCoordinator != null)
            _playbackCoordinator.StateChanged -= OnPlaybackStateChanged;

        _soundLibrary = library;
        _playbackCoordinator = coordinator;
        if (_playbackCoordinator != null)
            _playbackCoordinator.StateChanged += OnPlaybackStateChanged;

        OnPropertyChanged(nameof(ManagedSoundFolderPath));
        UpdatePlaybackStatus();
    }

    private void OnPlaybackStateChanged() => UpdatePlaybackStatus();

    // ---- load / save ----

    /// <summary>Reloads the view-model sound state from <paramref name="settings"/> (no save).</summary>
    private void LoadSoundUiFromSettings(AppSettings settings)
    {
        var previous = _suppressSoundUiSave;
        _suppressSoundUiSave = true;
        try
        {
            _soundDisplayNames = AppSettings.CloneSoundDisplayNames(settings.SoundDisplayNames);
            var rule = settings.DefaultSoundRule ?? new SoundRule();
            _defaultRuleEnabled = rule.Enabled;
            _defaultRuleExplicitSilence = rule.ExplicitSilence;
            _audioDispatchMode = Enum.IsDefined(settings.AudioDispatchMode) ? settings.AudioDispatchMode : AudioDispatchMode.OverlayAccepted;
            var policy = (settings.PlaybackPolicy ?? new PlaybackPolicy()).Clone();
            policy.Normalize();
            _minIntervalPerAppSeconds = policy.MinIntervalPerAppSeconds;
            _maxQueuedSounds = policy.MaxQueued;
            _maxQueueWaitSeconds = policy.MaxQueueWaitSeconds;
            _overflowBehavior = policy.OverflowBehavior;

            RebuildLibraryItemsFromEntries();
            DefaultRuleEditor.Load(rule, SoundLibraryItems, DescribeRuleFile);
            LinkDefaultChoices();
            _soundUiLoaded = true;
        }
        finally
        {
            _suppressSoundUiSave = previous;
        }

        foreach (var name in new[]
        {
            nameof(DefaultRuleEnabled), nameof(DefaultRuleExplicitSilence), nameof(SoundAudioDispatchMode),
            nameof(MinIntervalPerAppSeconds), nameof(MinIntervalPerAppText), nameof(MinIntervalSliderMaximum),
            nameof(MaxQueuedSounds), nameof(MaxQueuedSliderMaximum), nameof(MaxQueueWaitSeconds),
            nameof(MaxQueueWaitSliderMaximum), nameof(SoundOverflowBehavior),
        })
        {
            OnPropertyChanged(name);
        }

        RefreshPerAppSoundRules();
    }

    /// <summary>Writes the view-model sound state into a snapshot built by <c>BuildCurrentSettingsSnapshot</c>.</summary>
    private void ApplySoundUiStateTo(AppSettings snapshot)
    {
        if (!_soundUiLoaded)
            return;

        var rule = (snapshot.DefaultSoundRule ?? new SoundRule()).Clone();
        rule.Enabled = _defaultRuleEnabled;
        rule.ExplicitSilence = _defaultRuleExplicitSilence;
        DefaultRuleEditor.ApplyTo(rule);
        snapshot.DefaultSoundRule = rule;
        snapshot.AudioDispatchMode = _audioDispatchMode;
        snapshot.PlaybackPolicy = BuildPlaybackPolicy(snapshot.PlaybackPolicy);
        snapshot.SoundDisplayNames = AppSettings.CloneSoundDisplayNames(_soundDisplayNames);
    }

    private PlaybackPolicy BuildPlaybackPolicy(PlaybackPolicy? basis)
    {
        var policy = (basis ?? new PlaybackPolicy()).Clone();
        policy.MinIntervalPerAppSeconds = _minIntervalPerAppSeconds;
        policy.MaxQueued = _maxQueuedSounds;
        policy.MaxQueueWaitSeconds = _maxQueueWaitSeconds;
        policy.OverflowBehavior = _overflowBehavior;
        policy.Normalize();
        return policy;
    }

    private SoundRule BuildDefaultRuleFromEditor()
    {
        var rule = new SoundRule { Enabled = _defaultRuleEnabled, ExplicitSilence = _defaultRuleExplicitSilence };
        DefaultRuleEditor.ApplyTo(rule);
        return rule;
    }

    private void OnSoundUiEdited(bool defaultRuleChanged)
    {
        if (_suppressSoundUiSave)
            return;

        QueueSave();
        if (defaultRuleChanged)
            RefreshPerAppSummaries();
    }

    private void FlushPendingSave()
    {
        _saveDebounce.Stop();
        SaveSettings();
    }

    // ---- library ----

    /// <summary>Rescans the managed and registered folders (header check), then verifies with the engine in the background.</summary>
    public void RefreshSoundLibrary()
    {
        if (_soundLibrary == null)
            return;

        var generation = ++_libraryRefreshGeneration;
        SoundLibrarySnapshot snapshot;
        try
        {
            snapshot = _soundLibrary.Refresh(includeWindowsSounds: _showWindowsSounds);
        }
        catch (Exception ex)
        {
            SoundLibraryMessage = $"The sound library could not be read ({ex.GetType().Name}).";
            return;
        }

        _libraryEntries = snapshot.Entries.ToList();
        SoundLibraryFolderItems.Clear();
        foreach (var folder in snapshot.Folders)
            SoundLibraryFolderItems.Add(new SoundFolderItemViewModel(folder));

        RebuildLibraryItemsFromEntries();
        ApplyLibraryToEditors();
        _ = VerifyLibraryWithEngineAsync(generation);
    }

    private void RebuildLibraryItemsFromEntries()
    {
        SoundLibraryItems.Clear();
        foreach (var entry in _libraryEntries)
            SoundLibraryItems.Add(new SoundLibraryItemViewModel(entry, GetSoundDisplayName(entry.RuleReference, entry)));
        OnPropertyChanged(nameof(HasNoSoundLibraryItems));
    }

    private async Task VerifyLibraryWithEngineAsync(int generation)
    {
        var library = _soundLibrary;
        if (library == null)
            return;

        var changed = false;
        try
        {
            foreach (var item in SoundLibraryItems.Where(item => item.IsUsable && item.Entry.Source != SoundFileSource.WindowsSound).ToList())
            {
                var verified = await library.VerifyWithEngineAsync(item.Entry);
                if (generation != _libraryRefreshGeneration)
                    return;

                if (verified == item.Entry)
                    continue;

                var index = _libraryEntries.IndexOf(item.Entry);
                if (index >= 0)
                    _libraryEntries[index] = verified;
                item.UpdateEntry(verified);
                changed = true;
            }
        }
        catch
        {
            // Engine verification is best effort; header status stays visible.
        }

        if (changed && generation == _libraryRefreshGeneration)
            ApplyLibraryToEditors();
    }

    private void ApplyLibraryToEditors()
    {
        DefaultRuleEditor.UpdateLibrary(SoundLibraryItems, DescribeRuleFile);
        LinkDefaultChoices();
        foreach (var entry in PerAppConfigEntries)
            entry.SoundRule?.Editor.UpdateLibrary(SoundLibraryItems, DescribeRuleFile);
        RefreshPerAppSummaries();
    }

    private void LinkDefaultChoices()
    {
        foreach (var item in SoundLibraryItems)
        {
            item.DefaultChoice = DefaultRuleEditor.Choices.FirstOrDefault(choice =>
                string.Equals(choice.RuleReference, item.RuleReference, StringComparison.OrdinalIgnoreCase));
        }
    }

    private SoundRuleFileItem DescribeRuleFile(string reference)
    {
        var entry = _libraryEntries.FirstOrDefault(candidate =>
            string.Equals(candidate.RuleReference, reference, StringComparison.OrdinalIgnoreCase));
        if (entry == null && _soundLibrary != null)
        {
            try { entry = _soundLibrary.CheckRuleFile(reference); } catch { entry = null; }
        }

        var displayName = GetSoundDisplayName(reference, entry);
        if (entry == null)
            return new SoundRuleFileItem(reference, displayName, true, string.Empty);

        return new SoundRuleFileItem(reference, displayName, entry.IsUsable, entry.IsUsable ? string.Empty : SoundUiText.Status(entry.Status));
    }

    private string GetSoundDisplayName(string? reference, SoundLibraryEntry? entry)
    {
        if (!string.IsNullOrWhiteSpace(reference) && _soundDisplayNames.TryGetValue(reference, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        if (entry != null && !string.IsNullOrWhiteSpace(entry.DisplayName))
            return entry.DisplayName;

        return Path.GetFileNameWithoutExtension(reference ?? string.Empty);
    }

    private string GetDisplayNameForFullPath(string fullPath)
    {
        var entry = _libraryEntries.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
            return GetSoundDisplayName(entry.RuleReference, entry);

        var managedReference = ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(fullPath, ManagedAssetPathHelper.SoundsFolderName);
        return GetSoundDisplayName(string.IsNullOrEmpty(managedReference) ? fullPath : managedReference, null);
    }

    private void OpenManagedSoundFolder()
    {
        var path = ManagedSoundFolderPath;
        try
        {
            Directory.CreateDirectory(path);
            OpenFolderAction(path);
        }
        catch (Exception ex)
        {
            SoundLibraryMessage = $"The managed folder could not be opened ({ex.GetType().Name}).";
        }
    }

    private void AddSoundFolder()
    {
        if (_soundLibrary == null)
            return;

        var picked = PickSoundFolderAction();
        if (string.IsNullOrWhiteSpace(picked))
            return;

        var result = _soundLibrary.ValidateFolderForRegistration(picked);
        if (!result.Accepted || result.NormalizedPath == null)
        {
            SoundLibraryMessage = $"Folder not added: {SoundUiText.FolderRejection(result.Rejection)}";
            return;
        }

        var current = _settingsManager.Settings;
        if (current.SoundLibraryFolders.Contains(result.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            SoundLibraryMessage = "This folder is already in the library.";
            RefreshSoundLibrary();
            return;
        }

        var updated = current.Clone();
        updated.SoundLibraryFolders.Add(result.NormalizedPath);
        _settingsManager.Apply(updated);
        SoundLibraryMessage = $"Folder added as a reference (files stay where they are): {result.NormalizedPath}";
        RefreshSoundLibrary();
    }

    private void RemoveSoundFolder(SoundFolderItemViewModel? folder)
    {
        if (folder == null || !folder.IsExternal)
            return;

        if (!ConfirmSoundAction(
                "Remove folder reference",
                $"Stop using this folder?\n\n{folder.FolderPath}\n\nIts files are removed from every sound list. Nothing is deleted from disk."))
        {
            return;
        }

        FlushPendingSave();
        var updated = _settingsManager.Settings.Clone();
        updated.SoundLibraryFolders.RemoveAll(path => string.Equals(path, folder.FolderPath, StringComparison.OrdinalIgnoreCase));
        _settingsManager.Apply(updated); // normalization drops rule files that were inside the folder
        LoadSoundUiFromSettings(_settingsManager.Settings);
        SoundLibraryMessage = "Folder reference removed. No files were deleted.";
        RefreshSoundLibrary();
    }

    private void ImportSoundFiles()
    {
        if (_soundLibrary == null)
            return;

        var files = PickSoundFilesAction();
        if (files == null || files.Length == 0)
            return;

        var lines = new List<string>();
        var namesChanged = false;
        foreach (var file in files.Where(file => !string.IsNullOrWhiteSpace(file)))
        {
            var result = _soundLibrary.Import(file);
            if (result.Status == SoundImportStatus.Copied
                && !string.IsNullOrWhiteSpace(result.RuleReference)
                && !string.IsNullOrWhiteSpace(result.DisplayName)
                && !string.Equals(Path.GetFileNameWithoutExtension(result.InternalFileName), result.DisplayName, StringComparison.Ordinal)
                && !_soundDisplayNames.ContainsKey(result.RuleReference))
            {
                // Name collision: the copy got a unique internal name; keep the original name for display.
                _soundDisplayNames[result.RuleReference] = result.DisplayName;
                namesChanged = true;
            }

            lines.Add($"{Path.GetFileName(file)}: {SoundUiText.ImportStatus(result.Status)}");
        }

        if (namesChanged)
            FlushPendingSave();

        SoundLibraryMessage = "Import — " + string.Join("; ", lines);
        RefreshSoundLibrary();
    }

    private static string? ReferenceOf(object? parameter) => parameter switch
    {
        SoundLibraryItemViewModel item => item.RuleReference,
        SoundRuleFileItem file => file.RuleReference,
        SoundChoiceItem choice => choice.RuleReference,
        string reference => reference,
        _ => null,
    };

    private void PreviewSound(object? parameter)
    {
        var reference = ReferenceOf(parameter);
        if (string.IsNullOrWhiteSpace(reference))
            return;

        if (_playbackCoordinator == null)
        {
            SoundLibraryMessage = "Playback is not available.";
            return;
        }

        var outcome = _playbackCoordinator.Preview(reference);
        if (outcome == PlaybackRequestOutcome.SkippedNoUsableFile)
            SoundLibraryMessage = "This file cannot be played (missing, unsupported or not in an allowed folder).";
    }

    private void PreviewDefaultSound()
    {
        if (_playbackCoordinator == null)
        {
            SoundLibraryMessage = "Playback is not available.";
            return;
        }

        var outcome = _playbackCoordinator.PreviewRule(BuildDefaultRuleFromEditor());
        if (outcome == PlaybackRequestOutcome.SkippedNoUsableFile)
            SoundLibraryMessage = "The default list has no usable file to preview.";
    }

    /// <summary>"Remove from lists": takes the file out of every rule. The file on disk is not touched.</summary>
    private void RemoveSoundFromLists(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return;

        FlushPendingSave();
        _settingsManager.Apply(SoundLibraryService.RemoveFromRules(_settingsManager.Settings, reference));
        LoadSoundUiFromSettings(_settingsManager.Settings);
        SoundLibraryMessage = "Removed from all sound lists. The file was not deleted.";
    }

    /// <summary>"Delete file": managed files only, after an explicit confirmation.</summary>
    private void DeleteSoundFile(SoundLibraryItemViewModel? item)
    {
        if (item == null || _soundLibrary == null)
            return;

        if (!item.IsManaged)
        {
            SoundLibraryMessage = SoundUiText.DeleteResult(SoundDeleteResult.NotManagedFile);
            return;
        }

        if (!ConfirmSoundAction(
                "Delete sound file",
                $"Delete \"{item.DisplayName}\" ({item.FileName}) from the managed sound folder?\n\nThe file is removed from disk and from every sound list. This cannot be undone."))
        {
            SoundLibraryMessage = "Delete cancelled. Nothing was changed.";
            return;
        }

        _playbackCoordinator?.StopPreview();
        var result = _soundLibrary.DeleteManagedFile(item.RuleReference, deletePhysicalFile: true);
        if (result is SoundDeleteResult.Deleted or SoundDeleteResult.NotFound)
        {
            FlushPendingSave();
            var updated = SoundLibraryService.RemoveFromRules(_settingsManager.Settings, item.RuleReference);
            updated.SoundDisplayNames.Remove(item.RuleReference);
            _settingsManager.Apply(updated);
            LoadSoundUiFromSettings(_settingsManager.Settings);
        }

        SoundLibraryMessage = SoundUiText.DeleteResult(result);
        RefreshSoundLibrary();
    }

    // ---- status line ----

    internal void UpdatePlaybackStatus()
    {
        if (_playbackCoordinator == null)
        {
            PlaybackStatusText = "Playback: unavailable";
            return;
        }

        try
        {
            var diagnostics = _playbackCoordinator.GetDiagnostics();
            var lastPath = _playbackCoordinator.GetLastStartedSoundPath();
            var lastFile = string.IsNullOrWhiteSpace(lastPath) ? null : GetDisplayNameForFullPath(lastPath);
            PlaybackStatusText = SoundUiText.PlaybackStatus(diagnostics, lastFile);
        }
        catch
        {
            PlaybackStatusText = "Playback: unavailable";
        }
    }

    // ---- Apps tab ----

    private IEnumerable<string> GetAppNamesFromSoundRules(AppSettings settings)
    {
        var registry = _queueManager?.AppIdentities;
        foreach (var key in settings.AppSoundRules.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            yield return registry?.FindDisplayNameForAppId(key) ?? key;
        }
    }

    private AppSoundRuleViewModel CreateAppSoundRule(string appName)
    {
        var viewModel = new AppSoundRuleViewModel(appName, OnAppSoundRuleEdited);
        LoadAppSoundRule(viewModel, _settingsManager.Settings, preferredKey: null);
        return viewModel;
    }

    private void RefreshPerAppSoundRules()
    {
        var settings = _settingsManager.Settings;
        foreach (var entry in PerAppConfigEntries)
        {
            if (entry.SoundRule == null)
                continue;

            var preferred = settings.AppSoundRules.ContainsKey(entry.SoundRule.RuleKey) ? entry.SoundRule.RuleKey : null;
            LoadAppSoundRule(entry.SoundRule, settings, preferred);
            entry.RefreshHasOverrides();
        }

        RefreshPerAppConfigFilters();
    }

    private void RefreshPerAppSummaries()
    {
        foreach (var entry in PerAppConfigEntries)
        {
            if (entry.SoundRule != null)
                UpdateAppSummary(entry.SoundRule);
        }
    }

    private void LoadAppSoundRule(AppSoundRuleViewModel viewModel, AppSettings settings, string? preferredKey)
    {
        var appName = viewModel.AppName;
        var appIds = _queueManager?.AppIdentities.GetAppIds(appName) ?? Array.Empty<string>();

        string key = appName;
        SoundRule? rule = null;
        if (!string.IsNullOrWhiteSpace(preferredKey) && settings.AppSoundRules.TryGetValue(preferredKey, out var preferredRule))
        {
            key = preferredKey;
            rule = preferredRule;
        }
        else
        {
            foreach (var id in appIds)
            {
                if (settings.AppSoundRules.TryGetValue(id, out var idRule))
                {
                    key = id;
                    rule = idRule;
                    break;
                }
            }

            if (rule == null && settings.AppSoundRules.TryGetValue(appName, out var nameRule))
            {
                key = appName;
                rule = nameRule;
            }
        }

        var choice = rule == null
            ? AppSoundRuleChoice.Inherit
            : rule.ExplicitSilence
                ? AppSoundRuleChoice.Silence
                : rule.Enabled ? AppSoundRuleChoice.Custom : AppSoundRuleChoice.Inherit;

        var keyOptions = appIds.Count > 1
            ? new[] { new OptionItem<string>(appName, $"By name — all app IDs ({appName})") }
                .Concat(appIds.Select(id => new OptionItem<string>(id, id)))
                .ToList()
            : new List<OptionItem<string>>();

        viewModel.LoadState(choice, key, keyOptions);
        viewModel.WithoutNotifications(() => viewModel.Editor.Load(rule ?? new SoundRule(), SoundLibraryItems, DescribeRuleFile));

        viewModel.ReceivePathText = appIds.Count > 0
            ? appIds.Count > 1 ? $"{SoundUiText.ReceivePathWinRt}, {appIds.Count} app IDs" : SoundUiText.ReceivePathWinRt
            : _queueManager?.SeenAppNames.Contains(appName) == true
                ? SoundUiText.ReceivePathAccessibility
                : SoundUiText.ReceivePathNotSeen;

        ManualMuteConfirmation? confirmation = null;
        foreach (var candidate in new[] { key, appName }.Concat(appIds))
        {
            if (settings.ManualMuteConfirmations.TryGetValue(candidate, out confirmation))
                break;
        }

        viewModel.OriginalSoundStatusText = SoundUiText.ManualMuteState(confirmation?.State);

        var legacy = new List<string>();
        if (settings.PerAppSounds.TryGetValue(appName, out var legacySound) && !string.IsNullOrWhiteSpace(legacySound))
            legacy.Add($"Original app's per-app sound (not used for playback): {Path.GetFileNameWithoutExtension(legacySound)}");
        if (AppIdentityMatcher.ContainsDisplayName(settings.MutedApps, appName))
            legacy.Add("Notifications from this app are muted in Filtering.");
        viewModel.LegacyInfoText = string.Join(Environment.NewLine, legacy);

        UpdateAppSummary(viewModel);
    }

    private void UpdateAppSummary(AppSoundRuleViewModel viewModel)
    {
        string summary;
        switch (viewModel.Choice)
        {
            case AppSoundRuleChoice.Silence:
                summary = "Silent for this app; never falls back to the default sound.";
                break;

            case AppSoundRuleChoice.Custom:
                var count = viewModel.Editor.Files.Count;
                summary = count == 0
                    ? "Own list is empty, so this app is silent."
                    : $"Own list: {SoundUiText.Mode(viewModel.Editor.Mode)}, {count} file(s).";
                break;

            default:
                var defaultCount = DefaultRuleEditor.Files.Count;
                summary = !_defaultRuleEnabled || _defaultRuleExplicitSilence
                    ? "Uses the default sound, which is currently silent."
                    : defaultCount == 0
                        ? "Uses the default sound, which has no files (silent)."
                        : $"Uses the default sound: {SoundUiText.Mode(DefaultRuleEditor.Mode)}, {defaultCount} file(s).";
                break;
        }

        if (!SoundEnabled)
            summary += " Notification sounds are turned off in the Sounds tab.";

        viewModel.SummaryText = summary;
    }

    /// <param name="previousKey">Set when the user moved the rule key to another AUMID / the display name.</param>
    private void OnAppSoundRuleEdited(AppSoundRuleViewModel viewModel, string? previousKey)
    {
        var current = _settingsManager.Settings;
        if (previousKey != null)
        {
            if (current.AppSoundRules.ContainsKey(viewModel.RuleKey))
            {
                // The chosen key already has its own rule: edit that one instead of moving.
                LoadAppSoundRule(viewModel, current, viewModel.RuleKey);
                return;
            }

            if (current.AppSoundRules.TryGetValue(previousKey, out var movedRule))
            {
                var moved = current.Clone();
                moved.AppSoundRules.Remove(previousKey);
                moved.AppSoundRules[viewModel.RuleKey] = movedRule.Clone();
                _settingsManager.Apply(moved);
            }

            UpdateAppSummary(viewModel);
            return;
        }

        var updated = current.Clone();
        switch (viewModel.Choice)
        {
            case AppSoundRuleChoice.Inherit:
                updated.AppSoundRules.Remove(viewModel.RuleKey);
                break;

            case AppSoundRuleChoice.Silence:
                var silent = updated.AppSoundRules.TryGetValue(viewModel.RuleKey, out var existing) ? existing.Clone() : new SoundRule();
                silent.Enabled = true;
                silent.ExplicitSilence = true;
                viewModel.Editor.ApplyTo(silent);
                updated.AppSoundRules[viewModel.RuleKey] = silent;
                break;

            case AppSoundRuleChoice.Custom:
                var custom = new SoundRule { Enabled = true, ExplicitSilence = false };
                viewModel.Editor.ApplyTo(custom);
                updated.AppSoundRules[viewModel.RuleKey] = custom;
                break;
        }

        _settingsManager.Apply(updated);
        UpdateAppSummary(viewModel);
        PerAppConfigEntries.FirstOrDefault(entry => ReferenceEquals(entry.SoundRule, viewModel))?.RefreshHasOverrides();
        RefreshPerAppConfigFilters();
    }

    // ---- default dialogs / shell ----

    private static string? DefaultPickSoundFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a local folder with WAV files",
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string[]? DefaultPickSoundFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav",
            Title = "Import WAV files into the managed sound folder",
            Multiselect = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    private static bool DefaultConfirmSoundAction(string title, string message)
    {
        return System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            == MessageBoxResult.Yes;
    }

    private static void DefaultOpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
