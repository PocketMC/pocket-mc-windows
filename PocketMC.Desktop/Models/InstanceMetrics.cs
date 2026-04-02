using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PocketMC.Desktop.Models
{
    public class InstanceMetrics : INotifyPropertyChanged
    {
        private double _cpuUsage;
        public double CpuUsage
        {
            get => _cpuUsage;
            set { _cpuUsage = value; OnPropertyChanged(); }
        }

        private double _ramUsageMb;
        public double RamUsageMb
        {
            get => _ramUsageMb;
            set { _ramUsageMb = value; OnPropertyChanged(); }
        }

        private int _playerCount;
        public int PlayerCount
        {
            get => _playerCount;
            set { _playerCount = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
