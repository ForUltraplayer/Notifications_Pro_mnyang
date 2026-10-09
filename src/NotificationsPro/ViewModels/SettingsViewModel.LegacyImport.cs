// @file SettingsViewModel.LegacyImport.cs
// @author: rudals252
// @brief 프로필 탭 Import/Export 옆의 최소 진입점: 원본 Notifications Pro 설정 후보를 탐지해 사용자가 고르게 하고, LegacySettingsImporter로 가져온 뒤 화면을 다시 읽는다. 문구는 P3에서 리소스화한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Windows;
using System.Windows.Input;
using NotificationsPro.Services;
using NotificationsPro.Localization;

namespace NotificationsPro.ViewModels;

public partial class SettingsViewModel
{
    private ICommand? _importLegacySettingsCommand;
    public ICommand ImportLegacySettingsCommand => _importLegacySettingsCommand ??= new RelayCommand(_ => ImportLegacySettings());

    private void ImportLegacySettings()
    {
        var importer = new LegacySettingsImporter(_settingsManager, LegacySettingsImporter.GetDefaultSources());
        var found = importer.DetectCandidates().Where(candidate => candidate.Exists).ToList();
        if (found.Count == 0)
        {
            System.Windows.MessageBox.Show(
                LocalizationManager.Get("LegacyImport_NotFound"),
                LocalizationManager.Get("Profiles_ImportFromOriginalApp"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var chosen = ChooseLegacyCandidate(found);
        if (chosen == null)
            return;

        if (!chosen.CanImport)
        {
            System.Windows.MessageBox.Show(
                LocalizationManager.Format("LegacyImport_CannotImport", chosen.BlockReason),
                LocalizationManager.Get("Profiles_ImportFromOriginalApp"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _saveDebounce.Stop();
        LegacySettingsImportResult result;
        _setSettingsWindowBulkApplyStateAction?.Invoke(true);
        try
        {
            result = importer.Import(chosen);
            ReloadFromCurrentSettings();
            SyncResolvedSettingsThemeState();
        }
        finally
        {
            _setSettingsWindowBulkApplyStateAction?.Invoke(false);
        }

        _refreshSettingsWindowShellAction?.Invoke();
        RefreshProfiles();
        RefreshCustomThemes();

        var lines = new List<string>
        {
            result.Success ? LocalizationManager.Get("LegacyImport_Finished") : LocalizationManager.Format("LegacyImport_Incomplete", result.Error),
            LocalizationManager.Format("LegacyImport_AssetCounts", result.AssetsCopied, result.AssetsSkippedIdentical, result.AssetsBackedUp),
        };
        if (!string.IsNullOrWhiteSpace(result.SettingsBackupPath))
            lines.Add(LocalizationManager.Format("LegacyImport_SettingsBackup", result.SettingsBackupPath));
        if (!string.IsNullOrWhiteSpace(result.AssetBackupFolder))
            lines.Add(LocalizationManager.Format("LegacyImport_AssetBackups", result.AssetBackupFolder));

        System.Windows.MessageBox.Show(
            string.Join(Environment.NewLine, lines),
            LocalizationManager.Get("Profiles_ImportFromOriginalApp"),
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static LegacySettingsCandidate? ChooseLegacyCandidate(IReadOnlyList<LegacySettingsCandidate> found)
    {
        var backupNote = LocalizationManager.Get("LegacyImport_BackupNote");
        if (found.Count == 1)
        {
            var only = found[0];
            var answer = System.Windows.MessageBox.Show(
                LocalizationManager.Format("LegacyImport_ConfirmOne", DescribeLegacyCandidate(only), backupNote).Replace("\n", Environment.NewLine),
                LocalizationManager.Get("Profiles_ImportFromOriginalApp"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return answer == MessageBoxResult.Yes ? only : null;
        }

        var first = found[0];
        var second = found[1];
        var choice = System.Windows.MessageBox.Show(
            LocalizationManager.Format("LegacyImport_ConfirmTwo", DescribeLegacyCandidate(first), DescribeLegacyCandidate(second), backupNote).Replace("\n", Environment.NewLine),
            LocalizationManager.Get("Profiles_ImportFromOriginalApp"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return choice switch
        {
            MessageBoxResult.Yes => first,
            MessageBoxResult.No => second,
            _ => null,
        };
    }

    private static string DescribeLegacyCandidate(LegacySettingsCandidate candidate)
    {
        var kind = candidate.Kind == LegacySettingsSourceKind.PackagedMsix
            ? LocalizationManager.Get("LegacyImport_KindPackaged")
            : LocalizationManager.Get("LegacyImport_KindUnpackaged");
        var modified = candidate.LastWriteTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? LocalizationManager.Get("Common_Unknown");
        var schema = candidate.SchemaVersion?.ToString() ?? LocalizationManager.Get("Common_Unknown");
        return LocalizationManager.Format("LegacyImport_CandidateDescription", kind, candidate.RootPath, schema, modified);
    }
}
