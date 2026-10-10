// @file Program.cs
// @author: rudals252
// @brief KakaoTalk PC popup investigation (read-only): logs top-level windows that KakaoTalk.exe shows or creates
//        (class, size, position, styles, owner) and their UI Automation tree (control type, class, automation id,
//        text LENGTH only). Never logs message text. Runs a message loop for the given number of seconds.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace KakaoProbe;

internal static class Program
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int idx);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    private static readonly HashSet<IntPtr> Reported = new();
    private static StreamWriter? _log;
    private static WinEventProc? _proc;

    [STAThread]
    private static int Main(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 300;
        var outPath = args.Length > 1 ? args[1] : "kakao-probe.jsonl";
        _log = new StreamWriter(outPath, append: true) { AutoFlush = true };
        var kakao = Process.GetProcessesByName("KakaoTalk");
        Log("start", new { seconds, kakao_pids = kakao.Select(p => p.Id).ToArray() });
        if (kakao.Length == 0)
        {
            Log("error", new { message = "KakaoTalk.exe not running" });
            return 2;
        }

        foreach (var w in EnumTopLevel().Where(IsKakao))
            Log("baseline", Describe(w, includeTree: false));

        _proc = OnWinEvent;
        var hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        timer.Tick += (_, _) => System.Windows.Forms.Application.Exit();
        timer.Start();
        System.Windows.Forms.Application.Run();
        UnhookWinEvent(hook);
        Log("end", new { reported = Reported.Count });
        return 0;
    }

    private static void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0)
            return;
        if (evt != EVENT_OBJECT_SHOW && evt != EVENT_OBJECT_CREATE)
            return;
        if (GetAncestor(hwnd, 2) != hwnd) // GA_ROOT: top-level only
            return;
        if (!IsKakao(hwnd))
            return;

        if (evt == EVENT_OBJECT_SHOW)
        {
            var first = Reported.Add(hwnd);
            Thread.Sleep(400); // let the popup lay out its content
            Log(first ? "show" : "show-again", Describe(hwnd, includeTree: true));
        }
        else
        {
            Log("create", Describe(hwnd, includeTree: false));
        }
    }

    private static bool IsKakao(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName.Equals("KakaoTalk", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static object Describe(IntPtr hwnd, bool includeTree)
    {
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, 256);
        GetWindowRect(hwnd, out var r);
        var owner = GetWindow(hwnd, 4); // GW_OWNER
        var ownerCls = new StringBuilder(256);
        if (owner != IntPtr.Zero) GetClassName(owner, ownerCls, 256);
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        return new
        {
            hwnd = $"0x{hwnd.ToInt64():X}",
            cls = cls.ToString(),
            title_len = GetWindowTextLength(hwnd),
            visible = IsWindowVisible(hwnd),
            rect = new { r.L, r.T, w = r.R - r.L, h = r.B - r.T },
            work_area = new { screen.Left, screen.Top, screen.Right, screen.Bottom },
            style = $"0x{GetWindowLong(hwnd, GWL_STYLE):X8}",
            exstyle = $"0x{GetWindowLong(hwnd, GWL_EXSTYLE):X8}",
            owner_cls = ownerCls.ToString(),
            tree = includeTree ? UiaTree(hwnd) : null,
        };
    }

    private static List<object> UiaTree(IntPtr hwnd)
    {
        var nodes = new List<object>();
        try { Walk(AutomationElement.FromHandle(hwnd), 0, nodes); }
        catch (Exception ex) { nodes.Add(new { error = ex.GetType().Name }); }
        return nodes;
    }

    private static void Walk(AutomationElement e, int depth, List<object> nodes)
    {
        if (depth > 8 || nodes.Count > 200)
            return;
        string? valueLen = null;
        try
        {
            if (e.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                valueLen = ((ValuePattern)vp).Current.Value?.Length.ToString();
        }
        catch { }

        nodes.Add(new
        {
            d = depth,
            type = e.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
            cls = e.Current.ClassName,
            aid = e.Current.AutomationId,
            name_len = e.Current.Name?.Length ?? 0,
            value_len = valueLen,
            w = (int)e.Current.BoundingRectangle.Width,
            h = (int)e.Current.BoundingRectangle.Height,
        });

        var walker = TreeWalker.RawViewWalker;
        for (var c = walker.GetFirstChild(e); c != null; c = walker.GetNextSibling(c))
            Walk(c, depth + 1, nodes);
    }

    private static IEnumerable<IntPtr> EnumTopLevel()
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    private static void Log(string evt, object data)
    {
        var line = JsonSerializer.Serialize(new { t = DateTime.Now.ToString("HH:mm:ss.fff"), evt, data });
        Console.WriteLine(line);
        _log?.WriteLine(line);
    }
}
