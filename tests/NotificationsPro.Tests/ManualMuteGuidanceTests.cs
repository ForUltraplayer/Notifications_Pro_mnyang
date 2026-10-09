// @file ManualMuteGuidanceTests.cs
// @author: rudals252
// @brief P4 원래 소리 수동 음소거(R09): AUMID 기반 앱 유형 판정, 표시 상태(미확인/설정 필요/사용자 확인 완료/안내 미지원)와 전이, 저장 키(AUMID 우선·없으면 표시 이름, 기존 표시 이름 키는 읽기 전용 호환), 앱 탭 카드의 확인·다시 확인 필요 저장(이 PC 전용, 앱 검증 아님), 같은 AUMID를 쓰는 카드 공유, 프로필 제외, 트레이로 시작 기본값을 검증한다.

using System.IO;
using NotificationsPro.Helpers;
using NotificationsPro.Localization;
using NotificationsPro.Models;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class ManualMuteGuidanceTests : IDisposable
{
    private readonly string _root = TestAppDataIsolation.CreateUniqueDirectory("manual-mute");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("Discord", "com.squirrel.Discord.Discord", OriginalSoundAppKind.Discord)]
    [InlineData("Discord Canary", "com.squirrel.DiscordCanary.DiscordCanary", OriginalSoundAppKind.Discord)]
    [InlineData("ChatGPT", "OpenAI.Codex_2p2nqsd0c76g0!App", OriginalSoundAppKind.WindowsToast)]
    [InlineData("Claude", "Claude_pzs8sxrjxfjjc!Claude", OriginalSoundAppKind.WindowsToast)]
    [InlineData("Whatever", @"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Kakao\KakaoTalk\KakaoTalk.exe", OriginalSoundAppKind.KakaoTalk)]
    public void DetectKind_UsesObservedAumids(string name, string appId, OriginalSoundAppKind expected)
    {
        Assert.Equal(expected, ManualMuteGuidance.DetectKind(name, new[] { appId }));
    }

    [Fact]
    public void DetectKind_WithoutAumid_IsUnknown_ExceptKnownKakaoTalkName()
    {
        Assert.Equal(OriginalSoundAppKind.Unknown, ManualMuteGuidance.DetectKind("Discord", Array.Empty<string>()));
        Assert.Equal(OriginalSoundAppKind.Unknown, ManualMuteGuidance.DetectKind("Some App", new[] { " ", "" }));
        Assert.Equal(OriginalSoundAppKind.Unknown, ManualMuteGuidance.DetectKind(null, null));
        Assert.Equal(OriginalSoundAppKind.KakaoTalk, ManualMuteGuidance.DetectKind("카카오톡", null));
        Assert.Equal(OriginalSoundAppKind.KakaoTalk, ManualMuteGuidance.DetectKind("KakaoTalk", Array.Empty<string>()));
        Assert.False(ManualMuteGuidance.IsDiscordAppId("com.squirrel.Slack.Slack"));
    }

    [Fact]
    public void ResolveState_CoversAllFourStates()
    {
        var confirmed = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed };
        var recheck = new ManualMuteConfirmation { State = ManualMuteConfirmation.SetupNeeded };

        Assert.Equal(ManualMuteConfirmation.Unconfirmed, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.Unknown, null));
        Assert.Equal(ManualMuteConfirmation.SetupNeeded, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.WindowsToast, null));
        Assert.Equal(ManualMuteConfirmation.SetupNeeded, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.Discord, recheck));
        Assert.Equal(ManualMuteConfirmation.UserConfirmed, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.WindowsToast, confirmed));
        Assert.Equal(ManualMuteConfirmation.UserConfirmed, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.Unknown, confirmed));
        Assert.Equal(ManualMuteConfirmation.NotSupported, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.KakaoTalk, confirmed));
        Assert.Equal(ManualMuteConfirmation.Unconfirmed, ManualMuteGuidance.ResolveState(OriginalSoundAppKind.Unknown, recheck));
    }

    [Fact]
    public void Buttons_FollowStateAndKind()
    {
        Assert.True(ManualMuteGuidance.CanConfirm(OriginalSoundAppKind.WindowsToast, ManualMuteConfirmation.SetupNeeded));
        Assert.True(ManualMuteGuidance.CanConfirm(OriginalSoundAppKind.Discord, ManualMuteConfirmation.SetupNeeded));
        Assert.False(ManualMuteGuidance.CanConfirm(OriginalSoundAppKind.WindowsToast, ManualMuteConfirmation.UserConfirmed));
        Assert.False(ManualMuteGuidance.CanConfirm(OriginalSoundAppKind.KakaoTalk, ManualMuteConfirmation.NotSupported));
        Assert.False(ManualMuteGuidance.CanConfirm(OriginalSoundAppKind.Unknown, ManualMuteConfirmation.Unconfirmed));
        Assert.True(ManualMuteGuidance.CanRequireRecheck(ManualMuteConfirmation.UserConfirmed));
        Assert.False(ManualMuteGuidance.CanRequireRecheck(ManualMuteConfirmation.SetupNeeded));
        Assert.True(ManualMuteGuidance.UsesWindowsToggle(OriginalSoundAppKind.Discord));
        Assert.False(ManualMuteGuidance.UsesWindowsToggle(OriginalSoundAppKind.KakaoTalk));
        Assert.Equal("ms-settings:notifications", ManualMuteGuidance.WindowsNotificationSettingsUri);
    }

    [Fact]
    public void Transitions_StoreUserReportWithTime()
    {
        var now = new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.FromHours(9));
        var confirmed = ManualMuteGuidance.Confirm(now);
        var recheck = ManualMuteGuidance.RequireRecheck(now.AddMinutes(1));

        Assert.Equal(ManualMuteConfirmation.UserConfirmed, confirmed.State);
        Assert.Equal(now, confirmed.UpdatedAt);
        Assert.Equal(ManualMuteConfirmation.SetupNeeded, recheck.State);
        Assert.Equal(now.AddMinutes(1), recheck.UpdatedAt);
    }

    [Fact]
    public void StorageKey_IsAumidWhenKnown_OtherwiseDisplayName()
    {
        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0!App", ManualMuteGuidance.ResolveStorageKey("ChatGPT", new[] { "OpenAI.Codex_2p2nqsd0c76g0!App" }, "ChatGPT"));
        Assert.Equal("Mystery", ManualMuteGuidance.ResolveStorageKey("Mystery", Array.Empty<string>(), "Mystery"));
        Assert.Equal("Mystery", ManualMuteGuidance.ResolveStorageKey(" Mystery ", new[] { " " }, null));
        // Several AUMIDs: the card's selected rule key when it is one of them, else the display name.
        Assert.Equal("Chrome.Profile2", ManualMuteGuidance.ResolveStorageKey("Chrome", new[] { "Chrome.Profile1", "Chrome.Profile2" }, "chrome.profile2"));
        Assert.Equal("Chrome", ManualMuteGuidance.ResolveStorageKey("Chrome", new[] { "Chrome.Profile1", "Chrome.Profile2" }, "Chrome"));
        Assert.Null(ManualMuteGuidance.ResolveStorageKey(null, null, null));
    }

    [Fact]
    public void FindStored_PrefersAumid_ThenDisplayNameForCompatibility()
    {
        var aumid = new ManualMuteConfirmation { State = ManualMuteConfirmation.SetupNeeded };
        var legacyName = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed };
        var map = new Dictionary<string, ManualMuteConfirmation>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.squirrel.Discord.Discord"] = aumid,
            ["Discord"] = legacyName,
        };

        Assert.Same(aumid, ManualMuteGuidance.FindStored(map, "Discord", new[] { "com.squirrel.Discord.Discord" }, "Discord", out var key));
        Assert.Equal("com.squirrel.Discord.Discord", key);
        Assert.Same(legacyName, ManualMuteGuidance.FindStored(map, "Discord", Array.Empty<string>(), "Discord", out key));
        Assert.Equal("Discord", key);
        Assert.Null(ManualMuteGuidance.FindStored(map, "Other", new[] { "Other.App" }, null, out key));
        Assert.Null(key);
    }

    [Fact]
    public void GuidanceKeys_ExistForEveryKind()
    {
        foreach (var kind in Enum.GetValues<OriginalSoundAppKind>())
            Assert.True(LocalizationManager.Instance.HasKey(ManualMuteGuidance.GuidanceResourceKey(kind)), kind.ToString());
    }

    [Fact]
    public void AppCard_ConfirmAndRecheck_AreStoredLocallyAndNeverExported()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = new SettingsManager(Path.Combine(_root, "settings"));
            manager.Load();
            var profiles = new ProfileManager(Path.Combine(_root, "profiles"));
            var queue = new QueueManager(manager);
            var viewModel = new SettingsViewModel(manager, queue, profiles);
            var now = new DateTimeOffset(2026, 10, 9, 18, 30, 0, TimeSpan.FromHours(9));
            viewModel.ManualMuteClock = () => now;

            queue.AddNotification("Discord", "title", "body", "com.squirrel.Discord.Discord");
            queue.AddNotification("Mystery", "title", "body");
            viewModel.RefreshPerAppConfig();

            var discord = viewModel.PerAppConfigEntries.Single(e => e.AppName == "Discord").SoundRule!;
            Assert.Equal(OriginalSoundAppKind.Discord, discord.OriginalSoundKind);
            Assert.Equal(ManualMuteConfirmation.SetupNeeded, discord.ManualMuteState);
            Assert.Equal("Setup needed", discord.OriginalSoundStatusText);
            Assert.Contains("Message", discord.ManualMuteGuidanceText);
            Assert.True(discord.ShowWindowsNotificationSettingsButton);
            Assert.True(discord.CanConfirmManualMute);
            Assert.False(discord.CanRequireManualMuteRecheck);

            discord.ConfirmManualMuteCommand.Execute(null);
            // Stored under the AUMID (the Windows sound switch is per AUMID), not the display name.
            Assert.False(manager.Settings.ManualMuteConfirmations.ContainsKey("Discord"));
            var stored = manager.Settings.ManualMuteConfirmations["com.squirrel.Discord.Discord"];
            Assert.Equal(ManualMuteConfirmation.UserConfirmed, stored.State);
            Assert.Equal(now, stored.UpdatedAt);
            Assert.Equal(ManualMuteConfirmation.UserConfirmed, discord.ManualMuteState);
            Assert.StartsWith("Confirmed by you (not verified by the app)", discord.OriginalSoundStatusText);
            Assert.False(discord.CanConfirmManualMute);
            Assert.True(discord.CanRequireManualMuteRecheck);

            // Saved to disk right away (this PC only), and survives a reload.
            var reloaded = new SettingsManager(Path.Combine(_root, "settings"));
            reloaded.Load();
            Assert.Equal(ManualMuteConfirmation.UserConfirmed, reloaded.Settings.ManualMuteConfirmations["com.squirrel.Discord.Discord"].State);

            // Profiles never carry the confirmation.
            profiles.SaveProfile("p4", manager.Settings);
            Assert.DoesNotContain("ManualMuteConfirmations", File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "profiles"), "*.json").Single()));

            discord.RequireManualMuteRecheckCommand.Execute(null);
            Assert.Equal(ManualMuteConfirmation.SetupNeeded, manager.Settings.ManualMuteConfirmations["com.squirrel.Discord.Discord"].State);
            Assert.Equal("Setup needed", discord.OriginalSoundStatusText);
            Assert.True(discord.CanConfirmManualMute);

            var mystery = viewModel.PerAppConfigEntries.Single(e => e.AppName == "Mystery").SoundRule!;
            Assert.Equal(OriginalSoundAppKind.Unknown, mystery.OriginalSoundKind);
            Assert.Equal("Not checked", mystery.OriginalSoundStatusText);
            Assert.False(mystery.CanConfirmManualMute);
            Assert.False(mystery.ShowWindowsNotificationSettingsButton);
        });
    }

    [Fact]
    public void AppCards_SharingOneAumid_ShareOneConfirmation()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = new SettingsManager(Path.Combine(_root, "settings-shared"));
            manager.Load();
            var queue = new QueueManager(manager);
            var viewModel = new SettingsViewModel(manager, queue, new ProfileManager(Path.Combine(_root, "profiles-shared")));
            const string aumid = "OpenAI.Codex_2p2nqsd0c76g0!App";
            queue.AddNotification("ChatGPT", "t", "b", aumid);
            queue.AddNotification("Codex", "t", "b", aumid);
            viewModel.RefreshPerAppConfig();
            var chatGpt = viewModel.PerAppConfigEntries.Single(e => e.AppName == "ChatGPT").SoundRule!;
            var codex = viewModel.PerAppConfigEntries.Single(e => e.AppName == "Codex").SoundRule!;

            chatGpt.ConfirmManualMuteCommand.Execute(null);

            Assert.Equal(aumid, Assert.Single(manager.Settings.ManualMuteConfirmations).Key);
            Assert.Equal(ManualMuteConfirmation.UserConfirmed, codex.ManualMuteState);
            Assert.True(codex.CanRequireManualMuteRecheck);

            codex.RequireManualMuteRecheckCommand.Execute(null);
            Assert.Equal(ManualMuteConfirmation.SetupNeeded, chatGpt.ManualMuteState);
            Assert.Single(manager.Settings.ManualMuteConfirmations);
        });
    }

    [Fact]
    public void LegacyDisplayNameKey_IsReadOnly_NeverRekeyed()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = new SettingsManager(Path.Combine(_root, "settings-legacy"));
            manager.Load();
            var updated = manager.Settings.Clone();
            var legacyTime = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
            updated.ManualMuteConfirmations["Discord"] = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed, UpdatedAt = legacyTime };
            manager.Apply(updated);
            var queue = new QueueManager(manager);
            var viewModel = new SettingsViewModel(manager, queue, new ProfileManager(Path.Combine(_root, "profiles-legacy")));
            queue.AddNotification("Discord", "t", "b", "com.squirrel.Discord.Discord");
            viewModel.RefreshPerAppConfig();
            var discord = viewModel.PerAppConfigEntries.Single(e => e.AppName == "Discord").SoundRule!;

            // Read for compatibility, nothing written by loading.
            Assert.Equal(ManualMuteConfirmation.UserConfirmed, discord.ManualMuteState);
            Assert.Equal(new[] { "Discord" }, manager.Settings.ManualMuteConfirmations.Keys);

            // A new report goes to the AUMID key; the old name entry stays exactly as it was.
            discord.RequireManualMuteRecheckCommand.Execute(null);
            Assert.Equal(ManualMuteConfirmation.SetupNeeded, manager.Settings.ManualMuteConfirmations["com.squirrel.Discord.Discord"].State);
            Assert.Equal(ManualMuteConfirmation.UserConfirmed, manager.Settings.ManualMuteConfirmations["Discord"].State);
            Assert.Equal(legacyTime, manager.Settings.ManualMuteConfirmations["Discord"].UpdatedAt);
            Assert.Equal(ManualMuteConfirmation.SetupNeeded, discord.ManualMuteState);
        });
    }

    [Fact]
    public void StartMinimizedToTray_IsLocalOnly_AndSavedDirectly()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = new SettingsManager(Path.Combine(_root, "settings2"));
            manager.Load();
            var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(_root, "profiles2")));
            var overlayVisible = manager.Settings.OverlayVisible;
            Assert.True(viewModel.StartMinimizedToTray); // default: tray start (K12)

            viewModel.StartMinimizedToTray = false;
            Assert.False(manager.Settings.StartMinimizedToTray);
            Assert.Equal(overlayVisible, manager.Settings.OverlayVisible);

            var reloaded = new SettingsManager(Path.Combine(_root, "settings2"));
            reloaded.Load();
            Assert.False(reloaded.Settings.StartMinimizedToTray);

            Assert.Contains(nameof(AppSettings.StartMinimizedToTray), LocalOnlySettingsHelper.LocalOnlyPropertyNames);
            var snapshot = manager.Settings.Clone();
            snapshot.StartMinimizedToTray = true;
            manager.ApplySnapshot(snapshot); // profile / import / undo path keeps this PC's value
            Assert.False(manager.Settings.StartMinimizedToTray);

            var cleared = new AppSettings { StartMinimizedToTray = false };
            LocalOnlySettingsHelper.ClearLocalOnly(cleared);
            Assert.True(cleared.StartMinimizedToTray);
        });
    }
}
