// @file CliArgumentMessage.cs
// @author: rudals252
// @brief 두 번째 실행 → 주 인스턴스 명령줄 인자 전달 메시지 직렬화(4바이트 길이 + UTF-8 JSON {"v":1,"args":[...]})와 크기·개수 제한 검증.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotificationsPro.Helpers;

/// <summary>
/// Wire format of one forwarded command line: a 4-byte little-endian payload length followed by
/// UTF-8 JSON <c>{"v":1,"args":["--pause", ...]}</c>. Only the raw argument strings travel; the primary
/// parses them with <see cref="CliArguments.Parse"/>, exactly like its own start-up arguments.
/// </summary>
public static class CliArgumentMessage
{
    public const int Version = 1;
    public const int MaxPayloadBytes = 16 * 1024;
    public const int MaxArguments = 64;

    public static byte[] SerializePayload(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count > MaxArguments)
            throw new ArgumentException("Too many arguments.", nameof(args));

        var payload = JsonSerializer.SerializeToUtf8Bytes(new WireMessage { V = Version, Args = args.Select(a => a ?? string.Empty).ToArray() });
        if (payload.Length > MaxPayloadBytes)
            throw new ArgumentException("Arguments are too long.", nameof(args));

        return payload;
    }

    /// <summary>Length prefix + payload, ready to write to the pipe.</summary>
    public static byte[] SerializeFrame(IReadOnlyList<string> args)
    {
        var payload = SerializePayload(args);
        var frame = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), payload.Length);
        if (!BitConverter.IsLittleEndian)
            frame.AsSpan(0, 4).Reverse();
        payload.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>Reads the payload length from a 4-byte prefix; false when outside 1..<see cref="MaxPayloadBytes"/>.</summary>
    public static bool TryReadLength(ReadOnlySpan<byte> prefix, out int length)
    {
        length = 0;
        if (prefix.Length != 4)
            return false;

        Span<byte> buffer = stackalloc byte[4];
        prefix.CopyTo(buffer);
        if (!BitConverter.IsLittleEndian)
            buffer.Reverse();
        length = BitConverter.ToInt32(buffer);
        return length > 0 && length <= MaxPayloadBytes;
    }

    public static bool TryDeserializePayload(ReadOnlySpan<byte> payload, out IReadOnlyList<string> args)
    {
        args = Array.Empty<string>();
        if (payload.Length == 0 || payload.Length > MaxPayloadBytes)
            return false;

        try
        {
            var message = JsonSerializer.Deserialize<WireMessage>(payload);
            if (message == null || message.V != Version || message.Args == null || message.Args.Length > MaxArguments)
                return false;

            args = message.Args.Select(a => a ?? string.Empty).ToArray();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed class WireMessage
    {
        [JsonPropertyName("v")]
        public int V { get; set; }

        [JsonPropertyName("args")]
        public string[]? Args { get; set; }
    }
}
