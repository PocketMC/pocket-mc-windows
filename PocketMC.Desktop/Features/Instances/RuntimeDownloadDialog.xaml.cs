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
