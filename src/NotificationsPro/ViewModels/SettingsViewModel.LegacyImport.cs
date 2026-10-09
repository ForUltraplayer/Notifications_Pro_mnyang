// @file SettingsViewModel.LegacyImport.cs
// @author: rudals252
// @brief 프로필 탭 Import/Export 옆의 최소 진입점: 원본 Notifications Pro 설정 후보를 탐지해 사용자가 고르게 하고, LegacySettingsImporter로 가져온 뒤 화면을 다시 읽는다. 문구는 P3에서 리소스화한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Windows;
using System.Windows.Input;
using NotificationsPro.Services;

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
                "No settings from the original Notifications Pro were found on this PC.",
                "Import from Original App",
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
                $"This copy cannot be imported: {chosen.BlockReason}.",
                "Import from Original App",
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
            result.Success ? "Import finished. The original app's files were not changed." : $"Import did not complete: {result.Error}",
            $"Assets copied: {result.AssetsCopied}, already identical: {result.AssetsSkippedIdentical}, replaced (backed up): {result.AssetsBackedUp}",
        };
        if (!string.IsNullOrWhiteSpace(result.SettingsBackupPath))
            lines.Add($"Previous settings backup: {result.SettingsBackupPath}");
        if (!string.IsNullOrWhiteSpace(result.AssetBackupFolder))
            lines.Add($"Replaced asset backups: {result.AssetBackupFolder}");

        System.Windows.MessageBox.Show(
            string.Join(Environment.NewLine, lines),
            "Import from Original App",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static LegacySettingsCandidate? ChooseLegacyCandidate(IReadOnlyList<LegacySettingsCandidate> found)
    {
        const string backupNote = "Current settings are backed up first; the original app's files are only read.";
        if (found.Count == 1)
        {
            var only = found[0];
            var answer = System.Windows.MessageBox.Show(
                $"Import settings, themes, profiles and assets from:{Environment.NewLine}{Environment.NewLine}{DescribeLegacyCandidate(only)}{Environment.NewLine}{Environment.NewLine}{backupNote}",
                "Import from Original App",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return answer == MessageBoxResult.Yes ? only : null;
        }

        var first = found[0];
        var second = found[1];
        var choice = System.Windows.MessageBox.Show(
            $"Two copies of the original settings were found.{Environment.NewLine}{Environment.NewLine}"
            + $"Yes = {DescribeLegacyCandidate(first)}{Environment.NewLine}{Environment.NewLine}"
            + $"No = {DescribeLegacyCandidate(second)}{Environment.NewLine}{Environment.NewLine}"
            + $"Cancel = do not import.{Environment.NewLine}{backupNote}",
            "Import from Original App",
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
        var kind = candidate.Kind == LegacySettingsSourceKind.PackagedMsix ? "Installed app (MSIX)" : "Unpackaged run";
        var modified = candidate.LastWriteTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "unknown";
        var schema = candidate.SchemaVersion?.ToString() ?? "unknown";
        return $"{kind}: {candidate.RootPath} (schema {schema}, modified {modified})";
    }
}
