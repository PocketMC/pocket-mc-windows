using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PocketMC.Desktop.Features.Settings
{
    public partial class ServerSettingsPage
    {
        private Wpf.Ui.Controls.TextBox? _settingsSearchBox;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Loaded += ServerSettingsPage_SearchLoaded;
        }

        private void ServerSettingsPage_SearchLoaded(object sender, RoutedEventArgs e)
        {
            InstallSettingsSearchBox();
        }

        private void InstallSettingsSearchBox()
        {
            if (_settingsSearchBox != null || SidebarList?.Parent is not Grid sidebarHost)
            {
                return;
            }

            if (sidebarHost.RowDefinitions.Count == 0)
            {
                sidebarHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                sidebarHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(SidebarList, 1);
            }

            _settingsSearchBox = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = "Search settings...",
                Margin = new Thickness(0, 0, 0, 12),
                MinHeight = 38,
                ToolTip = "Search by RAM, backup, difficulty, port, icon, MOTD, mods, plugins, AI, crash, or update."
            };
            _settingsSearchBox.TextChanged += SettingsSearchBox_TextChanged;

            Grid.SetRow(_settingsSearchBox, 0);
            sidebarHost.Children.Add(_settingsSearchBox);

            RenameSettingsNavigationForClarity();
        }

        private void RenameSettingsNavigationForClarity()
        {
            SetNavContent(0, "Basic · Identity & Performance");
            SetNavContent(1, "Content · Version Updates");
            SetNavContent(2, "Basic · Gameplay Rules");
            SetNavContent(3, "Content · World & Files");
            SetNavContent(4, "Content · Addons");
            SetNavContent(5, "Safety · Backups");
            SetNavContent(6, "Safety · Fault Tolerance");
            SetNavContent(7, "Advanced · Editor");
            SetNavContent(8, "Advanced · AI Summaries");
        }

        private void SetNavContent(int index, string content)
        {
            if (SidebarList.MenuItems.Count > index && SidebarList.MenuItems[index] is Wpf.Ui.Controls.NavigationViewItem item)
            {
                item.Content = content;
            }
        }

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = _settingsSearchBox?.Text?.Trim() ?? string.Empty;

            for (int i = 0; i < SidebarList.MenuItems.Count; i++)
            {
                if (SidebarList.MenuItems[i] is not Wpf.Ui.Controls.NavigationViewItem item)
                {
                    continue;
                }

                bool visible = string.IsNullOrWhiteSpace(query) || NavItemMatchesSearch(i, item.Content?.ToString() ?? string.Empty, query);
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                ActivateFirstVisibleSearchResult();
            }
        }

        private static bool NavItemMatchesSearch(int index, string label, string query)
        {
            string keywords = index switch
            {
                0 => "identity performance name description icon motd ram memory java startup auto start",
                1 => "version update upgrade rollback minecraft loader paper fabric forge neoforge bedrock",
                2 => "gameplay rules difficulty gamemode whitelist allowlist players pvp spawn command blocks",
                3 => "world files folder seed level type import export properties port",
                4 => "addons mods plugins marketplace modrinth curseforge install update dependency",
                5 => "backup backups restore snapshot safety automatic schedule",
                6 => "fault tolerance restart crash watchdog auto restart failure resilience",
                7 => "advanced editor server properties config raw dangerous",
                8 => "ai summaries summary intelligence logs provider privacy token",
                _ => string.Empty
            };

            return label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   keywords.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void ActivateFirstVisibleSearchResult()
        {
            for (int i = 0; i < SidebarList.MenuItems.Count; i++)
            {
                if (SidebarList.MenuItems[i] is Wpf.Ui.Controls.NavigationViewItem item && item.Visibility == Visibility.Visible)
                {
                    MainTabControl.SelectedIndex = i;

                    foreach (var menuItem in SidebarList.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>())
                    {
                        menuItem.IsActive = false;
                    }

                    item.IsActive = true;
                    break;
                }
            }
        }
    }
}
