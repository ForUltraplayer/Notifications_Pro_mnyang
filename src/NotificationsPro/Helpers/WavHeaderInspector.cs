// @file WavHeaderInspector.cs
// @author: rudals252
// @brief WAV 파일의 RIFF/WAVE 구조와 fmt·data 청크를 읽어 형식을 판정한다. 기존 System.Media.SoundPlayer가 받아들이던 형식(PCM·MS ADPCM·IEEE float)을 거부하지 않도록 맞췄다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Structural WAV check used before a file is offered for playback. It is deliberately no stricter
/// than the upstream player: <c>System.Media.SoundPlayer</c> validates RIFF/WAVE + a "fmt " chunk and
/// accepts format tags 1 (PCM), 2 (MS ADPCM) and 3 (IEEE float); those stay accepted here. In addition
/// WAVE_FORMAT_EXTENSIBLE with a PCM or IEEE-float sub-format is accepted because the playback engine
/// (Media Foundation) plays it. Other codecs are reported as unsupported (PCM first, no conversion).
/// A file with no audio bytes is reported as having no data (SoundPlayer could not play it either).
/// Only the header area is read; the RIFF size field is not enforced because many tools write it wrong.
/// </summary>
public static class WavHeaderInspector
{
    public const ushort FormatPcm = 0x0001;
    public const ushort FormatMsAdpcm = 0x0002;
    public const ushort FormatIeeeFloat = 0x0003;
    public const ushort FormatExtensible = 0xFFFE;

    private static readonly Guid SubFormatPcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubFormatIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private const int MaxChunksToScan = 256;

    public static WavHeaderInfo Inspect(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
                return new WavHeaderInfo(WavHeaderStatus.Missing);

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Inspect(stream);
        }
        catch (FileNotFoundException)
        {
            return new WavHeaderInfo(WavHeaderStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new WavHeaderInfo(WavHeaderStatus.Missing);
        }
        catch
        {
            return new WavHeaderInfo(WavHeaderStatus.ReadError);
        }
    }

    public static WavHeaderInfo Inspect(Stream stream)
    {
        var length = stream.Length;
        if (length == 0)
            return new WavHeaderInfo(WavHeaderStatus.Empty);

        var header = new byte[12];
        if (length < 12 || ReadFully(stream, header, 12) != 12)
            return new WavHeaderInfo(WavHeaderStatus.NotRiffWave);

        if (!IsFourCc(header, 0, "RIFF") || !IsFourCc(header, 8, "WAVE"))
            return new WavHeaderInfo(WavHeaderStatus.NotRiffWave);

        byte[]? fmt = null;
        long dataBytes = -1;
        long position = 12;
        var chunkHeader = new byte[8];

        for (var scanned = 0; scanned < MaxChunksToScan && position + 8 <= length; scanned++)
        {
            stream.Position = position;
            if (ReadFully(stream, chunkHeader, 8) != 8)
                break;

            var size = BitConverter.ToUInt32(chunkHeader, 4);
            var bodyStart = position + 8;
            var available = Math.Max(0, length - bodyStart);

            if (IsFourCc(chunkHeader, 0, "fmt ") && fmt == null)
            {
                var toRead = (int)Math.Min(Math.Min(size, 64u), (ulong)available);
                fmt = new byte[toRead];
                if (ReadFully(stream, fmt, toRead) != toRead)
                    return new WavHeaderInfo(WavHeaderStatus.NoFormatChunk);
            }
            else if (IsFourCc(chunkHeader, 0, "data") && dataBytes < 0)
            {
                dataBytes = Math.Min(size, available);
            }

            if (fmt != null && dataBytes >= 0)
                break;

            // Chunks are word aligned (a pad byte follows odd-sized bodies).
            position = bodyStart + size + (size & 1);
        }

        if (fmt == null || fmt.Length < 16)
            return new WavHeaderInfo(WavHeaderStatus.NoFormatChunk);

        var tag = BitConverter.ToUInt16(fmt, 0);
        var channels = BitConverter.ToUInt16(fmt, 2);
        var sampleRate = BitConverter.ToInt32(fmt, 4);
        var avgBytesPerSecond = BitConverter.ToInt32(fmt, 8);
        var blockAlign = BitConverter.ToUInt16(fmt, 12);
        var bits = BitConverter.ToUInt16(fmt, 14);

        var supported = tag is FormatPcm or FormatMsAdpcm or FormatIeeeFloat
            || (tag == FormatExtensible && IsSupportedSubFormat(fmt));

        if (!supported)
            return new WavHeaderInfo(WavHeaderStatus.UnsupportedFormat, tag, channels, sampleRate, bits, Math.Max(0, dataBytes));

        if (dataBytes <= 0 || (blockAlign > 0 && dataBytes < blockAlign))
            return new WavHeaderInfo(WavHeaderStatus.NoAudioData, tag, channels, sampleRate, bits, Math.Max(0, dataBytes));

        TimeSpan? duration = null;
        if (tag != FormatMsAdpcm && blockAlign > 0 && sampleRate > 0)
            duration = TimeSpan.FromSeconds((double)(dataBytes / blockAlign) / sampleRate);
        else if (avgBytesPerSecond > 0)
            duration = TimeSpan.FromSeconds((double)dataBytes / avgBytesPerSecond);

        return new WavHeaderInfo(WavHeaderStatus.Valid, tag, channels, sampleRate, bits, dataBytes, duration);
    }

    private static bool IsSupportedSubFormat(byte[] fmt)
    {
        // WAVEFORMATEXTENSIBLE: cbSize(2) at 16, validBits(2), channelMask(4), SubFormat GUID(16) at 24.
        if (fmt.Length < 40)
            return false;

        var subFormat = new Guid(fmt.AsSpan(24, 16));
        return subFormat == SubFormatPcm || subFormat == SubFormatIeeeFloat;
    }

    private static bool IsFourCc(byte[] buffer, int offset, string fourCc)
    {
        return buffer[offset] == fourCc[0]
            && buffer[offset + 1] == fourCc[1]
            && buffer[offset + 2] == fourCc[2]
            && buffer[offset + 3] == fourCc[3];
    }

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read <= 0)
                break;
            total += read;
        }

        return total;
    }
}
