// @file SettingsViewModel.Resident.cs
// @author: rudals252
// @brief P4 상주 관련 시스템 탭 상태(로그온 자동 실행 작업 등록·해제·상태 줄, 트레이로 시작, 이전 실행 비정상 종료 안내 상태 줄)와 앱 탭 원래 소리 수동 음소거 확인 저장. 이 PC 전용 값은 undo/redo·프로필 경로를 거치지 않고 현재 설정에 바로 저장한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using NotificationsPro.Helpers;
using NotificationsPro.Localization;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.ViewModels;

public partial class SettingsViewModel
{
    private LogonTaskService? _logonTaskService;
    private LogonTaskStatus? _logonTaskStatus;
    private RunMarkerStartResult? _previousRunResult;
    private ICommand? _refreshLogonTaskStatusCommand;
    private ICommand? _unregisterLogonTaskCommand;

    /// <summary>Test seam for confirmation timestamps.</summary>
    internal Func<DateTimeOffset> ManualMuteClock { get; set; } = () => DateTimeOffset.Now;

    // ── Logon task (unpackaged) ────────────────────────────────────────────

    /// <summary>
    /// Unpackaged runs use the Task Scheduler logon task; packaged (MSIX) runs keep the original
    /// StartupTask toggle. Pass null for packaged runs.
    /// </summary>
    public void ConfigureLogonTask(LogonTaskService? service)
    {
        _logonTaskService = service;
        OnPropertyChanged(nameof(UsesLogonTask));
        OnPropertyChanged(nameof(UsesPackagedStartupTask));
        RefreshLogonTaskStatus();
    }

    public bool UsesLogonTask => _logonTaskService != null;

    public bool UsesPackagedStartupTask => _logonTaskService == null;

    public LogonTaskStatus? LogonTaskStatus => _logonTaskStatus;

    /// <summary>On only while the task exists and is enabled. Setting it registers or removes the task.</summary>
    public bool LogonTaskEnabled
    {
        get => _logonTaskStatus?.State == LogonTaskState.Registered;
        set
        {
            if (_logonTaskService == null || value == LogonTaskEnabled)
                return;

            SetLogonTaskStatus(value ? _logonTaskService.Register() : _logonTaskService.Unregister());
        }
    }

    public bool CanUnregisterLogonTask => _logonTaskStatus?.IsRegistered == true;

    public string LogonTaskStatusText => FormatLogonTaskStatus(_logonTaskStatus);

    public ICommand RefreshLogonTaskStatusCommand => _refreshLogonTaskStatusCommand ??= new RelayCommand(RefreshLogonTaskStatus);

    public ICommand UnregisterLogonTaskCommand => _unregisterLogonTaskCommand ??= new RelayCommand(
        () =>
        {
            if (_logonTaskService != null)
                SetLogonTaskStatus(_logonTaskService.Unregister());
        },
        () => CanUnregisterLogonTask);

    public void RefreshLogonTaskStatus()
    {
        SetLogonTaskStatus(_logonTaskService?.GetStatus());
    }

    private void SetLogonTaskStatus(LogonTaskStatus? status)
    {
        _logonTaskStatus = status;
        OnPropertyChanged(nameof(LogonTaskStatus));
        OnPropertyChanged(nameof(LogonTaskEnabled));
        OnPropertyChanged(nameof(LogonTaskStatusText));
        OnPropertyChanged(nameof(CanUnregisterLogonTask));
    }

    internal static string FormatLogonTaskStatus(LogonTaskStatus? status)
    {
        if (status == null)
            return string.Empty;

        var text = status.State switch
        {
            LogonTaskState.Registered => LocalizationManager.Get("System_LogonTaskRegistered"),
            LogonTaskState.RegisteredDisabled => LocalizationManager.Get("System_LogonTaskRegisteredDisabled"),
            LogonTaskState.NotRegistered => LocalizationManager.Get("System_LogonTaskNotRegistered"),
            LogonTaskState.PolicyBlocked => LocalizationManager.Get("System_LogonTaskPolicyBlocked"),
            _ => LocalizationManager.Get("System_LogonTaskCheckFailed"),
        };

        if (status.ErrorCode != 0)
            text += " (0x" + status.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ")";

        if (status.IsRegistered && status.PointsToOtherExecutable)
            text += Environment.NewLine + LocalizationManager.Get("System_LogonTaskOtherExecutable");

        return text;
    }

    // ── Start minimized to tray (this PC only) ────────────────────────────

    /// <summary>
    /// Start without the settings window (tray only). Never changes OverlayVisible or OBS capture state.
    /// App-local: saved directly, kept out of undo/redo, profiles and exports.
    /// </summary>
    public bool StartMinimizedToTray
    {
        get => _settingsManager.Settings.StartMinimizedToTray;
        set
        {
            if (_settingsManager.Settings.StartMinimizedToTray == value)
                return;

            _settingsManager.Settings.StartMinimizedToTray = value;
            _settingsManager.Save();
            OnPropertyChanged();
        }
    }

