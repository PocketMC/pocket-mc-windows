using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Settings
{
    public class PropertyItem : ViewModelBase
    {
        private string _key = string.Empty;
        public string Key
        {
            get => _key;
            set { if (SetProperty(ref _key, value)) IsDirty = true; }
        }

        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set { if (SetProperty(ref _value, value)) IsDirty = true; }
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        private bool _isCore;
        public bool IsCore
        {
            get => _isCore;
            set => SetProperty(ref _isCore, value);
        }

        public static PropertyItem CreateLoaded(string key, string value, bool isCore = false)
        {
            return new PropertyItem { _key = key, _value = value, _isCore = isCore, _isDirty = false };
        }
    }
}
