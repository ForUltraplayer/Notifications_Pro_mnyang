// @file App.Resident.cs
// @author: rudals252
// @brief P4 상주 처리: 두 번째 실행의 명령줄 인자 전달(이름 있는 파이프)·설정창 요청, 주 인스턴스의 인자 수신·시작 완료 전 대기열, 실행 표식(비정상 종료 안내) 시작·정리, 로그오프/Windows 종료 시 정리, 이전 비정상 종료 풍선, 시작 시 설정창 표시 판정.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Diagnostics;
using System.Windows;
using NotificationsPro.Helpers;
using NotificationsPro.Localization;
using NotificationsPro.Models;
using NotificationsPro.Services;
using WinForms = System.Windows.Forms;

namespace NotificationsPro;

public partial class App
{
    /// <summary>Exit code of a second launch whose arguments could not be delivered.</summary>
    internal const int ForwardFailedExitCode = 1;

    private RunMarker? _runMarker;
    private RunMarkerStartResult? _previousRunResult;
    private CliArgumentServer? _cliArgumentServer;
    private bool _cliStartupCompleted;
    private readonly List<IReadOnlyList<string>> _pendingForwardedArguments = new();

    private static string CurrentCliPipeName => ForkIdentity.GetCliPipeName(Process.GetCurrentProcess().SessionId);

    /// <summary>
    /// Second launch (K12, design §9.2): known CLI commands are forwarded once to the running instance;
    /// no arguments (or unknown ones only) ask it to show the settings window as in P1-A. The run marker,
    /// settings and listener are never touched here.
    /// </summary>
    private static int RunSecondaryInstance(string[] args)
    {
        if (CliArguments.ShouldForward(args))
            return CliArgumentClient.TrySend(CurrentCliPipeName, args) ? 0 : ForwardFailedExitCode;

        SingleInstanceGuard.SignalExistingInstance(ForkIdentity.ShowSettingsEventName);
        return 0;
    }

    /// <summary>Primary instance only, right after the single-instance check.</summary>
    private void StartCliArgumentServer()
    {
        _cliArgumentServer = new CliArgumentServer(
            CurrentCliPipeName,
            args => Dispatcher.BeginInvoke(new Action(() => OnForwardedArguments(args))));
        _cliArgumentServer.Start();
    }

    /// <summary>Forwarded commands wait until start-up (and its own arguments) finished, then run once each.</summary>
    private void OnForwardedArguments(IReadOnlyList<string> args)
    {
        if (!_cliStartupCompleted)
        {
            _pendingForwardedArguments.Add(args);
            return;
        }

        ProcessCliArguments(args);
    }

    private void CompleteCliStartup()
    {
        _cliStartupCompleted = true;
        var pending = _pendingForwardedArguments.ToList();
        _pendingForwardedArguments.Clear();
        foreach (var args in pending)
            ProcessCliArguments(args);
    }

    /// <summary>Primary instance only (after the single-instance check): records this run and reads the previous one.</summary>
    private void BeginRunMarker()
    {
        _runMarker = new RunMarker(ForkIdentity.RunMarkerPath);
        _previousRunResult = RunMarker.BeginForPrimaryInstance(isPrimaryInstance: true, _runMarker, GetCurrentVersionString());
        SessionEnding += OnSessionEnding;
    }

    /// <summary>Normal quit, sign-out and Windows shutdown all clear the marker of this run.</summary>
    private void EndRunMarker()
    {
        _runMarker?.EndSession();
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        // Sign-out / shutdown: the process may be ended before OnExit completes. Save settings that
        // are only held in memory (e.g. overlay position, visibility) and clear the run marker first.
        try { _settingsManager?.Save(); } catch { }
        EndRunMarker();
    }

    private void ShowPreviousRunBalloon()
    {
        if (_trayIcon == null || _previousRunResult?.PreviousRunEndedUncleanly != true)
            return;

        _trayIcon.ShowBalloonTip(
            8000,
            ForkIdentity.ProductDisplayName,
            LocalizationManager.Get("Tray_PreviousRunUncleanBalloon"),
            WinForms.ToolTipIcon.Warning);
    }

    private void ConfigureResidentSettingsState()
    {
        if (_settingsViewModel == null)
            return;

        _settingsViewModel.ConfigureLogonTask(HasPackageIdentity() ? null : LogonTaskService.CreateForCurrentProcess());
        _settingsViewModel.SetPreviousRunStatus(_previousRunResult);
    }

    private void ShowSettingsAtStartupIfNeeded(string[] args)
    {
        if (_settingsManager == null)
            return;

        if (CliArguments.ShouldShowSettingsAtStartup(
                _settingsManager.HadExistingSettingsFile,
                _settingsManager.Settings.StartMinimizedToTray,
                CliArguments.Contains(args, CliCommandKind.StartMinimized)))
        {
            ShowSettings();
        }
    }

    private void DisposeResidentState()
    {
        EndRunMarker();
        _cliArgumentServer?.Dispose();
        _cliArgumentServer = null;
    }
}
