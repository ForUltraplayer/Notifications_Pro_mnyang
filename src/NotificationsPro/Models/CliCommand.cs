// @file CliCommand.cs
// @author: rudals252
// @brief 해석된 명령줄 명령 하나(종류 + --theme의 테마 이름).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>One parsed command. <paramref name="Argument"/> is the theme name for <see cref="CliCommandKind.Theme"/>.</summary>
public sealed record CliCommand(CliCommandKind Kind, string? Argument = null);
