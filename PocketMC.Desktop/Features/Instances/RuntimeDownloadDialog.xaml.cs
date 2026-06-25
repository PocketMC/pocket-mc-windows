using System.Windows;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Instances
{
    public partial class RuntimeDownloadDialog : FluentWindow
    {
        private readonly RuntimeDownloadDialogViewModel _viewModel;

        public RuntimeDownloadDialog(RuntimeDownloadDialogViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            InitializeComponent();

            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            _viewModel.OnComplete += () =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        DialogResult = true;
                        Close();
                    }
                    catch { /* Dialog might already be closed */ }
                });
            };
        }
    }
}
