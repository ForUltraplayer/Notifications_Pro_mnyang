// @file WavHeaderInspectorTests.cs
// @author: rudals252
// @brief WAV 헤더 검사기 테스트: 합성 WAV 형식별 판정, 기존 System.Media.SoundPlayer 검증(리플렉션)과의 호환(기존에 받아들이던 WAV를 거부하지 않음), C:\Windows\Media WAV 전수 확인.

using System.IO;
using System.Media;
using System.Reflection;
using NotificationsPro.Helpers;
using NotificationsPro.Models;

namespace NotificationsPro.Tests;

public class WavHeaderInspectorTests
{
    private static WavHeaderInfo InspectBytes(byte[] bytes) => WavHeaderInspector.Inspect(new MemoryStream(bytes));

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    public void Pcm_IsValid_WithDuration(ushort bits)
    {
        var info = InspectBytes(TestWavFactory.Pcm(seconds: 0.5, bits: bits));

        Assert.Equal(WavHeaderStatus.Valid, info.Status);
        Assert.Equal(WavHeaderInspector.FormatPcm, info.FormatTag);
        Assert.NotNull(info.Duration);
        Assert.InRange(info.Duration!.Value.TotalMilliseconds, 495, 505);
    }

    [Fact]
    public void IeeeFloat_AndExtensiblePcm_AreValid()
    {
        Assert.True(InspectBytes(TestWavFactory.Float32()).IsValid);
        Assert.True(InspectBytes(TestWavFactory.ExtensiblePcm()).IsValid);
    }

    [Fact]
    public void MsAdpcmTag_IsAccepted_LikeSoundPlayer()
    {
        Assert.True(InspectBytes(TestWavFactory.FormatTag(WavHeaderInspector.FormatMsAdpcm)).IsValid);
    }

    [Theory]
    [InlineData((ushort)0x0011)] // IMA ADPCM
    [InlineData((ushort)0x0055)] // MP3
    [InlineData((ushort)0x0006)] // A-law
    public void OtherCodecs_AreUnsupported(ushort tag)
    {
        Assert.Equal(WavHeaderStatus.UnsupportedFormat, InspectBytes(TestWavFactory.FormatTag(tag)).Status);
    }

    [Fact]
    public void BrokenFiles_AreReportedPrecisely()
    {
        Assert.Equal(WavHeaderStatus.Empty, InspectBytes(Array.Empty<byte>()).Status);
        Assert.Equal(WavHeaderStatus.NotRiffWave, InspectBytes(TestWavFactory.BadHeader()).Status);
        Assert.Equal(WavHeaderStatus.NoAudioData, InspectBytes(TestWavFactory.HeaderOnly()).Status);
        Assert.Equal(WavHeaderStatus.Missing, WavHeaderInspector.Inspect(Path.Combine(TestAppDataIsolation.SessionRoot, "nope.wav")).Status);
    }

    [Fact]
    public void WrongRiffSize_IsTolerated()
    {
        var bytes = TestWavFactory.Pcm();
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 4);

        Assert.True(InspectBytes(bytes).IsValid);
    }

    // ---- compatibility with the upstream player ----

    private static MethodInfo? SoundPlayerValidate =>
        typeof(SoundPlayer).GetMethod("ValidateSoundFile", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool SoundPlayerAccepts(string path)
    {
        try
        {
            SoundPlayerValidate!.Invoke(new SoundPlayer(path), new object[] { path });
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }

    [Fact]
    public void EverySyntheticWavSoundPlayerAcceptsWithAudio_IsAlsoAccepted()
    {
        if (SoundPlayerValidate == null)
            return; // private API shape changed; covered by the Windows\Media test below

        var folder = TestAppDataIsolation.CreateUniqueDirectory("wav-compat");
        var samples = new Dictionary<string, byte[]>
        {
            ["pcm8.wav"] = TestWavFactory.Pcm(bits: 8),
            ["pcm16.wav"] = TestWavFactory.Pcm(),
            ["pcm24.wav"] = TestWavFactory.Pcm(bits: 24, channels: 2, sampleRate: 48000),
            ["float.wav"] = TestWavFactory.Float32(),
            ["adpcm.wav"] = TestWavFactory.FormatTag(2),
            ["ext.wav"] = TestWavFactory.ExtensiblePcm(),
            ["mp3.wav"] = TestWavFactory.FormatTag(0x55),
            ["bad.wav"] = TestWavFactory.BadHeader(),
            ["empty.wav"] = Array.Empty<byte>(),
        };

        foreach (var (name, bytes) in samples)
        {
            var path = TestWavFactory.Write(folder, name, bytes);
            if (SoundPlayerAccepts(path))
                Assert.True(WavHeaderInspector.Inspect(path).IsValid, $"{name} was accepted by SoundPlayer but rejected here");
        }
    }

    [Fact]
    public void AllWindowsMediaWavs_AreAccepted_WhereverSoundPlayerAcceptsThem()
    {
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        if (!Directory.Exists(media))
            return;

        var files = Directory.GetFiles(media, "*.wav");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var accepted = WavHeaderInspector.Inspect(file).IsValid;
            if (SoundPlayerValidate == null || SoundPlayerAccepts(file))
                Assert.True(accepted, $"{Path.GetFileName(file)} regressed");
        }
    }
}
