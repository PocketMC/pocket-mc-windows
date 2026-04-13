using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PocketMC.Desktop.Core.Mvvm
{
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) =>
            !_isExecuting && (_canExecute == null || _canExecute(parameter));

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                CommandManager.InvalidateRequerySuggested();
                try
                {
                    await _execute(parameter);
                }
                catch (Exception)
                {
                    // Swallow exception to prevent app crash
                }
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
