using System.Collections;
using System.Windows.Controls;
using NotificationsPro.Helpers;

namespace NotificationsPro.Tests;

public class SettingsTabNavigationHelperTests
{
    [Fact]
    public void FindTabIndexByHeader_MatchesNestedHeaderText_IgnoringCase()
    {
        StaThreadTestHelper.Run(() =>
        {
            IList tabItems = new List<object>
            {
                new TabItem { Header = "Appearance" },
                new TabItem
                {
                    Header = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new Border { Width = 8, Height = 8 },
                            new TextBlock { Text = "Filtering" }
                        }
                    }
                },
                new TabItem { Header = new TextBlock { Text = "Profiles" } }
            };

            var index = SettingsTabNavigationHelper.FindTabIndexByHeader(tabItems, "filtering");

            Assert.Equal(1, index);
        });
    }

    [Fact]
    public void FindTabIndexByHeader_MatchesInvariantTag_WhenHeaderIsLocalized()
    {
        // KR-Mnyang fork (P3): headers follow the UI language; Tag keeps the English tab id.
        StaThreadTestHelper.Run(() =>
        {
            IList tabItems = new List<object>
            {
                new TabItem { Header = new TextBlock { Text = "모양" }, Tag = "Appearance" },
                new TabItem { Header = new TextBlock { Text = "앱" }, Tag = "Apps" },
            };

            Assert.Equal(1, SettingsTabNavigationHelper.FindTabIndexByHeader(tabItems, "Apps"));
            Assert.Equal(0, SettingsTabNavigationHelper.FindTabIndexByHeader(tabItems, "모양"));
        });
    }

    [Fact]
    public void FindTabIndexByHeader_ReturnsMinusOne_WhenHeaderDoesNotExist()
    {
        StaThreadTestHelper.Run(() =>
        {
            IList tabItems = new List<object>
            {
                new TabItem { Header = "Appearance" },
                new TabItem { Header = "Behavior" }
            };

            var index = SettingsTabNavigationHelper.FindTabIndexByHeader(tabItems, "System");

            Assert.Equal(-1, index);
        });
    }
}
