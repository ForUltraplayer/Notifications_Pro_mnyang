// @file SoundRuleSelector.cs
// @author: rudals252
// @brief 앱 식별로 소리 규칙을 찾고(AUMID → 표시 이름 → 기본), 단일/랜덤/순차 모드로 후보 파일을 고른다. 후보 결정과 소비 확정(Commit)을 분리해 실제 재생이 시작된 때만 순서·직전 파일을 갱신한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.Services;

/// <summary>
/// Rule lookup and file choice (design §7.1, §7.2). The rule set (<see cref="AppSettings.DefaultSoundRule"/>,
/// <see cref="AppSettings.AppSoundRules"/>) is the single source of truth; the legacy
/// <see cref="AppSettings.DefaultSound"/>/<see cref="AppSettings.PerAppSounds"/> fields are not read.
/// <see cref="AppSettings.SoundEnabled"/> stays the master switch.
/// History (sequence position, last played file) is kept per history key in RAM only and changes only
/// through <see cref="Commit"/>, which callers invoke once playback has actually started. Previews,
/// cooldown skips and expired queue entries therefore never consume the order. A new instance
/// (app restart) starts every sequence from the first file.
/// </summary>
public sealed class SoundRuleSelector
{
    internal const int MaxHistoryEntries = 512;
    private const string AppKeyPrefix = "app:";
    private const string DefaultKeyPrefix = "default:";

    private readonly Func<int, int> _nextIndex;
    private readonly Dictionary<string, HistoryEntry> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <param name="nextIndex">Returns a uniform index in [0, maxExclusive). Injectable for tests.</param>
    public SoundRuleSelector(Func<int, int>? nextIndex = null)
    {
        _nextIndex = nextIndex ?? (maxExclusive => Random.Shared.Next(maxExclusive));
    }

    /// <summary>
    /// Finds the rule for an app: AUMID key, then display-name key, then the default rule.
    /// An app rule with <see cref="SoundRule.ExplicitSilence"/> is silent and never falls back.
    /// An app rule with <see cref="SoundRule.Enabled"/> = false inherits the default rule.
    /// History keys: "app:&lt;matched key&gt;" for app rules, "default:&lt;AUMID or name&gt;" otherwise,
    /// so apps that share the default rule still keep separate sequences.
    /// </summary>
    public SoundRuleResolution Resolve(AppSettings settings, string? appName, string? appId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var identity = (string.IsNullOrWhiteSpace(appId) ? appName : appId)?.Trim() ?? string.Empty;
        var defaultHistoryKey = DefaultKeyPrefix + identity;

        if (!settings.SoundEnabled)
            return new SoundRuleResolution(null, null, defaultHistoryKey, AppIdentityMatchKind.None, SoundSilenceReason.MasterSwitchOff);

        var matchKind = AppIdentityMatcher.TryFind(settings.AppSoundRules, appId, appName, out var appRule, out var matchedKey);
        if (matchKind != AppIdentityMatchKind.None && appRule != null && matchedKey != null)
        {
            var historyKey = AppKeyPrefix + matchedKey;
            if (appRule.ExplicitSilence)
                return new SoundRuleResolution(appRule, matchedKey, historyKey, matchKind, SoundSilenceReason.ExplicitSilence);

            if (appRule.Enabled)
                return Finish(appRule, matchedKey, historyKey, matchKind);
        }

        var defaultRule = settings.DefaultSoundRule ?? new SoundRule();
        if (defaultRule.ExplicitSilence)
            return new SoundRuleResolution(defaultRule, null, defaultHistoryKey, AppIdentityMatchKind.None, SoundSilenceReason.ExplicitSilence);

        if (!defaultRule.Enabled)
            return new SoundRuleResolution(defaultRule, null, defaultHistoryKey, AppIdentityMatchKind.None, SoundSilenceReason.RuleDisabled);

        return Finish(defaultRule, null, defaultHistoryKey, AppIdentityMatchKind.None);
    }

