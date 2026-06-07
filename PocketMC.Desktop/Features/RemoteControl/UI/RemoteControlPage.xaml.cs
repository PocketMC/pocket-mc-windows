using System.Windows.Controls;
using PocketMC.Desktop.Features.Setup.ViewModels;

namespace PocketMC.Desktop.Features.RemoteControl.UI
{
    public partial class RemoteControlPage : Page
    {
        public RemoteControlPage(RemoteControlSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
