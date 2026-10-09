// @file LocalOnlySettingsTests.cs
// @author: rudals252
// @brief 이 PC 전용 설정(UiLanguage, SoundLibraryFolders, ManualMuteConfirmations, RestartOnCrash)이 프로필·내보내기에서 빠지고, 프로필 적용·가져오기·테마 적용·undo/redo·일반 저장 후에도 현재 값이 보존되는지 고정한다.

using System.IO;
using System.Reflection;
using System.Text.Json;
using NotificationsPro.Models;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class LocalOnlySettingsTests : IDisposable
{
    private readonly string _root = TestAppDataIsolation.CreateUniqueDirectory("local-only");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private SettingsManager CreateLoadedManager()
    {
        var manager = new SettingsManager(Path.Combine(_root, "settings"));
        manager.Load();
        return manager;
    }

    private static void SetLocalValues(SettingsManager manager, string language, string libraryFolder)
    {
        var updated = manager.Settings.Clone();
        updated.UiLanguage = language;
        updated.SoundLibraryFolders = new List<string> { libraryFolder };
        updated.RestartOnCrash = true;
        updated.ManualMuteConfirmations["Discord"] = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed };
        manager.Apply(updated);
    }

    private static void AssertLocalValues(AppSettings settings, string language, string libraryFolder)
    {
        Assert.Equal(language, settings.UiLanguage);
        Assert.Equal(new[] { libraryFolder }, settings.SoundLibraryFolders);
        Assert.True(settings.RestartOnCrash);
        Assert.Equal(ManualMuteConfirmation.UserConfirmed, settings.ManualMuteConfirmations["Discord"].State);
    }

    private static void AssertNoLocalOnlyProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain("UiLanguage", names);
        Assert.DoesNotContain("SoundLibraryFolders", names);
        Assert.DoesNotContain("ManualMuteConfirmations", names);
        Assert.DoesNotContain("RestartOnCrash", names);
        // Portable fork values still travel.
        Assert.Contains("AudioDispatchMode", names);
        Assert.Contains("AppSoundRules", names);
        Assert.Contains("PlaybackPolicy", names);
    }

    [Fact]
    public void SaveProfile_ExcludesLocalOnlyValuesAndExternalRuleFiles()
    {
        var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
        var manager = CreateLoadedManager();
        SetLocalValues(manager, "ko-KR", library);
        var updated = manager.Settings.Clone();
        updated.AppSoundRules["Discord"] = new SoundRule { Files = { "sounds/managed.wav", Path.Combine(library, "external.wav") } };
        manager.Apply(updated);
        Assert.Equal(2, manager.Settings.AppSoundRules["Discord"].Files.Count); // local settings keep the external file

        var profilesDir = Path.Combine(_root, "profiles");
        new ProfileManager(profilesDir).SaveProfile("Work", manager.Settings);

        var json = File.ReadAllText(Path.Combine(profilesDir, "Work.json"));
        AssertNoLocalOnlyProperties(json);
        Assert.DoesNotContain("external.wav", json);
        Assert.Contains("sounds/managed.wav", json);
    }

    [Fact]
    public void ExportSettings_ExcludesLocalOnlyValues()
    {
        var manager = CreateLoadedManager();
        SetLocalValues(manager, "en-US", TestAppDataIsolation.CreateUniqueDirectory("lib"));
        var exportPath = Path.Combine(_root, "export.json");

        ThemeManager.ExportSettings(manager.Settings, exportPath);

        AssertNoLocalOnlyProperties(File.ReadAllText(exportPath));
        Assert.Equal("en-US", manager.Settings.UiLanguage); // exporting never mutates local values
    }

    [Fact]
    public void ApplySnapshot_KeepsLocalValues_EvenWhenSnapshotCarriesOthers()
    {
        var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
        var foreignLibrary = TestAppDataIsolation.CreateUniqueDirectory("foreign");
        var manager = CreateLoadedManager();
        SetLocalValues(manager, "ko-KR", library);

        var crafted = new AppSettings
        {
            SettingsSchemaVersion = 6,
            FontSize = 22,
            UiLanguage = "en-US",
            SoundLibraryFolders = new List<string> { foreignLibrary },
            RestartOnCrash = false,
            DefaultSoundRule = new SoundRule { Files = { Path.Combine(foreignLibrary, "x.wav") } },
        };
        manager.ApplySnapshot(crafted);

        Assert.Equal(22, manager.Settings.FontSize);
        AssertLocalValues(manager.Settings, "ko-KR", library);
        // Trust for the foreign folder did not travel, so its file reference is dropped.
        Assert.Empty(manager.Settings.DefaultSoundRule.Files);
    }

    [Fact]
    public void ApplySnapshot_LegacyProfile_KeepsCurrentDispatchModeAndConvertsSounds()
    {
        var manager = CreateLoadedManager();
        Assert.Equal(AudioDispatchMode.PolicyAccepted, manager.Settings.AudioDispatchMode);

        var legacy = new AppSettings
        {
            SettingsSchemaVersion = 5,
            AudioDispatchMode = AudioDispatchMode.PolicyAccepted,
            DefaultSound = "sounds/legacy.wav",
            PerAppSounds = new Dictionary<string, string> { ["Slack"] = "sounds/slack.wav" },
        };
        var updated = manager.Settings.Clone();
        updated.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
        manager.Apply(updated);

        manager.ApplySnapshot(legacy);

        Assert.Equal(AudioDispatchMode.OverlayAccepted, manager.Settings.AudioDispatchMode);
        Assert.Equal(new[] { "sounds/legacy.wav" }, manager.Settings.DefaultSoundRule.Files);
        Assert.Equal(new[] { "sounds/slack.wav" }, manager.Settings.AppSoundRules["Slack"].Files);
    }

    [Fact]
    public void ViewModel_LoadProfile_PreservesUiLanguage()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = CreateLoadedManager();
            SetLocalValues(manager, "ko-KR", TestAppDataIsolation.CreateUniqueDirectory("lib"));
            var profilesDir = Path.Combine(_root, "profiles");
            Directory.CreateDirectory(profilesDir);
            // A hand-made profile that (incorrectly) carries a language must not override this PC's choice.
            File.WriteAllText(Path.Combine(profilesDir, "Foreign.json"), """
                { "SettingsSchemaVersion": 6, "FontSize": 19, "UiLanguage": "en-US", "RestartOnCrash": false }
                """);
            var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(profilesDir));

            InvokePrivate(viewModel, "LoadProfile", "Foreign");

            Assert.Equal(19, manager.Settings.FontSize);
            Assert.Equal("ko-KR", manager.Settings.UiLanguage);
            Assert.True(manager.Settings.RestartOnCrash);
        });
    }

    [Fact]
    public void ViewModel_ApplyImportedSettings_PreservesUiLanguage()
    {
        StaThreadTestHelper.Run(() =>
        {
            var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
            var manager = CreateLoadedManager();
            SetLocalValues(manager, "en-US", library);
            var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(_root, "profiles")));

            InvokePrivate(viewModel, "ApplyImportedSettings", new AppSettings { SettingsSchemaVersion = 6, FontSize = 17, UiLanguage = "ko-KR" });

            Assert.Equal(17, manager.Settings.FontSize);
            AssertLocalValues(manager.Settings, "en-US", library);
        });
    }

    [Fact]
    public void ViewModel_ApplyTheme_PreservesUiLanguageAndForkValues()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = CreateLoadedManager();
            SetLocalValues(manager, "ko-KR", TestAppDataIsolation.CreateUniqueDirectory("lib"));
            var updated = manager.Settings.Clone();
            updated.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
            updated.AppSoundRules["Discord"] = new SoundRule { ExplicitSilence = true };
            manager.Apply(updated);
            var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(_root, "profiles")));

            InvokePrivate(viewModel, "ApplyTheme", ThemePreset.BuiltInThemes.First());

            Assert.Equal("ko-KR", manager.Settings.UiLanguage);
            Assert.Equal(AudioDispatchMode.OverlayAccepted, manager.Settings.AudioDispatchMode);
            Assert.True(manager.Settings.AppSoundRules["Discord"].ExplicitSilence);
        });
    }

    [Fact]
    public void ViewModel_SaveSettings_CarriesForkValuesInsteadOfResettingThem()
    {
        StaThreadTestHelper.Run(() =>
        {
            var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
            var manager = CreateLoadedManager();
            SetLocalValues(manager, "ko-KR", library);
            var updated = manager.Settings.Clone();
            updated.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
            updated.StartMinimizedToTray = true;
            updated.PlaybackPolicy.MaxQueued = 2;
            updated.DefaultSoundRule = new SoundRule { Mode = SoundSelectionMode.Random, Files = { "sounds/a.wav" } };
            manager.Apply(updated);
            var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(_root, "profiles")))
            {
                FontSize = 23,
            };

            InvokePrivate(viewModel, "SaveSettings");

            var s = manager.Settings;
            Assert.Equal(23, s.FontSize);
            AssertLocalValues(s, "ko-KR", library);
            Assert.Equal(AudioDispatchMode.OverlayAccepted, s.AudioDispatchMode);
            Assert.True(s.StartMinimizedToTray);
            Assert.Equal(2, s.PlaybackPolicy.MaxQueued);
            Assert.Equal(SoundSelectionMode.Random, s.DefaultSoundRule.Mode);
            Assert.Equal(6, s.SettingsSchemaVersion);
        });
    }

    [Fact]
    public void ViewModel_UndoRedo_DoesNotRevertUiLanguage()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = CreateLoadedManager();
            SetLocalValues(manager, "ko-KR", TestAppDataIsolation.CreateUniqueDirectory("lib"));
            var viewModel = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(_root, "profiles")));
            var originalFontSize = manager.Settings.FontSize;

            viewModel.FontSize = originalFontSize + 5;
            InvokePrivate(viewModel, "SaveSettings"); // pushes the previous state onto the undo stack

            // Language changes through its own (future P3) path, outside undo/redo.
            var languageChange = manager.Settings.Clone();
            languageChange.UiLanguage = "en-US";
            manager.Apply(languageChange);

            viewModel.UndoCommand.Execute(null);
            Assert.Equal(originalFontSize, manager.Settings.FontSize);
            Assert.Equal("en-US", manager.Settings.UiLanguage);

            viewModel.RedoCommand.Execute(null);
            Assert.Equal(originalFontSize + 5, manager.Settings.FontSize);
            Assert.Equal("en-US", manager.Settings.UiLanguage);
        });
    }

    private static void InvokePrivate(SettingsViewModel viewModel, string methodName, params object?[] args)
    {
        var method = typeof(SettingsViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, args.Length == 0 ? null : args);
    }
}
