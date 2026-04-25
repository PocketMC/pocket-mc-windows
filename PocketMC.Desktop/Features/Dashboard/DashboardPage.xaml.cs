using System;
using System.Windows;
using System.Windows.Controls;
// Removed legacy ViewModels using

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
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Activate();
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
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

        private async Task TrySetClipboardText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.ErrorCode == 0x800401D0)
                {
                    // Clipboard is locked by another process, wait and retry
                    await Task.Delay(100);
                }
                catch (Exception)
                {
                    // Other clipboard failures are ignored to prevent crashes
                    return;
                }
            }
        }

        private async Task ShowCopiedFeedback(FrameworkElement element)
        {
            // Visual feedback placeholder
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
        private void DashScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scv)
            {
                scv.ScrollToVerticalOffset(scv.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

    }
}
