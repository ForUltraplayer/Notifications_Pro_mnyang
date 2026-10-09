// @file TestAppDataIsolation.cs
// @author: rudals252
// @brief 테스트 실행 동안 앱의 AppData 루트를 고유 임시 폴더로 재지정하고, 종료 시 정리한다. 실제 %AppData%에 쓰는 테스트가 없도록 보장한다.

using System.IO;
using System.Runtime.CompilerServices;
using NotificationsPro.Helpers;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

/// <summary>
/// Redirects <see cref="ForkIdentity.AppDataRoot"/> to a per-run folder under the system temp
/// directory before any test runs, so default constructors (SettingsManager, ThemeManager,
/// ProfileManager, ManagedAssetPathHelper roots) never touch the real %AppData%.
/// </summary>
internal static class TestAppDataIsolation
{
    public static string SessionRoot { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Initialize()
    {
        SessionRoot = Path.Combine(
            Path.GetTempPath(),
            "NotificationsProKR-Mnyang.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(SessionRoot);
        ForkIdentity.SetAppDataRootOverrideForTesting(Path.Combine(SessionRoot, "AppData"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    /// <summary>Creates a unique, empty folder inside the session root.</summary>
    public static string CreateUniqueDirectory(string prefix)
    {
        var path = Path.Combine(SessionRoot, $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>SettingsManager bound to its own temp folder (replaces the parameterless constructor in tests).</summary>
    public static SettingsManager CreateSettingsManager()
    {
        return new SettingsManager(CreateUniqueDirectory("settings"));
    }

    private static void Cleanup()
    {
        try
        {
            if (Directory.Exists(SessionRoot))
                Directory.Delete(SessionRoot, recursive: true);

            var parent = Path.GetDirectoryName(SessionRoot);
            if (!string.IsNullOrEmpty(parent)
                && Directory.Exists(parent)
                && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch
        {
            // Best effort: leftovers stay under the temp directory, never under %AppData%.
        }
    }
}
