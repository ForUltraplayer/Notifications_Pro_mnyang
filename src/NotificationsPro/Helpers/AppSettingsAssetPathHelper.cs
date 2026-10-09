using System.IO;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Helpers;

public static class AppSettingsAssetPathHelper
{
    public static AppSettings CreatePortableSnapshot(AppSettings settings)
    {
        var snapshot = settings.Clone();
        NormalizeForRuntime(snapshot);

        snapshot.DefaultSound = ToPortableSoundReference(snapshot.DefaultSound);
        snapshot.PerAppSounds = snapshot.PerAppSounds
            .Select(entry => new KeyValuePair<string, string>(entry.Key, ToPortableSoundReference(entry.Value)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !IsDefaultSound(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        snapshot.DefaultIconPreset = ToPortableIconReference(snapshot.DefaultIconPreset);
        snapshot.PerAppIcons = snapshot.PerAppIcons
            .Select(entry => new KeyValuePair<string, string>(entry.Key, ToPortableIconReference(entry.Value)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key)
                && !string.IsNullOrWhiteSpace(entry.Value)
                && !string.Equals(entry.Value, "None", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        snapshot.CardBackgroundImagePath = ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(
            snapshot.CardBackgroundImagePath,
            ManagedAssetPathHelper.BackgroundsFolderName);
        snapshot.FullscreenOverlayImagePath = ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(
            snapshot.FullscreenOverlayImagePath,
            ManagedAssetPathHelper.BackgroundsFolderName);
        snapshot.PerAppBackgroundImages = snapshot.PerAppBackgroundImages
            .Select(entry => new KeyValuePair<string, string>(
                entry.Key,
                ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(
                    entry.Value,
                    ManagedAssetPathHelper.BackgroundsFolderName)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        return snapshot;
    }

    public static void NormalizeForRuntime(AppSettings settings)
    {
        settings.DefaultSound = NormalizeSoundReference(settings.DefaultSound);
        settings.PerAppSounds = settings.PerAppSounds
            .Select(entry => new KeyValuePair<string, string>(entry.Key, NormalizeSoundReference(entry.Value)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !IsDefaultSound(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        settings.DefaultIconPreset = NormalizeIconReference(settings.DefaultIconPreset);
        settings.PerAppIcons = settings.PerAppIcons
            .Select(entry => new KeyValuePair<string, string>(entry.Key, NormalizeIconReference(entry.Value)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key)
                && !string.IsNullOrWhiteSpace(entry.Value)
                && !string.Equals(entry.Value, "None", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        settings.CardBackgroundImagePath = ManagedAssetPathHelper.ResolveManagedPathOrEmpty(
            settings.CardBackgroundImagePath,
            ManagedAssetPathHelper.BackgroundsFolderName);
        settings.FullscreenOverlayImagePath = ManagedAssetPathHelper.ResolveManagedPathOrEmpty(
            settings.FullscreenOverlayImagePath,
            ManagedAssetPathHelper.BackgroundsFolderName);
        settings.PerAppBackgroundImages = settings.PerAppBackgroundImages
            .Select(entry => new KeyValuePair<string, string>(
                entry.Key,
                ManagedAssetPathHelper.ResolveManagedPathOrEmpty(
                    entry.Value,
                    ManagedAssetPathHelper.BackgroundsFolderName)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        NormalizeSoundRules(settings);
    }

    // KR-Mnyang fork (schema v6): sound rule files are kept in portable form both in memory and
    // on disk — managed sounds as "sounds/<file>", trusted Windows sounds as absolute paths, and
    // files directly inside a local SoundLibraryFolders entry as absolute paths. Anything else is
    // dropped. Profiles/exports clear SoundLibraryFolders first, so external paths never travel.
    private static void NormalizeSoundRules(AppSettings settings)
    {
        settings.SoundLibraryFolders = NormalizeSoundLibraryFolders(settings.SoundLibraryFolders);
        settings.DefaultSoundRule = NormalizeSoundRule(settings.DefaultSoundRule, settings.SoundLibraryFolders);

        var rules = new Dictionary<string, SoundRule>(StringComparer.OrdinalIgnoreCase);
        if (settings.AppSoundRules != null)
        {
            foreach (var (key, rule) in settings.AppSoundRules)
            {
                var trimmedKey = key?.Trim();
                if (string.IsNullOrWhiteSpace(trimmedKey) || rule == null)
                    continue;

                rules[trimmedKey] = NormalizeSoundRule(rule, settings.SoundLibraryFolders);
            }
        }

        settings.AppSoundRules = rules;
    }

    private static SoundRule NormalizeSoundRule(SoundRule? rule, IReadOnlyList<string> libraryFolders)
    {
        var normalized = rule?.Clone() ?? new SoundRule();
        if (!Enum.IsDefined(normalized.Mode))
            normalized.Mode = SoundSelectionMode.Single;

        normalized.Files = (normalized.Files ?? new List<string>())
            .Select(file => ToPortableRuleSoundReferenceOrEmpty(file, libraryFolders))
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .ToList();
        return normalized;
    }

    internal static List<string> NormalizeSoundLibraryFolders(IEnumerable<string>? folders)
    {
        var result = new List<string>();
        if (folders == null)
            return result;

        foreach (var folder in folders)
        {
            var expanded = Environment.ExpandEnvironmentVariables(folder?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(expanded) || !Path.IsPathFullyQualified(expanded))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
            }
            catch
            {
                continue;
            }

            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                continue; // network/UNC folders are not allowed as local libraries

            if (!result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                result.Add(fullPath);
        }

        return result;
    }

    /// <summary>
    /// Converts one sound rule file to its stored form, or returns empty when it is not allowed.
    /// </summary>
    internal static string ToPortableRuleSoundReferenceOrEmpty(string? value, IReadOnlyList<string>? libraryFolders)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (IsDefaultSound(trimmed))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        var trustedSystemSound = SoundService.GetWindowsSounds()
            .FirstOrDefault(sound => string.Equals(sound.WavPath, expanded, StringComparison.OrdinalIgnoreCase));
        if (trustedSystemSound != null)
            return trustedSystemSound.WavPath;

        var portableManagedSound = ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(
            expanded,
            ManagedAssetPathHelper.SoundsFolderName);
        if (!string.IsNullOrWhiteSpace(portableManagedSound))
            return portableManagedSound;

        return TryResolveSoundLibraryFile(expanded, libraryFolders, out var libraryFile)
            ? libraryFile
            : string.Empty;
    }

    private static bool TryResolveSoundLibraryFile(string value, IReadOnlyList<string>? libraryFolders, out string fullPath)
    {
        fullPath = string.Empty;
        if (libraryFolders == null || libraryFolders.Count == 0 || !Path.IsPathFullyQualified(value))
            return false;

        string candidate;
        try
        {
            candidate = Path.GetFullPath(value);
        }
        catch
        {
            return false;
        }

        if (candidate.StartsWith(@"\\", StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(candidate), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Only files directly inside an allowed folder (no recursive subfolders, design §7.3).
        var parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(candidate) ?? string.Empty);
        if (!libraryFolders.Any(folder => string.Equals(folder, parent, StringComparison.OrdinalIgnoreCase)))
            return false;

        fullPath = candidate;
        return true;
    }

    private static string NormalizeSoundReference(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (IsDefaultSound(trimmed))
            return "None";

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        var trustedSystemSound = SoundService.GetWindowsSounds()
            .FirstOrDefault(sound => string.Equals(sound.WavPath, expanded, StringComparison.OrdinalIgnoreCase));
        if (trustedSystemSound != null)
            return trustedSystemSound.WavPath;

        var managedSoundPath = ManagedAssetPathHelper.ResolveManagedPathOrEmpty(
            expanded,
            ManagedAssetPathHelper.SoundsFolderName);

        return string.IsNullOrWhiteSpace(managedSoundPath)
            ? "None"
            : managedSoundPath;
    }

    private static string ToPortableSoundReference(string? value)
    {
        var normalized = NormalizeSoundReference(value);
        if (IsDefaultSound(normalized))
            return "None";

        var portableManagedSound = ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(
            normalized,
            ManagedAssetPathHelper.SoundsFolderName);

        return string.IsNullOrWhiteSpace(portableManagedSound)
            ? normalized
            : portableManagedSound;
    }

    private static string NormalizeIconReference(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
            return "None";

        if (IconPreset.BuiltInIcons.ContainsKey(trimmed))
            return trimmed;

        return ManagedAssetPathHelper.ResolveManagedPathOrEmpty(trimmed, ManagedAssetPathHelper.IconsFolderName);
    }

    private static string ToPortableIconReference(string? value)
    {
        var normalized = NormalizeIconReference(value);
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase))
            return "None";

        if (IconPreset.BuiltInIcons.ContainsKey(normalized))
            return normalized;

        return ManagedAssetPathHelper.ToPortableManagedPathOrEmpty(normalized, ManagedAssetPathHelper.IconsFolderName);
    }

    private static bool IsDefaultSound(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase);
    }
}
