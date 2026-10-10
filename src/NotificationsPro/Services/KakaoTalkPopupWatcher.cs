// @file KakaoTalkPopupWatcher.cs
// @author: rudals252
// @brief [실험실] Windows 창 이벤트(EVENT_OBJECT_SHOW)로 카카오톡 PC 메시지 팝업이 뜨는 순간을 감지해 알린다. 카카오톡 파일·설정은 건드리지 않고, 창 텍스트(메시지 내용)는 읽지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NotificationsPro.Helpers;

namespace NotificationsPro.Services;

/// <summary>
/// Labs feature. Must be started on a thread with a message loop (the WPF UI thread): the WinEvent hook is
/// out-of-context and delivered through that loop. Each popup window is reported once.
/// </summary>
public sealed class KakaoTalkPopupWatcher : IDisposable
{
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint GA_ROOT = 2;
    private const uint GW_OWNER = 4;

    // Folder GUIDs Windows uses in Start-menu AppUserModelIds of plain desktop apps.
    private static readonly (string Folder, string Guid)[] KnownFolders =
    {
        (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "{6D809377-6AF0-444B-8957-A3773F02200E}"),
    };

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int idx);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    private readonly HashSet<IntPtr> _reported = new();
    private readonly Dictionary<uint, string> _processNames = new();
    private WinEventProc? _proc;
    private IntPtr _hook;
    private bool _disposed;

    /// <summary>Raised on the UI thread with the KakaoTalk AUMID (null when it could not be derived).</summary>
    public event Action<string?>? PopupDetected;

    /// <summary>Raised when the diagnostic state changes.</summary>
    public event Action? StateChanged;

    public bool IsRunning => _hook != IntPtr.Zero;
    public int DetectedCount { get; private set; }
    public DateTime? LastDetectedAt { get; private set; }
    public string? StartError { get; private set; }

    public void Start()
    {
        if (_disposed || IsRunning)
            return;

        _proc = OnWinEvent;
        _hook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        StartError = _hook == IntPtr.Zero ? $"SetWinEventHook failed (0x{Marshal.GetLastWin32Error():X8})" : null;
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        _reported.Clear();
        _processNames.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>True when KakaoTalk.exe is running for this user session (diagnostics only).</summary>
    public static bool IsKakaoTalkRunning()
    {
        try { return Process.GetProcessesByName(KakaoPopupClassifier.ProcessName).Length > 0; }
        catch { return false; }
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0)
            return;

        if (evt == EVENT_OBJECT_DESTROY)
        {
            _reported.Remove(hwnd);
            return;
        }

        if (evt != EVENT_OBJECT_SHOW || _reported.Contains(hwnd))
            return;

        try
        {
            if (GetAncestor(hwnd, GA_ROOT) != hwnd)
                return;

            GetWindowThreadProcessId(hwnd, out var pid);
            var processName = GetProcessName(pid, out var exePath);
            if (!string.Equals(processName, KakaoPopupClassifier.ProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            var facts = ReadFacts(hwnd, processName);
            if (!KakaoPopupClassifier.IsMessagePopup(facts))
                return;

            _reported.Add(hwnd);
            DetectedCount++;
            LastDetectedAt = DateTime.Now;
            StateChanged?.Invoke();
            PopupDetected?.Invoke(DeriveAppUserModelId(exePath));
        }
        catch
        {
            // A window can disappear while being inspected; ignore it.
        }
    }

    private string GetProcessName(uint pid, out string? exePath)
    {
        exePath = null;
        if (_processNames.TryGetValue(pid, out var cached))
        {
            var sep = cached.IndexOf('|');
            exePath = sep >= 0 && sep + 1 < cached.Length ? cached[(sep + 1)..] : null;
            return sep >= 0 ? cached[..sep] : cached;
        }

        string name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
            if (string.Equals(name, KakaoPopupClassifier.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                try { exePath = process.MainModule?.FileName; } catch { exePath = null; }
            }
        }
        catch
        {
            name = string.Empty;
        }

        if (_processNames.Count > 256)
            _processNames.Clear();
        _processNames[pid] = name + "|" + exePath;
        return name;
    }

    private static KakaoWindowFacts ReadFacts(IntPtr hwnd, string processName)
    {
        var cls = new StringBuilder(128);
        GetClassName(hwnd, cls, cls.Capacity);
        GetWindowRect(hwnd, out var r);
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        return new KakaoWindowFacts(
            processName,
            cls.ToString(),
            GetWindow(hwnd, GW_OWNER) != IntPtr.Zero,
            IsWindowVisible(hwnd),
            GetWindowLong(hwnd, GWL_STYLE),
            GetWindowLong(hwnd, GWL_EXSTYLE),
            r.L, r.T, r.R - r.L, r.B - r.T,
            work.Left, work.Top, work.Right, work.Bottom);
    }

    /// <summary>"{FOLDERID}\relative\path\KakaoTalk.exe" as listed in the Start menu, so "open app" works.</summary>
    internal static string? DeriveAppUserModelId(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        foreach (var (folder, guid) in KnownFolders)
        {
            if (string.IsNullOrEmpty(folder))
                continue;

            var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
            if (exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return guid + "\\" + exePath[root.Length..];
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }
}
