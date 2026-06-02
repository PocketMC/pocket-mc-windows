using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace PocketMC.Desktop.Features.Console
{
    public partial class ServerConsolePage
    {
        private bool _responsiveToolbarInstalled;
        private WrapPanel? _responsiveToolbar;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Loaded += ServerConsolePage_ResponsiveToolbarLoaded;
        }

        private void ServerConsolePage_ResponsiveToolbarLoaded(object sender, RoutedEventArgs e)
        {
            InstallResponsiveToolbar();
            UpdateReadOnlyCommandHint();
        }

        private void InstallResponsiveToolbar()
        {
            if (_responsiveToolbarInstalled || PageRoot == null)
            {
                return;
            }

            UIElement? originalToolbar = null;
            foreach (UIElement child in PageRoot.Children)
            {
                if (Grid.GetRow(child) == 0)
                {
                    originalToolbar = child;
                    break;
                }
            }

            if (originalToolbar != null)
            {
                originalToolbar.Visibility = Visibility.Collapsed;
            }

            _responsiveToolbar = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(_responsiveToolbar, 0);

            _responsiveToolbar.Children.Add(MakeUiButton("Back", Wpf.Ui.Controls.SymbolRegular.ArrowLeft24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnBack_Click));

            var search = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = "Search logs...",
                MinWidth = 220,
                MaxWidth = 360,
                Margin = ToolbarMargin,
                Text = TxtLogSearch?.Text ?? string.Empty
            };
            search.TextChanged += (_, _) =>
            {
                if (TxtLogSearch != null && !string.Equals(TxtLogSearch.Text, search.Text, StringComparison.Ordinal))
                {
                    TxtLogSearch.Text = search.Text;
                }
            };
            _responsiveToolbar.Children.Add(search);

            var regex = new ToggleButton
            {
                Content = ".*",
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                ToolTip = "Use Regex",
                Margin = ToolbarMargin
            };
            regex.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(IsRegexEnabled)) { Source = this, Mode = BindingMode.TwoWay });
            _responsiveToolbar.Children.Add(regex);

            _responsiveToolbar.Children.Add(new ToggleButton
            {
                Content = "Auto-scroll",
                IsChecked = BtnAutoScroll?.IsChecked ?? true,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = ToolbarMargin
            });

            _responsiveToolbar.Children.Add(MakeUiButton("Copy Logs", Wpf.Ui.Controls.SymbolRegular.Copy24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnCopyLogs_Click));

            var filter = new ComboBox
            {
                Width = 132,
                MinHeight = 34,
                Margin = ToolbarMargin,
                ToolTip = "Quick log filter"
            };
            filter.Items.Add("All");
            filter.Items.Add("Chat");
            filter.Items.Add("Info");
            filter.Items.Add("Warnings");
            filter.Items.Add("Errors");
            filter.Items.Add("System");
            filter.SelectedIndex = 0;
            filter.SelectionChanged += (_, _) => ApplyQuickFilter(filter.SelectedItem?.ToString() ?? "All");
            _responsiveToolbar.Children.Add(filter);

            var players = MakeUiButton(PlayerStatus, Wpf.Ui.Controls.SymbolRegular.People24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnPlayers_Click);
            players.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PlayerStatus)) { Source = this });
            players.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(CanUseLiveServerControls)) { Source = this });
            _responsiveToolbar.Children.Add(players);

            var stop = MakeUiButton("Stop Server", Wpf.Ui.Controls.SymbolRegular.Stop24, Wpf.Ui.Controls.ControlAppearance.Danger, BtnStop_Click);
            stop.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(CanStopServer)) { Source = this });
            _responsiveToolbar.Children.Add(stop);

            var restart = MakeUiButton("Restart", Wpf.Ui.Controls.SymbolRegular.ArrowSync24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnRestart_Click);
            restart.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(CanUseLiveServerControls)) { Source = this });
            _responsiveToolbar.Children.Add(restart);

            _responsiveToolbar.Children.Add(MakeUiButton("AI Summary", Wpf.Ui.Controls.SymbolRegular.Sparkle24, Wpf.Ui.Controls.ControlAppearance.Info, SafeAiSummary_Click));

            PageRoot.Children.Add(_responsiveToolbar);
            _responsiveToolbarInstalled = true;
        }

        private static Thickness ToolbarMargin => new(0, 0, 8, 8);

        private Wpf.Ui.Controls.Button MakeUiButton(string content, Wpf.Ui.Controls.SymbolRegular symbol, Wpf.Ui.Controls.ControlAppearance appearance, RoutedEventHandler handler)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Content = content,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = symbol },
                Appearance = appearance,
                Margin = ToolbarMargin,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += handler;
            return button;
        }

        private void ApplyQuickFilter(string filter)
        {
            IsFilterChat = filter is "All" or "Chat";
            IsFilterInfo = filter is "All" or "Info";
            IsFilterWarn = filter is "All" or "Warnings";
            IsFilterError = filter is "All" or "Errors";
            IsFilterSystem = filter is "All" or "System";
        }

        private void UpdateReadOnlyCommandHint()
        {
            if (TxtCommand == null)
            {
                return;
            }

            if (IsReadOnlySessionLog)
            {
                TxtCommand.PlaceholderText = "Start the server to send commands";
            }
        }

        private async void SafeAiSummary_Click(object sender, RoutedEventArgs e)
        {
            bool consent = PocketMC.Desktop.Infrastructure.AppDialog.Confirm(
                "AI Log Analysis",
                "This sends selected server logs to your configured AI provider. Logs may include player names, IP addresses, local paths, chat text, or tokens. Continue?");

            if (!consent)
            {
                return;
            }

            await SummarizeSessionAsync();
        }

        private void BtnOpenServerFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(_instancePath);
        }

        private void BtnOpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            string modsPath = Path.Combine(_instancePath, "mods");
            string pluginsPath = Path.Combine(_instancePath, "plugins");

            if (Directory.Exists(modsPath))
            {
                OpenFolder(modsPath);
                return;
            }

            if (Directory.Exists(pluginsPath))
            {
                OpenFolder(pluginsPath);
                return;
            }

            OpenFolder(_instancePath);
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logs.Add(new LogLine { Text = $"[PocketMC] Could not open folder: {ex.Message}", TextColor = System.Windows.Media.Brushes.OrangeRed, Level = LogLevel.System });
            }
        }
    }
}
