using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using PocketMC.Desktop.Core.Mvvm;

using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsAdvancedVM : ViewModelBase
    {
        private readonly ServerConfigurationService _configService;
        private string _serverDir;

        public void UpdateServerDir(string newDir) => _serverDir = newDir;
        private readonly Action _markDirty;

        private bool _enableAutoRestart;
        public bool EnableAutoRestart { get => _enableAutoRestart; set { if (SetProperty(ref _enableAutoRestart, value)) _markDirty(); } }

        private string _maxAutoRestarts = "3";
        public string MaxAutoRestarts { get => _maxAutoRestarts; set { if (SetProperty(ref _maxAutoRestarts, value)) _markDirty(); } }

        private string _autoRestartDelay = "10";
        public string AutoRestartDelay { get => _autoRestartDelay; set { if (SetProperty(ref _autoRestartDelay, value)) _markDirty(); } }

        public ObservableCollection<PropertyItem> AdvancedProperties { get; } = new();

        private string _rawServerProperties = "";
        private bool _isLoadingRawServerProperties;
        private bool _isRawServerPropertiesDirty;
        public bool IsRawServerPropertiesDirty => _isRawServerPropertiesDirty;

        public string RawServerProperties
        {
            get => _rawServerProperties;
            set
            {
                if (SetProperty(ref _rawServerProperties, value))
                {
                    if (!_isLoadingRawServerProperties) _isRawServerPropertiesDirty = true;
                    _markDirty();
                }
            }
        }

        public ICommand AddPropertyCommand { get; }
        public ICommand RemovePropertyCommand { get; }

        public SettingsAdvancedVM(string serverDir, ServerConfigurationService configService, Action markDirty)
        {
            _serverDir = serverDir;
            _configService = configService;
            _markDirty = markDirty;
            AdvancedProperties.CollectionChanged += (s, e) => _markDirty();

            AddPropertyCommand = new RelayCommand(_ => AddProperty());
            RemovePropertyCommand = new RelayCommand(p => RemoveProperty(p as PropertyItem));
        }

        public void LoadRawProperties()
        {
            _isLoadingRawServerProperties = true;
            RawServerProperties = _configService.LoadRawProperties(_serverDir);
            _isRawServerPropertiesDirty = false;
            _isLoadingRawServerProperties = false;
        }

        public void ClearDirtyRaw() => _isRawServerPropertiesDirty = false;

        public void AddProperty()
        {
            var property = new PropertyItem();
            property.PropertyChanged += (s, e) => _markDirty();
            AdvancedProperties.Add(property);
        }

        public void RemoveProperty(PropertyItem? item)
        {
            if (item != null)
            {
                AdvancedProperties.Remove(item);
                _markDirty();
            }
        }

        public PropertyItem CreatePropertyItem(string key, string value)
        {
            var item = PropertyItem.CreateLoaded(key, value, ServerConfigurationService.IsCoreProperty(key));
            item.PropertyChanged += (s, e) => _markDirty();
            return item;
        }
    }
}
