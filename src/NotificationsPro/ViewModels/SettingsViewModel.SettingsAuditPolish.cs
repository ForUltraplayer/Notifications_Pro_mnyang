using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using NotificationsPro.Localization;

namespace NotificationsPro.ViewModels;

public partial class SettingsViewModel
{
    private ICollectionView? _perAppConfigEntriesView;
    public ICollectionView PerAppConfigEntriesView => _perAppConfigEntriesView ??= CollectionViewSource.GetDefaultView(PerAppConfigEntries);

    private ICollectionView? _mutedAppEntriesView;
    public ICollectionView MutedAppEntriesView => _mutedAppEntriesView ??= CollectionViewSource.GetDefaultView(MutedAppEntries);

    private string _appOverridesFilterText = string.Empty;
    public string AppOverridesFilterText
    {
        get => _appOverridesFilterText;
        set
        {
            if (!SetProperty(ref _appOverridesFilterText, value))
                return;

            RefreshPerAppConfigFilters();
        }
    }

    private bool _showOnlyModifiedAppOverrides;
    public bool ShowOnlyModifiedAppOverrides
    {
        get => _showOnlyModifiedAppOverrides;
        set
        {
            if (!SetProperty(ref _showOnlyModifiedAppOverrides, value))
                return;

            RefreshPerAppConfigFilters();
        }
    }

    private string _mutedAppsFilterText = string.Empty;
    public string MutedAppsFilterText
    {
        get => _mutedAppsFilterText;
        set
        {
            if (!SetProperty(ref _mutedAppsFilterText, value))
                return;

            RefreshMutedAppFilters();
        }
    }

    private bool _showOnlyMutedApps;
    public bool ShowOnlyMutedApps
    {
        get => _showOnlyMutedApps;
        set
        {
            if (!SetProperty(ref _showOnlyMutedApps, value))
                return;

            RefreshMutedAppFilters();
        }
    }

    public ICommand ClearPerAppOverridesCommand { get; private set; } = null!;
    public ICommand RunCaptureDiagnosticCommand { get; private set; } = null!;

    private void InitializeSettingsAuditPolishCommands()
    {
        ClearPerAppOverridesCommand = new RelayCommand(ClearPerAppOverrides);
        RunCaptureDiagnosticCommand = new RelayCommand(_ => RunCaptureDiagnostic());
    }

    private void InitializeSettingsAuditPolishViews()
    {
        _perAppConfigEntriesView = CollectionViewSource.GetDefaultView(PerAppConfigEntries);
        _perAppConfigEntriesView.Filter = FilterPerAppConfigEntry;

        _mutedAppEntriesView = CollectionViewSource.GetDefaultView(MutedAppEntries);
        _mutedAppEntriesView.Filter = FilterMutedAppEntry;

        RefreshPerAppConfigFilters();
        RefreshMutedAppFilters();
    }

    private bool FilterPerAppConfigEntry(object item)
    {
        if (item is not PerAppConfigEntry entry)
            return false;

        if (ShowOnlyModifiedAppOverrides && !entry.HasOverrides)
            return false;

        if (string.IsNullOrWhiteSpace(AppOverridesFilterText))
            return true;

        return entry.AppName.Contains(AppOverridesFilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterMutedAppEntry(object item)
    {
        if (item is not MutedAppEntry entry)
            return false;

        if (ShowOnlyMutedApps && !entry.IsMuted)
            return false;

        if (string.IsNullOrWhiteSpace(MutedAppsFilterText))
            return true;

        return entry.AppName.Contains(MutedAppsFilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshPerAppConfigFilters()
    {
        _perAppConfigEntriesView?.Refresh();
    }

    private void RefreshMutedAppFilters()
    {
        _mutedAppEntriesView?.Refresh();
    }

    private void ClearPerAppOverrides(object? parameter)
    {
        if (parameter is not PerAppConfigEntry entry)
            return;

        var updated = _settingsManager.Settings.Clone();
        updated.PerAppSounds.Remove(entry.AppName);
        updated.PerAppIcons.Remove(entry.AppName);
        updated.PerAppBackgroundImages.Remove(entry.AppName);
        updated.SpokenMutedApps.RemoveAll(existing => string.Equals(existing, entry.AppName, StringComparison.OrdinalIgnoreCase));
        // KR-Mnyang fork (P2-B): also return the app to the default sound rule (name key and observed AUMIDs).
        updated.AppSoundRules.Remove(entry.AppName);
        foreach (var appId in _queueManager?.AppIdentities.GetAppIds(entry.AppName) ?? Array.Empty<string>())
            updated.AppSoundRules.Remove(appId);
        if (entry.SoundRule != null)
            updated.AppSoundRules.Remove(entry.SoundRule.RuleKey);
        _settingsManager.Apply(updated);

        entry.ApplyDefaults();
        RefreshPerAppConfig();
    }

    private void RunCaptureDiagnostic()
    {
        var settings = _settingsManager.Settings;
        var lines = new[]
        {
            LocalizationManager.Format("Diagnostic_Status", NotificationAccessStatusSummary),
            LocalizationManager.Format("Diagnostic_Detail", NotificationAccessStatusDetail),
            LocalizationManager.Format("Diagnostic_ConfiguredCaptureMode", settings.NotificationCaptureMode),
            LocalizationManager.Format("Diagnostic_LiveListenerMode", _listenerModeForDiagnostic ?? "Unknown"),
            LocalizationManager.Format("Diagnostic_ListenerStatus", _listenerStatusForDiagnostic ?? "Unknown"),
            LocalizationManager.Format("Diagnostic_SeenApps", _queueManager.SeenAppNames.Count),
            LocalizationManager.Format("Diagnostic_VisibleNotifications", _queueManager.VisibleNotifications.Count),
            LocalizationManager.Format("Diagnostic_OverflowCount", _queueManager.OverflowCount),
            LocalizationManager.Format("Diagnostic_SoundPlayback", SafePlaybackDiagnostics())
        };

        System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, lines));

        System.Windows.MessageBox.Show(
            string.Join(Environment.NewLine + Environment.NewLine, lines),
            LocalizationManager.Get("Diagnostic_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private Func<string?>? _playbackDiagnosticsProvider;

    /// <summary>KR-Mnyang fork: supplies the playback coordinator's counter summary (no app names or text).</summary>
    public void ConfigurePlaybackDiagnostics(Func<string?> provider)
    {
        _playbackDiagnosticsProvider = provider;
    }

    private string SafePlaybackDiagnostics()
    {
        try
        {
            return _playbackDiagnosticsProvider?.Invoke() ?? LocalizationManager.Get("Common_Unavailable");
        }
        catch
        {
            return LocalizationManager.Get("Common_Unavailable");
        }
    }

    private string? _listenerModeForDiagnostic;
    private string? _listenerStatusForDiagnostic;

    public void UpdateCaptureDiagnosticSnapshot(string? listenerMode, string? statusMessage)
    {
        _listenerModeForDiagnostic = string.IsNullOrWhiteSpace(listenerMode) ? "Unknown" : listenerMode.Trim();
        _listenerStatusForDiagnostic = string.IsNullOrWhiteSpace(statusMessage) ? "Unknown" : statusMessage.Trim();
    }
}
