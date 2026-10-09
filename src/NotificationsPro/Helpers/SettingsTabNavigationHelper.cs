using System.Collections;
using System.Windows.Controls;

namespace NotificationsPro.Helpers;

public static class SettingsTabNavigationHelper
{
    public static int FindTabIndexByHeader(IList tabItems, string tabHeader)
    {
        if (tabItems == null || string.IsNullOrWhiteSpace(tabHeader))
            return -1;

        for (var i = 0; i < tabItems.Count; i++)
        {
            // Tag holds the invariant tab id (English header); headers are localized (P3).
            if (tabItems[i] is TabItem tab
                && (string.Equals(tab.Tag as string, tabHeader, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetHeaderText(tab.Header), tabHeader, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    public static string? GetHeaderText(object? header)
    {
        switch (header)
        {
            case TabItem tab:
                return GetHeaderText(tab.Header);

            case string text:
                return text;

            case TextBlock textBlock:
                return textBlock.Text;

            case ContentControl contentControl:
                return GetHeaderText(contentControl.Content);

            case System.Windows.Controls.Panel panel:
                foreach (var child in panel.Children)
                {
                    var text = GetHeaderText(child);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
                break;
        }

        return null;
    }
}
