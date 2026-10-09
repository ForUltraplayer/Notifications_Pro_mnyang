// @file CliForwardingTests.cs
// @author: rudals252
// @brief P4 명령줄 인자(K12): 기존 인자 의미 유지 해석, 두 번째 실행 전달 여부(빈 인자·모르는 인자 → 설정창), 시작 시 설정창 표시 판정, 메시지 직렬화·검증, 실제 이름 있는 파이프로 한 번만 전달되는지 검증한다.

using System.Collections.Concurrent;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class CliForwardingTests
{
    private static string UniquePipeName() => "NotificationsProKR-Mnyang.Test." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Parse_KeepsOriginalMeaning_CaseInsensitive_ThemeTakesNextValue_UnknownIgnored()
    {
        var commands = CliArguments.Parse(new[] { "--PAUSE", "foo", "--theme", "Midnight Blue", "--send-test", "--hide", "--show", "--resume" });

        Assert.Equal(
            new[] { CliCommandKind.Pause, CliCommandKind.Theme, CliCommandKind.SendTest, CliCommandKind.Hide, CliCommandKind.Show, CliCommandKind.Resume },
            commands.Select(c => c.Kind));
        Assert.Equal("Midnight Blue", commands[1].Argument);
    }

    [Fact]
    public void Parse_ThemeWithoutValue_IsIgnored_AndNewCommandsAreRecognised()
    {
        Assert.Empty(CliArguments.Parse(new[] { "--theme" }));
        Assert.Equal(new[] { CliCommandKind.StartMinimized, CliCommandKind.Quit },
            CliArguments.Parse(new[] { ForkIdentity.StartMinimizedArgument, "--quit" }).Select(c => c.Kind));
        Assert.Empty(CliArguments.Parse(null));
    }

    [Fact]
    public void ShouldForward_OnlyWithKnownCommands()
    {
        Assert.False(CliArguments.ShouldForward(Array.Empty<string>()));
        Assert.False(CliArguments.ShouldForward(new[] { "C:\\some\\file.txt", "--unknown" }));
        Assert.True(CliArguments.ShouldForward(new[] { "--pause" }));
        Assert.True(CliArguments.ShouldForward(new[] { ForkIdentity.StartMinimizedArgument }));
        Assert.True(CliArguments.Contains(new[] { "--x", "--Start-Minimized" }, CliCommandKind.StartMinimized));
    }

    [Theory]
    [InlineData(false, false, false, true)]  // first run
    [InlineData(false, true, true, true)]    // first run wins over tray start
    [InlineData(true, false, false, true)]   // normal start, setting off
    [InlineData(true, true, false, false)]   // setting on
    [InlineData(true, false, true, false)]   // --start-minimized (logon task)
    public void ShouldShowSettingsAtStartup_FollowsDesign(bool hadSettings, bool setting, bool argument, bool expected)
    {
        Assert.Equal(expected, CliArguments.ShouldShowSettingsAtStartup(hadSettings, setting, argument));
    }

    [Fact]
    public void Message_RoundTripsArgumentsExactly()
    {
        var args = new[] { "--theme", "한글 테마 \"quoted\"", "--pause", "" };
        var frame = CliArgumentMessage.SerializeFrame(args);

        Assert.True(CliArgumentMessage.TryReadLength(frame.AsSpan(0, 4), out var length));
        Assert.Equal(frame.Length - 4, length);
        Assert.True(CliArgumentMessage.TryDeserializePayload(frame.AsSpan(4), out var decoded));
        Assert.Equal(args, decoded);
    }

    [Fact]
    public void Message_RejectsInvalidInput()
    {
        Assert.False(CliArgumentMessage.TryDeserializePayload("not json"u8, out _));
        Assert.False(CliArgumentMessage.TryDeserializePayload("{\"v\":2,\"args\":[]}"u8, out _));
        Assert.False(CliArgumentMessage.TryDeserializePayload("{\"v\":1}"u8, out _));
        Assert.False(CliArgumentMessage.TryDeserializePayload(ReadOnlySpan<byte>.Empty, out _));
        Assert.False(CliArgumentMessage.TryReadLength(BitConverter.GetBytes(0), out _));
        Assert.False(CliArgumentMessage.TryReadLength(BitConverter.GetBytes(CliArgumentMessage.MaxPayloadBytes + 1), out _));
        Assert.Throws<ArgumentException>(() => CliArgumentMessage.SerializeFrame(Enumerable.Repeat("--pause", CliArgumentMessage.MaxArguments + 1).ToArray()));
    }

    [Fact]
    public void Pipe_DeliversEachCommandLineExactlyOnce()
    {
        var pipeName = UniquePipeName();
        var received = new ConcurrentQueue<IReadOnlyList<string>>();
        using var server = new CliArgumentServer(pipeName, args => received.Enqueue(args));
        server.Start();

        Assert.True(CliArgumentClient.TrySend(pipeName, new[] { "--pause" }));
        Assert.True(CliArgumentClient.TrySend(pipeName, new[] { "--theme", "Dark" }));

        Assert.Equal(2, received.Count);
        Assert.Equal(new[] { "--pause" }, received.ElementAt(0));
        Assert.Equal(new[] { "--theme", "Dark" }, received.ElementAt(1));
    }

    [Fact]
    public void Pipe_WithoutPrimary_FailsWithoutThrowing()
    {
        Assert.False(CliArgumentClient.TrySend(UniquePipeName(), new[] { "--pause" }, connectTimeoutMs: 200));
    }

    [Fact]
    public void Pipe_RejectsGarbageAndKeepsListening()
    {
        var pipeName = UniquePipeName();
        var received = new ConcurrentQueue<IReadOnlyList<string>>();
        using var server = new CliArgumentServer(pipeName, args => received.Enqueue(args));
        server.Start();

        using (var raw = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            raw.Connect(5000);
            var garbage = new byte[] { 5, 0, 0, 0, (byte)'x', (byte)'y', (byte)'z', (byte)'!', (byte)'?' };
            raw.Write(garbage);
            raw.Flush();
            Assert.Equal(CliArgumentServer.Rejected, raw.ReadByte());
        }

        Assert.True(CliArgumentClient.TrySend(pipeName, new[] { "--resume" }));
        Assert.Equal(new[] { "--resume" }, Assert.Single(received));
    }

    [Fact]
    public void PipeName_IsPerSession()
    {
        Assert.Equal("NotificationsProKR-Mnyang.Cli.1", ForkIdentity.GetCliPipeName(1));
        Assert.NotEqual(ForkIdentity.GetCliPipeName(1), ForkIdentity.GetCliPipeName(2));
    }
}
