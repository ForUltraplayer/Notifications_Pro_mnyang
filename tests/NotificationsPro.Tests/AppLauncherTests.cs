// @file AppLauncherTests.cs
// @author: rudals252
// @brief 카드에서 앱 열기: AUMID 검증(Unknown·경로·URL·따옴표 거부), shell:AppsFolder 대상 생성, 설정 기본값(끔)과 저장 유지, 실제 실행 없이 실행 요청 확인.

using System.Reflection;
using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

[Collection(nameof(AppLauncherTests))] // the launch test hook is static
public class AppLauncherTests
{
    [Theory]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude")]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App")]
    [InlineData("com.squirrel.Discord.Discord")]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Kakao\KakaoTalk\KakaoTalk.exe")]
    public void RealAumids_AreLaunchable(string aumid)
    {
        Assert.Equal(@"shell:AppsFolder\" + aumid, AppLauncher.BuildShellTarget(aumid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"\server\share\x")]
    [InlineData("https://example.com")]
    [InlineData("App\" /evil")]
    [InlineData(@"..\..\x")]
    [InlineData("a\nb")]
    public void InvalidIds_AreRejected(string? aumid)
    {
        Assert.Null(AppLauncher.BuildShellTarget(aumid));
        Assert.False(AppLauncher.TryLaunch(aumid));
    }

    [Fact]
    public void TryLaunch_PassesTheShellTarget()
    {
        string? launched = null;
        AppLauncher.LaunchOverrideForTesting = target => { launched = target; return true; };
        try
        {
            Assert.True(AppLauncher.TryLaunch("Claude_pzs8sxrjxfjjc!Claude"));
            Assert.Equal(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude", launched);
        }
        finally
        {
            AppLauncher.LaunchOverrideForTesting = null;
        }
    }

    [Fact]
    public void Settings_DefaultOff_AndSurviveAGeneralSettingsSave()
    {
        Assert.False(new AppSettings().OpenAppOnCardDoubleClick);
        Assert.True(new AppSettings().DismissCardAfterOpeningApp);

        StaThreadTestHelper.Run(() =>
        {
            var manager = TestAppDataIsolation.CreateSettingsManager();
            var vm = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(TestAppDataIsolation.CreateUniqueDirectory("profiles")));
            vm.OpenAppOnCardDoubleClick = true;
            vm.DismissCardAfterOpeningApp = false;

            vm.FontSize = 23;
            typeof(SettingsViewModel).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);

            Assert.True(manager.Settings.OpenAppOnCardDoubleClick);
            Assert.False(manager.Settings.DismissCardAfterOpeningApp);

            var reloaded = new SettingsManager(manager.SettingsDirectory);
            reloaded.Load();
            Assert.True(reloaded.Settings.OpenAppOnCardDoubleClick);
            Assert.False(reloaded.Settings.DismissCardAfterOpeningApp);
        });
    }
}
