using System.Windows.Controls;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PlayitStatusPage : Page
    {
        private readonly PlayitStatusViewModel _viewModel;

        public PlayitStatusPage(PlayitStatusViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            
            Loaded += PlayitStatusPage_Loaded;
        }

        private async void PlayitStatusPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_viewModel.Monitors.Count == 0)
            {
                await _viewModel.LoadStatusAsync();
            }
        }
    }
}
