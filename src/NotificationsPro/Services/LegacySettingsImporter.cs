// @file LegacySettingsImporter.cs
// @author: rudals252
// @brief 원본 Notifications Pro 설정(MSIX 가상화 경로·언패키지 경로)을 탐지해 보고하고, 선택한 원본을 수정 없이 포크 AppData로 복사한 뒤 v6로 이관한다. 기존 포크 설정은 먼저 백업한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.Text.Json;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Imports the original app's settings into this fork (design §11.2, R13).
/// - Never modifies the source folder: files are only read and copied.
/// - Backs up an existing fork settings.json as settings.json.pre-import-&lt;timestamp&gt; first.
/// - Copies themes/profiles/icons/sounds/backgrounds keeping relative paths; a different fork file
///   with the same name is moved to pre-import-&lt;timestamp&gt;\&lt;folder&gt;\… instead of being overwritten.
/// - Migrates to schema v6 (AudioDispatchMode = OverlayAccepted, legacy sounds → Single rules) and keeps
///   the fork's app-local values (language, sound folders, manual mute confirmations, restart intent).
/// When both candidates exist the caller must let the user choose (<see cref="RequiresUserChoice"/>).
/// </summary>
public sealed class LegacySettingsImporter
{
    public const string PackagedFamilyName = "NotificationsPro_h8abfdfjdvdc6";
    public const string LegacyFolderName = "NotificationsPro";
    internal const string PreImportSuffixPrefix = ".pre-import-";
    internal const string AssetBackupFolderPrefix = "pre-import-";

    public static readonly IReadOnlyList<string> AssetFolderNames = new[]
    {
        "themes",
        "profiles",
        ManagedAssetPathHelper.IconsFolderName,
        ManagedAssetPathHelper.SoundsFolderName,
        ManagedAssetPathHelper.BackgroundsFolderName,
    };

    private readonly SettingsManager _target;
    private readonly IReadOnlyList<LegacySettingsSource> _sources;
    private readonly Func<DateTime> _clock;

    public LegacySettingsImporter(SettingsManager target, IReadOnlyList<LegacySettingsSource> sources, Func<DateTime>? clock = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>
    /// The two real locations of the original app's data on this PC. Only used by the app itself;
    /// tests pass fake sources under a temp folder instead.
    /// </summary>
    public static IReadOnlyList<LegacySettingsSource> GetDefaultSources()
    {
        var roamingLegacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LegacyFolderName);
        var packagedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            PackagedFamilyName,
            "LocalCache",
            "Roaming",
            LegacyFolderName);

