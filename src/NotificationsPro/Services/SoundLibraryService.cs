// @file SoundLibraryService.cs
// @author: rudals252
// @brief WAV 보관함: 관리 폴더(sounds/)와 등록된 외부 폴더 바로 아래 WAV 나열·검증, 가져오기(고유 내부 파일명), 목록 제외와 실제 삭제 분리, 외부 폴더 등록 검증(UNC·네트워크·링크 거부), 재생 직전 허용 위치·형식 확인.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.Security.Cryptography;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Sound library and runtime file gate (design §7.3).
/// <list type="bullet">
/// <item>Lists only <c>*.wav</c> directly inside the managed <c>sounds</c> folder and inside each registered
/// <see cref="AppSettings.SoundLibraryFolders"/> entry (no recursion, no file watching — call
/// <see cref="Refresh"/> when the screen opens or the user presses refresh).</item>
/// <item>Status comes from the WAV header (<see cref="WavHeaderInspector"/>) and, with
/// <see cref="RefreshAsync"/>, from opening the file in the real playback engine. The extension alone
/// never marks a file usable.</item>
/// <item>Allowed playback locations: managed sounds, trusted Windows sounds, and files directly inside a
/// registered local folder. Network paths and symbolic links/junctions are refused at registration and
/// again at runtime, so a link cannot widen the allowed range.</item>
/// <item>Import copies into the managed folder under a unique internal name and never overwrites or
/// deletes the source. Removing a file from rules and deleting a managed file are separate calls; the
/// latter needs an explicit flag and never touches files outside the managed folder.</item>
/// </list>
/// </summary>
public sealed class SoundLibraryService : ISoundFileGate
{
    internal const long MaxImportBytes = 64L * 1024 * 1024;
    internal const int MaxCacheEntries = 1024;

    private readonly Func<AppSettings> _settings;
    private readonly ISoundFileProber? _prober;
    private readonly Func<IEnumerable<string>> _trustedWindowsSounds;
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedHeader> _headerCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileStamp> _failedFiles = new(StringComparer.OrdinalIgnoreCase);

