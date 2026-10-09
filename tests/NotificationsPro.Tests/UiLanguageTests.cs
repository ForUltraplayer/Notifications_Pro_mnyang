// @file UiLanguageTests.cs
// @author: rudals252
// @brief P3 언어 선택 테스트: 전체 초기화가 이 PC 전용 값 4개를 보존, 설정창 언어 전환 후에도 재생 대기열·다른 설정·undo 기록이 그대로이고 UiLanguage만 저장됨, 리소스 기반 콤보 문구가 새 언어를 따름. 전역 UI 문화권을 바꾸는 테스트는 병렬 실행하지 않는 컬렉션에서 돌리고 끝나면 영어로 되돌린다.

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using NotificationsPro.Localization;
using NotificationsPro.Models;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiCultureCollection
{
    public const string Name = "UI culture (global state)";
}

public class UiLanguageResetTests
{
    [Fact]
    public void ResetToDefaults_KeepsTheFourLocalOnlyValues_AndResetsTheRest()
    {
        var library = TestAppDataIsolation.CreateUniqueDirectory("reset-lib");
        var manager = TestAppDataIsolation.CreateSettingsManager();
        manager.Load();
        var updated = manager.Settings.Clone();
        updated.UiLanguage = "ko-KR";
        updated.SoundLibraryFolders = new List<string> { library };
        updated.ManualMuteConfirmations["Discord"] = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed };
        updated.RestartOnCrash = true;
        updated.FontSize = 31;
        updated.MaxVisibleNotifications = 7;
        manager.Apply(updated);

        manager.ResetToDefaults();

        var defaults = new AppSettings();
        var settings = manager.Settings;
        Assert.Equal("ko-KR", settings.UiLanguage);
        Assert.Equal(new[] { library }, settings.SoundLibraryFolders);
        Assert.Equal(ManualMuteConfirmation.UserConfirmed, settings.ManualMuteConfirmations["Discord"].State);
        Assert.True(settings.RestartOnCrash);
        Assert.Equal(defaults.FontSize, settings.FontSize);
        Assert.Equal(defaults.MaxVisibleNotifications, settings.MaxVisibleNotifications);
    }
}

[Collection(UiCultureCollection.Name)]
public class UiLanguageSwitchTests
{
    private static string SerializeWithoutLanguage(AppSettings settings)
    {
        var node = (JsonObject)JsonSerializer.SerializeToNode(settings)!;
        node.Remove(nameof(AppSettings.UiLanguage));
        return node.ToJsonString();
    }

    [Fact]
    public void SwitchingLanguage_KeepsQueuedSoundsOtherSettingsAndUndoHistory()
    {
        StaThreadTestHelper.Run(() =>
        {
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
            var settingsDir = TestAppDataIsolation.CreateUniqueDirectory("language-switch");
            var manager = new SettingsManager(settingsDir);
            manager.Load();
            var arranged = manager.Settings.Clone();
            arranged.SoundEnabled = true;
            arranged.DefaultSoundRule = new SoundRule { Mode = SoundSelectionMode.Sequential, Files = { "sounds/1.wav", "sounds/2.wav", "sounds/3.wav" } };
            arranged.PlaybackPolicy = new PlaybackPolicy { MinIntervalPerAppSeconds = 0, MaxQueued = 5, MaxQueueWaitSeconds = 30 };
            arranged.FontSize = 23;
            manager.Apply(arranged);

            var engine = new FakeSoundEngine();
            var posts = new ManualPostQueue();
            var clock = new ManualClock();
            using var coordinator = new PlaybackCoordinator(manager, engine, new FakeSoundFileGate(), posts.Post, new SoundRuleSelector(), () => clock.UtcNow);
            var queue = new QueueManager(manager);
            var viewModel = new SettingsViewModel(manager, queue, new ProfileManager(Path.Combine(settingsDir, "profiles")));

            coordinator.EnqueueNotificationSound("Slack", null);
            coordinator.EnqueueNotificationSound("Slack", null);
            coordinator.EnqueueNotificationSound("Slack", null);
            posts.RunAll();
            var playing = engine.Current;
            Assert.NotNull(playing);
            Assert.Equal(2, coordinator.GetDiagnostics().QueuedSounds);

            var settingsBefore = SerializeWithoutLanguage(manager.Settings);
            var canUndoBefore = viewModel.CanUndo;

            try
            {
                viewModel.SelectedUiLanguage = LocalizationManager.KoreanCultureName;
                posts.RunAll();

                Assert.Equal("ko-KR", LocalizationManager.Instance.Culture.Name);
                Assert.Equal("ko-KR", CultureInfo.CurrentUICulture.Name);
                Assert.Equal("ko-KR", manager.Settings.UiLanguage);
                Assert.Equal("ko-KR", viewModel.SelectedUiLanguage);

                // Playback queue untouched (a settings Apply would have cancelled queued sounds).
                Assert.Same(playing, engine.Current);
                Assert.Equal(2, coordinator.GetDiagnostics().QueuedSounds);

                // Nothing else changed, nothing was pushed to undo/redo.
                Assert.Equal(settingsBefore, SerializeWithoutLanguage(manager.Settings));
                Assert.Equal(canUndoBefore, viewModel.CanUndo);

                // Saved immediately (this PC's preference).
                var reloaded = new SettingsManager(settingsDir);
                reloaded.Load();
                Assert.Equal("ko-KR", reloaded.Settings.UiLanguage);

                // Resource-backed texts follow the new language.
                Assert.Equal("재생: 사용할 수 없음", LocalizationManager.Get("Sounds_PlaybackUnavailable"));
                Assert.StartsWith("화면 수용 기준", SettingsViewModel.AudioDispatchModeOptions[0].Label);

                // OS default is stored as null.
                viewModel.SelectedUiLanguage = SettingsViewModel.UiLanguageOsDefault;
                Assert.Null(manager.Settings.UiLanguage);
                Assert.Equal(2, coordinator.GetDiagnostics().QueuedSounds);
            }
            finally
            {
                LocalizationManager.Instance.SetCulture(CultureInfo.GetCultureInfo(LocalizationManager.EnglishCultureName));
                CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            }
        });
    }

    [Fact]
    public void LocalizedOptionLabel_RaisesLabelChangedWhenCultureChanges()
    {
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var option = OptionItem<int>.Localized(1, "Tray_Quit");
        var raised = new List<string?>();
        option.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        try
        {
            Assert.Equal("Quit", option.Label);
            LocalizationManager.Instance.SetCulture(CultureInfo.GetCultureInfo(LocalizationManager.KoreanCultureName));
            Assert.Equal("종료", option.Label);
            Assert.Contains(nameof(OptionItem<int>.Label), raised);
        }
        finally
        {
            LocalizationManager.Instance.SetCulture(CultureInfo.GetCultureInfo(LocalizationManager.EnglishCultureName));
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }
}
