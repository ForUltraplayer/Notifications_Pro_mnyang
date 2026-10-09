using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Threading;
using NotificationsPro.Helpers;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NotificationsPro.Services;

/// <summary>
/// Captures Windows toast notifications and forwards app/title/body text to QueueManager.
///
/// Strategy:
///   1. Try the WinRT UserNotificationListener API. Polling and the NotificationChanged
///      subscription are independent: if only the subscription fails (unpackaged runs,
///      0x80070490), WinRT stays active with 1-second polling.
///   2. If WinRT access or querying fails, fall back to accessibility mode: SetWinEventHook
///      detects new toast windows, then UI Automation reads the text from them
///
/// Privacy: only notification keys (app id, system uint id, creation time) or transient
/// automation elements are kept. No notification content is stored beyond QueueManager's
/// in-memory queue.
/// </summary>
public class NotificationListener
{
    private readonly QueueManager _queueManager;
    private readonly Dispatcher _dispatcher;
    private readonly SettingsManager _settingsManager;
    private string _configuredCaptureMode;

    // WinRT API
    private UserNotificationListener? _listener;
    private readonly WinRtPollTracker _pollTracker = new();
    private DispatcherTimer? _pollTimer;
    private bool _isPolling;
    private bool _isStartingWinRt;
    private bool _winRtSubscribed;
    private int _winRtSession;
    private int _pollTimeoutCount;
    private WinRtListenerState _winRtState = WinRtListenerState.None;

    // Subscription succeeded: events + backup poll. Subscription failed (unpackaged): poll only.
    internal static readonly TimeSpan EventModeBackupPollInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan PollOnlyInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    private enum WinRtListenerState
    {
        None,
        Events,
        PollingOnly,
        AccessRevoked
    }

    // Accessibility fallback
    private IntPtr _winEventHook;
    private WinEventDelegate? _winEventDelegate; // prevent GC collection
    private DispatcherTimer? _accessibilityStatusTimer;
    private readonly object _accessibilityGate = new();
    private readonly Dictionary<IntPtr, DateTime> _recentAccessibilityCandidates = new();
    private bool _usingAccessibility;
    private int _accessibilityEventCount;
    private int _accessibilityCandidateCount;

    // WinRT auto-retry
    private DispatcherTimer? _winrtRetryTimer;
    private int _winrtRetryCount;

    // Shell host process ID cache (lock-free — only accessed from dispatcher thread)
    // Avoids expensive Process.GetProcessById on every accessibility event
    private readonly Dictionary<uint, (bool IsShellHost, DateTime CachedAt)> _shellHostCache = new();
    private static readonly TimeSpan ShellHostCacheTtl = TimeSpan.FromSeconds(60);

    // Shared state
    private bool _isRunning;
    private int _pollCount;
    private int _capturedCount;

    public bool IsAccessGranted { get; private set; }
    public string ListenerMode => _usingAccessibility
        ? "Accessibility"
        : _winRtState switch
        {
            WinRtListenerState.Events => "WinRT (events)",
            WinRtListenerState.PollingOnly => $"WinRT (polling {PollOnlyInterval.TotalSeconds:0}s)",
            WinRtListenerState.AccessRevoked => "WinRT (access revoked)",
            _ => IsAccessGranted ? "WinRT" : "Initializing"
        };

    /// <summary>NotificationChanged registration result (technical status/HRESULT only).</summary>
    public string SubscriptionState { get; private set; } = "Not attempted";
    public string StatusMessage { get; private set; } = "Not initialized";
    public event Action? StatusChanged;

    // --- Win32 imports for accessibility fallback ---

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private static readonly TimeSpan AccessibilityDebounceWindow = TimeSpan.FromMilliseconds(150);
    private static readonly string[] KnownBrowserHostNames =
    {
        "Google Chrome",
        "Chrome",
        "Microsoft Edge",
        "Edge",
        "Mozilla Firefox",
        "Firefox",
        "Brave",
        "Brave Browser",
        "Opera",
        "Vivaldi"
    };

    public NotificationListener(QueueManager queueManager, Dispatcher dispatcher, SettingsManager settingsManager)
    {
        _queueManager = queueManager;
        _dispatcher = dispatcher;
        _settingsManager = settingsManager;
        _configuredCaptureMode = NotificationCaptureModeHelper.NormalizeMode(_settingsManager.Settings.NotificationCaptureMode);
        _settingsManager.SettingsChanged += OnSettingsChanged;
    }

