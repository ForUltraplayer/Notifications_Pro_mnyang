// @file SoundRuleSelectorTests.cs
// @author: rudals252
// @brief 소리 규칙 선택기 계약 테스트(설계 §14.1 "WAV 선택"): 단일, 빈 목록, 랜덤 1개·다수·중복 방지, 순차 순환, 결손 파일, 실패 후 순서, 앱별 독립, 후보 결정과 소비 확정 분리, 앱 식별 우선순위, 무음 사유.

using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class SoundRuleSelectorTests
{
    private static readonly Func<string, bool> AllUsable = _ => true;

    private static AppSettings Settings(SoundRule? defaultRule = null, Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings
        {
            SoundEnabled = true,
            DefaultSoundRule = defaultRule ?? SoundRule.CreateSingle("sounds/default.wav"),
        };
        configure?.Invoke(settings);
        return settings;
    }

    private static SoundRule Rule(SoundSelectionMode mode, params string[] files)
        => new() { Mode = mode, Files = files.ToList() };

    /// <summary>Plays (select + commit) like a real start would.</summary>
    private static string? Play(SoundRuleSelector selector, SoundRuleResolution resolution, Func<string, bool>? usable = null)
    {
        var selection = selector.Select(resolution, usable ?? AllUsable);
        selector.Commit(resolution.HistoryKey, selection);
        return selection.File;
    }

    // ---- Single / empty ----

    [Fact]
    public void Single_PlaysTheChosenFile_Repeatedly()
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Single, "sounds/a.wav")), "Slack", null);

        Assert.Equal("sounds/a.wav", Play(selector, resolution));
        Assert.Equal("sounds/a.wav", Play(selector, resolution));
    }

    [Fact]
    public void Single_UsesOnlyTheFirstListedFile_AndIsSilentWhenItIsUnavailable()
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Single, "sounds/a.wav", "sounds/b.wav")), "Slack", null);

        var selection = selector.Select(resolution, file => file != "sounds/a.wav");

        Assert.False(selection.HasFile);
        Assert.Equal(SoundSilenceReason.AllFilesUnavailable, selection.SilenceReason);
    }

    [Theory]
    [InlineData(SoundSelectionMode.Single)]
    [InlineData(SoundSelectionMode.Random)]
    [InlineData(SoundSelectionMode.Sequential)]
    public void EmptyList_IsSilent(SoundSelectionMode mode)
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(mode)), "Slack", null);

        Assert.True(resolution.IsSilent);
        Assert.Equal(SoundSilenceReason.EmptyList, resolution.SilenceReason);
        Assert.Equal(SoundSilenceReason.EmptyList, selector.Select(resolution, AllUsable).SilenceReason);
    }

    // ---- Random ----

    [Fact]
    public void Random_SingleFile_RepeatsEvenWithAvoidRepeat()
    {
        var selector = new SoundRuleSelector(_ => throw new InvalidOperationException("no draw needed"));
        var rule = Rule(SoundSelectionMode.Random, "sounds/only.wav");
        rule.AvoidRepeat = true;
        var resolution = selector.Resolve(Settings(rule), "Slack", null);

        Assert.Equal("sounds/only.wav", Play(selector, resolution));
        Assert.Equal("sounds/only.wav", Play(selector, resolution));
    }

    [Fact]
    public void Random_ManyFiles_DrawsUniformlyOverUsableFiles()
    {
        var bounds = new List<int>();
        var draws = new Queue<int>(new[] { 0, 1, 2 });
        var selector = new SoundRuleSelector(max => { bounds.Add(max); return draws.Dequeue(); });
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Random, "sounds/a.wav", "sounds/b.wav", "sounds/c.wav")), "Slack", null);

        var picked = new[] { Play(selector, resolution), Play(selector, resolution), Play(selector, resolution) };

        Assert.Equal(new[] { "sounds/a.wav", "sounds/b.wav", "sounds/c.wav" }, picked);
        Assert.All(bounds, max => Assert.Equal(3, max)); // each draw is over all 3 usable files
    }

    [Fact]
    public void Random_DefaultRandomSource_StaysInsideTheList()
    {
        var selector = new SoundRuleSelector();
        var files = new[] { "sounds/a.wav", "sounds/b.wav", "sounds/c.wav" };
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Random, files)), "Slack", null);

        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
            seen.Add(Play(selector, resolution)!);

        Assert.Subset(files.ToHashSet(), seen);
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Random_AvoidRepeat_ExcludesTheLastPlayedFile()
    {
        var bounds = new List<int>();
        var selector = new SoundRuleSelector(max => { bounds.Add(max); return 0; });
        var rule = Rule(SoundSelectionMode.Random, "sounds/a.wav", "sounds/b.wav", "sounds/c.wav");
        rule.AvoidRepeat = true;
        var resolution = selector.Resolve(Settings(rule), "Slack", null);

        Assert.Equal("sounds/a.wav", Play(selector, resolution)); // draw over 3
        Assert.Equal("sounds/b.wav", Play(selector, resolution)); // a excluded, draw over 2
        Assert.Equal("sounds/a.wav", Play(selector, resolution)); // b excluded
        Assert.Equal(new[] { 3, 2, 2 }, bounds);
    }

    [Fact]
    public void Random_WithoutAvoidRepeat_MayRepeat()
    {
        var selector = new SoundRuleSelector(_ => 0);
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Random, "sounds/a.wav", "sounds/b.wav")), "Slack", null);

        Assert.Equal("sounds/a.wav", Play(selector, resolution));
        Assert.Equal("sounds/a.wav", Play(selector, resolution));
    }

    [Fact]
    public void Random_AvoidRepeat_AllowsRepeatWhenOnlyTheLastFileIsStillUsable()
    {
        var selector = new SoundRuleSelector(_ => 0);
        var rule = Rule(SoundSelectionMode.Random, "sounds/a.wav", "sounds/b.wav");
        rule.AvoidRepeat = true;
        var resolution = selector.Resolve(Settings(rule), "Slack", null);
        Assert.Equal("sounds/a.wav", Play(selector, resolution));

        Assert.Equal("sounds/a.wav", Play(selector, resolution, file => file == "sounds/a.wav"));
    }

    [Fact]
    public void Random_SkipsUnusableFiles()
    {
        var selector = new SoundRuleSelector(max => max - 1);
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Random, "sounds/a.wav", "sounds/missing.wav")), "Slack", null);

        for (var i = 0; i < 5; i++)
            Assert.Equal("sounds/a.wav", Play(selector, resolution, file => file != "sounds/missing.wav"));
    }

    // ---- Sequential ----

    [Fact]
    public void Sequential_FollowsListOrder_AndWrapsAround()
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav", "sounds/3.wav")), "Slack", null);

        var order = Enumerable.Range(0, 7).Select(_ => Play(selector, resolution)).ToArray();

        Assert.Equal(new[] { "sounds/1.wav", "sounds/2.wav", "sounds/3.wav", "sounds/1.wav", "sounds/2.wav", "sounds/3.wav", "sounds/1.wav" }, order);
    }

    [Fact]
    public void Sequential_MissingFile_IsSkipped_ToTheNextUsableFile()
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav", "sounds/3.wav")), "Slack", null);
        Func<string, bool> usable = file => file != "sounds/2.wav";

        var order = Enumerable.Range(0, 4).Select(_ => Play(selector, resolution, usable)).ToArray();

        Assert.Equal(new[] { "sounds/1.wav", "sounds/3.wav", "sounds/1.wav", "sounds/3.wav" }, order);
    }

    [Fact]
    public void AllFilesUnavailable_IsSilent_AndDoesNotChangeHistory()
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav")), "Slack", null);
        Play(selector, resolution); // 1 played, next = 2

        var selection = selector.Select(resolution, _ => false);
        selector.Commit(resolution.HistoryKey, selection);

        Assert.Equal(SoundSilenceReason.AllFilesUnavailable, selection.SilenceReason);
        Assert.Equal(1, selector.GetNextSequentialIndex(resolution.HistoryKey));
        Assert.Equal("sounds/1.wav", selector.GetLastPlayed(resolution.HistoryKey));
    }

    [Fact]
    public void FailedStart_WithoutCommit_KeepsTheOrder_ThenTheNextStartAdvancesFromThePlayedFile()
    {
        var selector = new SoundRuleSelector();
        var resolution = selector.Resolve(Settings(Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav", "sounds/3.wav")), "Slack", null);
        Assert.Equal("sounds/1.wav", Play(selector, resolution));

        // Candidate 2 is chosen but playback fails before starting: no commit.
        var failed = selector.Select(resolution, AllUsable);
        Assert.Equal("sounds/2.wav", failed.File);
        Assert.Equal(1, selector.GetNextSequentialIndex(resolution.HistoryKey));

        // The coordinator retries without the failed file; 3 starts and becomes the position.
        var retry = selector.Select(resolution, file => file != "sounds/2.wav");
        selector.Commit(resolution.HistoryKey, retry);
        Assert.Equal("sounds/3.wav", retry.File);

        Assert.Equal("sounds/1.wav", Play(selector, resolution)); // wraps after 3
    }

    [Fact]
    public void SelectWithoutCommit_NeverConsumes_LikePreviewCooldownOrExpiry()
    {
        var selector = new SoundRuleSelector(_ => 0);
        var rule = Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav");
        var resolution = selector.Resolve(Settings(rule), "Slack", null);

        for (var i = 0; i < 3; i++)
            Assert.Equal("sounds/1.wav", selector.Select(resolution, AllUsable).File);

        Assert.Equal(0, selector.GetNextSequentialIndex(resolution.HistoryKey));
        Assert.Null(selector.GetLastPlayed(resolution.HistoryKey));
    }

    [Fact]
    public void NewSelector_AfterRestart_StartsFromTheFirstFile()
    {
        var settings = Settings(Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav"));
        var first = new SoundRuleSelector();
        Play(first, first.Resolve(settings, "Slack", null));

        var restarted = new SoundRuleSelector();
        Assert.Equal("sounds/1.wav", Play(restarted, restarted.Resolve(settings, "Slack", null)));
    }

    // ---- per-app history ----

    [Fact]
    public void History_IsSeparatePerApp_ForAppRulesAndForTheSharedDefaultRule()
    {
        var sequence = Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav", "sounds/3.wav");
        var settings = Settings(sequence.Clone(), s =>
        {
            s.AppSoundRules["Slack.App"] = sequence.Clone();
            s.AppSoundRules["Teams"] = sequence.Clone();
        });
        var selector = new SoundRuleSelector();

        var slack = selector.Resolve(settings, "Slack", "Slack.App");
        var teams = selector.Resolve(settings, "Teams", null);
        var mailDefault = selector.Resolve(settings, "Mail", "Mail.App");
        var otherDefault = selector.Resolve(settings, "Other", null);

        Assert.Equal("sounds/1.wav", Play(selector, slack));
        Assert.Equal("sounds/2.wav", Play(selector, slack));
        Assert.Equal("sounds/1.wav", Play(selector, teams));
        Assert.Equal("sounds/1.wav", Play(selector, mailDefault));
        Assert.Equal("sounds/2.wav", Play(selector, mailDefault));
        Assert.Equal("sounds/1.wav", Play(selector, otherDefault));
        Assert.Equal("sounds/3.wav", Play(selector, slack));

        Assert.Equal(4, new[] { slack.HistoryKey, teams.HistoryKey, mailDefault.HistoryKey, otherDefault.HistoryKey }.Distinct().Count());
    }

    [Fact]
    public void History_IsCappedInRam()
    {
        var selector = new SoundRuleSelector();
        var settings = Settings(Rule(SoundSelectionMode.Sequential, "sounds/1.wav", "sounds/2.wav"));
        for (var i = 0; i < SoundRuleSelector.MaxHistoryEntries + 50; i++)
            Play(selector, selector.Resolve(settings, "App" + i, null));

        Assert.Equal(SoundRuleSelector.MaxHistoryEntries, selector.HistoryCount);
    }

    // ---- identity / rule resolution ----

    [Fact]
    public void Resolve_PrefersAumidRule_ThenDisplayName_ThenDefault()
    {
        var settings = Settings(SoundRule.CreateSingle("sounds/default.wav"), s =>
        {
            s.AppSoundRules["Chat.App"] = SoundRule.CreateSingle("sounds/by-aumid.wav");
            s.AppSoundRules["Chat"] = SoundRule.CreateSingle("sounds/by-name.wav");
        });
        var selector = new SoundRuleSelector();

        var byId = selector.Resolve(settings, "Chat", "chat.app");
        var byName = selector.Resolve(settings, "Chat", null);
        var none = selector.Resolve(settings, "Other", "Other.App");

        Assert.Equal(AppIdentityMatchKind.AppId, byId.MatchKind);
        Assert.Equal("sounds/by-aumid.wav", selector.Select(byId, AllUsable).File);
        Assert.Equal(AppIdentityMatchKind.DisplayName, byName.MatchKind);
        Assert.Equal("sounds/by-name.wav", selector.Select(byName, AllUsable).File);
        Assert.Equal(AppIdentityMatchKind.None, none.MatchKind);
        Assert.Null(none.RuleKey);
        Assert.Equal("sounds/default.wav", selector.Select(none, AllUsable).File);
    }

    [Fact]
    public void ExplicitSilence_IsSilent_AndNeverFallsBackToTheDefault()
    {
        var settings = Settings(SoundRule.CreateSingle("sounds/default.wav"), s =>
            s.AppSoundRules["Quiet"] = new SoundRule { ExplicitSilence = true, Files = { "sounds/x.wav" } });
        var selector = new SoundRuleSelector();

        var resolution = selector.Resolve(settings, "Quiet", null);

        Assert.True(resolution.IsSilent);
        Assert.Equal(SoundSilenceReason.ExplicitSilence, resolution.SilenceReason);
        Assert.False(selector.Select(resolution, AllUsable).HasFile);
    }

    [Fact]
    public void DisabledAppRule_InheritsTheDefaultRule()
    {
        var settings = Settings(SoundRule.CreateSingle("sounds/default.wav"), s =>
            s.AppSoundRules["Slack"] = new SoundRule { Enabled = false, Files = { "sounds/app.wav" } });
        var selector = new SoundRuleSelector();

        var resolution = selector.Resolve(settings, "Slack", null);

        Assert.Equal("sounds/default.wav", selector.Select(resolution, AllUsable).File);
    }

    [Fact]
    public void DisabledDefaultRule_IsSilent_WithRuleDisabledReason()
    {
        var rule = SoundRule.CreateSingle("sounds/default.wav");
        rule.Enabled = false;
        var selector = new SoundRuleSelector();

        var resolution = selector.Resolve(Settings(rule), "Slack", null);

        Assert.Equal(SoundSilenceReason.RuleDisabled, resolution.SilenceReason);
    }

    [Fact]
    public void MasterSwitchOff_IsSilent_EvenWithAnAppRule()
    {
        var settings = Settings(SoundRule.CreateSingle("sounds/default.wav"), s =>
        {
            s.SoundEnabled = false;
            s.AppSoundRules["Slack"] = SoundRule.CreateSingle("sounds/app.wav");
        });
        var selector = new SoundRuleSelector();

        Assert.Equal(SoundSilenceReason.MasterSwitchOff, selector.Resolve(settings, "Slack", null).SilenceReason);
    }

    [Fact]
    public void LegacyDefaultSoundAndPerAppSounds_AreNotRead()
    {
        var settings = Settings(new SoundRule(), s =>
        {
            s.DefaultSound = @"C:\Windows\Media\Windows Ding.wav";
            s.PerAppSounds["Slack"] = @"C:\Windows\Media\Windows Ding.wav";
        });
        var selector = new SoundRuleSelector();

        Assert.True(selector.Resolve(settings, "Slack", null).IsSilent);
    }
}
