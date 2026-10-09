// @file TestWavFactory.cs
// @author: rudals252
// @brief 테스트용 합성 WAV 바이트 생성기(무음 PCM·IEEE float·Extensible, 미지원 코덱 태그, 잘못된 헤더, 빈 파일, data 없음). 사용자 WAV를 읽거나 복사하지 않는다.

using System.IO;
using System.Text;

namespace NotificationsPro.Tests;

internal static class TestWavFactory
{
    public static byte[] Pcm(double seconds = 0.2, int sampleRate = 22050, ushort channels = 1, ushort bits = 16, byte fill = 0)
        => Build(1, channels, sampleRate, bits, seconds, fill);

    public static byte[] Float32(double seconds = 0.2) => Build(3, 2, 48000, 32, seconds);

    public static byte[] FormatTag(ushort tag, double seconds = 0.2) => Build(tag, 2, 44100, 16, seconds);

    public static byte[] ExtensiblePcm(double seconds = 0.2)
    {
        const ushort channels = 2;
        const int rate = 48000;
        const ushort bits = 16;
        var blockAlign = (ushort)(channels * bits / 8);
        var dataLength = (int)(rate * seconds) * blockAlign;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(60 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(40);
        writer.Write((ushort)0xFFFE);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * blockAlign);
        writer.Write(blockAlign);
        writer.Write(bits);
        writer.Write((ushort)22);
        writer.Write(bits);
        writer.Write(3u);
        writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }

    /// <summary>Valid RIFF/WAVE + fmt but no data chunk at all.</summary>
    public static byte[] HeaderOnly()
    {
        var full = Pcm(0.1);
        return full.Take(36).ToArray();
    }

    public static byte[] BadHeader() => Encoding.ASCII.GetBytes("NOTARIFFFILE-not-a-wave-file-at-all");

    public static string Write(string folder, string fileName, byte[] content)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Build(ushort tag, ushort channels, int rate, ushort bits, double seconds, byte fill = 0)
    {
        var blockAlign = (ushort)(channels * bits / 8);
        var dataLength = (int)(rate * seconds) * blockAlign;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write(tag);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * blockAlign);
        writer.Write(blockAlign);
        writer.Write(bits);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        var data = new byte[dataLength];
        if (bits == 8 && fill == 0)
            Array.Fill(data, (byte)0x80);
        else if (fill != 0)
            Array.Fill(data, fill);
        writer.Write(data);
        return stream.ToArray();
    }
}
