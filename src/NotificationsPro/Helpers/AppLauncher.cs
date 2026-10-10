// @file AppLauncher.cs
// @author: rudals252
// @brief 알림 카드의 AppUserModelId(AUMID)로 해당 앱을 실행하거나 앞으로 가져온다(shell:AppsFolder\AUMID). 알림이 가리키는 특정 대화로 이동하지는 않는다(토스트 활성화 인자는 리스너에 공개되지 않음).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Diagnostics;
using NotificationsPro.Services;

namespace NotificationsPro.Helpers;

/// <summary>
/// Opens the app that sent a notification. The AUMID comes from Windows notification metadata
/// (never from notification text) and is still validated before it reaches the shell.
/// </summary>
public static class AppLauncher
{
    private const int MaxAumidLength = 512;

    /// <summary>Test seam: receives the shell target instead of starting explorer.exe.</summary>
    internal static Func<string, bool>? LaunchOverrideForTesting { get; set; }

    /// <summary>True when <paramref name="appUserModelId"/> looks like a launchable AUMID.</summary>
    public static bool CanLaunch(string? appUserModelId) => BuildShellTarget(appUserModelId) != null;

    /// <summary>
    /// Returns "shell:AppsFolder\&lt;AUMID&gt;" or null when the id is missing, the "Unknown" placeholder,
    /// too long, or contains characters that have no place in an AUMID (quotes, control characters,
    /// "..", leading separators or a drive/URL form).
    /// </summary>
    internal static string? BuildShellTarget(string? appUserModelId)
    {
        var id = appUserModelId?.Trim();
        if (string.IsNullOrEmpty(id)
            || id.Length > MaxAumidLength
            || string.Equals(id, WinRtPollTracker.UnknownAppId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (id.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|' or '*' or '?')
            || id.Contains("..", StringComparison.Ordinal)
            || id.StartsWith('\\') || id.StartsWith('/')
            || id.Contains("://", StringComparison.Ordinal)
            || (id.Length >= 2 && id[1] == ':'))
        {
            return null;
        }

        return @"shell:AppsFolder\" + id;
    }

    /// <summary>Starts or activates the app. Returns false when the id is not launchable or the shell refused.</summary>
    public static bool TryLaunch(string? appUserModelId)
    {
        var target = BuildShellTarget(appUserModelId);
        if (target == null)
            return false;

        var testOverride = LaunchOverrideForTesting;
        if (testOverride != null)
            return testOverride(target);

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(target);
            using var process = Process.Start(startInfo);
            return process != null;
        }
        catch
        {
            return false;
        }
    }
}
