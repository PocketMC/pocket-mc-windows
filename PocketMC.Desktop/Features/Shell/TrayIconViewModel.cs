using System;
using System.Linq;
using System.Windows;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Features.Shell
{
    public class TrayIconViewModel : ViewModelBase
    {
        private readonly ServerProcessManager _processManager;

        public TrayIconViewModel(ServerProcessManager processManager)
        {
            _processManager = processManager;
            _processManager.OnInstanceStateChanged += (id, state) => Application.Current.Dispatcher.Invoke(UpdateTooltip);
            UpdateTooltip();
        }

        private string _tooltipText = "PocketMC Desktop";
        public string TooltipText
        {
            get => _tooltipText;
            set => SetProperty(ref _tooltipText, value);
        }

        private Visibility _iconVisibility = Visibility.Collapsed;
        public Visibility IconVisibility
        {
            get => _iconVisibility;
            set => SetProperty(ref _iconVisibility, value);
        }

        public void EnsureVisible()
        {
            IconVisibility = Visibility.Visible;
            UpdateTooltip();
        }

        public void Hide()
        {
            IconVisibility = Visibility.Collapsed;
        }

        public void UpdateTooltip()
        {
            int runningCount = _processManager.ActiveProcesses.Count(p => 
                p.Value.State == PocketMC.Desktop.Models.ServerState.Online || 
                p.Value.State == PocketMC.Desktop.Models.ServerState.Starting);
            
            if (runningCount > 0)
                TooltipText = $"PocketMC Desktop - {runningCount} server{(runningCount == 1 ? "" : "s")} running";
            else
                TooltipText = "PocketMC Desktop";
        }
    }
}
