using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PocketMC.Desktop.Features.InstanceCreation
{
    public partial class NewInstancePage
    {
        private bool _goalPresetsInstalled;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Loaded += NewInstancePage_GoalPresetsLoaded;
        }

        private void NewInstancePage_GoalPresetsLoaded(object sender, RoutedEventArgs e)
        {
            InstallGoalPresetCard();
        }

        private void InstallGoalPresetCard()
        {
            if (_goalPresetsInstalled || ContentLayoutRoot == null)
            {
                return;
            }

            var card = new Wpf.Ui.Controls.Card
            {
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 14)
            };

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "Start with a goal",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextFillColorPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 4)
            });
            root.Children.Add(new TextBlock
            {
                Text = "Pick what you are trying to make. PocketMC will choose sane defaults, since apparently dropdown archaeology is not a hobby most users requested.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = FindResource("TextFillColorSecondaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var presets = new WrapPanel { Orientation = Orientation.Horizontal };
            presets.Children.Add(MakePresetButton("Survival with friends", "Paper", "Easy", "Survival", enableGeyser: false));
            presets.Children.Add(MakePresetButton("Plugins server", "Paper", "Normal", "Survival", enableGeyser: false));
            presets.Children.Add(MakePresetButton("Java mods", "Fabric", "Normal", "Survival", enableGeyser: false));
            presets.Children.Add(MakePresetButton("Bedrock server", "Bedrock (BDS)", "Easy", "Survival", enableGeyser: false));
            presets.Children.Add(MakePresetButton("Cross-play server", "Paper", "Easy", "Survival", enableGeyser: true));
            root.Children.Add(presets);

            var advancedNote = new TextBlock
            {
                Text = "Advanced fields remain below for seed, world type, loader version, and custom worlds.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = FindResource("TextFillColorTertiaryBrush") as Brush,
                Margin = new Thickness(0, 10, 0, 0)
            };
            root.Children.Add(advancedNote);

            card.Content = root;
            ContentLayoutRoot.Children.Insert(0, card);
            _goalPresetsInstalled = true;
        }

        private Wpf.Ui.Controls.Button MakePresetButton(string label, string serverType, string difficulty, string gamemode, bool enableGeyser)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Content = label,
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Use {serverType} with {gamemode}/{difficulty} defaults."
            };
            button.Click += (_, _) => ApplyGoalPreset(serverType, difficulty, gamemode, enableGeyser);
            return button;
        }

        private void ApplyGoalPreset(string serverType, string difficulty, string gamemode, bool enableGeyser)
        {
            SelectComboBoxItemByContent(CmbServerType, serverType);
            SelectComboBoxItemByContent(CmbDifficulty, difficulty);
            SelectComboBoxItemByContent(CmbGamemode, gamemode);

            if (TxtMaxPlayers != null && string.IsNullOrWhiteSpace(TxtMaxPlayers.Text))
            {
                TxtMaxPlayers.Text = "20";
            }

            if (ChkEnableGeyser != null)
            {
                ChkEnableGeyser.IsChecked = enableGeyser;
            }
        }

        private static void SelectComboBoxItemByContent(ComboBox comboBox, string content)
        {
            foreach (object item in comboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }
        }
    }
}
