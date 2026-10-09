// @file CliArguments.cs
// @author: rudals252
// @brief 명령줄 인자 해석(기존 의미 유지: 대소문자 무시, --theme 다음 값, 모르는 인자 무시)과 두 번째 실행의 전달 여부·시작 시 설정창 표시 여부 판정.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Parses the app's command line the same way the original <c>ProcessCliArguments</c> loop did
/// (case-insensitive, <c>--theme</c> consumes the next value, unknown arguments ignored) and adds the P4
/// rules for second launches and the start-up window.
/// </summary>
public static class CliArguments
{
    public const string Pause = "--pause";
    public const string Resume = "--resume";
    public const string Theme = "--theme";
    public const string SendTest = "--send-test";
    public const string Hide = "--hide";
    public const string Show = "--show";
    public const string StartMinimized = ForkIdentity.StartMinimizedArgument;
    public const string Quit = "--quit";

    public static IReadOnlyList<CliCommand> Parse(IReadOnlyList<string>? args)
    {
        var commands = new List<CliCommand>();
        if (args == null)
            return commands;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = (args[i] ?? string.Empty).Trim().ToLowerInvariant();
            switch (arg)
            {
                case Pause:
                    commands.Add(new CliCommand(CliCommandKind.Pause));
                    break;
                case Resume:
                    commands.Add(new CliCommand(CliCommandKind.Resume));
                    break;
                case Theme when i + 1 < args.Count:
                    commands.Add(new CliCommand(CliCommandKind.Theme, args[++i]));
                    break;
                case SendTest:
                    commands.Add(new CliCommand(CliCommandKind.SendTest));
                    break;
                case Hide:
                    commands.Add(new CliCommand(CliCommandKind.Hide));
                    break;
                case Show:
                    commands.Add(new CliCommand(CliCommandKind.Show));
                    break;
                case StartMinimized:
                    commands.Add(new CliCommand(CliCommandKind.StartMinimized));
                    break;
                case Quit:
                    commands.Add(new CliCommand(CliCommandKind.Quit));
                    break;
            }
        }

        return commands;
    }

    /// <summary>
    /// A second launch forwards its arguments when they contain at least one known command. Without
    /// arguments (or with unknown ones only) it asks the running instance to show its settings window (P1-A).
    /// </summary>
    public static bool ShouldForward(IReadOnlyList<string>? args) => Parse(args).Count > 0;

    public static bool Contains(IReadOnlyList<string>? args, CliCommandKind kind) => Parse(args).Any(command => command.Kind == kind);

    /// <summary>
    /// Settings window at start-up (design §5.3): always on the first run (no settings file yet);
    /// otherwise only when neither the StartMinimizedToTray setting nor <c>--start-minimized</c> asks for tray start.
    /// The overlay and OBS capture state are decided elsewhere and never changed by this.
    /// </summary>
    public static bool ShouldShowSettingsAtStartup(bool hadExistingSettingsFile, bool startMinimizedToTraySetting, bool startMinimizedArgument)
    {
        if (!hadExistingSettingsFile)
            return true;

        return !startMinimizedToTraySetting && !startMinimizedArgument;
    }
}
