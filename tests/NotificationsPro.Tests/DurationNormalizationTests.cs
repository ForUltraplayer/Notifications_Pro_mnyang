// @file DurationNormalizationTests.cs
// @author: rudals252
// @brief 잘못된 표시 시간·자동 시간·애니메이션 시간 값이 로드 시 기본값/상한으로 보정되고, 메모리 값이 잘못돼도 알림 대기열 추가가 예외 없이 동작하는지 검증한다.

using System.IO;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class DurationNormalizationTests
{
    [Theory]
    [InlineData("-5", 5.0)]
    [InlineData("0", 5.0)]
    [InlineData("1e300", SettingsManager.MaxDurationSeconds)]
    [InlineData("30", 30.0)]
    public void Load_SanitizesNotificationDuration(string raw, double expected)
    {
        var dir = TestAppDataIsolation.CreateUniqueDirectory("duration");
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            "{ \"SettingsSchemaVersion\": 6, \"NotificationDuration\": " + raw + ", \"AnimationDurationMs\": -1, \"AutoDurationBaseSeconds\": -2 }");

        var manager = new SettingsManager(dir);
        manager.Load();

        Assert.Equal(expected, manager.Settings.NotificationDuration);
        Assert.Equal(new AppSettings().AnimationDurationMs, manager.Settings.AnimationDurationMs);
        Assert.Equal(new AppSettings().AutoDurationBaseSeconds, manager.Settings.AutoDurationBaseSeconds);
    }

    [Fact]
    public void Apply_SanitizesNaNAndInfinity()
    {
        var manager = TestAppDataIsolation.CreateSettingsManager();
        var updated = manager.Settings.Clone();
        updated.NotificationDuration = double.NaN;
        updated.AutoDurationSecondsPerLine = double.PositiveInfinity;

        manager.Apply(updated);

        Assert.Equal(new AppSettings().NotificationDuration, manager.Settings.NotificationDuration);
        Assert.Equal(new AppSettings().AutoDurationSecondsPerLine, manager.Settings.AutoDurationSecondsPerLine);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-3.0)]
    [InlineData(double.MaxValue)]
    public void AddNotification_WithInvalidInMemoryDuration_DoesNotThrow(double duration)
    {
        var manager = TestAppDataIsolation.CreateSettingsManager();
        manager.Settings.NotificationDuration = duration;
        manager.Settings.AnimationDurationMs = double.NaN;
        var queue = new QueueManager(manager);

        queue.AddNotification("App", "Title", "Body");

        Assert.Single(queue.VisibleNotifications);
    }
}
