// @file KakaoPopupClassifierTests.cs
// @author: rudals252
// @brief [실험실] 카카오톡 팝업 판별 규칙을 2026-10-10 실측 창 정보(팝업·메인 창·그림자 창·툴팁·최소화 창)로 검증하고, 시작 메뉴 AUMID 유도, 카드 없이 소리만 내는 경로(일시 정지·음소거 적용), 설정 기본값(끔)을 확인한다.

using NotificationsPro.Helpers;
using NotificationsPro.Models;
using NotificationsPro.Services;

namespace NotificationsPro.Tests;

public class KakaoPopupClassifierTests
{
    // Work area of the measured monitor: 0,0 - 3440,1392.
    private static KakaoWindowFacts Facts(
        string cls = "EVA_Window_Dblclk", bool owner = false, bool visible = true,
        uint style = 0x94000000, uint ex = 0x00080088,
        int l = 3128, int t = 1257, int w = 310, int h = 130, string process = "KakaoTalk")
        => new(process, cls, owner, visible, unchecked((int)style), unchecked((int)ex), l, t, w, h, 0, 0, 3440, 1392);

    [Fact]
    public void MeasuredMessagePopup_IsDetected()
    {
        Assert.True(KakaoPopupClassifier.IsMessagePopup(Facts()));
        Assert.True(KakaoPopupClassifier.IsMessagePopup(Facts(t: 1298, h: 89))); // first frame before growing
    }

    [Theory]
    [InlineData("EVA_Window_Dblclk", false, true, 0xB60F0000u, 0x00040100u, -32000, -32000, 160, 28, "main window (framed, minimized)")]
    [InlineData("KakaoTalkShadowWndClass", true, true, 0x9C880000u, 0x000800A8u, 3124, 1254, 318, 138, "shadow window")]
    [InlineData("tooltips_class32", true, false, 0x84000001u, 0x00080088u, 0, 0, 0, 0, "tooltip")]
    [InlineData("EVA_Window", false, false, 0x84000000u, 0x000800A8u, 0, 0, 0, 0, "hidden helper window")]
    public void OtherKakaoWindows_AreRejected(string cls, bool owner, bool visible, uint style, uint ex, int l, int t, int w, int h, string label)
    {
        var reason = KakaoPopupClassifier.GetRejectReason(Facts(cls, owner, visible, style, ex, l, t, w, h));
        Assert.False(reason == null, label);
    }

    [Fact]
    public void SameWindowFromAnotherProcess_IsRejected()
    {
        Assert.Equal("not KakaoTalk", KakaoPopupClassifier.GetRejectReason(Facts(process: "explorer")));
    }

    [Fact]
    public void ChatWindowSizedPopup_IsRejectedBySize()
    {
        Assert.NotNull(KakaoPopupClassifier.GetRejectReason(Facts(w: 390, h: 700)));
    }

    [Fact]
    public void AppUserModelId_IsDerivedFromTheInstallFolder()
    {
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var aumid = KakaoTalkPopupWatcher.DeriveAppUserModelId(Path.Combine(x86, "Kakao", "KakaoTalk", "KakaoTalk.exe"));

        Assert.Equal(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Kakao\KakaoTalk\KakaoTalk.exe", aumid);
        Assert.True(AppLauncher.CanLaunch(aumid));
        Assert.Null(KakaoTalkPopupWatcher.DeriveAppUserModelId(@"D:\Portable\KakaoTalk.exe"));
        Assert.Null(KakaoTalkPopupWatcher.DeriveAppUserModelId(null));
    }

    [Fact]
    public void SoundOnlyNotification_AddsNoCard_ButHonoursPauseAndMute()
    {
        var manager = TestAppDataIsolation.CreateSettingsManager();
        var queue = new QueueManager(manager);

        Assert.True(queue.AcceptSoundOnlyNotification("카카오톡", null));
        Assert.Empty(queue.VisibleNotifications);
        Assert.Contains("카카오톡", queue.SeenAppNames);

        var muted = manager.Settings.Clone();
        muted.MutedApps.Add("카카오톡");
        manager.Apply(muted);
        Assert.False(queue.AcceptSoundOnlyNotification("카카오톡", null));

        var paused = manager.Settings.Clone();
        paused.MutedApps.Clear();
        paused.NotificationsPaused = true;
        manager.Apply(paused);
        Assert.False(queue.AcceptSoundOnlyNotification("카카오톡", null));
    }

    [Fact]
    public void LabsSetting_IsOffByDefault_AndSurvivesSnapshotCopies()
    {
        Assert.False(new AppSettings().LabsKakaoPopupDetection);
        Assert.False(new AppSettings().LabsKakaoShowCard);

        var settings = new AppSettings { LabsKakaoPopupDetection = true, LabsKakaoShowCard = true };
        var copy = new AppSettings();
        settings.CopyForkExtensionsTo(copy);
        Assert.True(copy.LabsKakaoPopupDetection);
        Assert.True(copy.LabsKakaoShowCard);
        Assert.True(settings.Clone().LabsKakaoPopupDetection);
        Assert.True(settings.Clone().LabsKakaoShowCard);
    }
}
