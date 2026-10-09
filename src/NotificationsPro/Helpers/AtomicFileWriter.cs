// @file AtomicFileWriter.cs
// @author: rudals252
// @brief 설정·테마·프로필 JSON을 같은 폴더의 임시 파일에 먼저 쓴 뒤 교체해, 쓰기 실패 시 기존 파일을 그대로 보존하는 저장 헬퍼.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.Text;

namespace NotificationsPro.Helpers;

/// <summary>
/// Writes text through a temporary file in the destination folder, then swaps it in with
/// <see cref="File.Replace(string, string, string?, bool)"/> (or a move when the destination does not exist yet).
/// If anything fails before the swap, the existing destination file is left untouched and the
/// temporary file is removed. Exceptions are propagated so callers keep their own error policy.
/// </summary>
public static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents)
        => WriteAllText(path, contents, beforeCommitForTesting: null);

    /// <summary>Test seam: <paramref name="beforeCommitForTesting"/> runs after the temp file is written and before the swap.</summary>
    internal static void WriteAllText(string path, string contents, Action<string>? beforeCommitForTesting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Destination has no parent directory.");
        Directory.CreateDirectory(directory);

        // Hidden-style name with a .tmp suffix so *.json enumerations (themes, profiles) never pick it up.
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(contents ?? string.Empty);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            beforeCommitForTesting?.Invoke(tempPath);

            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup; the destination file is unaffected either way.
            }
        }
    }

    /// <summary>
    /// Returns "<paramref name="basePath"/><paramref name="suffix"/>" or, if that already exists,
    /// the same name with "-1", "-2", … appended so earlier backups are never overwritten.
    /// </summary>
    public static string GetUniqueSiblingPath(string basePath, string suffix)
    {
        var candidate = basePath + suffix;
        var counter = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = $"{basePath}{suffix}-{counter}";
            counter++;
        }

        return candidate;
    }
}
