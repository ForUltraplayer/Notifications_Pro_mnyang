using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using WinForms = System.Windows.Forms;

namespace NotificationsPro.Services;

public class SettingsManager
{
    // v6 = KR-Mnyang fork schema (sound rules, audio dispatch mode, app-local values). See SettingsSchemaMigrator.
    public const int CurrentSettingsSchemaVersion = SettingsSchemaMigrator.ForkSchemaVersion;
    internal const long MaxSettingsFileBytes = 1_048_576;
    internal const string SettingsFileName = "settings.json";
    internal const string CorruptBackupSuffixPrefix = ".corrupt-";
    internal const string BackupTimestampFormat = "yyyyMMdd-HHmmss";

    private readonly string _settingsDir;
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppSettings Settings { get; private set; } = new();
    public bool HadExistingSettingsFile { get; private set; }

    /// <summary>Folder that holds settings.json.</summary>
    public string SettingsDirectory => _settingsDir;

    /// <summary>
    /// True when settings.json was written by a newer schema than this build understands.
    /// The values are used for this session, but <see cref="Save"/> never overwrites the file.
    /// </summary>
    public bool IsReadOnlyDueToNewerSchema { get; private set; }

    /// <summary>Schema version found in settings.json at load time (null when absent, unreadable, or no file).</summary>
    public int? LoadedFileSchemaVersion { get; private set; }

    /// <summary>Copy of an unreadable/oversized settings.json preserved before defaults were saved over it.</summary>
    public string? PreservedCorruptFilePath { get; private set; }

    public SettingsSaveStatus LastSaveStatus { get; private set; } = SettingsSaveStatus.NotAttempted;

    /// <summary>Clock used for backup file names (overridable in tests).</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public event Action? SettingsChanged;

    public SettingsManager() : this(ForkIdentity.AppDataRoot) { }

    /// <summary>
    /// Constructor accepting a custom settings directory (used for testing).
    /// </summary>
    public SettingsManager(string settingsDir)
    {
        _settingsDir = settingsDir;
        _settingsPath = Path.Combine(settingsDir, SettingsFileName);
    }

    public void Load()
    {
        HadExistingSettingsFile = File.Exists(_settingsPath);
        IsReadOnlyDueToNewerSchema = false;
        LoadedFileSchemaVersion = null;
        PreservedCorruptFilePath = null;
        var unreadable = false;

        try
        {
            if (HadExistingSettingsFile)
            {
                var fileInfo = new FileInfo(_settingsPath);
                if (fileInfo.Length > MaxSettingsFileBytes)
                {
                    Settings = new AppSettings();
                    unreadable = true;
                }
                else
                {
                    var json = File.ReadAllText(_settingsPath);
                    LoadedFileSchemaVersion = TryReadSchemaVersion(json);
                    IsReadOnlyDueToNewerSchema = LoadedFileSchemaVersion > CurrentSettingsSchemaVersion;

                    if (TryParseSettingsJson(json, beforeAssetNormalization: null, out var loaded))
                    {
                        Settings = loaded;
                    }
                    else
                    {
                        Settings = new AppSettings();
                        unreadable = true;
                    }
                }
            }
        }
        catch
        {
            // If settings file is corrupted, use defaults silently.
            // Never log notification content — settings file never contains any.
            Settings = new AppSettings();
            unreadable = true;
        }

        if (IsReadOnlyDueToNewerSchema)
        {
            // A newer build wrote this file: use what could be read for this session, never rewrite it.
            return;
        }

        if (unreadable)
            PreserveUnreadableSettingsFile();

        var changed = SettingsSchemaMigrator.MigrateLoadedSettings(Settings, HadExistingSettingsFile);
        var primaryScreenHeight = WinForms.Screen.PrimaryScreen?.WorkingArea.Height;
        changed |= StartupSettingsMigrationHelper.Apply(Settings, primaryScreenHeight, HadExistingSettingsFile);
        if (changed)
            Save();
    }