        return new[]
        {
            new LegacySettingsSource(LegacySettingsSourceKind.PackagedMsix, packagedRoot, roamingLegacyRoot),
            new LegacySettingsSource(LegacySettingsSourceKind.Unpackaged, roamingLegacyRoot, roamingLegacyRoot),
        };
    }

    /// <summary>Read-only inspection of every configured source.</summary>
    public IReadOnlyList<LegacySettingsCandidate> DetectCandidates()
        => _sources.Select(Inspect).ToList();

    /// <summary>True when more than one source has a settings.json, so the user must pick one.</summary>
    public bool RequiresUserChoice()
        => DetectCandidates().Count(candidate => candidate.Exists) > 1;

    public static LegacySettingsCandidate Inspect(LegacySettingsSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var settingsPath = Path.Combine(source.RootPath, SettingsManager.SettingsFileName);
        try
        {
            var assetFolders = AssetFolderNames
                .Where(folder => Directory.Exists(Path.Combine(source.RootPath, folder)))
                .ToList();

            if (!File.Exists(settingsPath))
            {
                return new LegacySettingsCandidate
                {
                    Source = source,
                    SettingsPath = settingsPath,
                    Exists = false,
                    AssetFolders = assetFolders,
                };
            }

            var info = new FileInfo(settingsPath);
            var tooLarge = info.Length > SettingsManager.MaxSettingsFileBytes;
            int? schemaVersion = null;
            var validJson = false;
            if (!tooLarge)
            {
                var json = File.ReadAllText(settingsPath);
                validJson = IsJsonObject(json);
                schemaVersion = SettingsManager.TryReadSchemaVersion(json);
            }

            return new LegacySettingsCandidate
            {
                Source = source,
                SettingsPath = settingsPath,
                Exists = true,
                SizeBytes = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                SchemaVersion = schemaVersion,
                IsValidJson = validJson,
                IsTooLarge = tooLarge,
                IsNewerSchema = schemaVersion > SettingsManager.CurrentSettingsSchemaVersion,
                AssetFolders = assetFolders,
            };
        }
        catch
        {
            return new LegacySettingsCandidate
            {
                Source = source,
                SettingsPath = settingsPath,
                Exists = File.Exists(settingsPath),
                IsValidJson = false,
            };
        }
    }

    public LegacySettingsImportResult Import(LegacySettingsCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var source = candidate.Source;

        if (_target.IsReadOnlyDueToNewerSchema)
            return LegacySettingsImportResult.Fail("The fork settings file uses a newer schema and is read-only.", source.Kind);

        var targetRoot = Path.GetFullPath(_target.SettingsDirectory);
        if (PathsEqual(source.RootPath, targetRoot))
            return LegacySettingsImportResult.Fail("Source and target folders are the same.", source.Kind);

        // Re-inspect: the file may have changed since detection.
        var fresh = Inspect(source);
        if (!fresh.CanImport)
            return LegacySettingsImportResult.Fail(fresh.BlockReason ?? "Source cannot be imported.", source.Kind, fresh.SchemaVersion);

        string json;
        try
        {
            json = File.ReadAllText(fresh.SettingsPath);
        }
        catch (Exception ex)
        {
            return LegacySettingsImportResult.Fail($"Could not read source settings ({ex.GetType().Name}).", source.Kind, fresh.SchemaVersion);
        }

        // Parse before touching anything so an unreadable source leaves the fork untouched.
        var legacyRoots = new[] { source.RootPath, source.AppVisibleRootPath }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .ToList();
        if (!SettingsManager.TryParseSettingsJson(json, settings => RebaseLegacyAssetPaths(settings, legacyRoots), out var imported))
            return LegacySettingsImportResult.Fail("Source settings could not be parsed.", source.Kind, fresh.SchemaVersion);

        var timestamp = _clock().ToString(SettingsManager.BackupTimestampFormat, System.Globalization.CultureInfo.InvariantCulture);
        string? settingsBackupPath = null;
        AssetCopyStats assetStats;
        try
        {
            Directory.CreateDirectory(targetRoot);
            var targetSettingsPath = Path.Combine(targetRoot, SettingsManager.SettingsFileName);
            if (File.Exists(targetSettingsPath))
            {
                settingsBackupPath = AtomicFileWriter.GetUniqueSiblingPath(targetSettingsPath, PreImportSuffixPrefix + timestamp);
                File.Copy(targetSettingsPath, settingsBackupPath, overwrite: false);
            }

            assetStats = CopyAssets(source.RootPath, targetRoot, timestamp);
        }
        catch (Exception ex)
        {
            return new LegacySettingsImportResult
            {
                Success = false,
                Error = $"Copying files failed ({ex.GetType().Name}). Settings were not changed.",
                SourceKind = source.Kind,
                SourceSchemaVersion = fresh.SchemaVersion,
                SettingsBackupPath = settingsBackupPath,
            };
        }

        // The imported file is treated exactly like an existing legacy settings.json.
        SettingsSchemaMigrator.MigrateLoadedSettings(imported, hadExistingSettingsFile: true);
        StartupSettingsMigrationHelper.Apply(imported, primaryWorkAreaHeight: null, hadExistingSettingsFile: true);
        LocalOnlySettingsHelper.CopyLocalOnly(_target.Settings, imported);
        _target.Apply(imported);

        return new LegacySettingsImportResult
        {
            Success = _target.LastSaveStatus == SettingsSaveStatus.Saved,
            Error = _target.LastSaveStatus == SettingsSaveStatus.Saved ? null : "Imported settings are active but could not be saved.",
            SourceKind = source.Kind,
            SourceSchemaVersion = fresh.SchemaVersion,
            SettingsBackupPath = settingsBackupPath,
            AssetBackupFolder = assetStats.BackupFolder,
            AssetsCopied = assetStats.Copied,
            AssetsSkippedIdentical = assetStats.SkippedIdentical,
            AssetsBackedUp = assetStats.BackedUp,
            SaveStatus = _target.LastSaveStatus,
        };
    }

    /// <summary>
    /// Older versions could store absolute asset paths under the original data folder. Rewrites
    /// those to portable "folder/file" references so they resolve inside the fork folder.
    /// </summary>
    internal static void RebaseLegacyAssetPaths(AppSettings settings, IReadOnlyList<string> legacyRoots)
    {
        settings.DefaultSound = RebaseAssetPath(settings.DefaultSound, legacyRoots);
        settings.DefaultIconPreset = RebaseAssetPath(settings.DefaultIconPreset, legacyRoots);
        settings.CardBackgroundImagePath = RebaseAssetPath(settings.CardBackgroundImagePath, legacyRoots);
        settings.FullscreenOverlayImagePath = RebaseAssetPath(settings.FullscreenOverlayImagePath, legacyRoots);
        settings.PerAppSounds = RebaseMap(settings.PerAppSounds, legacyRoots);
        settings.PerAppIcons = RebaseMap(settings.PerAppIcons, legacyRoots);
        settings.PerAppBackgroundImages = RebaseMap(settings.PerAppBackgroundImages, legacyRoots);
    }

    internal static string RebaseAssetPath(string? value, IReadOnlyList<string> legacyRoots)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (!Path.IsPathFullyQualified(expanded))
            return value;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(expanded);
        }
        catch
        {
            return value;
        }

        foreach (var root in legacyRoots)
        {
            string rootWithSeparator;
            try
            {
                rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            }
            catch
            {
                continue;
            }

            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = fullPath[rootWithSeparator.Length..].Replace('\\', '/');
            var firstSegment = relative.Split('/')[0];
            if (AssetFolderNames.Contains(firstSegment, StringComparer.OrdinalIgnoreCase))
                return relative;
        }

        return value;
    }

    private static Dictionary<string, string> RebaseMap(Dictionary<string, string>? map, IReadOnlyList<string> legacyRoots)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map == null)
            return result;

        foreach (var (key, value) in map)
        {
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = RebaseAssetPath(value, legacyRoots);
        }

        return result;
    }

    private readonly record struct AssetCopyStats(int Copied, int SkippedIdentical, int BackedUp, string? BackupFolder);

    private static AssetCopyStats CopyAssets(string sourceRoot, string targetRoot, string timestamp)
    {
        var copied = 0;
        var skipped = 0;
        var backedUp = 0;
        var backupFolder = Path.Combine(targetRoot, AssetBackupFolderPrefix + timestamp);
        var usedBackupFolder = false;

        foreach (var folder in AssetFolderNames)
        {
            var sourceFolder = Path.Combine(sourceRoot, folder);
            if (!Directory.Exists(sourceFolder) || IsReparsePoint(sourceFolder))
                continue;

            foreach (var sourceFile in EnumerateFilesWithoutReparsePoints(sourceFolder))
            {
                var relative = Path.GetRelativePath(sourceFolder, sourceFile);
                var destination = Path.Combine(targetRoot, folder, relative);

                if (File.Exists(destination))
                {
                    if (FilesHaveSameContent(sourceFile, destination))
                    {
                        skipped++;
                        continue;
                    }

                    var backupPath = Path.Combine(backupFolder, folder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    backupPath = AtomicFileWriter.GetUniqueSiblingPath(backupPath, string.Empty);
                    File.Move(destination, backupPath);
                    usedBackupFolder = true;
                    backedUp++;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourceFile, destination, overwrite: false);
                copied++;
            }
        }

        return new AssetCopyStats(copied, skipped, backedUp, usedBackupFolder ? backupFolder : null);
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!IsReparsePoint(file))
                    yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!IsReparsePoint(child))
                    pending.Push(child);
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true; // unreadable entries are skipped
        }
    }

    private static bool FilesHaveSameContent(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length)
            return false;

        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        var firstBuffer = new byte[81920];
        var secondBuffer = new byte[81920];
        while (true)
        {
            var read = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            if (read == 0)
                return true;

            secondStream.ReadExactly(secondBuffer, 0, read);
            if (!firstBuffer.AsSpan(0, read).SequenceEqual(secondBuffer.AsSpan(0, read)))
                return false;
        }
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
