using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class DependencyConfirmationViewModel : ViewModelBase
    {
        public ObservableCollection<ResolvedDependency> Dependencies { get; }

        public bool HasIncompatible { get; }
        public bool HasErrors { get; }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public bool? Result { get; private set; }

        public bool CanInstall => !HasIncompatible && Dependencies.Any(d => d.IsSelected);

        public DependencyConfirmationViewModel(IEnumerable<ResolvedDependency> dependencies)
        {
            Dependencies = new ObservableCollection<ResolvedDependency>(dependencies);
            
            foreach (var dep in Dependencies)
            {
                dep.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ResolvedDependency.IsSelected)) OnPropertyChanged(nameof(CanInstall)); };
            }

            HasIncompatible = Dependencies.Any(d => d.Type == Models.DependencyType.Incompatible);
            HasErrors = Dependencies.Any(d => !string.IsNullOrEmpty(d.Error));

            ConfirmCommand = new RelayCommand(_ => { Result = true; CloseRequested?.Invoke(); });
            CancelCommand = new RelayCommand(_ => { Result = false; CloseRequested?.Invoke(); });
        }

        public event System.Action? CloseRequested;
    }
}
