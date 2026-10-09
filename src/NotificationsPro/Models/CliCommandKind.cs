// @file CliCommandKind.cs
// @author: rudals252
// @brief 인식하는 명령줄 인자 종류(기존 --pause/--resume/--theme/--send-test/--hide/--show + P4 --start-minimized/--quit).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Command-line commands understood by the app.</summary>
public enum CliCommandKind
{
    Pause,
    Resume,
    Theme,
    SendTest,
    Hide,
    Show,

    /// <summary>P4: start without showing the settings window (used by the logon task). No effect when forwarded.</summary>
    StartMinimized,

    /// <summary>P4: quit through the normal exit path (clears the run marker). Used for clean shutdowns and updates.</summary>
    Quit,
}
