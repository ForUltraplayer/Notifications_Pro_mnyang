// @file LocalizationResourceTests.cs
// @author: rudals252
// @brief P3 리소스 검증: ko-KR에 영어 키가 모두 있음, {0} 자리표시자 일치, 서식 문자열 유효, 번역 금지 대상(포크 정체성·CLI 인자·경로) 미포함, 기술 토큰 보존, XAML·C#이 참조하는 키 존재, 쓰지 않는 키 없음, 콤보 저장값 표시 키 존재.

using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NotificationsPro.Helpers;
using NotificationsPro.Localization;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class LocalizationResourceTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{\d+(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    private static string RepoRoot([CallerFilePath] string path = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    private static string SourceRoot => Path.Combine(RepoRoot(), "src", "NotificationsPro");

    private static Dictionary<string, string> LoadResx(string fileName)
    {
        var document = XDocument.Load(Path.Combine(SourceRoot, "Resources", fileName));
        return document.Root!.Elements("data")
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => (string?)data.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static Dictionary<string, string> English => LoadResx("Strings.resx");

    private static Dictionary<string, string> Korean => LoadResx("Strings.ko-KR.resx");

    private static List<string> Placeholders(string value)
        => PlaceholderPattern.Matches(value).Select(match => match.Value).OrderBy(token => token, StringComparer.Ordinal).ToList();

    [Fact]
    public void KoreanResources_ContainExactlyTheEnglishKeys()
    {
        var english = English;
        var korean = Korean;

        Assert.NotEmpty(english);
        Assert.Empty(english.Keys.Except(korean.Keys));
        Assert.Empty(korean.Keys.Except(english.Keys));
    }

    [Fact]
    public void AllValues_AreNonEmpty()
    {
        Assert.DoesNotContain(English, pair => string.IsNullOrWhiteSpace(pair.Value));
        Assert.DoesNotContain(Korean, pair => string.IsNullOrWhiteSpace(pair.Value));
    }

    [Fact]
    public void Placeholders_MatchBetweenEnglishAndKorean()
    {
        var korean = Korean;
        var mismatches = English
            .Where(pair => !Placeholders(pair.Value).SequenceEqual(Placeholders(korean[pair.Key])))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void FormatStrings_AreValidInBothLanguages()
    {
        foreach (var (name, values) in new[] { ("en", English), ("ko", Korean) })
        {
            foreach (var pair in values)
            {
                var indices = PlaceholderPattern.Matches(pair.Value)
                    .Select(match => int.Parse(Regex.Match(match.Value, @"\d+").Value))
                    .ToList();
                var args = Enumerable.Repeat((object)7, indices.Count == 0 ? 0 : indices.Max() + 1).ToArray();
                var exception = Record.Exception(() => string.Format(System.Globalization.CultureInfo.InvariantCulture, pair.Value, args));
                Assert.True(exception == null, $"{name}:{pair.Key} is not a valid format string");
            }
        }
    }

    [Fact]
    public void Values_NeverContainForkIdentityCliArgumentsOrPaths()
    {
        var forbidden = new[]
        {
            ForkIdentity.ProductDisplayName,
            ForkIdentity.AppDataFolderName,
            ForkIdentity.ExecutableFileName,
            ForkIdentity.UnpackagedAppUserModelId,
            ForkIdentity.SingleInstanceMutexName,
            ForkIdentity.ShowSettingsEventName,
            ForkIdentity.LogonTaskName,
            "--pause", "--resume", "--theme", "--send-test", "--hide", "--show",
            "%AppData%", "HKEY_", @"C:\",
        };

        foreach (var values in new[] { English, Korean })
        {
            foreach (var pair in values)
            {
                foreach (var token in forbidden)
                    Assert.False(pair.Value.Contains(token, StringComparison.OrdinalIgnoreCase), $"{pair.Key} contains '{token}'");
            }
        }
    }

    [Fact]
    public void TechnicalTokens_StayUntranslatedInKorean()
    {
        var tokens = new[]
        {
            "Notifications Pro", "WinRT", "AUMID", "OBS", "WAV", "JSON", "HEX", "UI", "DWM", "XAML", "API",
            "settings.json", "Ctrl+", "Shift+", "HH:mm", "Voice Access", "GitHub", "PowerPoint",
            "Teams", "Slack", "Outlook", "Windows 11",
        };
        var hexColor = new Regex(@"#[0-9A-Fa-f]{6}\b");
        var filePattern = new Regex(@"\*\.[a-z]+");
        var korean = Korean;

        foreach (var pair in English)
        {
            var ko = korean[pair.Key];
            foreach (var token in tokens)
            {
                // Whole-token match so "UI" inside "QUIET" does not count.
                var pattern = new Regex("(?<![A-Za-z])" + Regex.Escape(token) + "(?![A-Za-z])");
                if (pattern.IsMatch(pair.Value))
                    Assert.True(pattern.IsMatch(ko), $"{pair.Key}: '{token}' missing in Korean");
            }

            foreach (Match match in hexColor.Matches(pair.Value))
                Assert.True(ko.Contains(match.Value, StringComparison.OrdinalIgnoreCase), $"{pair.Key}: '{match.Value}' missing in Korean");
            foreach (Match match in filePattern.Matches(pair.Value))
                Assert.True(ko.Contains(match.Value, StringComparison.Ordinal), $"{pair.Key}: '{match.Value}' missing in Korean");
        }
    }

    [Fact]
    public void DialogFilters_KeepPatternSegments()
    {
        var korean = Korean;
        foreach (var pair in English.Where(pair => pair.Key.StartsWith("Dialog_", StringComparison.Ordinal) && pair.Key.EndsWith("Filter", StringComparison.Ordinal)))
        {
            var en = pair.Value.Split('|');
            var ko = korean[pair.Key].Split('|');
            Assert.Equal(en.Length, ko.Length);
            for (var i = 1; i < en.Length; i += 2)
                Assert.Equal(en[i], ko[i]);
        }
    }

    [Fact]
    public void EveryKeyReferencedInXamlAndCode_Exists()
    {
        var keys = English.Keys.ToHashSet(StringComparer.Ordinal);
        var prefixes = keys.Select(key => key.Split('_')[0]).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var reference in ReferencedKeys(prefixes))
        {
            if (!keys.Contains(reference.Key))
                missing.Add($"{reference.Key} ({reference.File})");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryResourceKey_IsUsed()
    {
        var keys = English.Keys.ToHashSet(StringComparer.Ordinal);
        var prefixes = keys.Select(key => key.Split('_')[0]).ToHashSet(StringComparer.Ordinal);
        var used = ReferencedKeys(prefixes).Select(reference => reference.Key).ToHashSet(StringComparer.Ordinal);

        // Option_/ThemeMode_ keys are looked up from stored values at run time (LocOptionExtension).
        var unused = keys
            .Where(key => !used.Contains(key)
                && !key.StartsWith(LocOptionExtension.DefaultPrefix + "_", StringComparison.Ordinal)
                && !key.StartsWith("ThemeMode_", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(unused);
    }

    [Fact]
    public void StoredOptionValues_HaveDisplayKeys()
    {
        var keys = English.Keys.ToHashSet(StringComparer.Ordinal);
        var values = new List<string>();
        values.AddRange(new[] { "Thin", "Light", "Normal", "Medium", "SemiBold", "Bold" });
        values.AddRange(new[] { "Left", "Center", "Right", "Top", "Bottom" });
        values.AddRange(new[] { "Relative", "Time", "DateTime" });
        values.AddRange(new[] { "Framed Group", "Header Chip", "Minimal Label" });
        values.AddRange(new[] { "Window", "Popup" });
        values.AddRange(new[] { "Compact", "Comfortable", "Spacious", "Default" });
        values.AddRange(NotificationAnimationStyleHelper.KnownModes);
        values.AddRange(AnimationEasingHelper.KnownModes);
        values.AddRange(HighlightAnimationHelper.KnownModes);
        values.AddRange(HighlightBorderModeHelper.KnownModes);
        values.AddRange(CardBackgroundModeHelper.KnownModes);
        values.AddRange(CardBackgroundImageFitModeHelper.KnownModes);
        values.AddRange(CardBackgroundImagePlacementHelper.KnownPlacements);
        values.AddRange(ImageVerticalFocusHelper.KnownModes);
        values.AddRange(NotificationMatchScopeHelper.KnownScopes);
        values.AddRange(NarrationRuleActionHelper.KnownActions);
        values.AddRange(NarrationTriggerModeHelper.KnownModes);
        values.Add(NarrationRuleReadModeHelper.UseGlobal);
        values.Add(KeywordHighlightEntry.UseGlobalSetting);
        values.AddRange(new[]
        {
            SpokenNotificationTextFormatter.ModeBodyOnly, SpokenNotificationTextFormatter.ModeTitleOnly,
            SpokenNotificationTextFormatter.ModeTitleBody, SpokenNotificationTextFormatter.ModeBodyTimestamp,
            SpokenNotificationTextFormatter.ModeTitleTimestamp, SpokenNotificationTextFormatter.ModeTitleBodyTimestamp,
            VoiceAccessTextFormatter.ModeOff, VoiceAccessTextFormatter.ModeBodyOnly, VoiceAccessTextFormatter.ModeTitleBodyTimestamp,
            NotificationCaptureModeHelper.ModeAuto, NotificationCaptureModeHelper.ModeWinRt, NotificationCaptureModeHelper.ModeAccessibility,
        });
        values.AddRange(Models.IconPreset.PresetNames);

        var missing = values
            .Distinct(StringComparer.Ordinal)
            .Select(value => LocOptionExtension.KeyFor(LocOptionExtension.DefaultPrefix, value))
            .Where(key => !keys.Contains(key))
            .ToList();

        Assert.Empty(missing);
        Assert.Contains("ThemeMode_System", keys);
        Assert.Contains("ThemeMode_Custom", keys);
    }

    private static IEnumerable<(string Key, string File)> ReferencedKeys(HashSet<string> prefixes)
    {
        var xamlPattern = new Regex(@"\{loc:Loc(?:Format)?\s+([A-Za-z0-9_]+)");
        var codePattern = new Regex("\"([A-Za-z]+_[A-Za-z0-9_]+)\"");

        foreach (var file in Directory.EnumerateFiles(SourceRoot, "*.xaml", SearchOption.AllDirectories).Where(IsSource))
        {
            foreach (Match match in xamlPattern.Matches(File.ReadAllText(file)))
                yield return (match.Groups[1].Value, Path.GetFileName(file));
        }

        foreach (var file in Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories).Where(IsSource))
        {
            foreach (Match match in codePattern.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (prefixes.Contains(key.Split('_')[0]))
                    yield return (key, Path.GetFileName(file));
            }
        }
    }

    private static bool IsSource(string path)
    {
        var relative = Path.GetRelativePath(SourceRoot, path);
        return !relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