    // ========================
    //  Initialization
    // ========================

    public async Task<bool> InitializeAsync()
    {
        _configuredCaptureMode = NotificationCaptureModeHelper.NormalizeMode(_settingsManager.Settings.NotificationCaptureMode);

        if (string.Equals(_configuredCaptureMode, NotificationCaptureModeHelper.ModeAccessibility, StringComparison.OrdinalIgnoreCase))
        {
            IsAccessGranted = false;
            StatusMessage = "Capture mode is forced to accessibility";
            StatusChanged?.Invoke();
            StartAccessibilityCapture("Listening via forced accessibility mode");
            return true;
        }

        // Try the WinRT API first
        try
        {
            _listener = UserNotificationListener.Current;
            var accessStatus = await _listener.RequestAccessAsync();

            if (accessStatus == UserNotificationListenerAccessStatus.Allowed)
            {
                IsAccessGranted = true;
                StatusMessage = "Access granted — starting WinRT listener";
                StatusChanged?.Invoke();
                await StartWinRTListeningAsync();
                return true;
            }

            // Access denied or pending — fall through to accessibility
            StatusMessage = $"WinRT access: {accessStatus} — switching to accessibility mode";
        }
        catch (Exception ex)
        {
            StatusMessage = $"WinRT unavailable ({ex.GetType().Name}) — switching to accessibility mode";
        }

        StatusChanged?.Invoke();

        // Fall back to accessibility-based capture
        StartAccessibilityCapture();

        // Do NOT auto-retry WinRT — auto-upgrading would kill the working
        // accessibility hook. (Unpackaged runs with "Allowed" access no longer
        // land here: they keep WinRT via 1s polling when only the
        // NotificationChanged subscription fails.)
        // Users can manually retry via the tray menu if needed.

        return _usingAccessibility;
    }

    // ========================
    //  WinRT API approach
    // ========================

    private async Task StartWinRTListeningAsync()
    {
        if (_listener == null || _isRunning || _isStartingWinRt) return;
        _isStartingWinRt = true;
        _usingAccessibility = false;
        var session = ++_winRtSession;

        try
        {
            // 1. Seed the baseline first. Notifications that already exist at start/retry are
            //    never forwarded retroactively.
            int seededCount;
            try
            {
                var existing = await GetToastSnapshotAsync();
                if (session != _winRtSession) return;

                _pollTracker.Seed(existing.Select(CreateTrackerKey));
                seededCount = existing.Count;
            }
            catch (Exception ex)
            {
                if (session != _winRtSession) return;
                SwitchToAccessibility($"WinRT seed failed ({DescribeException(ex)})");
                return;
            }

            // 2. Subscribe separately. Unpackaged processes can query notifications but fail to
            //    register NotificationChanged (COMException 0x80070490); that must not disable polling.
            TrySubscribeToNotificationChanged();

            _winRtState = _winRtSubscribed ? WinRtListenerState.Events : WinRtListenerState.PollingOnly;
            _isRunning = true;
            StartPollTimer(_winRtSubscribed ? EventModeBackupPollInterval : PollOnlyInterval);

            StatusMessage = _winRtSubscribed
                ? $"Seeded {seededCount} — events + {EventModeBackupPollInterval.TotalSeconds:0}s backup poll"
                : $"Seeded {seededCount} — event subscription failed ({SubscriptionState}), polling every {PollOnlyInterval.TotalSeconds:0}s";
            StatusChanged?.Invoke();
        }
        finally
        {
            _isStartingWinRt = false;
        }
    }

    private void TrySubscribeToNotificationChanged()
    {
        if (_listener == null) return;

        try
        {
            _listener.NotificationChanged += OnNotificationChanged;
            _winRtSubscribed = true;
            SubscriptionState = "Subscribed";
        }
        catch (Exception ex)
        {
            _winRtSubscribed = false;
            SubscriptionState = $"Failed {DescribeException(ex)}";
        }
    }

    private void UnsubscribeFromNotificationChanged()
    {
        if (_listener != null && _winRtSubscribed)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }

