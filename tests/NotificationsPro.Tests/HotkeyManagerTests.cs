// @file HotkeyManagerTests.cs
// @author: rudals252
// @brief 단축키 해제·재등록이 오버레이 창의 HwndSource를 폐기하지 않는지(창이 살아 있는지) 검증한다. 실제 단축키는 등록하지 않는다(잘못된 형식 문자열 사용).

using System.Windows.Interop;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void UnregisterAndDispose_DoNotDisposeTheWindowsHwndSource()
    {
        StaThreadTestHelper.Run(() =>
        {
            // Stand-in for the overlay window: a hidden HwndSource owned by the "window", not by the manager.
            using var windowSource = new HwndSource(new HwndSourceParameters("hotkey-test") { Width = 1, Height = 1 });
            var hwnd = windowSource.Handle;

            // Invalid hotkey strings: Register attaches its hook but calls no RegisterHotKey,
            // so the test never takes a real system-wide hotkey.
            var manager = new HotkeyManager();
            manager.Register(hwnd, "", "", "");
            manager.Unregister();
            Assert.False(windowSource.IsDisposed);

            // Settings changes re-register (RefreshHotkeys disposes the old manager first).
            manager.Register(hwnd, "", "", "");
            manager.Dispose();
            Assert.False(windowSource.IsDisposed);

            var second = new HotkeyManager();
            second.Register(hwnd, "", "", "");
            second.Dispose();
            Assert.False(windowSource.IsDisposed);
        });
    }
}
