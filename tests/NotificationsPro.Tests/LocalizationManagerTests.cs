// @file LocalizationManagerTests.cs
// @author: rudals252
// @brief P3 LocalizationManager 단위 테스트: 기본 문화권 영어, 한국어 조회, 한국어 키 누락 시 영어 대체(테스트 전용 resx), 전체 누락 시 키 반환, 저장값 표시 키 규칙. 전역 문화권은 바꾸지 않는다.

using System.Globalization;
using System.Resources;
using NotificationsPro.Localization;

namespace NotificationsPro.Tests;

public class LocalizationManagerTests
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static ResourceManager FallbackResources { get; } =
        new("NotificationsPro.Tests.TestResources.FallbackStrings", typeof(LocalizationManagerTests).Assembly);

    [Fact]
    public void NewManager_StartsInEnglish()
    {
        var manager = new LocalizationManager(LocalizationManager.DefaultResourceManager);

        Assert.Equal("en-US", manager.Culture.Name);
        Assert.Equal("Reset to Defaults", manager.GetString("Shell_ResetToDefaults"));
    }

    [Fact]
    public void Lookup_ReturnsKoreanForKoKr_AndEnglishOtherwise()
    {
        var resources = LocalizationManager.DefaultResourceManager;

        Assert.Equal("기본값으로 초기화", LocalizationManager.Lookup(resources, "Shell_ResetToDefaults", Korean));
        Assert.Equal("Reset to Defaults", LocalizationManager.Lookup(resources, "Shell_ResetToDefaults", English));
        Assert.Equal("Reset to Defaults", LocalizationManager.Lookup(resources, "Shell_ResetToDefaults", CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void MissingKoreanKey_FallsBackToEnglish()
    {
        Assert.Equal("둘 다 (ko)", LocalizationManager.Lookup(FallbackResources, "Both", Korean));
        Assert.Equal("Only in English", LocalizationManager.Lookup(FallbackResources, "OnlyInEnglish", Korean));
    }

    [Fact]
    public void KeyMissingEverywhere_ReturnsTheKey()
    {
        var manager = new LocalizationManager(FallbackResources);

        Assert.Null(manager.TryGetString("NoSuchKey"));
        Assert.False(manager.HasKey("NoSuchKey"));
        Assert.Equal("NoSuchKey", manager.GetString("NoSuchKey"));
        Assert.True(manager.HasKey("OnlyInEnglish"));
    }

    [Theory]
    [InlineData("ko-KR", "en-US", "ko-KR")]
    [InlineData("en-US", "ko-KR", "en-US")]
    [InlineData(null, "ko-KR", "ko-KR")]
    [InlineData(null, "ko", "ko-KR")]
    [InlineData(null, "en-US", "en-US")]
    [InlineData(null, "ja-JP", "en-US")]
    [InlineData(null, "", "en-US")]
    [InlineData("fr-FR", "ko-KR", "ko-KR")]
    public void ResolveCulture_ExplicitChoiceWins_OtherwiseOsKoreanSelectsKorean(string? uiLanguage, string osCulture, string expected)
    {
        var os = string.IsNullOrEmpty(osCulture) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(osCulture);

        Assert.Equal(expected, LocalizationManager.ResolveCulture(uiLanguage, os).Name);
    }

    [Theory]
    [InlineData("Framed Group", "Option_FramedGroup")]
    [InlineData("Use global", "Option_UseGlobal")]
    [InlineData("Use Global", "Option_UseGlobal")]
    [InlineData("Title + Body + Timestamp", "Option_TitleBodyTimestamp")]
    [InlineData("Prefer WinRT", "Option_PreferWinRT")]
    [InlineData("SemiBold", "Option_SemiBold")]
    public void OptionKey_DropsSeparatorsAndCapitalizesWords(string value, string expected)
    {
        Assert.Equal(expected, LocOptionExtension.KeyFor(LocOptionExtension.DefaultPrefix, value));
    }

    [Fact]
    public void OptionDisplay_UnknownValueIsShownUnchanged()
    {
        Assert.Equal("My Custom Theme", LocOptionExtension.Display("ThemeMode", "My Custom Theme"));
        Assert.Equal(string.Empty, LocOptionExtension.Display(LocOptionExtension.DefaultPrefix, null));
    }
}
