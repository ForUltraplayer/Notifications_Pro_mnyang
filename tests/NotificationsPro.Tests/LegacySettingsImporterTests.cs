// @file LegacySettingsImporterTests.cs
// @author: rudals252
// @brief 원본 설정 가져오기 서비스 테스트. 실제 %AppData%·%LocalAppData%\Packages는 읽지 않고, 임시 폴더에 가짜 MSIX/언패키지 원본 구조를 만들어 탐지·복사·백업·이관·원본 불변을 검증한다.

using System.IO;
using System.Security.Cryptography;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class LegacySettingsImporterTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 10, 9, 15, 30, 0);

    private readonly string _root = TestAppDataIsolation.CreateUniqueDirectory("legacy-import");
    private string PackagedRoot => Path.Combine(_root, "Packages", "NotificationsPro_h8abfdfjdvdc6", "LocalCache", "Roaming", "NotificationsPro");
    private string UnpackagedRoot => Path.Combine(_root, "Roaming", "NotificationsPro");
    private string TargetRoot => Path.Combine(_root, "Fork");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private IReadOnlyList<LegacySettingsSource> Sources => new[]
    {
        new LegacySettingsSource(LegacySettingsSourceKind.PackagedMsix, PackagedRoot, UnpackagedRoot),
        new LegacySettingsSource(LegacySettingsSourceKind.Unpackaged, UnpackagedRoot, UnpackagedRoot),
    };

    private SettingsManager CreateTarget()
    {
        var manager = new SettingsManager(TargetRoot);
        manager.Load();
        return manager;
    }

    private LegacySettingsImporter CreateImporter(SettingsManager target) => new(target, Sources, () => FixedNow);

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void CreatePackagedSource(string? settingsJson = null)
    {
        WriteFile(Path.Combine(PackagedRoot, "settings.json"), settingsJson ?? """
            {
              "SettingsSchemaVersion": 5,
              "FontSize": 19,
              "SoundEnabled": true,
              "DefaultSound": "sounds/ding.wav",
              "PerAppSounds": { "Discord": "sounds/discord.wav", "Slack": "None" },
              "PerAppIcons": { "Discord": "icons/discord.png" }
            }
            """);
        WriteFile(Path.Combine(PackagedRoot, "sounds", "ding.wav"), "RIFF-ding");
        WriteFile(Path.Combine(PackagedRoot, "sounds", "discord.wav"), "RIFF-discord");
        WriteFile(Path.Combine(PackagedRoot, "icons", "discord.png"), "PNG-discord");
        WriteFile(Path.Combine(PackagedRoot, "themes", "Night.json"), """{ "Name": "Night" }""");
        WriteFile(Path.Combine(PackagedRoot, "profiles", "Work.json"), """{ "SettingsSchemaVersion": 5 }""");
        WriteFile(Path.Combine(PackagedRoot, "backgrounds", "bg.png"), "PNG-bg");
    }

    private static Dictionary<string, string> SnapshotTree(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                    + "|" + File.GetLastWriteTimeUtc(path).Ticks);
    }

    [Fact]
    public void DefaultSources_PointAtPackagedVirtualizedAndUnpackagedPaths()
    {
        // Path construction only — nothing is read from these folders in tests.
        var sources = LegacySettingsImporter.GetDefaultSources();

        Assert.Equal(2, sources.Count);
        Assert.Equal(LegacySettingsSourceKind.PackagedMsix, sources[0].Kind);
        Assert.EndsWith(Path.Combine("Packages", "NotificationsPro_h8abfdfjdvdc6", "LocalCache", "Roaming", "NotificationsPro"), sources[0].RootPath);
        Assert.Equal(LegacySettingsSourceKind.Unpackaged, sources[1].Kind);
        Assert.EndsWith(Path.Combine("Roaming", "NotificationsPro"), sources[1].RootPath);
        Assert.Equal(sources[1].RootPath, sources[0].AppVisibleRootPath);
    }

    [Fact]
    public void Detect_ReportsExistenceSchemaAndModifiedTime()
    {
        CreatePackagedSource();
        File.SetLastWriteTimeUtc(Path.Combine(PackagedRoot, "settings.json"), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var importer = CreateImporter(CreateTarget());

        var candidates = importer.DetectCandidates();

        var packaged = candidates.Single(c => c.Kind == LegacySettingsSourceKind.PackagedMsix);
        Assert.True(packaged.Exists);
        Assert.True(packaged.CanImport);
        Assert.Equal(5, packaged.SchemaVersion);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), packaged.LastWriteTimeUtc);
        Assert.Contains("sounds", packaged.AssetFolders);
        var unpackaged = candidates.Single(c => c.Kind == LegacySettingsSourceKind.Unpackaged);
        Assert.False(unpackaged.Exists);
        Assert.False(importer.RequiresUserChoice());
    }

    [Fact]
    public void Detect_BothPresent_RequiresUserChoice()
    {
        CreatePackagedSource();
        WriteFile(Path.Combine(UnpackagedRoot, "settings.json"), """{ "SettingsSchemaVersion": 4 }""");

        var importer = CreateImporter(CreateTarget());

        Assert.True(importer.RequiresUserChoice());
        Assert.Equal(new int?[] { 5, 4 }, importer.DetectCandidates().Select(c => c.SchemaVersion));
    }

    [Fact]
    public void Detect_FlagsInvalidAndNewerSources()
    {
        WriteFile(Path.Combine(PackagedRoot, "settings.json"), "{ broken");
        WriteFile(Path.Combine(UnpackagedRoot, "settings.json"), """{ "SettingsSchemaVersion": 9 }""");

        var candidates = CreateImporter(CreateTarget()).DetectCandidates();

        Assert.False(candidates[0].IsValidJson);
        Assert.False(candidates[0].CanImport);
        Assert.True(candidates[1].IsNewerSchema);
        Assert.False(candidates[1].CanImport);
    }

    [Fact]
    public void Import_CopiesAssets_MigratesToV6_BacksUpForkSettings_AndLeavesSourceUntouched()
    {
        CreatePackagedSource();
        var sourceBefore = SnapshotTree(PackagedRoot);
        var target = CreateTarget(); // creates a fork settings.json (new install)
        var forkBefore = File.ReadAllText(Path.Combine(TargetRoot, "settings.json"));
        var importer = CreateImporter(target);
        var candidate = importer.DetectCandidates().Single(c => c.Kind == LegacySettingsSourceKind.PackagedMsix);

        var result = importer.Import(candidate);

        Assert.True(result.Success, result.Error);
        Assert.Equal(SettingsSaveStatus.Saved, result.SaveStatus);
        Assert.Equal(5, result.SourceSchemaVersion);
        Assert.Equal(6, result.AssetsCopied);
        Assert.Equal(0, result.AssetsBackedUp);

        // Source is byte-for-byte and timestamp unchanged; nothing added there.
        Assert.Equal(sourceBefore, SnapshotTree(PackagedRoot));

        // Fork settings were backed up before being replaced.
        var backup = Path.Combine(TargetRoot, "settings.json.pre-import-20261009-153000");
        Assert.Equal(backup, result.SettingsBackupPath);
        Assert.Equal(forkBefore, File.ReadAllText(backup));

        // Assets copied with the same relative layout.
        Assert.Equal("RIFF-ding", File.ReadAllText(Path.Combine(TargetRoot, "sounds", "ding.wav")));
        Assert.True(File.Exists(Path.Combine(TargetRoot, "themes", "Night.json")));
        Assert.True(File.Exists(Path.Combine(TargetRoot, "profiles", "Work.json")));
        Assert.True(File.Exists(Path.Combine(TargetRoot, "backgrounds", "bg.png")));

        // Migrated and active in the running manager, and persisted as v6.
        var s = target.Settings;
        Assert.Equal(19, s.FontSize);
        Assert.Equal(AudioDispatchMode.OverlayAccepted, s.AudioDispatchMode);
        Assert.Equal(new[] { "sounds/ding.wav" }, s.DefaultSoundRule.Files);
        Assert.Equal(new[] { "sounds/discord.wav" }, Assert.Single(s.AppSoundRules).Value.Files);
        var reloaded = new SettingsManager(TargetRoot);
        reloaded.Load();
        Assert.Equal(6, reloaded.Settings.SettingsSchemaVersion);
        Assert.Equal(AudioDispatchMode.OverlayAccepted, reloaded.Settings.AudioDispatchMode);
        Assert.Contains("\"DefaultSound\": \"sounds/ding.wav\"", File.ReadAllText(Path.Combine(TargetRoot, "settings.json")));
    }

    [Fact]
    public void Import_PreservesForkLocalValues()
    {
        CreatePackagedSource();
        var target = CreateTarget();
        var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
        var local = target.Settings.Clone();
        local.UiLanguage = "ko-KR";
        local.SoundLibraryFolders = new List<string> { library };
        local.ManualMuteConfirmations["Discord"] = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed };
        target.Apply(local);
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.True(result.Success, result.Error);
        Assert.Equal("ko-KR", target.Settings.UiLanguage);
        Assert.Equal(new[] { library }, target.Settings.SoundLibraryFolders);
        Assert.Equal(ManualMuteConfirmation.UserConfirmed, target.Settings.ManualMuteConfirmations["Discord"].State);
    }

    [Fact]
    public void Import_ConflictingForkAsset_IsMovedAsideNotOverwritten_IdenticalIsSkipped()
    {
        CreatePackagedSource();
        WriteFile(Path.Combine(TargetRoot, "sounds", "ding.wav"), "FORK-own-ding");
        WriteFile(Path.Combine(TargetRoot, "icons", "discord.png"), "PNG-discord");
        var target = CreateTarget();
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.AssetsBackedUp);
        Assert.Equal(1, result.AssetsSkippedIdentical);
        Assert.Equal(Path.Combine(TargetRoot, "pre-import-20261009-153000"), result.AssetBackupFolder);
        Assert.Equal("FORK-own-ding", File.ReadAllText(Path.Combine(result.AssetBackupFolder!, "sounds", "ding.wav")));
        Assert.Equal("RIFF-ding", File.ReadAllText(Path.Combine(TargetRoot, "sounds", "ding.wav")));
    }

    [Fact]
    public void Import_WithoutExistingForkSettings_CreatesNoSettingsBackup()
    {
        CreatePackagedSource();
        var target = new SettingsManager(TargetRoot); // not loaded/saved yet → no settings.json
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.SettingsBackupPath);
        Assert.True(File.Exists(Path.Combine(TargetRoot, "settings.json")));
    }

    [Fact]
    public void Import_RebasesAbsoluteLegacyAssetPaths()
    {
        var absoluteSound = Path.Combine(UnpackagedRoot, "sounds", "ding.wav");
        var escaped = System.Text.Json.JsonSerializer.Serialize(absoluteSound);
        CreatePackagedSource($$"""
            { "SettingsSchemaVersion": 5, "SoundEnabled": true, "DefaultSound": {{escaped}}, "PerAppSounds": { "Mail": {{escaped}} } }
            """);
        var target = CreateTarget();
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(new[] { "sounds/ding.wav" }, target.Settings.DefaultSoundRule.Files);
        Assert.Equal(new[] { "sounds/ding.wav" }, target.Settings.AppSoundRules["Mail"].Files);
        Assert.Contains("\"DefaultSound\": \"sounds/ding.wav\"", File.ReadAllText(Path.Combine(TargetRoot, "settings.json")));
    }

    [Fact]
    public void Import_RefusesInvalidSource_WithoutTouchingFork()
    {
        WriteFile(Path.Combine(PackagedRoot, "settings.json"), "{ broken");
        WriteFile(Path.Combine(PackagedRoot, "sounds", "x.wav"), "RIFF");
        var target = CreateTarget();
        var forkBefore = SnapshotTree(TargetRoot);
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.False(result.Success);
        Assert.Equal(forkBefore, SnapshotTree(TargetRoot));
    }

    [Fact]
    public void Import_RefusesWhenForkSettingsAreReadOnly()
    {
        CreatePackagedSource();
        WriteFile(Path.Combine(TargetRoot, "settings.json"), """{ "SettingsSchemaVersion": 99 }""");
        var target = CreateTarget();
        Assert.True(target.IsReadOnlyDueToNewerSchema);
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.False(result.Success);
        Assert.Equal("""{ "SettingsSchemaVersion": 99 }""", File.ReadAllText(Path.Combine(TargetRoot, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(TargetRoot, "sounds")));
    }

    [Fact]
    public void Import_LegacySource_GetsNoPerAppCooldown()
    {
        CreatePackagedSource();
        var target = CreateTarget();
        Assert.Equal(4, target.Settings.PlaybackPolicy.MinIntervalPerAppSeconds); // new install default
        var importer = CreateImporter(target);

        var result = importer.Import(importer.DetectCandidates()[0]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, target.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
        var reloaded = new SettingsManager(TargetRoot);
        reloaded.Load();
        Assert.Equal(0, reloaded.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
    }
}
