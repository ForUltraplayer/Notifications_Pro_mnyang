// @file SoundLibraryServiceTests.cs
// @author: rudals252
// @brief WAV 보관함 서비스 테스트: 바로 아래 WAV만 나열, 헤더 기반 상태(사용 가능·손상·미지원·결손·드라이브 분리), 엔진 확인, 가져오기 충돌·동일 내용·원본 불변, 목록 제외와 실제 삭제 분리, 외부 폴더 등록(UNC·상대 경로·없음·정션 거부), 재생 직전 허용 위치 확인.

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class SoundLibraryServiceTests
{
    private sealed class FakeProber : ISoundFileProber
    {
        public HashSet<string> Unplayable { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Probed { get; } = new();

        public Task<SoundProbeResult> ProbeAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            Probed.Add(fullPath);
            return Task.FromResult(Unplayable.Contains(Path.GetFileName(fullPath))
                ? new SoundProbeResult(false, null, SoundEngineFailureKind.FileUnsupported)
                : new SoundProbeResult(true, TimeSpan.FromMilliseconds(200), null));
        }
    }

    private sealed class Env
    {
        public Env(IEnumerable<string>? trusted = null)
        {
            Root = TestAppDataIsolation.CreateUniqueDirectory("library");
            Managed = Path.Combine(Root, "sounds");
            External = Path.Combine(Root, "external");
            Directory.CreateDirectory(Managed);
            Directory.CreateDirectory(External);
            Settings = new AppSettings();
            Trusted = trusted?.ToList() ?? new List<string>();
            Service = new SoundLibraryService(() => Settings, Prober, Managed, () => Trusted);
        }

        public string Root { get; }
        public string Managed { get; }
        public string External { get; }
        public AppSettings Settings { get; }
        public List<string> Trusted { get; }
        public FakeProber Prober { get; } = new();
        public SoundLibraryService Service { get; }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ---- listing / status ----

    [Fact]
    public void Refresh_ListsOnlyTopLevelWavs_InManagedAndRegisteredFolders()
    {
        var env = new Env();
        TestWavFactory.Write(env.Managed, "a.wav", TestWavFactory.Pcm());
        TestWavFactory.Write(Path.Combine(env.Managed, "sub"), "nested.wav", TestWavFactory.Pcm());
        File.WriteAllText(Path.Combine(env.Managed, "notes.txt"), "x");
        TestWavFactory.Write(env.External, "ext.wav", TestWavFactory.Pcm());
        TestWavFactory.Write(Path.Combine(env.External, "deeper"), "hidden.wav", TestWavFactory.Pcm());
        env.Settings.SoundLibraryFolders.Add(env.External);

        var snapshot = env.Service.Refresh();

        Assert.Equal(new[] { "a", "ext" }, snapshot.Entries.Select(e => e.DisplayName).OrderBy(x => x));
        var managed = snapshot.Entries.Single(e => e.Source == SoundFileSource.Managed);
        Assert.Equal("sounds/a.wav", managed.RuleReference);
        var external = snapshot.Entries.Single(e => e.Source == SoundFileSource.ExternalFolder);
        Assert.Equal(Path.Combine(env.External, "ext.wav"), external.RuleReference);
        Assert.All(snapshot.Folders, folder => Assert.Equal(SoundFileStatus.Usable, folder.Status));
    }

    [Fact]
    public void Refresh_StatusComesFromTheHeader_NotTheExtension()
    {
        var env = new Env();
        TestWavFactory.Write(env.Managed, "good.wav", TestWavFactory.Pcm());
        TestWavFactory.Write(env.Managed, "badheader.wav", TestWavFactory.BadHeader());
        TestWavFactory.Write(env.Managed, "empty.wav", Array.Empty<byte>());
        TestWavFactory.Write(env.Managed, "mp3inside.wav", TestWavFactory.FormatTag(0x55));

        var byName = env.Service.Refresh().Entries.ToDictionary(e => e.DisplayName, e => e);

        Assert.Equal(SoundFileStatus.Usable, byName["good"].Status);
        Assert.NotNull(byName["good"].Duration);
        Assert.Equal(SoundFileStatus.Damaged, byName["badheader"].Status);
        Assert.Equal(SoundFileStatus.Damaged, byName["empty"].Status);
        Assert.Equal(SoundFileStatus.Unsupported, byName["mp3inside"].Status);
    }

    [Fact]
    public async Task RefreshAsync_VerifiesUsableFilesWithTheEngine()
    {
        var env = new Env();
        TestWavFactory.Write(env.Managed, "good.wav", TestWavFactory.Pcm());
        TestWavFactory.Write(env.Managed, "engine-refuses.wav", TestWavFactory.Pcm());
        TestWavFactory.Write(env.Managed, "badheader.wav", TestWavFactory.BadHeader());
        env.Prober.Unplayable.Add("engine-refuses.wav");

        var snapshot = await env.Service.RefreshAsync(verifyWithEngine: true);
        var byName = snapshot.Entries.ToDictionary(e => e.DisplayName, e => e.Status);

        Assert.Equal(SoundFileStatus.Usable, byName["good"]);
        Assert.Equal(SoundFileStatus.Unsupported, byName["engine-refuses"]);
        Assert.Equal(SoundFileStatus.Damaged, byName["badheader"]);
        Assert.DoesNotContain(env.Prober.Probed, path => path.EndsWith("badheader.wav"));
    }

    [Fact]
    public void MissingFolder_AndUnavailableDrive_AreReported()
    {
        var env = new Env();
        var unusedDrive = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(c => $"{(char)c}:\\")
            .FirstOrDefault(root => !Directory.Exists(root));
        env.Settings.SoundLibraryFolders.Add(Path.Combine(env.Root, "does-not-exist"));
        if (unusedDrive != null)
            env.Settings.SoundLibraryFolders.Add(unusedDrive + "Sounds");

        var folders = env.Service.Refresh().Folders.Where(f => f.Source == SoundFileSource.ExternalFolder).ToList();

        Assert.Contains(folders, f => f.Status == SoundFileStatus.Missing);
        if (unusedDrive != null)
        {
            Assert.Contains(folders, f => f.Status == SoundFileStatus.DriveUnavailable);
            Assert.Equal(SoundFileStatus.DriveUnavailable, env.Service.CheckRuleFile(unusedDrive + @"Sounds\x.wav").Status);
        }
    }

    [Fact]
    public void CheckRuleFile_ReportsMissingManagedFile()
    {
        var env = new Env();

        Assert.Equal(SoundFileStatus.Missing, env.Service.CheckRuleFile("sounds/gone.wav").Status);
    }

    // ---- runtime gate ----

    [Fact]
    public void ResolvePlayablePath_AllowsManagedTrustedAndRegisteredFolderFiles_Only()
    {
        var windowsSound = TestWavFactory.Write(TestAppDataIsolation.CreateUniqueDirectory("fake-windows-media"), "Ding.wav", TestWavFactory.Pcm());
        var env = new Env(trusted: new[] { windowsSound });
        var managed = TestWavFactory.Write(env.Managed, "m.wav", TestWavFactory.Pcm());
        var external = TestWavFactory.Write(env.External, "e.wav", TestWavFactory.Pcm());
        var nested = TestWavFactory.Write(Path.Combine(env.External, "sub"), "n.wav", TestWavFactory.Pcm());
        var outside = TestWavFactory.Write(Path.Combine(env.Root, "elsewhere"), "o.wav", TestWavFactory.Pcm());

        Assert.Equal(managed, env.Service.ResolvePlayablePath("sounds/m.wav"));
        Assert.Equal(windowsSound, env.Service.ResolvePlayablePath(windowsSound));
        Assert.Null(env.Service.ResolvePlayablePath(external)); // folder not registered yet
        env.Settings.SoundLibraryFolders.Add(env.External);
        Assert.Equal(external, env.Service.ResolvePlayablePath(external));
        Assert.Null(env.Service.ResolvePlayablePath(nested));
        Assert.Null(env.Service.ResolvePlayablePath(outside));
        Assert.Null(env.Service.ResolvePlayablePath(@"\\server\share\x.wav"));
        Assert.Null(env.Service.ResolvePlayablePath("None"));
        Assert.Null(env.Service.ResolvePlayablePath("sounds/../../escape.wav"));
    }

    [Fact]
    public void ResolvePlayablePath_RejectsDamagedFiles_AndRemembersEngineFailuresUntilTheFileChanges()
    {
        var env = new Env();
        TestWavFactory.Write(env.Managed, "bad.wav", TestWavFactory.BadHeader());
        var good = TestWavFactory.Write(env.Managed, "good.wav", TestWavFactory.Pcm());

        Assert.Null(env.Service.ResolvePlayablePath("sounds/bad.wav"));
        Assert.Equal(good, env.Service.ResolvePlayablePath("sounds/good.wav"));

        env.Service.MarkUnplayable(good);
        Assert.Null(env.Service.ResolvePlayablePath("sounds/good.wav"));
        Assert.Equal(SoundFileStatus.PlaybackFailed, env.Service.CheckRuleFile("sounds/good.wav").Status);

        File.WriteAllBytes(good, TestWavFactory.Pcm(seconds: 0.3)); // replaced file is retried
        Assert.Equal(good, env.Service.ResolvePlayablePath("sounds/good.wav"));

        env.Service.MarkUnplayable(good);
        env.Service.Refresh(); // refresh also clears remembered failures
        Assert.Equal(good, env.Service.ResolvePlayablePath("sounds/good.wav"));
    }

    // ---- import ----

    [Fact]
    public void Import_CopiesIntoManagedFolder_AndKeepsTheSourceUntouched()
    {
        var env = new Env();
        var source = TestWavFactory.Write(Path.Combine(env.Root, "downloads"), "Chime.wav", TestWavFactory.Pcm());
        var sourceHash = Hash(source);
        var sourceTime = File.GetLastWriteTimeUtc(source);

        var result = env.Service.Import(source);

        Assert.Equal(SoundImportStatus.Copied, result.Status);
        Assert.Equal("sounds/Chime.wav", result.RuleReference);
        Assert.Equal("Chime", result.DisplayName);
        Assert.Equal(sourceHash, Hash(Path.Combine(env.Managed, "Chime.wav")));
        Assert.Equal(sourceHash, Hash(source));
        Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(source));
        Assert.Empty(Directory.GetFiles(env.Managed, "*.tmp"));
    }

    [Fact]
    public void Import_NameCollisionWithDifferentContent_GetsAUniqueInternalName_AndNeverOverwrites()
    {
        var env = new Env();
        var existing = TestWavFactory.Write(env.Managed, "Chime.wav", TestWavFactory.Pcm(seconds: 0.1));
        var existingHash = Hash(existing);
        var source = TestWavFactory.Write(Path.Combine(env.Root, "downloads"), "Chime.wav", TestWavFactory.Pcm(seconds: 0.4));

        var result = env.Service.Import(source);

        Assert.Equal(SoundImportStatus.Copied, result.Status);
        Assert.NotEqual("Chime.wav", result.InternalFileName);
        Assert.StartsWith("Chime-", result.InternalFileName);
        Assert.Equal("Chime", result.DisplayName);
        Assert.Equal(existingHash, Hash(existing));
        Assert.Equal(Hash(source), Hash(Path.Combine(env.Managed, result.InternalFileName!)));
    }

    [Fact]
    public void Import_SameContentAlreadyPresent_ReusesTheExistingFile()
    {
        var env = new Env();
        var bytes = TestWavFactory.Pcm(seconds: 0.25);
        TestWavFactory.Write(env.Managed, "Original.wav", bytes);
        var source = TestWavFactory.Write(Path.Combine(env.Root, "downloads"), "Copy.wav", bytes);

        var result = env.Service.Import(source);

        Assert.Equal(SoundImportStatus.AlreadyPresent, result.Status);
        Assert.Equal("sounds/Original.wav", result.RuleReference);
        Assert.Single(Directory.GetFiles(env.Managed, "*.wav"));
    }

    [Fact]
    public void Import_RejectsMissingNonWavAndInvalidFiles_WithoutCopying()
    {
        var env = new Env();
        var downloads = Path.Combine(env.Root, "downloads");
        var text = Path.Combine(downloads, "readme.txt");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(text, "x");
        var broken = TestWavFactory.Write(downloads, "broken.wav", TestWavFactory.BadHeader());

        Assert.Equal(SoundImportStatus.SourceMissing, env.Service.Import(Path.Combine(downloads, "nope.wav")).Status);
        Assert.Equal(SoundImportStatus.NotWav, env.Service.Import(text).Status);
        Assert.Equal(SoundImportStatus.Unsupported, env.Service.Import(broken).Status);
        Assert.Empty(Directory.GetFiles(env.Managed));
    }

    // ---- remove vs delete ----

    [Fact]
    public void RemoveFromRules_ChangesOnlySettings_NotTheFile()
    {
        var env = new Env();
        var file = TestWavFactory.Write(env.Managed, "a.wav", TestWavFactory.Pcm());
        env.Settings.DefaultSoundRule = new SoundRule { Files = { "sounds/a.wav", "sounds/b.wav" } };
        env.Settings.AppSoundRules["Slack"] = new SoundRule { Files = { "sounds/a.wav" } };

        var updated = SoundLibraryService.RemoveFromRules(env.Settings, "sounds/a.wav");

        Assert.Equal(new[] { "sounds/b.wav" }, updated.DefaultSoundRule.Files);
        Assert.Empty(updated.AppSoundRules["Slack"].Files);
        Assert.True(File.Exists(file));
        Assert.Equal(2, env.Settings.DefaultSoundRule.Files.Count); // original settings object untouched
    }

    [Fact]
    public void DeleteManagedFile_RequiresTheExplicitFlag_AndNeverDeletesExternalFiles()
    {
        var env = new Env();
        var managed = TestWavFactory.Write(env.Managed, "a.wav", TestWavFactory.Pcm());
        var external = TestWavFactory.Write(env.External, "e.wav", TestWavFactory.Pcm());
        env.Settings.SoundLibraryFolders.Add(env.External);

        Assert.Equal(SoundDeleteResult.NotConfirmed, env.Service.DeleteManagedFile("sounds/a.wav", deletePhysicalFile: false));
        Assert.True(File.Exists(managed));
        Assert.Equal(SoundDeleteResult.NotManagedFile, env.Service.DeleteManagedFile(external, deletePhysicalFile: true));
        Assert.True(File.Exists(external));
        Assert.Equal(SoundDeleteResult.Deleted, env.Service.DeleteManagedFile("sounds/a.wav", deletePhysicalFile: true));
        Assert.False(File.Exists(managed));
        Assert.Equal(SoundDeleteResult.NotFound, env.Service.DeleteManagedFile("sounds/a.wav", deletePhysicalFile: true));
    }

    // ---- external folder registration ----

    [Fact]
    public void ValidateFolderForRegistration_AcceptsANormalizedLocalFolder()
    {
        var env = new Env();

        var result = env.Service.ValidateFolderForRegistration(env.External + Path.DirectorySeparatorChar);

        Assert.True(result.Accepted);
        Assert.Equal(env.External, result.NormalizedPath);
    }

    [Theory]
    [InlineData(@"\\server\share\sounds", SoundFolderRejection.NetworkPath)]
    [InlineData(@"\\?\C:\Windows\Media", SoundFolderRejection.NetworkPath)]
    [InlineData(@"relative\sounds", SoundFolderRejection.NotFullyQualified)]
    [InlineData("", SoundFolderRejection.Empty)]
    public void ValidateFolderForRegistration_RejectsNonLocalOrRelativePaths(string path, SoundFolderRejection expected)
    {
        var env = new Env();

        var result = env.Service.ValidateFolderForRegistration(path);

        Assert.False(result.Accepted);
        Assert.Equal(expected, result.Rejection);
    }

    [Fact]
    public void ValidateFolderForRegistration_RejectsMissingAndManagedFolders()
    {
        var env = new Env();

        Assert.Equal(SoundFolderRejection.NotFound, env.Service.ValidateFolderForRegistration(Path.Combine(env.Root, "nope")).Rejection);
        Assert.Equal(SoundFolderRejection.ManagedFolder, env.Service.ValidateFolderForRegistration(env.Managed).Rejection);
    }

    [Fact]
    public void Junctions_AreRejected_AtRegistrationAndAtRuntime()
    {
        var env = new Env();
        var target = Path.Combine(env.Root, "real-target");
        Directory.CreateDirectory(target);
        var file = TestWavFactory.Write(target, "t.wav", TestWavFactory.Pcm());
        var junction = Path.Combine(env.Root, "junction");
        if (!TryCreateJunction(junction, target))
            return; // junctions unavailable on this file system

        try
        {
            Assert.Equal(SoundFolderRejection.LinkOrJunction, env.Service.ValidateFolderForRegistration(junction).Rejection);

            // Even if a junction slipped into settings (e.g. edited by hand), runtime refuses its files.
            env.Settings.SoundLibraryFolders.Add(junction);
            Assert.Null(env.Service.ResolvePlayablePath(Path.Combine(junction, "t.wav")));
            Assert.Contains(env.Service.Refresh().Folders, f => f.FolderPath == junction && f.Status == SoundFileStatus.NotAllowed);
            Assert.True(File.Exists(file));
        }
        finally
        {
            // Remove only the link itself so the session temp cleanup can delete the folder tree.
            try { Directory.Delete(junction, recursive: false); } catch { }
        }
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(link) && new DirectoryInfo(link).LinkTarget != null;
        }
        catch
        {
            return false;
        }
    }
}
