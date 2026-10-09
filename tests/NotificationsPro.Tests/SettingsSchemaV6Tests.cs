// @file SettingsSchemaV6Tests.cs
// @author: rudals252
// @brief 설정 스키마 v6 계약 테스트: v5→v6 이관(단일 WAV·앱별 WAV·None/빈 값 의미), 신규 설치 기본값, 미래 스키마 읽기 전용, 손상 파일 보존, 원자적 저장 실패 시 원본 보존, enum 문자열 저장.

using System.IO;
using System.Text.Json;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class SettingsSchemaV6Tests : IDisposable
{
    private readonly string _dir = TestAppDataIsolation.CreateUniqueDirectory("schema-v6");
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SettingsManager LoadFrom(string json)
    {
        File.WriteAllText(SettingsPath, json);
        var manager = new SettingsManager(_dir) { Clock = () => new DateTime(2026, 10, 9, 12, 34, 56) };
        manager.Load();
        return manager;
    }

    [Fact]
    public void CurrentSchemaVersion_IsSix()
    {
        Assert.Equal(6, SettingsManager.CurrentSettingsSchemaVersion);
    }

    [Fact]
    public void NewInstall_DefaultsToPolicyAcceptedAndSilentDefaultRule()
    {
        var manager = new SettingsManager(_dir);
        manager.Load();

        Assert.False(manager.HadExistingSettingsFile);
        Assert.Equal(AudioDispatchMode.PolicyAccepted, manager.Settings.AudioDispatchMode);
        Assert.Equal(6, manager.Settings.SettingsSchemaVersion);
        Assert.Null(manager.Settings.UiLanguage);
        Assert.False(manager.Settings.SoundEnabled);
        Assert.Empty(manager.Settings.DefaultSoundRule.Files);
        Assert.Empty(manager.Settings.AppSoundRules);
        Assert.False(manager.Settings.StartMinimizedToTray);
        Assert.False(manager.Settings.RestartOnCrash);
        Assert.Equal(4, manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
        Assert.Equal(5, manager.Settings.PlaybackPolicy.MaxQueued);
        Assert.Equal(30, manager.Settings.PlaybackPolicy.MaxQueueWaitSeconds);
        Assert.Equal(PlaybackOverflowBehavior.Queue, manager.Settings.PlaybackPolicy.OverflowBehavior);

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"AudioDispatchMode\": \"PolicyAccepted\"", json);
        Assert.Contains("\"SettingsSchemaVersion\": 6", json);
    }

    [Fact]
    public void V5File_MigratesToOverlayAcceptedAndSingleRules_KeepingLegacyFields()
    {
        var manager = LoadFrom("""
            {
              "SettingsSchemaVersion": 5,
              "SoundEnabled": true,
              "DefaultSound": "sounds/ding.wav",
              "PerAppSounds": {
                "Discord": "sounds/discord.wav",
                "Slack": "None",
                "Teams": "",
                "Evil": "C:\\outside\\evil.wav"
              }
            }
            """);

        var s = manager.Settings;
        Assert.Equal(AudioDispatchMode.OverlayAccepted, s.AudioDispatchMode);
        Assert.Equal(6, s.SettingsSchemaVersion);

        Assert.True(s.DefaultSoundRule.Enabled);
        Assert.Equal(SoundSelectionMode.Single, s.DefaultSoundRule.Mode);
        Assert.Equal(new[] { "sounds/ding.wav" }, s.DefaultSoundRule.Files);

        // Only valid per-app sounds become rules; "None"/empty/untrusted inherited the default in v5.
        var discord = Assert.Single(s.AppSoundRules);
        Assert.Equal("Discord", discord.Key);
        Assert.Equal(SoundSelectionMode.Single, discord.Value.Mode);
        Assert.Equal(new[] { "sounds/discord.wav" }, discord.Value.Files);
        Assert.False(discord.Value.ExplicitSilence);

        // Legacy fields are kept so the original app can still read the file.
        Assert.True(s.SoundEnabled);
        Assert.Single(s.PerAppSounds);
        var saved = File.ReadAllText(SettingsPath);
        Assert.Contains("\"DefaultSound\": \"sounds/ding.wav\"", saved);
        Assert.Contains("\"Discord\": \"sounds/discord.wav\"", saved);
        Assert.Contains("\"AudioDispatchMode\": \"OverlayAccepted\"", saved);
        Assert.Contains("\"Mode\": \"Single\"", saved);
    }

    [Fact]
    public void V5File_DefaultNone_BecomesEmptySingleRule_AndSoundDisabledStaysDisabled()
    {
        var manager = LoadFrom("""
            { "SettingsSchemaVersion": 5, "SoundEnabled": false, "DefaultSound": "None", "PerAppSounds": {} }
            """);

        Assert.False(manager.Settings.SoundEnabled);
        Assert.Equal(SoundSelectionMode.Single, manager.Settings.DefaultSoundRule.Mode);
        Assert.Empty(manager.Settings.DefaultSoundRule.Files);
        Assert.Empty(manager.Settings.AppSoundRules);
        Assert.Equal(AudioDispatchMode.OverlayAccepted, manager.Settings.AudioDispatchMode);
    }

    [Fact]
    public void V5File_TrustedWindowsSound_IsKeptAsAbsolutePath()
    {
        var windowsSound = SoundService.GetWindowsSounds().FirstOrDefault();
        if (windowsSound == null)
            return; // No registry sounds on this machine; covered by managed-sound cases.

        var escaped = JsonSerializer.Serialize(windowsSound.WavPath);
        var manager = LoadFrom($$"""
            { "SettingsSchemaVersion": 5, "SoundEnabled": true, "DefaultSound": {{escaped}}, "PerAppSounds": { "Mail": {{escaped}} } }
            """);

        Assert.Equal(new[] { windowsSound.WavPath }, manager.Settings.DefaultSoundRule.Files);
        Assert.Equal(new[] { windowsSound.WavPath }, manager.Settings.AppSoundRules["Mail"].Files);
    }

    [Fact]
    public void FileWithoutSchemaVersion_IsTreatedAsLegacy()
    {
        var manager = LoadFrom("""{ "FontSize": 18 }""");

        Assert.Equal(AudioDispatchMode.OverlayAccepted, manager.Settings.AudioDispatchMode);
        Assert.Equal(18, manager.Settings.FontSize);
        Assert.Equal(6, manager.Settings.SettingsSchemaVersion);
    }

    [Fact]
    public void V6File_KeepsStoredModeAndRules()
    {
        var manager = LoadFrom("""
            {
              "SettingsSchemaVersion": 6,
              "AudioDispatchMode": "PolicyAccepted",
              "UiLanguage": "ko-KR",
              "DefaultSoundRule": { "Enabled": true, "Mode": "Random", "Files": ["sounds/a.wav", "sounds/b.wav"], "AvoidRepeat": true },
              "AppSoundRules": { "OpenAI.Codex_2p2nqsd0c76g0!App": { "Mode": "Sequential", "Files": ["sounds/c.wav"] }, "Discord": { "ExplicitSilence": true } }
            }
            """);

        var s = manager.Settings;
        Assert.Equal(AudioDispatchMode.PolicyAccepted, s.AudioDispatchMode);
        Assert.Equal("ko-KR", s.UiLanguage);
        Assert.Equal(SoundSelectionMode.Random, s.DefaultSoundRule.Mode);
        Assert.True(s.DefaultSoundRule.AvoidRepeat);
        Assert.Equal(2, s.DefaultSoundRule.Files.Count);
        Assert.Equal(SoundSelectionMode.Sequential, s.AppSoundRules["openai.codex_2p2nqsd0c76g0!app"].Mode);
        Assert.True(s.AppSoundRules["discord"].ExplicitSilence);
    }

    [Fact]
    public void UnknownEnumAndLanguageValues_FallBackSafely_WithoutResettingOtherSettings()
    {
        var manager = LoadFrom("""
            {
              "SettingsSchemaVersion": 6,
              "FontSize": 21,
              "AudioDispatchMode": "Sometimes",
              "UiLanguage": "fr-FR",
              "DefaultSoundRule": { "Mode": "Shuffle" },
              "PlaybackPolicy": { "OverflowBehavior": 99, "MaxQueued": 500 }
            }
            """);

        Assert.Null(manager.PreservedCorruptFilePath);
        Assert.Equal(21, manager.Settings.FontSize);
        Assert.Equal(AudioDispatchMode.OverlayAccepted, manager.Settings.AudioDispatchMode);
        Assert.Null(manager.Settings.UiLanguage);
        Assert.Equal(SoundSelectionMode.Single, manager.Settings.DefaultSoundRule.Mode);
        Assert.Equal(PlaybackOverflowBehavior.Queue, manager.Settings.PlaybackPolicy.OverflowBehavior);
        Assert.Equal(100, manager.Settings.PlaybackPolicy.MaxQueued);
    }

    [Fact]
    public void FutureSchema_IsReadOnly_AndNeverOverwritten()
    {
        const string futureJson = """
            { "SettingsSchemaVersion": 7, "FontSize": 20, "SomeFutureField": { "x": 1 } }
            """;
        var manager = LoadFrom(futureJson);

        Assert.True(manager.IsReadOnlyDueToNewerSchema);
        Assert.Equal(7, manager.LoadedFileSchemaVersion);
        Assert.Equal(20, manager.Settings.FontSize);
        Assert.Equal(futureJson, File.ReadAllText(SettingsPath));

        manager.Save();
        Assert.Equal(SettingsSaveStatus.SkippedReadOnly, manager.LastSaveStatus);

        var updated = manager.Settings.Clone();
        updated.FontSize = 30;
        manager.Apply(updated);
        Assert.Equal(30, manager.Settings.FontSize); // in-memory for this session
        Assert.Equal(futureJson, File.ReadAllText(SettingsPath));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void ApplyingSnapshotWithNewerVersion_IsSavedAsCurrentSchema_SoNextStartIsNotReadOnly()
    {
        var manager = new SettingsManager(_dir);
        manager.Load();

        manager.ApplySnapshot(new AppSettings { SettingsSchemaVersion = 9, FontSize = 25 });

        var reloaded = new SettingsManager(_dir);
        reloaded.Load();
        Assert.False(reloaded.IsReadOnlyDueToNewerSchema);
        Assert.Equal(6, reloaded.Settings.SettingsSchemaVersion);
        Assert.Equal(25, reloaded.Settings.FontSize);
    }

    [Fact]
    public void CorruptFile_IsPreservedBeforeDefaultsAreSaved()
    {
        const string corrupt = "{ this is not json";
        var manager = LoadFrom(corrupt);

        Assert.False(manager.IsReadOnlyDueToNewerSchema);
        Assert.NotNull(manager.PreservedCorruptFilePath);
        Assert.Equal(Path.Combine(_dir, "settings.json.corrupt-20261009-123456"), manager.PreservedCorruptFilePath);
        Assert.Equal(corrupt, File.ReadAllText(manager.PreservedCorruptFilePath!));

        // Defaults still apply (upstream behavior) and the new file is valid v6 JSON.
        Assert.Equal("Segoe UI", manager.Settings.FontFamily);
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(6, document.RootElement.GetProperty("SettingsSchemaVersion").GetInt32());
    }

    [Fact]
    public void CorruptFile_SecondBackupDoesNotOverwriteFirst()
    {
        File.WriteAllText(SettingsPath + ".corrupt-20261009-123456", "older backup");
        var manager = LoadFrom("not json at all");

        Assert.Equal(SettingsPath + ".corrupt-20261009-123456-1", manager.PreservedCorruptFilePath);
        Assert.Equal("older backup", File.ReadAllText(SettingsPath + ".corrupt-20261009-123456"));
    }

    [Fact]
    public void OversizedFile_IsPreservedBeforeDefaultsAreSaved()
    {
        var huge = "{\"FontFamily\":\"" + new string('x', (int)SettingsManager.MaxSettingsFileBytes) + "\"}";
        var manager = LoadFrom(huge);

        Assert.NotNull(manager.PreservedCorruptFilePath);
        Assert.Equal(huge.Length, new FileInfo(manager.PreservedCorruptFilePath!).Length);
        Assert.Equal("Segoe UI", manager.Settings.FontFamily);
    }

    [Fact]
    public void ValidFile_DoesNotCreateCorruptBackup()
    {
        var manager = LoadFrom("""{ "SettingsSchemaVersion": 6, "FontSize": 15 }""");

        Assert.Null(manager.PreservedCorruptFilePath);
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Save_WhenReplaceFails_KeepsPreviousFileAndLeavesNoTempFiles()
    {
        var manager = new SettingsManager(_dir);
        manager.Load();
        var original = File.ReadAllText(SettingsPath);

        var updated = manager.Settings.Clone();
        updated.FontSize = 33;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            manager.Apply(updated);
            Assert.Equal(SettingsSaveStatus.Failed, manager.LastSaveStatus);
        }

        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));

        manager.Save();
        Assert.Equal(SettingsSaveStatus.Saved, manager.LastSaveStatus);
        Assert.Contains("\"FontSize\": 33", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void AtomicFileWriter_FailureBeforeCommit_PreservesOriginal()
    {
        var path = Path.Combine(_dir, "data.json");
        File.WriteAllText(path, "original");

        Assert.Throws<IOException>(() =>
            AtomicFileWriter.WriteAllText(path, "replacement", _ => throw new IOException("simulated")));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(new[] { "data.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void AtomicFileWriter_CreatesAndReplacesFiles()
    {
        var path = Path.Combine(_dir, "nested", "data.json");
        AtomicFileWriter.WriteAllText(path, "first");
        Assert.Equal("first", File.ReadAllText(path));

        AtomicFileWriter.WriteAllText(path, "second");
        Assert.Equal("second", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void ForkFields_RoundTripThroughSaveAndLoad()
    {
        var manager = new SettingsManager(_dir);
        manager.Load();
        var updated = manager.Settings.Clone();
        updated.UiLanguage = "en-US";
        updated.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
        updated.StartMinimizedToTray = true;
        updated.RestartOnCrash = true;
        updated.PlaybackPolicy.MinIntervalPerAppSeconds = 0;
        updated.PlaybackPolicy.OverflowBehavior = PlaybackOverflowBehavior.Skip;
        updated.DefaultSoundRule = new SoundRule { Mode = SoundSelectionMode.Sequential, Files = { "sounds/1.wav", "sounds/2.wav" } };
        updated.AppSoundRules["Discord"] = new SoundRule { ExplicitSilence = true };
        updated.ManualMuteConfirmations["Discord"] = new ManualMuteConfirmation
        {
            State = ManualMuteConfirmation.UserConfirmed,
            UpdatedAt = new DateTimeOffset(2026, 10, 9, 1, 2, 3, TimeSpan.Zero),
        };
        manager.Apply(updated);

        var reloaded = new SettingsManager(_dir);
        reloaded.Load();
        var s = reloaded.Settings;
        Assert.Equal("en-US", s.UiLanguage);
        Assert.Equal(AudioDispatchMode.OverlayAccepted, s.AudioDispatchMode);
        Assert.True(s.StartMinimizedToTray);
        Assert.True(s.RestartOnCrash);
        Assert.Equal(0, s.PlaybackPolicy.MinIntervalPerAppSeconds);
        Assert.Equal(PlaybackOverflowBehavior.Skip, s.PlaybackPolicy.OverflowBehavior);
        Assert.Equal(SoundSelectionMode.Sequential, s.DefaultSoundRule.Mode);
        Assert.Equal(new[] { "sounds/1.wav", "sounds/2.wav" }, s.DefaultSoundRule.Files);
        Assert.True(s.AppSoundRules["Discord"].ExplicitSilence);
        Assert.Equal(ManualMuteConfirmation.UserConfirmed, s.ManualMuteConfirmations["discord"].State);

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"OverflowBehavior\": \"Skip\"", json);
        Assert.Contains("\"Mode\": \"Sequential\"", json);
    }

    [Fact]
    public void SoundRuleFiles_KeepManagedRelative_DropOutsidePaths_AllowLocalLibraryFolder()
    {
        var library = TestAppDataIsolation.CreateUniqueDirectory("library");
        var libraryWav = Path.Combine(library, "voice.wav");
        var nestedWav = Path.Combine(library, "sub", "nested.wav");
        var managedAbsolute = Path.Combine(ManagedAssetPathHelper.GetRoot(ManagedAssetPathHelper.SoundsFolderName), "m.wav");

        var manager = new SettingsManager(_dir);
        manager.Load();
        var updated = manager.Settings.Clone();
        updated.SoundLibraryFolders = new List<string> { library + Path.DirectorySeparatorChar, library, @"\\server\share" };
        updated.DefaultSoundRule = new SoundRule
        {
            Mode = SoundSelectionMode.Random,
            Files = { managedAbsolute, libraryWav, nestedWav, @"C:\outside\x.wav", Path.Combine(library, "notes.txt"), "None", "" },
        };
        manager.Apply(updated);

        Assert.Equal(new[] { library }, manager.Settings.SoundLibraryFolders);
        Assert.Equal(new[] { "sounds/m.wav", libraryWav }, manager.Settings.DefaultSoundRule.Files);
    }

    // ---- P2-B follow-up (reviewer decision 4): migrated files get MinIntervalPerAppSeconds = 0 ----

    [Fact]
    public void V5File_MigratedPolicy_HasNoPerAppCooldown_OtherPolicyValuesStayDefault()
    {
        var manager = LoadFrom("""
            { "SettingsSchemaVersion": 5, "SoundEnabled": true, "DefaultSound": "sounds/ding.wav" }
            """);

        var policy = manager.Settings.PlaybackPolicy;
        Assert.Equal(0, policy.MinIntervalPerAppSeconds);
        Assert.Equal(5, policy.MaxQueued);
        Assert.Equal(30, policy.MaxQueueWaitSeconds);
        Assert.Equal(PlaybackOverflowBehavior.Queue, policy.OverflowBehavior);
        Assert.Contains("\"MinIntervalPerAppSeconds\": 0", File.ReadAllText(SettingsPath));

        // The saved v6 file is not migrated a second time on the next start.
        var reloaded = new SettingsManager(_dir);
        reloaded.Load();
        Assert.Equal(0, reloaded.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
    }

    [Fact]
    public void FileWithoutSchemaVersion_AndCorruptFile_AreLegacy_SoCooldownIsZero()
    {
        Assert.Equal(0, LoadFrom("""{ "FontSize": 18 }""").Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
        Assert.Equal(0, LoadFrom("{ not json").Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
    }

    [Fact]
    public void NewInstall_KeepsFourSecondCooldown()
    {
        var manager = new SettingsManager(_dir);
        manager.Load();
        Assert.Equal(4, manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    public void V6File_PlaybackPolicyIsNotTouched(double minInterval)
    {
        var manager = LoadFrom($$"""
            { "SettingsSchemaVersion": 6, "PlaybackPolicy": { "MinIntervalPerAppSeconds": {{minInterval}}, "MaxQueued": 3 } }
            """);

        Assert.Equal(minInterval, manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
        Assert.Equal(3, manager.Settings.PlaybackPolicy.MaxQueued);
    }

    [Fact]
    public void LegacySnapshot_Applied_HasNoPerAppCooldown_ButV6SnapshotKeepsItsPolicy()
    {
        var manager = new SettingsManager(_dir);
        manager.Load();
        Assert.Equal(4, manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);

        var legacyProfile = new AppSettings { SettingsSchemaVersion = 5, FontSize = 21 };
        manager.ApplySnapshot(legacyProfile);
        Assert.Equal(0, manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
        Assert.Equal(21, manager.Settings.FontSize);

        var v6Profile = manager.Settings.Clone();
        v6Profile.PlaybackPolicy.MinIntervalPerAppSeconds = 9;
        manager.ApplySnapshot(v6Profile);
        Assert.Equal(9, manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
    }
}
