// @file App.Language.cs
// @author: rudals252
// @brief UI 언어(이 PC 전용 UiLanguage) 시작 적용과 실행 중 전환 처리. 언어가 바뀌면 WinForms 트레이 메뉴를 다시 만들고 툴팁·상태 문구와 설정창 계산 문구를 새 언어로 갱신한다. 설정값·재생 대기 상태는 건드리지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Localization;

namespace NotificationsPro;

public partial class App
{
    /// <summary>
    /// Applies the stored language (null = OS default) before any window, tray item or view model
    /// reads a string, then listens for later changes made in the settings window.
    /// </summary>
    private void InitializeUiLanguage()
    {
        LocalizationManager.Instance.Apply(_settingsManager?.Settings.UiLanguage);
        LocalizationManager.Instance.CultureChanged -= OnUiCultureChanged;
        LocalizationManager.Instance.CultureChanged += OnUiCultureChanged;
    }

    private void OnUiCultureChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnUiCultureChanged(sender, e)));
            return;
        }

        // XAML text follows the {loc:Loc} bindings by itself. WinForms menus cannot bind,
        // so the tray menu is regenerated; the tooltip and status item are rewritten.
        RebuildTrayContextMenu();
        UpdateStatusItem();
        _settingsViewModel?.RefreshLocalizedText();
    }

    private void RebuildTrayContextMenu()
    {
        if (_trayIcon == null)
            return;

        var previous = _trayIcon.ContextMenuStrip;
        if (previous != null)
            previous.Opening -= OnTrayMenuOpening;

        _trayIcon.ContextMenuStrip = CreateTrayContextMenu();
        previous?.Dispose();
        UpdateMenuLabels();
    }
}
