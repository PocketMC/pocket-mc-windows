using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Application.Interfaces;

namespace PocketMC.Desktop.Features.Tunnel
{
    public sealed class PlayitDialogService : IPlayitDialogService
    {
        private readonly IServiceProvider _serviceProvider;

        public PlayitDialogService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void ShowSetupWizard()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = ActivatorUtilities.CreateInstance<PlayitSetupWizardDialog>(_serviceProvider);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                dialog.ShowDialog();
            });
        }
    }
}
