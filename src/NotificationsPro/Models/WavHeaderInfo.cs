// @file WavHeaderInfo.cs
// @author: rudals252
// @brief WAV 헤더 검사 결과(상태, 형식 태그, 채널, 샘플레이트, 비트, 데이터 크기, 읽을 수 있을 때의 길이).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>Header facts read from a WAV file. <see cref="Duration"/> is null when it cannot be computed.</summary>
public sealed record WavHeaderInfo(
    WavHeaderStatus Status,
    ushort FormatTag = 0,
    ushort Channels = 0,
    int SampleRate = 0,
    ushort BitsPerSample = 0,
    long DataBytes = 0,
    TimeSpan? Duration = null)
{
    public bool IsValid => Status == WavHeaderStatus.Valid;

    public string FormatDescription
    {
        get
        {
            var codec = FormatTag switch
            {
                0x0001 => "PCM",
                0x0002 => "MS ADPCM",
                0x0003 => "IEEE float",
                0xFFFE => "Extensible",
                _ => $"0x{FormatTag:X4}",
            };
            return Status == WavHeaderStatus.Valid || Status == WavHeaderStatus.UnsupportedFormat
                ? $"{codec} {BitsPerSample}-bit {SampleRate} Hz {Channels}ch"
                : Status.ToString();
        }
    }
}