    public SoundLibraryService(
        Func<AppSettings> settings,
        ISoundFileProber? prober = null,
        string? managedRoot = null,
        Func<IEnumerable<string>>? trustedWindowsSounds = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _prober = prober;
        ManagedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            managedRoot ?? ManagedAssetPathHelper.GetRoot(ManagedAssetPathHelper.SoundsFolderName)));
        _trustedWindowsSounds = trustedWindowsSounds
            ?? (() => SoundService.GetWindowsSounds().Select(sound => sound.WavPath));
    }

    public string ManagedRoot { get; }

    // ---- listing ----

    /// <summary>Re-reads the folders (header check only) and clears remembered playback failures.</summary>
    public SoundLibrarySnapshot Refresh(bool includeWindowsSounds = false)
    {
        lock (_sync)
        {
            _headerCache.Clear();
            _failedFiles.Clear();
        }

        var entries = new List<SoundLibraryEntry>();
        var folders = new List<SoundFolderStatus>();

        folders.Add(ListFolder(ManagedRoot, SoundFileSource.Managed, entries, managedMissingIsEmpty: true));

        foreach (var folder in AppSettingsAssetPathHelper.NormalizeSoundLibraryFolders(_settings().SoundLibraryFolders))
            folders.Add(ListFolder(folder, SoundFileSource.ExternalFolder, entries, managedMissingIsEmpty: false));

        if (includeWindowsSounds)
        {
            foreach (var path in SafeTrustedWindowsSounds())
            {
                var (status, header) = CheckFile(path, SoundFileSource.WindowsSound, folderBoundary: null);
                entries.Add(CreateEntry(path, path, SoundFileSource.WindowsSound, Path.GetDirectoryName(path) ?? string.Empty, status, header));
            }
        }

        return new SoundLibrarySnapshot(entries, folders);
    }

    /// <summary>
    /// <see cref="Refresh"/> plus opening each header-valid file in the real engine (silent, no playback).
    /// Files the engine cannot open become <see cref="SoundFileStatus.Unsupported"/>.
    /// </summary>
    public async Task<SoundLibrarySnapshot> RefreshAsync(bool verifyWithEngine, bool includeWindowsSounds = false, CancellationToken cancellationToken = default)
    {
        var snapshot = Refresh(includeWindowsSounds);
        if (!verifyWithEngine || _prober == null)
            return snapshot;

        var verified = new List<SoundLibraryEntry>(snapshot.Entries.Count);
        foreach (var entry in snapshot.Entries)
            verified.Add(await VerifyWithEngineAsync(entry, cancellationToken).ConfigureAwait(false));

        return snapshot with { Entries = verified };
    }

    public async Task<SoundLibraryEntry> VerifyWithEngineAsync(SoundLibraryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_prober == null || entry.Status != SoundFileStatus.Usable)
            return entry;

        var probe = await _prober.ProbeAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
        if (probe.Playable)
            return entry with { Duration = entry.Duration ?? probe.Duration };

        return probe.Failure == SoundEngineFailureKind.DeviceOrOther
            ? entry
            : entry with { Status = SoundFileStatus.Unsupported };
    }

    /// <summary>Status of one stored rule entry (for showing missing/disabled files in a rule's list).</summary>
    public SoundLibraryEntry CheckRuleFile(string ruleFile)
    {
        var settings = _settings();
        if (!TryResolveAllowed(ruleFile, settings, out var fullPath, out var source, out var boundary))
        {
            return new SoundLibraryEntry(
                fullPath ?? ruleFile ?? string.Empty,
                ruleFile ?? string.Empty,
                SafeStem(ruleFile),
                SoundFileSource.ExternalFolder,
                string.Empty,
                SoundFileStatus.NotAllowed,
                SoundFileStatus.NotAllowed.ToString(),
                null);
        }

        var (status, header) = CheckFile(fullPath!, source, boundary);
        return CreateEntry(fullPath!, ruleFile!, source, Path.GetDirectoryName(fullPath!) ?? string.Empty, status, header);
    }

    // ---- runtime gate ----

    public string? ResolvePlayablePath(string ruleFile)
    {
        var settings = _settings();
        if (!TryResolveAllowed(ruleFile, settings, out var fullPath, out var source, out var boundary))
            return null;

        var (status, _) = CheckFile(fullPath!, source, boundary);
        return status == SoundFileStatus.Usable ? fullPath : null;
    }

    public void MarkUnplayable(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return;

        var stamp = TryGetStamp(fullPath);
        if (stamp == null)
            return;

        lock (_sync)
        {
            if (!_failedFiles.ContainsKey(fullPath) && _failedFiles.Count >= MaxCacheEntries)
                _failedFiles.Clear();
            _failedFiles[fullPath] = stamp.Value;
        }
    }

    // ---- import / remove / delete ----

    /// <summary>Copies a WAV into the managed folder. Never overwrites an existing file or changes the source.</summary>
    public SoundImportResult Import(string sourcePath)
    {
        string fullSource;
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return new SoundImportResult(SoundImportStatus.SourceMissing);
            fullSource = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourcePath.Trim()));
        }
        catch
        {
            return new SoundImportResult(SoundImportStatus.SourceMissing);
        }

        if (!File.Exists(fullSource))
            return new SoundImportResult(SoundImportStatus.SourceMissing);

        if (!string.Equals(Path.GetExtension(fullSource), ".wav", StringComparison.OrdinalIgnoreCase))
            return new SoundImportResult(SoundImportStatus.NotWav);

        var displayName = SafeStem(fullSource);
        if (IsDirectChildOf(fullSource, ManagedRoot))
            return new SoundImportResult(SoundImportStatus.AlreadyManaged, ToManagedReference(Path.GetFileName(fullSource)), Path.GetFileName(fullSource), displayName);

        long length;
        try
        {
            length = new FileInfo(fullSource).Length;
        }
        catch
        {
            return new SoundImportResult(SoundImportStatus.Failed);
        }

        if (length > MaxImportBytes)
            return new SoundImportResult(SoundImportStatus.TooLarge);

        if (!WavHeaderInspector.Inspect(fullSource).IsValid)
            return new SoundImportResult(SoundImportStatus.Unsupported);

        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(ManagedRoot);
            var hash = ComputeHash(fullSource);

            foreach (var existing in EnumerateTopLevelWavs(ManagedRoot))
            {
                if (new FileInfo(existing).Length == length && ComputeHash(existing) == hash)
                {
                    var existingName = Path.GetFileName(existing);
                    return new SoundImportResult(SoundImportStatus.AlreadyPresent, ToManagedReference(existingName), existingName, displayName);
                }
            }

            var internalName = ChooseUniqueName(SanitizeStem(displayName), hash);
            var destination = Path.Combine(ManagedRoot, internalName);
            tempPath = Path.Combine(ManagedRoot, $".import-{Guid.NewGuid():N}.tmp");
            File.Copy(fullSource, tempPath, overwrite: false);
            File.Move(tempPath, destination, overwrite: false);
            tempPath = null;

            lock (_sync)
                _headerCache.Remove(destination);

            return new SoundImportResult(SoundImportStatus.Copied, ToManagedReference(internalName), internalName, displayName);
        }
        catch
        {
            return new SoundImportResult(SoundImportStatus.Failed);
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    /// <summary>
    /// "Remove from list": returns a copy of <paramref name="settings"/> without <paramref name="ruleFile"/> in
    /// any rule. The file on disk is not touched.
    /// </summary>
    public static AppSettings RemoveFromRules(AppSettings settings, string ruleFile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var updated = settings.Clone();
        if (string.IsNullOrWhiteSpace(ruleFile))
            return updated;

        updated.DefaultSoundRule?.Files?.RemoveAll(file => string.Equals(file, ruleFile, StringComparison.OrdinalIgnoreCase));
        foreach (var rule in updated.AppSoundRules.Values)
            rule?.Files?.RemoveAll(file => string.Equals(file, ruleFile, StringComparison.OrdinalIgnoreCase));

        return updated;
    }

    /// <summary>
    /// Deletes a file directly inside the managed folder, only when <paramref name="deletePhysicalFile"/> is true.
    /// External-folder and Windows sounds are never deleted. Callers remove rule references separately.
    /// </summary>
    public SoundDeleteResult DeleteManagedFile(string ruleFile, bool deletePhysicalFile)
    {
        if (!deletePhysicalFile)
            return SoundDeleteResult.NotConfirmed;

        var fullPath = ManagedAssetPathHelper.ResolveManagedPathOrEmpty(ruleFile, ManagedAssetPathHelper.SoundsFolderName, ManagedRoot);
        if (string.IsNullOrWhiteSpace(fullPath) || !IsDirectChildOf(fullPath, ManagedRoot)
            || !string.Equals(Path.GetExtension(fullPath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return SoundDeleteResult.NotManagedFile;
        }

        if (!File.Exists(fullPath))
            return SoundDeleteResult.NotFound;

        try
        {
            if (new FileInfo(fullPath).LinkTarget != null)
                return SoundDeleteResult.NotManagedFile;

            File.Delete(fullPath);
            lock (_sync)
            {
                _headerCache.Remove(fullPath);
                _failedFiles.Remove(fullPath);
            }

            return SoundDeleteResult.Deleted;
        }
        catch
        {
            return SoundDeleteResult.Failed;
        }
    }

    // ---- external folder registration ----

    /// <summary>
    /// Validates a folder the user picked on this PC: normalized local full path only; UNC, mapped network
    /// drives, missing folders, links/junctions (the folder or an ancestor) and the managed folder are refused.
    /// </summary>
    public SoundFolderRegistrationResult ValidateFolderForRegistration(string? folder)
    {
        var expanded = Environment.ExpandEnvironmentVariables(folder?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(expanded))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.Empty);

        if (expanded.StartsWith(@"\\", StringComparison.Ordinal) || expanded.StartsWith("//", StringComparison.Ordinal))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.NetworkPath);

        if (!Path.IsPathFullyQualified(expanded))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.NotFullyQualified);

        string fullPath;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch
        {
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.Invalid);
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) || IsNetworkDrive(fullPath))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.NetworkPath);

        if (!Directory.Exists(fullPath))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.NotFound);

        if (IsLinkOrUnderLink(fullPath))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.LinkOrJunction);

        if (string.Equals(fullPath, ManagedRoot, StringComparison.OrdinalIgnoreCase))
            return new SoundFolderRegistrationResult(null, SoundFolderRejection.ManagedFolder);

        return new SoundFolderRegistrationResult(fullPath, SoundFolderRejection.None);
    }

    // ---- internals ----

    private SoundFolderStatus ListFolder(string folder, SoundFileSource source, List<SoundLibraryEntry> entries, bool managedMissingIsEmpty)
    {
        var folderState = GetFolderState(folder, source);
        if (folderState != SoundFileStatus.Usable)
        {
            if (source == SoundFileSource.Managed && managedMissingIsEmpty && folderState == SoundFileStatus.Missing)
                return new SoundFolderStatus(folder, source, SoundFileStatus.Usable, 0);
            return new SoundFolderStatus(folder, source, folderState, 0);
        }

        var count = 0;
        foreach (var file in EnumerateTopLevelWavs(folder))
        {
            var reference = source == SoundFileSource.Managed ? ToManagedReference(Path.GetFileName(file)) : file;
            var (status, header) = CheckFile(file, source, source == SoundFileSource.ExternalFolder ? folder : null);
            entries.Add(CreateEntry(file, reference, source, folder, status, header));
            count++;
        }

        return new SoundFolderStatus(folder, source, SoundFileStatus.Usable, count);
    }

    private SoundFileStatus GetFolderState(string folder, SoundFileSource source)
    {
        try
        {
            var root = Path.GetPathRoot(folder);
            if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
                return SoundFileStatus.DriveUnavailable;

            if (!Directory.Exists(folder))
                return SoundFileStatus.Missing;

            if (source == SoundFileSource.ExternalFolder && (IsNetworkDrive(folder) || IsLinkOrUnderLink(folder)))
                return SoundFileStatus.NotAllowed;

            return SoundFileStatus.Usable;
        }
        catch
        {
            return SoundFileStatus.NotAllowed;
        }
    }

    private static IEnumerable<string> EnumerateTopLevelWavs(string folder)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.wav", SearchOption.TopDirectoryOnly)
                .Where(file => string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            files = Array.Empty<string>();
        }

        return files;
    }

    private bool TryResolveAllowed(string? ruleFile, AppSettings settings, out string? fullPath, out SoundFileSource source, out string? folderBoundary)
    {
        fullPath = null;
        source = SoundFileSource.Managed;
        folderBoundary = null;

        var trimmed = ruleFile?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
            return false;

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (expanded.StartsWith(@"\\", StringComparison.Ordinal) || expanded.StartsWith("//", StringComparison.Ordinal))
            return false;

        try
        {
            var managed = ManagedAssetPathHelper.ResolveManagedPathOrEmpty(expanded, ManagedAssetPathHelper.SoundsFolderName, ManagedRoot);
            if (!string.IsNullOrWhiteSpace(managed))
            {
                fullPath = managed;
                source = SoundFileSource.Managed;
                return string.Equals(Path.GetExtension(managed), ".wav", StringComparison.OrdinalIgnoreCase);
            }

            if (!Path.IsPathFullyQualified(expanded))
                return false;

            var candidate = Path.GetFullPath(expanded);
            fullPath = candidate;

            if (SafeTrustedWindowsSounds().Any(path => string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                source = SoundFileSource.WindowsSound;
                return true;
            }

            if (!string.Equals(Path.GetExtension(candidate), ".wav", StringComparison.OrdinalIgnoreCase))
                return false;

            var parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(candidate) ?? string.Empty);
            var folders = AppSettingsAssetPathHelper.NormalizeSoundLibraryFolders(settings.SoundLibraryFolders);
            var match = folders.FirstOrDefault(folder => string.Equals(folder, parent, StringComparison.OrdinalIgnoreCase));
            if (match == null || IsNetworkDrive(match))
                return false;

            source = SoundFileSource.ExternalFolder;
            folderBoundary = match;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private (SoundFileStatus Status, WavHeaderInfo? Header) CheckFile(string fullPath, SoundFileSource source, string? folderBoundary)
    {
        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
                return (SoundFileStatus.DriveUnavailable, null);

            var info = new FileInfo(fullPath);
            if (!info.Exists)
                return (SoundFileStatus.Missing, null);

            if (source != SoundFileSource.WindowsSound)
            {
                if (info.LinkTarget != null)
                    return (SoundFileStatus.NotAllowed, null);
                if (folderBoundary != null && IsLinkOrUnderLink(folderBoundary))
                    return (SoundFileStatus.NotAllowed, null);
            }

            var stamp = new FileStamp(info.Length, info.LastWriteTimeUtc);
            WavHeaderInfo header;
            lock (_sync)
            {
                if (_failedFiles.TryGetValue(fullPath, out var failed))
                {
                    if (failed == stamp)
                        return (SoundFileStatus.PlaybackFailed, null);
                    _failedFiles.Remove(fullPath);
                }

                if (_headerCache.TryGetValue(fullPath, out var cached) && cached.Stamp == stamp)
                {
                    header = cached.Header;
                    return (MapHeader(header), header);
                }
            }

            header = WavHeaderInspector.Inspect(fullPath);
            lock (_sync)
            {
                if (!_headerCache.ContainsKey(fullPath) && _headerCache.Count >= MaxCacheEntries)
                    _headerCache.Clear();
                _headerCache[fullPath] = new CachedHeader(stamp, header);
            }

            return (MapHeader(header), header);
        }
        catch
        {
            return (SoundFileStatus.Damaged, null);
        }
    }

    private static SoundFileStatus MapHeader(WavHeaderInfo header)
    {
        return header.Status switch
        {
            WavHeaderStatus.Valid => SoundFileStatus.Usable,
            WavHeaderStatus.Missing => SoundFileStatus.Missing,
            WavHeaderStatus.UnsupportedFormat => SoundFileStatus.Unsupported,
            _ => SoundFileStatus.Damaged,
        };
    }

    private static SoundLibraryEntry CreateEntry(string fullPath, string reference, SoundFileSource source, string folder, SoundFileStatus status, WavHeaderInfo? header)
    {
        return new SoundLibraryEntry(
            fullPath,
            reference,
            SafeStem(fullPath),
            source,
            folder,
            status,
            header?.FormatDescription ?? status.ToString(),
            header?.Duration);
    }

    private IEnumerable<string> SafeTrustedWindowsSounds()
    {
        try
        {
            return _trustedWindowsSounds().Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private string ChooseUniqueName(string stem, string hash)
    {
        var plain = stem + ".wav";
        if (!File.Exists(Path.Combine(ManagedRoot, plain)))
            return plain;

        var withHash = $"{stem}-{hash[..8]}.wav";
        if (!File.Exists(Path.Combine(ManagedRoot, withHash)))
            return withHash;

        for (var counter = 2; ; counter++)
        {
            var numbered = $"{stem}-{hash[..8]}-{counter}.wav";
            if (!File.Exists(Path.Combine(ManagedRoot, numbered)))
                return numbered;
        }
    }

    private static string SanitizeStem(string stem)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((stem ?? string.Empty).Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "sound";
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    private static string SafeStem(string? path)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(path ?? string.Empty);
        }
        catch
        {
            return path ?? string.Empty;
        }
    }

    private string ToManagedReference(string fileName) => $"{ManagedAssetPathHelper.SoundsFolderName}/{fileName}";

    private static bool IsDirectChildOf(string fullPath, string folder)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(fullPath)) ?? string.Empty);
        return string.Equals(parent, Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static FileStamp? TryGetStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsLinkOrUnderLink(string folder)
    {
        try
        {
            for (var directory = new DirectoryInfo(folder); directory != null; directory = directory.Parent)
            {
                if (directory.Exists && directory.LinkTarget != null)
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNetworkDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return !string.IsNullOrEmpty(root);

            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc);

    private sealed record CachedHeader(FileStamp Stamp, WavHeaderInfo Header);
}
