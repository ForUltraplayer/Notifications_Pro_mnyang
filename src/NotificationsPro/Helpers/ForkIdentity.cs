// @file ForkIdentity.cs
// @author: rudals252
// @brief Notifications Pro KR-Mnyang 포크의 설치 정체성(AppData 폴더, AUMID, mutex, 스케줄러 작업 이름, 표시명) 상수와 테스트용 AppData 루트 재지정.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;

namespace NotificationsPro.Helpers;

/// <summary>
/// Single source of truth for the fork's install identity so it never collides with the
/// original Notifications Pro install (different AppData folder, AUMID, mutex, task name).
/// </summary>
public static class ForkIdentity
{
    /// <summary>Product name shown in the tray and first-run balloon.</summary>
    public const string ProductDisplayName = "Notifications Pro KR-Mnyang";

    /// <summary>Folder name under %AppData% for settings, themes, profiles and assets.</summary>
    public const string AppDataFolderName = "NotificationsProKR-Mnyang";

    /// <summary>Executable file name (matches the csproj AssemblyName).</summary>
    public const string ExecutableFileName = "NotificationsProKR-Mnyang.exe";

    /// <summary>
    /// Explicit AppUserModelID for unpackaged runs. Uses dots instead of hyphens because the
    /// documented AUMID form is CompanyName.ProductName.SubProduct.VersionInformation.
    /// </summary>
    public const string UnpackagedAppUserModelId = "rudals252.NotificationsProKR.Mnyang";

    /// <summary>Per-session single-instance mutex name.</summary>
    public const string SingleInstanceMutexName = @"Local\NotificationsProKR-Mnyang.SingleInstance";

    /// <summary>Event a second launch signals so the running instance opens its settings window.</summary>
    public const string ShowSettingsEventName = @"Local\NotificationsProKR-Mnyang.ShowSettings";

    /// <summary>Task Scheduler logon task name (registered from the System tab, P4).</summary>
    public const string LogonTaskName = "NotificationsProKR-Mnyang-Logon";

    /// <summary>Argument the logon task passes so the app starts without showing the settings window (P4).</summary>
    public const string StartMinimizedArgument = "--start-minimized";

    /// <summary>
    /// Local operational metadata (not a setting, never in profiles/exports): marks a running primary
    /// instance so the next start can tell that the previous run did not end normally (design §9.2).
    /// </summary>
    public const string RunMarkerFileName = "run-state.json";

    private const string CliPipeNamePrefix = "NotificationsProKR-Mnyang.Cli.";

    /// <summary>
    /// Named pipe a second launch uses to forward its CLI arguments to the primary instance. Pipes are
    /// machine-wide, so the Windows session id keeps it per session like the Local\ mutex (P4).
    /// </summary>
    public static string GetCliPipeName(int sessionId) => CliPipeNamePrefix + sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Path of <see cref="RunMarkerFileName"/> under <see cref="AppDataRoot"/>.</summary>
    public static string RunMarkerPath => Path.Combine(AppDataRoot, RunMarkerFileName);

    /// <summary>
    /// App names treated as "this app" by the self-notification filter: both the original
    /// Notifications Pro names and the fork names.
    /// </summary>
    public static readonly IReadOnlyList<string> SelfAppNames = new[]
    {
        "Notifications Pro",
        "NotificationsPro",
        ProductDisplayName,
        AppDataFolderName,
    };

    private static volatile string? _appDataRootOverride;

    /// <summary>
    /// Root folder for all persisted settings/assets, normally %AppData%\NotificationsProKR-Mnyang.
    /// </summary>
    public static string AppDataRoot => _appDataRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDataFolderName);

    /// <summary>
    /// Test-only hook: redirects <see cref="AppDataRoot"/> to a temporary folder so unit tests
    /// never touch the real %AppData%. Pass null to restore the default.
    /// </summary>
    internal static void SetAppDataRootOverrideForTesting(string? path)
    {
        _appDataRootOverride = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    public static bool IsSelfAppName(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return false;

        var trimmed = appName.Trim();
        return SelfAppNames.Any(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
