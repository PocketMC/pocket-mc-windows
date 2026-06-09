using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Setup.ViewModels;

namespace PocketMC.Desktop.Features.RemoteControl.UI
{
    public partial class RemoteControlPage : Page
    {
        public RemoteControlPage(RemoteControlSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.EnableMouseWheelScrolling(this, RemoteControlScrollViewer);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.DisableMouseWheelScrolling(this);
        }
    }
}
