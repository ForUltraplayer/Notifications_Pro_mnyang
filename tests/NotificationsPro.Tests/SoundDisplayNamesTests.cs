// @file SoundDisplayNamesTests.cs
// @author: rudals252
// @brief SoundDisplayNames(파일 참조 → 표시 이름, 스키마 v6 선택 필드) 계약 테스트: 저장·로드 왕복, 허용되지 않은 키 제거, 값 정리, 프로필 이전(관리 파일만, 외부 경로 제외), 설정창 저장 시 보존.

using System.IO;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class SoundDisplayNamesTests : IDisposable
{
    private readonly string _root = TestAppDataIsolation.CreateUniqueDirectory("display-names");

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

    [Fact]
    public void NewInstall_HasEmptyDisplayNames()
    {
        Assert.Empty(CreateLoadedManager().Settings.SoundDisplayNames);
    }

    [Fact]
    public void DisplayNames_RoundTrip_AndInvalidEntriesAreDropped()
    {
        var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
        var external = Path.Combine(library, "voice.wav");
        var manager = CreateLoadedManager();
        var updated = manager.Settings.Clone();
        updated.SoundLibraryFolders = new List<string> { library };
        updated.SoundDisplayNames = new Dictionary<string, string>
        {
            ["sounds/ding-1a2b3c4d.wav"] = "  ding\t ",
            [external] = "My voice",
            [@"C:\outside\x.wav"] = "outside",
            ["sounds/empty.wav"] = "   ",
            ["sounds/long.wav"] = new string('a', 300),
        };
        manager.Apply(updated);

        var reloaded = new SettingsManager(Path.Combine(_root, "settings"));
        reloaded.Load();
        var names = reloaded.Settings.SoundDisplayNames;
        Assert.Equal("ding", names["sounds/DING-1a2b3c4d.wav"]);
        Assert.Equal("My voice", names[external]);
        Assert.False(names.ContainsKey(@"C:\outside\x.wav"));
        Assert.False(names.ContainsKey("sounds/empty.wav"));
        Assert.Equal(AppSettingsAssetPathHelper.MaxSoundDisplayNameLength, names["sounds/long.wav"].Length);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void DisplayNames_TravelWithProfiles_ButExternalPathsDoNot()
    {
        var library = TestAppDataIsolation.CreateUniqueDirectory("lib");
        var external = Path.Combine(library, "voice.wav");
        var manager = CreateLoadedManager();
        var updated = manager.Settings.Clone();
        updated.SoundLibraryFolders = new List<string> { library };
        updated.SoundDisplayNames = new Dictionary<string, string>
        {
            ["sounds/chime-0011aabb.wav"] = "chime",
            [external] = "My voice",
        };
        manager.Apply(updated);

        var profiles = new ProfileManager(Path.Combine(_root, "profiles"));
        profiles.SaveProfile("Work", manager.Settings.Clone());

        var json = File.ReadAllText(Path.Combine(_root, "profiles", "Work.json"));
        Assert.Contains("chime-0011aabb.wav", json);
        Assert.DoesNotContain(library.Replace("\\", "\\\\"), json);
        var loaded = profiles.LoadProfile("Work");
        Assert.NotNull(loaded);
        Assert.Equal("chime", Assert.Single(loaded!.SoundDisplayNames).Value);
    }

    [Fact]
    public void Clone_DeepCopiesDisplayNames()
    {
        var settings = new AppSettings();
        settings.SoundDisplayNames["sounds/a.wav"] = "a";
        var clone = settings.Clone();
        clone.SoundDisplayNames["sounds/b.wav"] = "b";
        Assert.Single(settings.SoundDisplayNames);
    }
}
