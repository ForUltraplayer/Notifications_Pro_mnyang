// @file SaveFeedbackTests.cs
// @author: rudals252
// @brief 설정 저장이 실제로 기록됐을 때만 '저장됨'을 표시하고, 읽기 전용(새 스키마)·실패 때는 경고를 표시하며 다음 성공 저장에서 지우는지 검증한다.

using System.IO;
using System.Reflection;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class SaveFeedbackTests
{
    private static void Save(SettingsViewModel viewModel)
        => typeof(SettingsViewModel).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewModel, null);

    [Fact]
    public void ReadOnlySettings_ShowWarning_NotSaved()
    {
        StaThreadTestHelper.Run(() =>
        {
            var dir = TestAppDataIsolation.CreateUniqueDirectory("savefeedback");
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"SettingsSchemaVersion\": 99 }");
            var manager = new SettingsManager(dir);
            manager.Load();
            Assert.True(manager.IsReadOnlyDueToNewerSchema);
            var vm = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(Path.Combine(dir, "profiles")));

            vm.FontSize = 21;
            Save(vm);

            Assert.False(vm.ShowSavedIndicator);
            Assert.True(vm.ShowSaveProblemIndicator);
            Assert.False(string.IsNullOrWhiteSpace(vm.SaveProblemTooltip));
        });
    }

    [Fact]
    public void SuccessfulSave_ShowsSaved_AndClearsAnEarlierWarning()
    {
        StaThreadTestHelper.Run(() =>
        {
            var manager = TestAppDataIsolation.CreateSettingsManager();
            var vm = new SettingsViewModel(manager, new QueueManager(manager), new ProfileManager(TestAppDataIsolation.CreateUniqueDirectory("profiles")));

            vm.ShowSaveResultFeedback(SettingsSaveStatus.Failed);
            Assert.True(vm.ShowSaveProblemIndicator);
            Assert.False(vm.ShowSavedIndicator);

            vm.FontSize = 22;
            Save(vm);

            Assert.Equal(SettingsSaveStatus.Saved, manager.LastSaveStatus);
            Assert.True(vm.ShowSavedIndicator);
            Assert.False(vm.ShowSaveProblemIndicator);
        });
    }
}
