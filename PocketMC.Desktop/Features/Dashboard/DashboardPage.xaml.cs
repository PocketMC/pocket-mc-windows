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
                        System.Windows.MessageBox.Show("Modpack import is now available from Server Settings > Mods.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
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
                System.Windows.Clipboard.SetText(vm.TunnelAddress!);
                await ShowCopiedFeedback(fe);
            }
        }

        private async void BtnCopyNumericIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm && vm.HasNumericTunnelAddress)
            {
                System.Windows.Clipboard.SetText(vm.NumericTunnelAddress!);
                await ShowCopiedFeedback(fe);
            }
        }

        private async Task ShowCopiedFeedback(FrameworkElement element)
        {
            // Simple visual feedback: temporarily change the icon if possible,
            // or we can just rely on the existing label change if applied.
            // For now, no complex feedback.
        }

        private async void BtnCopyBedrockIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstanceCardViewModel vm)
            {
                string addressToCopy = vm.HasBedrockNumericTunnelAddress ? vm.BedrockNumericTunnelAddress! : (vm.HasGeyser && !string.IsNullOrEmpty(vm.BedrockTunnelAddress) ? vm.BedrockTunnelAddress : vm.BedrockIpDisplayText);
                if (addressToCopy.Contains("local") || string.IsNullOrWhiteSpace(addressToCopy)) return;

                System.Windows.Clipboard.SetText(addressToCopy);

                // Keep the property nulling clean so we can read the raw string for saving. Wait, we can just save it.
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
