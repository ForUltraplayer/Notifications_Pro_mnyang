// @file AppIdentityAndFingerprintTests.cs
// @author: rudals252
// @brief AppIdentityMatcher 우선순위(AUMID > 표시 이름 > 없음)·대소문자, AppIdentityRegistry 상한, ContentFingerprintCache의 TTL·크기 제한·본문 미보관을 고정한다.

using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class AppIdentityAndFingerprintTests
{
    private const string CodexAumid = "OpenAI.Codex_2p2nqsd0c76g0!App";

    [Fact]
    public void Matcher_PrefersExactAumidOverDisplayName()
    {
        var map = new Dictionary<string, string>
        {
            ["ChatGPT"] = "by-name",
            [CodexAumid] = "by-aumid",
        };

        var kind = AppIdentityMatcher.TryFind(map, CodexAumid, "ChatGPT", out var value, out var key);

        Assert.Equal(AppIdentityMatchKind.AppId, kind);
        Assert.Equal("by-aumid", value);
        Assert.Equal(CodexAumid, key);
    }

    [Fact]
    public void Matcher_FallsBackToDisplayName_WhenAumidMissingOrUnknown()
    {
        var map = new Dictionary<string, string> { ["Discord"] = "name-rule" };

        Assert.Equal(AppIdentityMatchKind.DisplayName, AppIdentityMatcher.TryFind(map, null, "Discord", out var v1, out _));
        Assert.Equal("name-rule", v1);
        Assert.Equal(AppIdentityMatchKind.DisplayName, AppIdentityMatcher.TryFind(map, "Discord.Other!App", "discord", out var v2, out _));
        Assert.Equal("name-rule", v2);
    }

    [Fact]
    public void Matcher_ReturnsNone_WhenNothingMatches()
    {
        var map = new Dictionary<string, string> { ["Discord"] = "x" };

        Assert.Equal(AppIdentityMatchKind.None, AppIdentityMatcher.TryFind(map, "Slack.App", "Slack", out var value, out var key));
        Assert.Null(value);
        Assert.Null(key);
        Assert.Equal(AppIdentityMatchKind.None, AppIdentityMatcher.TryFind<string>(null, CodexAumid, "Codex", out _, out _));
    }

    [Fact]
    public void Matcher_IsCaseInsensitive_EvenForOrdinalDictionaries()
    {
        var ordinal = new Dictionary<string, string>(StringComparer.Ordinal) { [CodexAumid] = "x" };

        Assert.Equal(AppIdentityMatchKind.AppId, AppIdentityMatcher.TryFind(ordinal, CodexAumid.ToLowerInvariant(), null, out var value, out _));
        Assert.Equal("x", value);
    }

    [Fact]
    public void FindAppSoundRule_UsesAumidThenNameAndKeepsLegacyNameRules()
    {
        var settings = new AppSettings();
        settings.AppSoundRules["Discord"] = new SoundRule { Files = { "sounds/name.wav" } };
        settings.AppSoundRules[CodexAumid] = new SoundRule { ExplicitSilence = true };
        settings.PerAppSounds["Discord"] = "sounds/name.wav";
        settings.MutedApps.Add("Slack");

        Assert.Equal(AppIdentityMatchKind.AppId, AppIdentityMatcher.FindAppSoundRule(settings, CodexAumid, "ChatGPT", out var codexRule));
        Assert.True(codexRule!.ExplicitSilence);
        Assert.Equal(AppIdentityMatchKind.DisplayName, AppIdentityMatcher.FindAppSoundRule(settings, "Discord.Store!App", "Discord", out var discordRule));
        Assert.Equal("sounds/name.wav", Assert.Single(discordRule!.Files));
        Assert.Equal(AppIdentityMatchKind.None, AppIdentityMatcher.FindAppSoundRule(settings, "Other!App", "Other", out _));

        // Existing name-based lists are untouched and still match by display name only.
        Assert.True(AppIdentityMatcher.ContainsDisplayName(settings.MutedApps, "slack"));
        Assert.False(AppIdentityMatcher.ContainsDisplayName(settings.MutedApps, "Slack.Desktop!App"));
        Assert.Single(settings.PerAppSounds);
    }

    [Fact]
    public void Registry_IgnoresBlankValues_AndIsBounded()
    {
        var registry = new AppIdentityRegistry();
        registry.Observe("", "X.App");
        registry.Observe("Name", "");
        registry.Observe(null, null);
        Assert.Empty(registry.GetAmbiguousDisplayNames());

        for (var i = 0; i < AppIdentityRegistry.MaxAppIdsPerDisplayName + 5; i++)
            registry.Observe("Browser", $"Site{i}.App");

        Assert.Equal(AppIdentityRegistry.MaxAppIdsPerDisplayName, registry.GetAppIds("Browser").Count);
        Assert.True(registry.HasMultipleAppIds("BROWSER"));
    }

    [Fact]
    public void Fingerprint_IsStableAndContentSensitive()
    {
        var a = ContentFingerprintCache.ComputeFingerprint("App", "Title", "Body");
        var b = ContentFingerprintCache.ComputeFingerprint("App", "Title", "Body");
        var c = ContentFingerprintCache.ComputeFingerprint("App", "Title", "Body!");
        var d = ContentFingerprintCache.ComputeFingerprint("AppT", "itle", "Body");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Fact]
    public void FingerprintCache_ExpiresAfterWindow()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        var cache = new ContentFingerprintCache(clock: () => now);
        var fingerprint = ContentFingerprintCache.ComputeFingerprint("App", "T", "B");
        var window = TimeSpan.FromSeconds(2);

        cache.Record(fingerprint, window);
        now = now.AddSeconds(1);
        Assert.True(cache.Contains(fingerprint, window));
        now = now.AddSeconds(1.5);
        Assert.False(cache.Contains(fingerprint, window));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void FingerprintCache_IsBoundedInSize()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        var cache = new ContentFingerprintCache(maxEntries: 3, clock: () => now);
        var window = TimeSpan.FromMinutes(5);
        var first = ContentFingerprintCache.ComputeFingerprint("A", "0", "0");

        cache.Record(first, window);
        for (var i = 1; i <= 3; i++)
            cache.Record(ContentFingerprintCache.ComputeFingerprint("A", i.ToString(), "x"), window);

        Assert.Equal(3, cache.Count);
        Assert.False(cache.Contains(first, window));
    }

    [Fact]
    public void FingerprintCache_StoresNoText()
    {
        var stringFields = typeof(ContentFingerprintCache)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(string) || f.FieldType.ToString().Contains("String", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(stringFields);
    }
}
