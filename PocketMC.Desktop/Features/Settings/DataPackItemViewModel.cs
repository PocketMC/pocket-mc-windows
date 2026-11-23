using System.Windows.Input;
using PocketMC.Application.Services.Instances;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Settings
{
    public class DataPackItemViewModel : ViewModelBase
    {
        private MarketplaceVersion? _update;

        public DataPackItemViewModel(DataPackEntry entry) => Entry = entry;

        public DataPackEntry Entry { get; }
        public string Name => Entry.Name;
        public string ToggleText => Entry.IsEnabled ? "Disable" : "Enable";
        public string StatusText => Entry.IsEnabled ? "Enabled" : "Disabled";

        public MarketplaceVersion? Update
        {
            get => _update;
            set { if (SetProperty(ref _update, value)) OnPropertyChanged(nameof(HasUpdate)); }
        }
        public bool HasUpdate => _update != null;

        public ICommand? ToggleCommand { get; set; }
        public ICommand? RemoveCommand { get; set; }
        public ICommand? UpdateCommand { get; set; }
    }
}
