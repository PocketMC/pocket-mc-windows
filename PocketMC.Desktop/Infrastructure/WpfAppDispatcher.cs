using PocketMC.Desktop.Core.Interfaces;
using System;
using System.Threading.Tasks;
using System.Windows;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class WpfAppDispatcher : IAppDispatcher
    {
        public void Invoke(Action action)
        {
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        public async Task InvokeAsync(Func<Task> action)
        {
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
            }
            else
            {
                await action();
            }
        }

        public async Task InvokeAsync(Action action)
        {
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        }
    }
}