    public void Save()
    {
        if (IsReadOnlyDueToNewerSchema)
        {
            LastSaveStatus = SettingsSaveStatus.SkippedReadOnly;
            return;
        }

        try
        {
            Directory.CreateDirectory(_settingsDir);
            // This build always writes its own schema. (Upstream only raised lower versions; a higher
            // value can now only come from an applied snapshot and would make the next start read-only.)
            Settings.SettingsSchemaVersion = CurrentSettingsSchemaVersion;
            var snapshot = AppSettingsAssetPathHelper.CreatePortableSnapshot(Settings);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            // Temp file + replace: a failed write leaves the previous settings.json intact.
            AtomicFileWriter.WriteAllText(_settingsPath, json);
            LastSaveStatus = SettingsSaveStatus.Saved;
        }
        catch
        {
            // Fail silently — settings are UI preferences only. The previous file is preserved.
            LastSaveStatus = SettingsSaveStatus.Failed;
        }
    }

    public void Apply(AppSettings updated)
    {
        NormalizeLoadedSettings(updated, hasCardBackgroundMode: true, hasNotificationAnimationStyle: true);
        AppSettingsAssetPathHelper.NormalizeForRuntime(updated);
        Settings = updated;
        Save();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Applies a whole-settings snapshot coming from a profile, an imported file, or undo/redo.
    /// App-local values (language, external sound folders, manual mute confirmations, restart intent, tray start)
    /// keep their current values, and pre-v6 snapshots get their legacy sounds converted to rules.
    /// </summary>
    public void ApplySnapshot(AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var current = Settings;
        SettingsSchemaMigrator.MigrateLegacySnapshot(snapshot, current);
        LocalOnlySettingsHelper.CopyLocalOnly(current, snapshot);
        Apply(snapshot);
    }

    /// <summary>
    /// Deserializes and normalizes settings JSON exactly like <see cref="Load"/> does (without migration).
    /// <paramref name="beforeAssetNormalization"/> can rewrite raw asset paths first (used by the legacy importer).
    /// </summary>
    internal static bool TryParseSettingsJson(string json, Action<AppSettings>? beforeAssetNormalization, out AppSettings settings)
    {
        settings = new AppSettings();
        try
        {
            var hasCardBackgroundMode = JsonPropertyExists(json, nameof(AppSettings.CardBackgroundMode));
            var hasNotificationAnimationStyle = JsonPropertyExists(json, nameof(AppSettings.NotificationAnimationStyle));
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded == null)
                return false;

            beforeAssetNormalization?.Invoke(loaded);
            NormalizeLoadedSettings(loaded, hasCardBackgroundMode, hasNotificationAnimationStyle);
            AppSettingsAssetPathHelper.NormalizeForRuntime(loaded);
            settings = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads only the top-level SettingsSchemaVersion number; null when absent or unreadable.</summary>
    internal static int? TryReadSchemaVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, nameof(AppSettings.SettingsSchemaVersion), StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var version))
                {
                    return version;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private void PreserveUnreadableSettingsFile()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            var backupPath = AtomicFileWriter.GetUniqueSiblingPath(
                _settingsPath,
                CorruptBackupSuffixPrefix + Clock().ToString(BackupTimestampFormat, System.Globalization.CultureInfo.InvariantCulture));
            File.Copy(_settingsPath, backupPath, overwrite: false);
            PreservedCorruptFilePath = backupPath;
        }
        catch
        {
            // Best effort. Without a preserved copy we still fall back to defaults like upstream.
        }
    }

    public void ResetToDefaults()
    {
        // KR-Mnyang fork (P3): "reset all" keeps this PC's local-only values (UI language, external
        // sound folders, manual mute confirmations, restart intent, tray start); see LocalOnlySettingsHelper.
        var defaults = new AppSettings();
        LocalOnlySettingsHelper.CopyLocalOnly(Settings, defaults);
        Settings = defaults;
        Save();
        SettingsChanged?.Invoke();
    }

    private static void NormalizeLoadedSettings(AppSettings settings, bool hasCardBackgroundMode, bool hasNotificationAnimationStyle)
    {
        settings.MutedApps ??= new List<string>();
        settings.HighlightKeywords ??= new List<string>();
        settings.PerKeywordColors ??= new Dictionary<string, string>();
        settings.MuteKeywords ??= new List<string>();
        settings.HighlightKeywordRegexFlags ??= new Dictionary<string, bool>();
        settings.MuteKeywordRegexFlags ??= new Dictionary<string, bool>();
        settings.HighlightRules ??= new List<HighlightRuleDefinition>();
        settings.MuteRules ??= new List<MuteRuleDefinition>();
        settings.NarrationRules ??= new List<NarrationRuleDefinition>();
        settings.SpokenMutedApps ??= new List<string>();
        settings.PerAppIcons ??= new Dictionary<string, string>();
        settings.PerAppSounds ??= new Dictionary<string, string>();
        settings.PerAppBackgroundImages ??= new Dictionary<string, string>();
        settings.PresentationApps ??= new List<string>();
        NormalizeForkSettings(settings);
        settings.ReadNotificationsAloudTriggerMode = NarrationTriggerModeHelper.Normalize(settings.ReadNotificationsAloudTriggerMode);
        settings.NotificationAnimationStyle = NotificationAnimationStyleHelper.Normalize(
            hasNotificationAnimationStyle
                ? settings.NotificationAnimationStyle
                : NotificationAnimationStyleHelper.FromLegacyFadeOnly(settings.FadeOnlyAnimation));
        settings.FadeOnlyAnimation = NotificationAnimationStyleHelper.IsLegacyFadeOnly(settings.NotificationAnimationStyle);
        settings.AnimationEasing = AnimationEasingHelper.Normalize(settings.AnimationEasing);
        settings.HighlightOverlayOpacity = double.IsNaN(settings.HighlightOverlayOpacity)
            ? 0.25
            : Math.Clamp(settings.HighlightOverlayOpacity, 0.05, 0.80);
        settings.HighlightAnimation = HighlightAnimationHelper.Normalize(settings.HighlightAnimation);
        settings.HighlightBorderMode = HighlightBorderModeHelper.Normalize(settings.HighlightBorderMode);
        settings.HighlightBorderThickness = double.IsNaN(settings.HighlightBorderThickness)
            ? 1
            : Math.Clamp(settings.HighlightBorderThickness, 0.5, 8.0);
        settings.MaxVisibleNotifications = Math.Clamp(settings.MaxVisibleNotifications, 1, AppSettings.MaxVisibleNotificationsUpperBound);
        settings.OverlayScrollbarTrackColor = string.IsNullOrWhiteSpace(settings.OverlayScrollbarTrackColor)
            ? "#141414"
            : settings.OverlayScrollbarTrackColor;
        settings.OverlayScrollbarThumbColor = string.IsNullOrWhiteSpace(settings.OverlayScrollbarThumbColor)
            ? "#4F4F4F"
            : settings.OverlayScrollbarThumbColor;
        settings.OverlayScrollbarThumbHoverColor = string.IsNullOrWhiteSpace(settings.OverlayScrollbarThumbHoverColor)
            ? "#0078D4"
            : settings.OverlayScrollbarThumbHoverColor;
        settings.OverlayScrollbarPadding = double.IsNaN(settings.OverlayScrollbarPadding)
            ? 1.5
            : Math.Clamp(settings.OverlayScrollbarPadding, 0.0, 6.0);
        settings.OverlayScrollbarContentGap = double.IsNaN(settings.OverlayScrollbarContentGap)
            ? 10.0
            : Math.Clamp(settings.OverlayScrollbarContentGap, 0.0, 24.0);
        settings.OverlayScrollbarCornerRadius = double.IsNaN(settings.OverlayScrollbarCornerRadius)
            ? 6.0
            : Math.Clamp(settings.OverlayScrollbarCornerRadius, 0.0, 12.0);
        settings.CardBackgroundMode = CardBackgroundModeHelper.Normalize(
            !hasCardBackgroundMode && !string.IsNullOrWhiteSpace(settings.CardBackgroundImagePath)
                ? CardBackgroundModeHelper.Image
                : settings.CardBackgroundMode);
        settings.CardBackgroundImageOpacity = double.IsNaN(settings.CardBackgroundImageOpacity)
            ? 0.45
            : Math.Clamp(settings.CardBackgroundImageOpacity, 0.0, 1.0);
        settings.CardBackgroundImageHueDegrees = double.IsNaN(settings.CardBackgroundImageHueDegrees)
            ? 0.0
            : Math.Clamp(settings.CardBackgroundImageHueDegrees, -180, 180);
        settings.CardBackgroundImageBrightness = double.IsNaN(settings.CardBackgroundImageBrightness)
            ? 1.0
            : Math.Clamp(settings.CardBackgroundImageBrightness, 0.2, 2.0);
        settings.CardBackgroundImageSaturation = double.IsNaN(settings.CardBackgroundImageSaturation)
            ? 1.0
            : Math.Clamp(settings.CardBackgroundImageSaturation, 0.0, 2.0);
        settings.CardBackgroundImageContrast = double.IsNaN(settings.CardBackgroundImageContrast)
            ? 1.0
            : Math.Clamp(settings.CardBackgroundImageContrast, 0.2, 2.0);
        settings.CardBackgroundImageFitMode = CardBackgroundImageFitModeHelper.Normalize(settings.CardBackgroundImageFitMode);
        settings.CardBackgroundImagePlacement = CardBackgroundImagePlacementHelper.Normalize(settings.CardBackgroundImagePlacement);
        settings.CardBackgroundImageVerticalFocus = ImageVerticalFocusHelper.Normalize(settings.CardBackgroundImageVerticalFocus);
        settings.FullscreenOverlayImageHueDegrees = double.IsNaN(settings.FullscreenOverlayImageHueDegrees)
            ? 0.0
            : Math.Clamp(settings.FullscreenOverlayImageHueDegrees, -180, 180);
        settings.FullscreenOverlayImageBrightness = double.IsNaN(settings.FullscreenOverlayImageBrightness)
            ? 1.0
            : Math.Clamp(settings.FullscreenOverlayImageBrightness, 0.2, 2.0);
        settings.FullscreenOverlayImageSaturation = double.IsNaN(settings.FullscreenOverlayImageSaturation)
            ? 1.0
            : Math.Clamp(settings.FullscreenOverlayImageSaturation, 0.0, 2.0);
        settings.FullscreenOverlayImageContrast = double.IsNaN(settings.FullscreenOverlayImageContrast)
            ? 1.0
            : Math.Clamp(settings.FullscreenOverlayImageContrast, 0.2, 2.0);
        settings.FullscreenOverlayImageFitMode = CardBackgroundImageFitModeHelper.Normalize(settings.FullscreenOverlayImageFitMode);
        settings.FullscreenOverlayImageVerticalFocus = ImageVerticalFocusHelper.Normalize(settings.FullscreenOverlayImageVerticalFocus);
        foreach (var rule in settings.HighlightRules)
        {
            rule.Scope = NotificationMatchScopeHelper.Normalize(rule.Scope);
            rule.AppFilter = rule.AppFilter?.Trim() ?? string.Empty;
            rule.Animation = string.IsNullOrWhiteSpace(rule.Animation)
                ? string.Empty
                : HighlightAnimationHelper.Normalize(rule.Animation);
            rule.BorderMode = string.IsNullOrWhiteSpace(rule.BorderMode)
                ? string.Empty
                : HighlightBorderModeHelper.Normalize(rule.BorderMode);
            rule.OverlayOpacity = rule.OverlayOpacity.HasValue
                ? Math.Clamp(rule.OverlayOpacity.Value, 0.05, 0.80)
                : null;
            rule.BorderThickness = rule.BorderThickness.HasValue
                ? Math.Clamp(rule.BorderThickness.Value, 0.5, 8.0)
                : null;
        }
        settings.SettingsThemeMode = SettingsThemeService.ResolveThemeModeForLoadedSettings(settings);
    }

    // KR-Mnyang fork (schema v6): null-safety, case-insensitive app keys, value ranges.
    // Sound rule file paths are normalized by AppSettingsAssetPathHelper.NormalizeForRuntime.
    private static void NormalizeForkSettings(AppSettings settings)
    {
        settings.UiLanguage = NormalizeUiLanguage(settings.UiLanguage);
        if (!Enum.IsDefined(settings.AudioDispatchMode))
            settings.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
        settings.DefaultSoundRule ??= new SoundRule();
        settings.AppSoundRules = AppSettings.CloneSoundRules(settings.AppSoundRules);
        settings.SoundLibraryFolders ??= new List<string>();
        settings.PlaybackPolicy ??= new PlaybackPolicy();
        settings.PlaybackPolicy.Normalize();
        settings.ManualMuteConfirmations = AppSettings.CloneManualMuteConfirmations(settings.ManualMuteConfirmations);
        settings.SoundDisplayNames = AppSettings.CloneSoundDisplayNames(settings.SoundDisplayNames);
    }

    /// <summary>Only "ko-KR" and "en-US" are stored; anything else means "use the OS default" (null).</summary>
    internal static string? NormalizeUiLanguage(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "ko-KR", StringComparison.OrdinalIgnoreCase))
            return "ko-KR";
        if (string.Equals(trimmed, "en-US", StringComparison.OrdinalIgnoreCase))
            return "en-US";
        return null;
    }

    private static bool JsonPropertyExists(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
