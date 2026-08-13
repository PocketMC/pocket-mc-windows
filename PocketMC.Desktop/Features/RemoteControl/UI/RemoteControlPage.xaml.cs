using PocketMC.Desktop.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.OS;
using PocketMC.Desktop.Features.Setup.ViewModels;

namespace PocketMC.Desktop.Features.RemoteControl.UI
{
    public partial class RemoteControlPage : Page
    {
        private RemoteControlSettingsViewModel ViewModel => (RemoteControlSettingsViewModel)DataContext;

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
            
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new System.Action(() => 
            {
                PocketMC.Desktop.Views.Behaviors.AnimatedNavIndicatorBehavior.AnimateToActiveItem(SidebarList);
            }));
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.DisableMouseWheelScrolling(this);
        }

        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.NavigationViewItem clickedItem)
            {
                int idx = SidebarList.MenuItems.IndexOf(clickedItem);
                if (idx != -1 && ViewModel.SelectedTab != idx)
                {
                    ViewModel.SelectedTab = idx;
                    
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new System.Action(() => 
                    {
                        PocketMC.Desktop.Views.Behaviors.AnimatedNavIndicatorBehavior.AnimateToActiveItem(SidebarList);
                    }));
                }
            }
        }
    }
}
