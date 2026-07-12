using PocketMC.Application.Interfaces.Mods;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure.Telemetry;
using System.Threading.Tasks;
using System.Windows;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class CurseForgeApiKeyDialogService : ICurseForgeApiKeyDialogService
    {
        private readonly ApplicationState _appState;
        private readonly SettingsManager _settingsManager;

        public CurseForgeApiKeyDialogService(ApplicationState appState, SettingsManager settingsManager)
        {
            _appState = appState;
            _settingsManager = settingsManager;
        }

        public Task<string?> PromptForApiKeyAsync()
        {
            var tcs = new TaskCompletionSource<string?>();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new CurseForgeApiKeyDialogWindow();
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ApiKey))
                {
                    _appState.Settings.CurseForgeApiKey = dialog.ApiKey;
                    _settingsManager.Save(_appState.Settings);
                    tcs.SetResult(dialog.ApiKey);
                }
                else
                {
                    tcs.SetResult(null);
                }
            });

            return tcs.Task;
        }
    }
}
