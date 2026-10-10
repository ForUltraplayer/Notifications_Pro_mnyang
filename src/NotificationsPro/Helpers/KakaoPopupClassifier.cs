// @file KakaoPopupClassifier.cs
// @author: rudals252
// @brief [실험실] 카카오톡 PC의 새 메시지 알림 팝업 창을 창 정보(프로세스·클래스·스타일·크기·위치)만으로 판별하는 순수 로직. 2026-10-10 카카오톡 26.8.2 실측 기준이며 업데이트로 바뀔 수 있다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Helpers;

/// <summary>Window facts gathered from Win32 for one top-level window (no window text).</summary>
public readonly record struct KakaoWindowFacts(
    string ProcessName,
    string ClassName,
    bool HasOwner,
    bool Visible,
    int Style,
    int ExStyle,
    int Left,
    int Top,
    int Width,
    int Height,
    int WorkLeft,
    int WorkTop,
    int WorkRight,
    int WorkBottom);

/// <summary>
/// Labs: recognises KakaoTalk PC's own message popup. KakaoTalk does not use Windows notifications, and the
/// popup content is not exposed to UI Automation, so only "a popup appeared" can be detected (no sender/text).
/// Measured on KakaoTalk 26.8.2 (2026-10-10): an unowned, visible, topmost tool window of class
/// "EVA_Window_Dblclk" with popup style and no frame, about 310 x 89-130 px, near the work-area corner.
/// The main window has the same class but a sizing frame / system menu and is not a tool window.
/// </summary>
public static class KakaoPopupClassifier
{
    public const string ProcessName = "KakaoTalk";
    public const string PopupClassName = "EVA_Window_Dblclk";

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    internal const int MinWidth = 150, MaxWidth = 800, MinHeight = 40, MaxHeight = 600;

    /// <summary>Returns null when the window looks like a message popup, otherwise a short technical reason.</summary>
    public static string? GetRejectReason(KakaoWindowFacts w)
    {
        if (!string.Equals(w.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase))
            return "not KakaoTalk";
        if (!string.Equals(w.ClassName, PopupClassName, StringComparison.Ordinal))
            return "class " + w.ClassName;
        if (w.HasOwner)
            return "owned window";
        if (!w.Visible)
            return "hidden";
        if ((w.Style & WS_POPUP) == 0)
            return "not a popup";
        if ((w.Style & (WS_THICKFRAME | WS_SYSMENU | WS_CAPTION)) != 0)
            return "framed window (main/chat)";
        if ((w.ExStyle & WS_EX_TOOLWINDOW) == 0 || (w.ExStyle & WS_EX_TOPMOST) == 0)
            return "not a topmost tool window";
        if (w.Width < MinWidth || w.Width > MaxWidth || w.Height < MinHeight || w.Height > MaxHeight)
            return $"size {w.Width}x{w.Height}";
        if (w.Left < w.WorkLeft - 50 || w.Top < w.WorkTop - 50
            || w.Left + w.Width > w.WorkRight + 50 || w.Top + w.Height > w.WorkBottom + 50)
            return "outside the work area";
        return null;
    }

    public static bool IsMessagePopup(KakaoWindowFacts w) => GetRejectReason(w) == null;
}