    /// <summary>Chooses a candidate for a resolved rule without changing history.</summary>
    public SoundSelection Select(SoundRuleResolution resolution, Func<string, bool> isUsable)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.IsSilent)
            return SoundSelection.Silent(resolution.SilenceReason == SoundSilenceReason.None
                ? SoundSilenceReason.EmptyList
                : resolution.SilenceReason);

        return Select(resolution.Rule!, resolution.HistoryKey, isUsable);
    }

    /// <summary>
    /// Chooses a candidate without changing history:
    /// Single = the first listed file; Random = uniform over usable files (the last played file is
    /// excluded when <see cref="SoundRule.AvoidRepeat"/> is on and another usable file exists);
    /// Sequential = the next usable file in list order from the saved position, wrapping around.
    /// Unusable (missing/damaged/failed) files are skipped; when none is usable the result is silent.
    /// </summary>
    public SoundSelection Select(SoundRule rule, string historyKey, Func<string, bool> isUsable)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(isUsable);

        var files = rule.Files ?? new List<string>();
        if (files.Count == 0)
            return SoundSelection.Silent(SoundSilenceReason.EmptyList);

        HistoryEntry? history;
        lock (_sync)
            _history.TryGetValue(historyKey ?? string.Empty, out history);

        switch (rule.Mode)
        {
            case SoundSelectionMode.Random:
                return SelectRandom(files, rule.AvoidRepeat, history?.LastFile, isUsable);

            case SoundSelectionMode.Sequential:
                return SelectSequential(files, history?.NextIndex ?? 0, isUsable);

            default:
                var first = files[0];
                return !string.IsNullOrWhiteSpace(first) && SafeIsUsable(isUsable, first)
                    ? new SoundSelection(first, 0, SoundSilenceReason.None)
                    : SoundSelection.Silent(SoundSilenceReason.AllFilesUnavailable);
        }
    }

    /// <summary>Records that <paramref name="selection"/> actually started playing.</summary>
    public void Commit(string historyKey, SoundSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.HasFile)
            return;

        var key = historyKey ?? string.Empty;
        lock (_sync)
        {
            if (!_history.ContainsKey(key) && _history.Count >= MaxHistoryEntries)
                _history.Remove(_history.Keys.First());

            _history[key] = new HistoryEntry(selection.File, selection.Index + 1);
        }
    }

    public string? GetLastPlayed(string historyKey)
    {
        lock (_sync)
            return _history.TryGetValue(historyKey ?? string.Empty, out var entry) ? entry.LastFile : null;
    }

    public int GetNextSequentialIndex(string historyKey)
    {
        lock (_sync)
            return _history.TryGetValue(historyKey ?? string.Empty, out var entry) ? entry.NextIndex : 0;
    }

    public int HistoryCount
    {
        get
        {
            lock (_sync)
                return _history.Count;
        }
    }

    public void ResetHistory()
    {
        lock (_sync)
            _history.Clear();
    }

    private static SoundRuleResolution Finish(SoundRule rule, string? ruleKey, string historyKey, AppIdentityMatchKind kind)
    {
        var reason = rule.Files == null || rule.Files.Count == 0
            ? SoundSilenceReason.EmptyList
            : SoundSilenceReason.None;
        return new SoundRuleResolution(rule, ruleKey, historyKey, kind, reason);
    }

    private SoundSelection SelectRandom(List<string> files, bool avoidRepeat, string? lastFile, Func<string, bool> isUsable)
    {
        var usable = new List<(string File, int Index)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (string.IsNullOrWhiteSpace(file) || !seen.Add(file))
                continue;
            if (SafeIsUsable(isUsable, file))
                usable.Add((file, i));
        }

        if (usable.Count == 0)
            return SoundSelection.Silent(SoundSilenceReason.AllFilesUnavailable);

        if (avoidRepeat && usable.Count > 1 && lastFile != null)
        {
            var filtered = usable
                .Where(candidate => !string.Equals(candidate.File, lastFile, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count > 0)
                usable = filtered;
        }

        var pick = usable.Count == 1 ? 0 : Math.Clamp(_nextIndex(usable.Count), 0, usable.Count - 1);
        return new SoundSelection(usable[pick].File, usable[pick].Index, SoundSilenceReason.None);
    }

    private static SoundSelection SelectSequential(List<string> files, int nextIndex, Func<string, bool> isUsable)
    {
        var count = files.Count;
        var start = ((nextIndex % count) + count) % count;
        for (var offset = 0; offset < count; offset++)
        {
            var index = (start + offset) % count;
            var file = files[index];
            if (!string.IsNullOrWhiteSpace(file) && SafeIsUsable(isUsable, file))
                return new SoundSelection(file, index, SoundSilenceReason.None);
        }

        return SoundSelection.Silent(SoundSilenceReason.AllFilesUnavailable);
    }

    private static bool SafeIsUsable(Func<string, bool> isUsable, string file)
    {
        try
        {
            return isUsable(file);
        }
        catch
        {
            return false;
        }
    }

    private sealed record HistoryEntry(string? LastFile, int NextIndex);
}