    // ── Previous run (R08) ─────────────────────────────────────────────────

    /// <summary>Called once by the app after the run marker check (null when not checked).</summary>
    public void SetPreviousRunStatus(RunMarkerStartResult? result)
    {
        _previousRunResult = result;
        OnPropertyChanged(nameof(PreviousRunStatusText));
        OnPropertyChanged(nameof(PreviousRunEndedUncleanly));
    }

    public bool PreviousRunEndedUncleanly => _previousRunResult?.PreviousRunEndedUncleanly == true;

    public string PreviousRunStatusText => FormatPreviousRunStatus(_previousRunResult);

    internal static string FormatPreviousRunStatus(RunMarkerStartResult? result)
    {
        if (result == null)
            return string.Empty;

        var lines = new List<string>();
        if (result.PreviousRunEndedUncleanly)
        {
            lines.Add(result.PreviousStartedAt is { } startedAt
                ? LocalizationManager.Format(
                    "System_PreviousRunUncleanDetail",
                    startedAt.ToLocalTime().ToString("g", LocalizationManager.Instance.Culture),
                    string.IsNullOrWhiteSpace(result.PreviousVersion) ? "?" : result.PreviousVersion)
                : LocalizationManager.Get("System_PreviousRunUnclean"));
        }
        else
        {
            lines.Add(LocalizationManager.Get("System_PreviousRunClean"));
        }

        if (result.WriteFailed)
            lines.Add(LocalizationManager.Format("System_RunMarkerWriteFailed", result.ErrorDescription ?? string.Empty));

        return string.Join(Environment.NewLine, lines);
    }

    // ── Original sound: manual mute (R09) ──────────────────────────────────

    private void LoadManualMuteState(AppSoundRuleViewModel viewModel, AppSettings settings, string ruleKey, IReadOnlyCollection<string> appIds)
    {
        // AUMID first, then the display name (also reads older display-name entries, read-only).
        var stored = ManualMuteGuidance.FindStored(settings.ManualMuteConfirmations, viewModel.AppName, appIds, ruleKey, out _);

        var kind = ManualMuteGuidance.DetectKind(viewModel.AppName, appIds);
        var state = ManualMuteGuidance.ResolveState(kind, stored);
        var statusText = SoundUiText.ManualMuteState(state);
        if (state == ManualMuteConfirmation.UserConfirmed && stored?.UpdatedAt is { } updatedAt)
            statusText += " · " + updatedAt.ToLocalTime().ToString("g", LocalizationManager.Instance.Culture);

        viewModel.LoadManualMute(kind, state, statusText, LocalizationManager.Get(ManualMuteGuidance.GuidanceResourceKey(kind)));
    }

    /// <summary>
    /// Stores the user's own report (this PC only; not a verification) under the app's AUMID when one is
    /// known, otherwise under the display name. An older display-name entry is left as it is.
    /// <paramref name="confirmed"/> false = "needs checking again".
    /// </summary>
    internal void OnManualMuteChanged(AppSoundRuleViewModel viewModel, bool confirmed)
    {
        var appIds = _queueManager?.AppIdentities.GetAppIds(viewModel.AppName) ?? Array.Empty<string>();
        var key = ManualMuteGuidance.ResolveStorageKey(viewModel.AppName, appIds, viewModel.RuleKey);
        if (string.IsNullOrWhiteSpace(key))
            return;

        var now = ManualMuteClock();
        var settings = _settingsManager.Settings;
        settings.ManualMuteConfirmations[key] = confirmed
            ? ManualMuteGuidance.Confirm(now)
            : ManualMuteGuidance.RequireRecheck(now);
        _settingsManager.Save();

        // Cards sharing the AUMID (e.g. ChatGPT and Codex) show the same state right away.
        foreach (var entry in PerAppConfigEntries)
        {
            if (entry.SoundRule is { } card)
                LoadManualMuteState(card, settings, card.RuleKey, _queueManager?.AppIdentities.GetAppIds(card.AppName) ?? Array.Empty<string>());
        }

        if (!PerAppConfigEntries.Any(entry => ReferenceEquals(entry.SoundRule, viewModel)))
            LoadManualMuteState(viewModel, settings, viewModel.RuleKey, appIds);
    }

    private void OpenWindowsNotificationSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ManualMuteGuidance.WindowsNotificationSettingsUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                SafeErrorDialogHelper.BuildErrorMessage(
                    LocalizationManager.Get("ManualMute_OpenSettingsFailed"),
                    ex,
                    LocalizationManager.Get("ManualMute_OpenSettingsFailedNextStep")),
                LocalizationManager.Get("ManualMute_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