        _winRtSubscribed = false;
    }

    private void StartPollTimer(TimeSpan interval)
    {
        StopPollTimer();
        _pollTimer = new DispatcherTimer { Interval = interval };
        _pollTimer.Tick += OnPollTimerTick;
        _pollTimer.Start();
    }

    private void StopPollTimer()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Tick -= OnPollTimerTick;
            _pollTimer.Stop();
            _pollTimer = null;
        }

        _isPolling = false;
    }

    private async void OnPollTimerTick(object? sender, EventArgs e)
    {
        await PollForNewNotificationsAsync();
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added) return;
        var notificationId = args.UserNotificationId;
        _dispatcher.InvokeAsync(async () => await ProcessNewNotificationAsync(notificationId));
    }

    private async Task PollForNewNotificationsAsync()
    {
        if (_listener == null || !_isRunning || _isPolling) return;
        _isPolling = true;
        var session = _winRtSession;

        try
        {
            // Re-check permission every tick so a revoked permission is never mistaken for
            // "no new notifications" (GetNotificationsAsync may just return an empty list).
            var accessStatus = _listener.GetAccessStatus();
            switch (WinRtPollTracker.EvaluateAccess(accessStatus, _winRtState == WinRtListenerState.AccessRevoked))
            {
                case WinRtAccessDecision.EnterRevoked:
                    EnterAccessRevokedState(accessStatus);
                    return;
                case WinRtAccessDecision.StayRevoked:
                    return;
                case WinRtAccessDecision.Resume:
                    await ResumeAfterAccessRestoredAsync(session);
                    return;
            }

            _pollCount++;
            var snapshotToken = _pollTracker.BeginSnapshot();
            IReadOnlyList<UserNotification> notifications;
            try
            {
                notifications = await GetToastSnapshotAsync();
            }
            catch (TimeoutException)
            {
                // Skip this tick; the next tick polls again.
                _pollTimeoutCount++;
                StatusMessage = $"{ListenerMode} — poll #{_pollCount} timed out after {PollTimeout.TotalSeconds:0}s, skipped ({_pollTimeoutCount} timeouts)";
                StatusChanged?.Invoke();
                return;
            }

            if (session != _winRtSession || !_isRunning) return;

            var keys = new List<WinRtNotificationKey>(notifications.Count);
            foreach (var notification in notifications)
                keys.Add(CreateTrackerKey(notification));

            var newKeys = _pollTracker.ProcessSnapshot(snapshotToken, keys);
            if (newKeys.Count > 0)
            {
                var newKeySet = newKeys.ToHashSet();
                for (var i = 0; i < notifications.Count; i++)
                {
                    if (!newKeySet.Remove(keys[i])) continue;
                    _capturedCount++;
                    ExtractAndForwardWinRT(notifications[i]);
                }
            }

            var subscriptionSuffix = _winRtSubscribed ? string.Empty : $"; events: {SubscriptionState}";
            StatusMessage = $"{ListenerMode} — {_capturedCount} captured, {notifications.Count} in system (poll #{_pollCount}){subscriptionSuffix}";
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            if (session == _winRtSession)
                SwitchToAccessibility($"WinRT poll failed ({DescribeException(ex)})");
        }
        finally
        {
            if (session == _winRtSession)
                _isPolling = false;
        }
    }

    private void EnterAccessRevokedState(UserNotificationListenerAccessStatus accessStatus)
    {
        _winRtState = WinRtListenerState.AccessRevoked;
        IsAccessGranted = false;
        _pollTracker.Reset();
        StatusMessage = $"WinRT access {accessStatus} — revoked or denied, polling stopped. Allow it in Windows Settings > Privacy > Notifications";
        StatusChanged?.Invoke();
    }

    private async Task ResumeAfterAccessRestoredAsync(int session)
    {
        var existing = await GetToastSnapshotAsync();
        if (session != _winRtSession || !_isRunning) return;

        // Re-seed: notifications that arrived while access was revoked are not replayed.
        _pollTracker.Seed(existing.Select(CreateTrackerKey));
        IsAccessGranted = true;
        _winRtState = _winRtSubscribed ? WinRtListenerState.Events : WinRtListenerState.PollingOnly;
        StatusMessage = $"WinRT access allowed again — re-seeded {existing.Count}, polling resumed";
        StatusChanged?.Invoke();
    }

    private async Task ProcessNewNotificationAsync(uint notificationId)
    {
        if (_listener == null || !_isRunning || _winRtState != WinRtListenerState.Events) return;
        var session = _winRtSession;

        try
        {
            var notifications = await GetToastSnapshotAsync();
            if (session != _winRtSession || !_isRunning || _winRtState != WinRtListenerState.Events) return;

            var notification = notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification == null) return;

            // Same key as the poll path, so an event and a poll report are handled once.
            if (!_pollTracker.TryMarkSeen(CreateTrackerKey(notification))) return;

            _capturedCount++;
            ExtractAndForwardWinRT(notification);
        }
        catch { }
    }

    private async Task<IReadOnlyList<UserNotification>> GetToastSnapshotAsync()
    {
        var listener = _listener ?? throw new InvalidOperationException("WinRT listener not initialized");
        var operation = listener.GetNotificationsAsync(NotificationKinds.Toast);
        var task = operation.AsTask();
        var completed = await Task.WhenAny(task, Task.Delay(PollTimeout));
        if (completed != task)
        {
            try { operation.Cancel(); } catch { }
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException("WinRT notification query timed out");
        }

        return await task;
    }

    private static WinRtNotificationKey CreateTrackerKey(UserNotification notification)
    {
        return WinRtPollTracker.CreateKey(TryReadAppUserModelId(notification), notification.Id, notification.CreationTime);
    }

    // Tray balloon tips and some Win32 senders throw E_NOTIMPL (0x80004001) from AppInfo,
    // so each identity field is read separately and never aborts processing.
    private static string? TryReadAppUserModelId(UserNotification notification)
    {
        try { return notification.AppInfo?.AppUserModelId; }
        catch { return null; }
    }

    private static string? TryReadAppDisplayName(UserNotification notification)
    {
        try { return notification.AppInfo?.DisplayInfo?.DisplayName; }
        catch { return null; }
    }

    private static string DescribeException(Exception ex)
    {
        return $"{ex.GetType().Name} 0x{ex.HResult:X8}";
    }

    private void ExtractAndForwardWinRT(UserNotification notification)
    {
        try
        {
            var toastBinding = notification.Notification.Visual.GetBinding(
                KnownNotificationBindings.ToastGeneric);
            if (toastBinding == null) return;

            var textElements = toastBinding.GetTextElements();
            var texts = textElements
                .Select(t => NormalizeText(t.Text))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var displayName = NormalizeText(TryReadAppDisplayName(notification) ?? string.Empty);
            var fields = BuildNotificationFields(texts, displayName, assumeLeadingAppNameWhenUnknown: false);

            // Unidentifiable senders (e.g. tray balloon tips) are forwarded as "Unknown" instead of being dropped.
            var appName = string.IsNullOrWhiteSpace(fields.AppName) ? WinRtPollTracker.UnknownAppId : fields.AppName;
            if (ShouldIgnoreCapturedNotification(appName, fields.Title, fields.Body))
                return;

            // Suppress the Windows toast popup before forwarding to the overlay so the system
            // toast has less chance to flash first. The notification payload has already been
            // captured into memory at this point, so forwarding can still succeed even if
            // Windows removes the toast immediately.
            if (_settingsManager.Settings.SuppressToastPopups && _listener != null)
            {
                try { _listener.RemoveNotification(notification.Id); } catch { }
            }

            // Pass the AUMID (when readable) so per-app rules can match exactly; the display name
            // ("Unknown" when absent) keeps driving the existing name-based rules.
            _queueManager.AddNotification(appName, fields.Title, fields.Body, TryReadAppUserModelId(notification));
        }
        catch { }
    }

    // ========================
    //  Accessibility fallback
    // ========================

    private void StartAccessibilityCapture(string? statusPrefix = null)
    {
        _usingAccessibility = true;
        IsAccessGranted = false;
        _isRunning = true;

        // SetWinEventHook must be called on a thread with a message loop.
        // The WPF dispatcher thread qualifies.
        _winEventDelegate = OnWinEvent;
        _winEventHook = SetWinEventHook(
            EVENT_OBJECT_SHOW, EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _winEventDelegate,
            0, 0, // all processes, all threads
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (_winEventHook != IntPtr.Zero)
        {
            StartAccessibilityStatusTimer();
            UpdateAccessibilityStatus(string.IsNullOrWhiteSpace(statusPrefix)
                ? "Listening via accessibility mode"
                : statusPrefix);
        }
        else
        {
            StatusMessage = string.IsNullOrWhiteSpace(statusPrefix)
                ? "Failed to start accessibility listener"
                : $"{statusPrefix} — failed to start accessibility listener";
            StatusChanged?.Invoke();
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isRunning || hwnd == IntPtr.Zero) return;
        Interlocked.Increment(ref _accessibilityEventCount);

        var classNameBuilder = new StringBuilder(256);
        if (GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity) > 0)
            _lastAccessibilityClassName = classNameBuilder.ToString();

        // Toast hosting windows live under ShellExperienceHost / StartMenuExperienceHost.
        if (!IsShellHostWindow(hwnd)) return;

        // Keep a size-based filter to avoid scanning large host windows.
        if (!GetWindowRect(hwnd, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width > 800 || height > 700 || width < 80 || height < 40) return;

        if (!ShouldProcessAccessibilityCandidate(hwnd)) return;
        Interlocked.Increment(ref _accessibilityCandidateCount);

        // Extract text on a background thread to avoid blocking UI.
        var capturedHwnd = hwnd;
        _ = Task.Run(() => ExtractToastViaAutomation(capturedHwnd));
    }

    private void ExtractToastViaAutomation(IntPtr hwnd)
    {
        try
        {
            // Wait for stacked notifications to render. 300 ms gives enough time for a second
            // simultaneous toast (e.g. Reddit + X arriving together) to appear in the same host
            // window so FindNotificationSplitPanes can separate them in one scan.
            Thread.Sleep(300);

            var element = AutomationElement.FromHandle(hwnd);
            if (element == null) return;

            // When multiple toasts are stacked in the same host window they can appear as
            // Pane/Group/ListItem containers depending on Windows build. Search these
            // container types at several depths so simultaneous notifications (e.g. Reddit + X)
            // are captured as separate cards.
            var splitContainers = FindNotificationSplitContainers(element);
            if (splitContainers.Count >= 2)
            {
                foreach (var container in splitContainers)
                    ExtractAndDispatchFromElement(container);
            }
            else
            {
                ExtractAndDispatchFromElement(element);
            }
        }
        catch
        {
            // Automation element may have been disposed — skip
        }
    }

    /// <summary>
    /// Searches for notification container boundaries at depth 1 then depth 2/3.
    /// Returns the list of split containers when ≥2 found, or an empty list when the
    /// notification appears to be a single item.
    /// </summary>
    private static List<AutomationElement> FindNotificationSplitContainers(AutomationElement root)
    {
        var containerCondition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

        // Depth 1 — most common case
        var depth1 = FilterContainersWithRelevantText(root.FindAll(TreeScope.Children, containerCondition));
        if (depth1.Count >= 2)
            return depth1;

        // Depth 2 — notifications nested inside a single intermediate pane
        if (depth1.Count == 1)
        {
            var depth2 = FilterContainersWithRelevantText(depth1[0].FindAll(TreeScope.Children, containerCondition));
            if (depth2.Count >= 2)
                return depth2;

            // Depth 3 — one more level of nesting (seen on some Windows 11 builds)
            if (depth2.Count == 1)
            {
                var depth3 = FilterContainersWithRelevantText(depth2[0].FindAll(TreeScope.Children, containerCondition));
                if (depth3.Count >= 2)
                    return depth3;
            }
        }

        return new List<AutomationElement>();
    }

    private static List<AutomationElement> FilterContainersWithRelevantText(AutomationElementCollection candidates)
    {
        var filtered = new List<AutomationElement>();
        foreach (AutomationElement candidate in candidates)
        {
            if (HasRelevantTextDescendants(candidate))
                filtered.Add(candidate);
        }
        return filtered;
    }

    private static bool HasRelevantTextDescendants(AutomationElement element)
    {
        try
        {
            var textCondition = new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.Text);
            var textElements = element.FindAll(TreeScope.Descendants, textCondition);
            foreach (AutomationElement textEl in textElements)
            {
                var text = NormalizeText(textEl.Current.Name);
                if (!string.IsNullOrWhiteSpace(text) && !IsIgnoredUiAutomationText(text))
                    return true;
            }
        }
        catch
        {
            // Ignore stale/disposed accessibility elements.
        }

        return false;
    }

    private void ExtractAndDispatchFromElement(AutomationElement element)
    {
        var textCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty, ControlType.Text);
        var textElements = element.FindAll(TreeScope.Descendants, textCondition);

        var texts = new List<string>();
        foreach (AutomationElement textEl in textElements)
        {
            var text = NormalizeText(textEl.Current.Name);
            if (!string.IsNullOrWhiteSpace(text) && !IsIgnoredUiAutomationText(text))
                texts.Add(text);
        }

        if (texts.Count == 0) return;

        // Try to split merged browser notifications from a single accessibility host
        // (e.g., simultaneous Reddit + X toasts from Chrome).
        var splitFields = SplitCombinedBrowserToasts(texts);
        var fieldsToDispatch = new List<(string AppName, string Title, string Body)>();

        if (splitFields.Count >= 2)
        {
            fieldsToDispatch.AddRange(splitFields);
        }
        else
        {
            var distinctTexts = texts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var fields = BuildNotificationFields(distinctTexts, appName: string.Empty, assumeLeadingAppNameWhenUnknown: true);
            if (!string.IsNullOrWhiteSpace(fields.Title) || !string.IsNullOrWhiteSpace(fields.Body))
                fieldsToDispatch.Add(fields);
        }

        if (fieldsToDispatch.Count == 0)
            return;

        _dispatcher.InvokeAsync(() =>
        {
            foreach (var fields in fieldsToDispatch)
            {
                if (ShouldIgnoreCapturedNotification(fields.AppName, fields.Title, fields.Body))
                    continue;

                _queueManager.AddNotification(fields.AppName, fields.Title, fields.Body);
                _capturedCount++;
            }
            UpdateAccessibilityStatus();
        });
    }

    internal static List<(string AppName, string Title, string Body)> SplitCombinedBrowserToasts(IReadOnlyList<string> texts)
    {
        var parts = texts
            .Select(NormalizeText)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !IsIgnoredUiAutomationText(t))
            .ToList();

        if (parts.Count < 4)
            return new();

        var marker = parts[0];
        if (string.IsNullOrWhiteSpace(marker) || !IsKnownBrowserHostName(marker))
            return new();

        var markerIndexes = new List<int>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals(marker, StringComparison.OrdinalIgnoreCase))
                markerIndexes.Add(i);
        }

        if (markerIndexes.Count < 2 || markerIndexes[0] != 0)
            return new();

        // Reject invalid split points that would produce empty segments.
        for (var i = 1; i < markerIndexes.Count; i++)
        {
            if (markerIndexes[i] - markerIndexes[i - 1] < 2)
                return new();
        }

        markerIndexes.Add(parts.Count);

        var results = new List<(string AppName, string Title, string Body)>();
        for (var i = 0; i < markerIndexes.Count - 1; i++)
        {
            var start = markerIndexes[i];
            var end = markerIndexes[i + 1];
            var segment = parts.Skip(start).Take(end - start).ToList();
            if (segment.Count < 2)
                return new();
            if (!segment[0].Equals(marker, StringComparison.OrdinalIgnoreCase))
                return new();

            segment.RemoveAt(0); // remove browser app marker
            if (segment.Count == 0)
                return new();

            var fields = BuildNotificationFields(
                segment,
                appName: string.Empty,
                assumeLeadingAppNameWhenUnknown: true);
            var appName = string.IsNullOrWhiteSpace(fields.AppName) ? marker : fields.AppName;

            if (!string.IsNullOrWhiteSpace(fields.Title) || !string.IsNullOrWhiteSpace(fields.Body))
                results.Add((appName, fields.Title, fields.Body));
        }

        return results.Count >= 2 ? results : new();
    }

    private static bool IsKnownBrowserHostName(string value)
    {
        return KnownBrowserHostNames.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool ShouldIgnoreCapturedNotification(string appName, string title, string body)
    {
        var normalizedAppName = NormalizeText(appName);

        // Both the original Notifications Pro names and this fork's names count as "self".
        if (ForkIdentity.IsSelfAppName(normalizedAppName))
            return true;

        // Tray balloon tips (including this app's own first-run balloon) expose no AppInfo and
        // arrive as unidentified, so match them by their title (the balloon title) instead.
        if (string.IsNullOrWhiteSpace(normalizedAppName)
            || string.Equals(normalizedAppName, WinRtPollTracker.UnknownAppId, StringComparison.OrdinalIgnoreCase))
        {
            return ForkIdentity.IsSelfAppName(NormalizeText(title));
        }

        return false;
    }

    /// <summary>
    /// Checks if the window belongs to a shell host process (toast container).
    /// Uses a lock-free cache to avoid calling Process.GetProcessById on every
    /// accessibility event (hundreds/sec). Safe because OnWinEvent always runs
    /// on the dispatcher thread (WINEVENT_OUTOFCONTEXT).
    /// </summary>
    private bool IsShellHostWindow(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0) return false;

        var now = DateTime.UtcNow;

        // Check cache first (no lock needed — single-threaded access)
        if (_shellHostCache.TryGetValue(processId, out var cached) &&
            now - cached.CachedAt < ShellHostCacheTtl)
        {
            return cached.IsShellHost;
        }

        bool isShellHost;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            isShellHost = process.ProcessName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            isShellHost = false;
        }

        _shellHostCache[processId] = (isShellHost, now);

        // Periodic eviction to prevent unbounded growth
        if (_shellHostCache.Count > 100)
        {
            var staleKeys = _shellHostCache
                .Where(kvp => now - kvp.Value.CachedAt > ShellHostCacheTtl)
                .Select(kvp => kvp.Key).ToList();
            foreach (var key in staleKeys) _shellHostCache.Remove(key);
        }

        return isShellHost;
    }

    private bool ShouldProcessAccessibilityCandidate(IntPtr hwnd)
    {
        var now = DateTime.UtcNow;
        lock (_accessibilityGate)
        {
            if (_recentAccessibilityCandidates.TryGetValue(hwnd, out var lastSeen) &&
                now - lastSeen < AccessibilityDebounceWindow)
            {
                return false;
            }

            _recentAccessibilityCandidates[hwnd] = now;

            if (_recentAccessibilityCandidates.Count > 256)
            {
                var staleCutoff = now - TimeSpan.FromMinutes(1);
                var staleHandles = _recentAccessibilityCandidates
                    .Where(kvp => kvp.Value < staleCutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var staleHandle in staleHandles)
                    _recentAccessibilityCandidates.Remove(staleHandle);
            }
        }

        return true;
    }

    private void StartAccessibilityStatusTimer()
    {
        _accessibilityStatusTimer?.Stop();
        _accessibilityStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _accessibilityStatusTimer.Tick += OnAccessibilityStatusTick;
        _accessibilityStatusTimer.Start();
    }

    private void OnAccessibilityStatusTick(object? sender, EventArgs e)
    {
        if (!_usingAccessibility || !_isRunning) return;
        UpdateAccessibilityStatus();
    }

    private void UpdateAccessibilityStatus(string? prefix = null)
    {
        var classHint = string.Empty;
        if (_lastAccessibilityClassName != null)
            classHint = $", last class {_lastAccessibilityClassName}";

        var suffix = $"{_capturedCount} captured, {_accessibilityCandidateCount} candidates, {_accessibilityEventCount} events{classHint}";
        StatusMessage = string.IsNullOrWhiteSpace(prefix)
            ? $"Accessibility mode — {suffix}"
            : $"{prefix} — {suffix}";
        StatusChanged?.Invoke();
    }

    private string? _lastAccessibilityClassName;

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        return normalized;
    }

    private static bool IsIgnoredUiAutomationText(string text)
    {
        return text.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Close", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Notification", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Notification center", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Clear all notifications", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Do not disturb", StringComparison.OrdinalIgnoreCase);
    }

    private static (string AppName, string Title, string Body) BuildNotificationFields(
        IReadOnlyList<string> texts, string appName, bool assumeLeadingAppNameWhenUnknown)
    {
        var normalizedAppName = NormalizeText(appName);
        var parts = texts
            .Select(NormalizeText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (parts.Count == 0)
            return (normalizedAppName, string.Empty, string.Empty);

        if (!string.IsNullOrWhiteSpace(normalizedAppName)
            && parts.Count > 1
            && parts[0].Equals(normalizedAppName, StringComparison.OrdinalIgnoreCase))
        {
            parts.RemoveAt(0);
        }
        else if (string.IsNullOrWhiteSpace(normalizedAppName)
                 && assumeLeadingAppNameWhenUnknown
                 && parts.Count >= 3)
        {
            normalizedAppName = parts[0];
            parts.RemoveAt(0);
        }

        if (parts.Count == 0)
            return (normalizedAppName, string.Empty, string.Empty);

        var title = parts[0];
        var body = parts.Count > 1
            ? string.Join("\n", parts.Skip(1))
            : string.Empty;

        return (normalizedAppName, title, body);
    }

    // ========================
    //  Shared
    // ========================

    private void OnSettingsChanged()
    {
        var normalizedMode = NotificationCaptureModeHelper.NormalizeMode(_settingsManager.Settings.NotificationCaptureMode);
        if (string.Equals(normalizedMode, _configuredCaptureMode, StringComparison.OrdinalIgnoreCase))
            return;

        _configuredCaptureMode = normalizedMode;
        _dispatcher.InvokeAsync(async () => await RetryAccessAsync());
    }

    private void SwitchToAccessibility(string reason)
    {
        if (_usingAccessibility)
        {
            UpdateAccessibilityStatus(reason);
            return;
        }

        _winRtSession++;
        StopPollTimer();
        UnsubscribeFromNotificationChanged();
        _pollTracker.Reset();
        _winRtState = WinRtListenerState.None;
        _isRunning = false;
        _usingAccessibility = false;
        IsAccessGranted = false;
        StatusMessage = $"{reason} — switching to accessibility mode";
        StatusChanged?.Invoke();
        StartAccessibilityCapture(reason);
    }

    private void StartWinRTRetryTimer()
    {
        _winrtRetryTimer?.Stop();
        _winrtRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _winrtRetryTimer.Tick += async (_, _) => await TryWinRTUpgradeAsync();
        _winrtRetryTimer.Start();
    }

    private async Task TryWinRTUpgradeAsync()
    {
        _winrtRetryCount++;
        try
        {
            var listener = UserNotificationListener.Current;
            var accessStatus = await listener.RequestAccessAsync();

            if (accessStatus == UserNotificationListenerAccessStatus.Allowed)
            {
                // WinRT is now available — switch from accessibility
                _winrtRetryTimer?.Stop();
                _winrtRetryTimer = null;

                // Stop accessibility capture
                if (_winEventHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_winEventHook);
                    _winEventHook = IntPtr.Zero;
                }
                _accessibilityStatusTimer?.Stop();
                _usingAccessibility = false;

                // Start WinRT
                _listener = listener;
                IsAccessGranted = true;
                _isRunning = false; // Reset so StartWinRTListeningAsync proceeds
                StatusMessage = "Upgraded to WinRT listener (access granted on retry)";
                StatusChanged?.Invoke();
                await StartWinRTListeningAsync();
            }
        }
        catch
        {
            // Still unavailable — keep retrying
        }
    }

    public async Task RetryAccessAsync()
    {
        Stop();
        _pollCount = 0;
        _capturedCount = 0;
        _accessibilityCandidateCount = 0;
        _accessibilityEventCount = 0;
        await InitializeAsync();
    }

    public void Stop()
    {
        _isRunning = false;
        _winRtSession++;
        StopPollTimer();
        _winrtRetryTimer?.Stop();
        _winrtRetryTimer = null;

        if (_accessibilityStatusTimer != null)
        {
            _accessibilityStatusTimer.Tick -= OnAccessibilityStatusTick;
            _accessibilityStatusTimer.Stop();
            _accessibilityStatusTimer = null;
        }

        UnsubscribeFromNotificationChanged();

        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        _pollTracker.Reset();
        _winRtState = WinRtListenerState.None;
        _shellHostCache.Clear();
        lock (_accessibilityGate)
        {
            _recentAccessibilityCandidates.Clear();
        }
        _usingAccessibility = false;
    }
}
