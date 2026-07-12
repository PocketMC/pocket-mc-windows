using PocketMC.Desktop.Infrastructure;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.OS;

namespace PocketMC.Desktop.Features.Dashboard
{
    public partial class DashboardPage : Page
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage(DashboardViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;

            Loaded += DashboardPage_Loaded;
            Unloaded += DashboardPage_Unloaded;
            KeyDown += DashboardPage_KeyDown;
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.EnableMouseWheelScrolling(this, DashboardScrollViewer);
            ViewModel.Activate();
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.DisableMouseWheelScrolling(this);
            ViewModel.Deactivate();
        }

        // Keep UI-specific visual handlers (like drag-drop visual effects, hover animations, scrollbar adjustments) here
        private void Page_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        { }

        private void Page_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string zipPath = files[0];
                    if (zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        PocketMC.Desktop.Infrastructure.AppDialog.ShowInfo("Info", "Modpack import is now available from Server Settings > Mods.");
                    }
                }
            }
        }


        private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is DashboardViewModel vm) vm.NewInstanceCommand.Execute(null);
        }

        private void BtnMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.DataContext = btn.DataContext;
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private async void BtnCopyIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm && vm.HasTunnelAddress)
            {
                await TrySetClipboardText(vm.TunnelAddress!);
                await ShowCopiedFeedback(fe);
            }
        }

        private async void BtnCopyLanIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm && vm.HasLanAddress)
            {
                await TrySetClipboardText(vm.LanAddressDisplayText!);
                await ShowCopiedFeedback(fe);
            }
        }

        private async void BtnCopyNumericIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm && vm.HasNumericTunnelAddress)
            {
                await TrySetClipboardText(vm.NumericTunnelAddress!);
                await ShowCopiedFeedback(fe);
            }
        }

        private async void BtnCopyBedrockNumericIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm && vm.HasBedrockNumericTunnelAddress)
            {
                await TrySetClipboardText(vm.BedrockNumericTunnelAddress!);
                await ShowCopiedFeedback(fe);
            }
        }


        private Task TrySetClipboardText(string text)
        {
            return Infrastructure.ClipboardHelper.TrySetTextAsync(text);
        }

        private async Task ShowCopiedFeedback(FrameworkElement element)
        {
            if (element is Wpf.Ui.Controls.Button btn)
            {
                if (btn.Tag?.ToString() == "Copying") return;
                btn.Tag = "Copying";
                
                var oldForeground = btn.Foreground;
                btn.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24);
                btn.Foreground = System.Windows.Media.Brushes.LimeGreen;
                
                await System.Threading.Tasks.Task.Delay(1000);
                
                btn.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Copy16);
                btn.Foreground = oldForeground;
                btn.Tag = null;
            }
        }

        private async void BtnCopyBedrockIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm)
            {
                string addressToCopy = vm.HasGeyser && !string.IsNullOrEmpty(vm.BedrockTunnelAddress) ? vm.BedrockTunnelAddress : vm.BedrockIpDisplayText;
                if (addressToCopy.Contains("local") || string.IsNullOrWhiteSpace(addressToCopy)) return;

                await TrySetClipboardText(addressToCopy);

                // Keep the property nulling clean so we can read the raw string for saving
                vm.BedrockIpDisplayText = "\u2713 Copied";
                await System.Threading.Tasks.Task.Delay(1500);
                if (vm.BedrockIpDisplayText == "\u2713 Copied")
                {
                    vm.BedrockIpDisplayText = null!; // resets to computed property
                }
            }
        }

        private void DashboardPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (ViewModel.RefreshInstancesCommand.CanExecute(null))
                {
                    ViewModel.RefreshInstancesCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
