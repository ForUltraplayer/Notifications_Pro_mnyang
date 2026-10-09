// @file RunMarkerTests.cs
// @author: rudals252
// @brief P4 실행 표식(R08): 정상 종료 후 표식 없음·오탐 없음, 강제 종료 시뮬레이션 후 다음 시작 안내, 보조 실행(단일 인스턴스 실패)은 표식 불변, 이전 세션 토큰이 현재 실행에 영향 없음, 손상 파일·쓰기 실패 처리, 표식 내용(토큰·시각·버전만)과 상태 줄 문구를 검증한다.

using System.IO;
using System.Text.Json;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class RunMarkerTests : IDisposable
{
    private readonly string _root = TestAppDataIsolation.CreateUniqueDirectory("run-marker");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string MarkerPath => Path.Combine(_root, ForkIdentity.RunMarkerFileName);

    [Fact]
    public void NormalExit_LeavesNoMarker_AndNextStartReportsNothing()
    {
        var first = new RunMarker(MarkerPath);
        var start = first.BeginSession("1.1.10.37");
        Assert.False(start.PreviousRunEndedUncleanly);
        Assert.False(start.WriteFailed);
        Assert.True(File.Exists(MarkerPath));

        Assert.True(first.EndSession());
        Assert.False(File.Exists(MarkerPath));
        Assert.False(first.EndSession()); // idempotent, nothing left to clear

        var next = new RunMarker(MarkerPath).BeginSession("1.1.10.37");
        Assert.False(next.PreviousRunEndedUncleanly);
    }

    [Fact]
    public void ForcedEnd_MarkerLeftBehind_IsReportedAtNextStart()
    {
        var startedAt = new DateTimeOffset(2026, 10, 9, 17, 30, 0, TimeSpan.FromHours(9));
        var crashed = new RunMarker(MarkerPath, () => startedAt);
        crashed.BeginSession("1.1.10.37");
        // Process killed: EndSession never runs.

        var next = new RunMarker(MarkerPath).BeginSession("1.1.10.38");

        Assert.True(next.PreviousRunEndedUncleanly);
        Assert.Equal(startedAt, next.PreviousStartedAt);
        Assert.Equal("1.1.10.37", next.PreviousVersion);
        Assert.False(next.WriteFailed);
    }

    [Fact]
    public void SecondaryLaunch_NeverTouchesTheMarker()
    {
        var names = Guid.NewGuid().ToString("N");
        using var primaryGuard = SingleInstanceGuard.TryAcquire($@"Local\NPKR.Test.{names}.M", $@"Local\NPKR.Test.{names}.E");
        Assert.NotNull(primaryGuard);
        var primary = new RunMarker(MarkerPath);
        RunMarker.BeginForPrimaryInstance(isPrimaryInstance: primaryGuard != null, primary, "1.0");
        var before = File.ReadAllBytes(MarkerPath);
        var beforeTime = File.GetLastWriteTimeUtc(MarkerPath);

        var secondaryGuard = SingleInstanceGuard.TryAcquire($@"Local\NPKR.Test.{names}.M", $@"Local\NPKR.Test.{names}.E");
        Assert.Null(secondaryGuard);
        var secondary = new RunMarker(MarkerPath);
        var result = RunMarker.BeginForPrimaryInstance(isPrimaryInstance: secondaryGuard != null, secondary, "1.0");

        Assert.Null(result);
        Assert.False(secondary.EndSession());
        Assert.Equal(before, File.ReadAllBytes(MarkerPath));
        Assert.Equal(beforeTime, File.GetLastWriteTimeUtc(MarkerPath));

        Assert.True(primary.EndSession());
    }

    [Fact]
    public void PreviousSessionToken_DoesNotAffectTheCurrentRun()
    {
        var old = new RunMarker(MarkerPath);
        old.BeginSession("1.0");
        var oldToken = old.CurrentToken;

        var current = new RunMarker(MarkerPath);
        Assert.True(current.BeginSession("1.1").PreviousRunEndedUncleanly);
        Assert.NotEqual(oldToken, current.CurrentToken);

        // A late clean-up from the old run must not clear the current run's marker.
        Assert.False(old.EndSession());
        Assert.True(File.Exists(MarkerPath));
        Assert.Equal(current.CurrentToken, ReadToken());

        Assert.True(current.EndSession());
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void EndSession_WithoutBegin_DoesNotDeleteAnything()
    {
        new RunMarker(MarkerPath).BeginSession("1.0");
        Assert.False(new RunMarker(MarkerPath).EndSession());
        Assert.True(File.Exists(MarkerPath));
    }

    [Fact]
    public void CorruptMarker_StillCountsAsUnclean_WithoutDetails()
    {
        File.WriteAllText(MarkerPath, "{ not json");
        var result = new RunMarker(MarkerPath).BeginSession("1.0");

        Assert.True(result.PreviousRunEndedUncleanly);
        Assert.Null(result.PreviousStartedAt);
        Assert.Null(result.PreviousVersion);
    }

    [Fact]
    public void WriteFailure_IsReported_AndEndSessionLeavesThingsAlone()
    {
        Directory.CreateDirectory(MarkerPath); // a folder where the file should go
        var marker = new RunMarker(MarkerPath);
        var result = marker.BeginSession("1.0");

        Assert.True(result.WriteFailed);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorDescription));
        Assert.Null(marker.CurrentToken);
        Assert.False(marker.EndSession());
        Assert.True(Directory.Exists(MarkerPath));
    }

    [Fact]
    public void MarkerFile_HoldsTokenStartTimeAndVersionOnly()
    {
        new RunMarker(MarkerPath).BeginSession("1.1.10.37");
        using var document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Format", "SessionToken", "StartedAt", "Version" }, names);
    }

    [Fact]
    public void MarkerPath_IsOutsideSettingsThemesAndProfiles()
    {
        Assert.Equal(Path.Combine(ForkIdentity.AppDataRoot, "run-state.json"), ForkIdentity.RunMarkerPath);
        Assert.NotEqual("settings.json", ForkIdentity.RunMarkerFileName);
    }

    [Fact]
    public void StatusText_DescribesObservationOnly()
    {
        Assert.Equal(string.Empty, SettingsViewModel.FormatPreviousRunStatus(null));
        Assert.Equal("No unfinished previous run was recorded.",
            SettingsViewModel.FormatPreviousRunStatus(new RunMarkerStartResult(false, null, null, false, null)));

        var unclean = SettingsViewModel.FormatPreviousRunStatus(new RunMarkerStartResult(true, null, null, false, null));
        Assert.StartsWith("The previous run did not end normally.", unclean);
        Assert.Contains("cannot be determined", unclean);

        var detailed = SettingsViewModel.FormatPreviousRunStatus(
            new RunMarkerStartResult(true, new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero), "1.1.10.37", false, null));
        Assert.Contains("version 1.1.10.37", detailed);
        Assert.Contains("did not end normally", detailed);

        var failed = SettingsViewModel.FormatPreviousRunStatus(new RunMarkerStartResult(false, null, null, true, "IOException 0x80070005"));
        Assert.Contains("IOException 0x80070005", failed);
    }

    private string? ReadToken()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
        return document.RootElement.GetProperty("SessionToken").GetString();
    }
}
