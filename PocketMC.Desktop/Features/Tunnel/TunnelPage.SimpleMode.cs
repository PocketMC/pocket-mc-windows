using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class TunnelPage
    {
        private bool _simpleModeInstalled;
        private Wpf.Ui.Controls.Card? _simpleModeCard;
        private Button? _toggleAdvancedButton;
        private bool _advancedTunnelDetailsVisible;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Loaded += TunnelPage_SimpleModeLoaded;
        }

        private void TunnelPage_SimpleModeLoaded(object sender, RoutedEventArgs e)
        {
            InstallSimpleMode();
        }

        private void InstallSimpleMode()
        {
            if (_simpleModeInstalled || Content is not Grid rootGrid)
            {
                return;
            }

            TxtStatusDetail.Text = "Use the setup steps below to make servers reachable outside your Wi-Fi.";

            _simpleModeCard = new Wpf.Ui.Controls.Card
            {
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(_simpleModeCard, 1);

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "Make my server public",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextFillColorPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 4)
            });
            root.Children.Add(new TextBlock
            {
                Text = "PocketMC uses Playit to create public join addresses. Follow the steps in order instead of guessing which button does what, a radical user-interface innovation.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = FindResource("TextFillColorSecondaryBrush") as Brush,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var steps = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            steps.Children.Add(MakeStep("1", "Download", "Install the Playit helper."));
            steps.Children.Add(MakeStep("2", "Setup", "Link your Playit account."));
            steps.Children.Add(MakeStep("3", "Connect", "Start the public connection."));
            steps.Children.Add(MakeStep("4", "Share", "Copy the join address."));
            root.Children.Add(steps);

            var actionRow = new WrapPanel { Orientation = Orientation.Horizontal };
            actionRow.Children.Add(MakeActionButton("Download Agent", Wpf.Ui.Controls.SymbolRegular.ArrowDownload24, Wpf.Ui.Controls.ControlAppearance.Primary, BtnDownloadAgent_Click));
            actionRow.Children.Add(MakeActionButton("Setup Agent", Wpf.Ui.Controls.SymbolRegular.PersonAdd24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnSetupAgent_Click));
            actionRow.Children.Add(MakeActionButton("Connect", Wpf.Ui.Controls.SymbolRegular.Play24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnConnect_Click));
            actionRow.Children.Add(MakeActionButton("Refresh", Wpf.Ui.Controls.SymbolRegular.ArrowSync24, Wpf.Ui.Controls.ControlAppearance.Secondary, BtnRefresh_Click));

            _toggleAdvancedButton = new Button
            {
                Content = "Show advanced tunnel controls",
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _toggleAdvancedButton.Click += ToggleAdvancedTunnelDetails_Click;
            actionRow.Children.Add(_toggleAdvancedButton);
            root.Children.Add(actionRow);

            root.Children.Add(new TextBlock
            {
                Text = "Danger zone actions like deleting the agent stay in advanced controls so nobody nukes their setup while trying to invite a friend.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = FindResource("TextFillColorTertiaryBrush") as Brush,
                Margin = new Thickness(0, 4, 0, 0)
            });

            _simpleModeCard.Content = root;

            foreach (UIElement child in rootGrid.Children)
            {
                if (Grid.GetRow(child) == 1)
                {
                    Grid.SetRow(child, 2);
                }
                else if (Grid.GetRow(child) == 2)
                {
                    Grid.SetRow(child, 3);
                }
            }

            if (rootGrid.RowDefinitions.Count < 4)
            {
                rootGrid.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
            }

            rootGrid.Children.Add(_simpleModeCard);
            SetAdvancedTunnelDetailsVisible(false);
            _simpleModeInstalled = true;
        }

        private Border MakeStep(string number, string title, string description)
        {
            var stack = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = number,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("SystemFillColorAttentionBrush") as Brush,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextFillColorPrimaryBrush") as Brush,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = FindResource("TextFillColorSecondaryBrush") as Brush
            });

            return new Border
            {
                Background = FindResource("ControlFillColorDefaultBrush") as Brush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 8, 0),
                Child = stack
            };
        }

        private Wpf.Ui.Controls.Button MakeActionButton(string content, Wpf.Ui.Controls.SymbolRegular symbol, Wpf.Ui.Controls.ControlAppearance appearance, RoutedEventHandler handler)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Content = content,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = symbol },
                Appearance = appearance,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += handler;
            return button;
        }

        private void ToggleAdvancedTunnelDetails_Click(object sender, RoutedEventArgs e)
        {
            SetAdvancedTunnelDetailsVisible(!_advancedTunnelDetailsVisible);
        }

        private void SetAdvancedTunnelDetailsVisible(bool visible)
        {
            _advancedTunnelDetailsVisible = visible;
            if (_toggleAdvancedButton != null)
            {
                _toggleAdvancedButton.Content = visible ? "Hide advanced tunnel controls" : "Show advanced tunnel controls";
            }

            TxtExecutablePath.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BtnDisconnect.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BtnDeleteAgent.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
