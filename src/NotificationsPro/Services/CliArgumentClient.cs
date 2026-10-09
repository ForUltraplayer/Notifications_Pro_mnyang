// @file CliArgumentClient.cs
// @author: rudals252
// @brief 두 번째 실행이 주 인스턴스 파이프에 명령줄 인자를 한 번 보내고 수신 확인을 기다린다. 연결 전 실패만 재시도하고, 쓰기 후에는 다시 보내지 않아 명령이 두 번 실행되지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.IO.Pipes;
using NotificationsPro.Helpers;

namespace NotificationsPro.Services;

/// <summary>Sends one forwarded command line to the primary instance (K12).</summary>
public static class CliArgumentClient
{
    /// <summary>
    /// Connects (waiting up to <paramref name="connectTimeoutMs"/> for the primary to open its pipe),
    /// writes the frame once and waits for the acknowledgement. Returns true only when the primary accepted it.
    /// </summary>
    public static bool TrySend(string pipeName, IReadOnlyList<string> args, int connectTimeoutMs = 5000, int replyTimeoutMs = 5000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(args);

        byte[] frame;
        try
        {
            frame = CliArgumentMessage.SerializeFrame(args);
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(connectTimeoutMs);

            // From here on the message may have been delivered: never retry.
            using var timeout = new CancellationTokenSource(replyTimeoutMs);
            client.WriteAsync(frame, timeout.Token).AsTask().GetAwaiter().GetResult();
            client.FlushAsync(timeout.Token).GetAwaiter().GetResult();

            var reply = new byte[1];
            var read = client.ReadAsync(reply, timeout.Token).AsTask().GetAwaiter().GetResult();
            return read == 1 && reply[0] == CliArgumentServer.Accepted;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
