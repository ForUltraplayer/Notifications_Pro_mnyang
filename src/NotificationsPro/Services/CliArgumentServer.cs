// @file CliArgumentServer.cs
// @author: rudals252
// @brief 주 인스턴스가 여는 이름 있는 파이프(현재 사용자 전용, 세션별 이름) 서버. 두 번째 실행이 보낸 명령줄 인자 메시지 하나를 연결 하나당 한 번만 받아 콜백에 넘기고 수신 확인 1바이트를 돌려준다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.IO.Pipes;
using NotificationsPro.Helpers;

namespace NotificationsPro.Services;

/// <summary>
/// Receives command lines forwarded by second launches (K12). One connection carries one message and
/// is handed to the callback exactly once, then acknowledged with <see cref="Accepted"/>. The pipe is
/// restricted to the current user (<see cref="PipeOptions.CurrentUserOnly"/>) and its name carries the
/// session id, mirroring the per-session Local\ mutex.
/// </summary>
public sealed class CliArgumentServer : IDisposable
{
    public const byte Accepted = 1;
    public const byte Rejected = 0;

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private const int MaxConsecutiveCreateFailures = 5;

    private readonly string _pipeName;
    private readonly Action<IReadOnlyList<string>> _onArguments;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _disposed;

    /// <param name="onArguments">Called on a thread-pool thread once per received message.</param>
    public CliArgumentServer(string pipeName, Action<IReadOnlyList<string>> onArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _onArguments = onArguments ?? throw new ArgumentNullException(nameof(onArguments));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var createFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                createFailures = 0;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Name already taken (e.g. a leftover instance). Retry a few times, then stop listening.
                if (++createFailures >= MaxConsecutiveCreateFailures)
                    return;

                try { await Task.Delay(500, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await HandleConnectionAsync(server, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // A broken or slow client must not stop the listener.
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        var prefix = new byte[4];
        await server.ReadExactlyAsync(prefix, timeout.Token).ConfigureAwait(false);
        if (!CliArgumentMessage.TryReadLength(prefix, out var length))
        {
            await ReplyAsync(server, Rejected, timeout.Token).ConfigureAwait(false);
            return;
        }

        var payload = new byte[length];
        await server.ReadExactlyAsync(payload, timeout.Token).ConfigureAwait(false);
        if (!CliArgumentMessage.TryDeserializePayload(payload, out var args))
        {
            await ReplyAsync(server, Rejected, timeout.Token).ConfigureAwait(false);
            return;
        }

        // Exactly once per message: the client never resends after its frame was written.
        _onArguments(args);
        await ReplyAsync(server, Accepted, timeout.Token).ConfigureAwait(false);
    }

    private static async Task ReplyAsync(Stream stream, byte value, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { value }, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown path: ignore listener errors.
        }

        _cts.Dispose();
    }
}
