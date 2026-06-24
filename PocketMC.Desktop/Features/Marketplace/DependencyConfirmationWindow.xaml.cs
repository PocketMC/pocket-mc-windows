using PocketMC.Desktop.Features.Marketplace.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using PocketMC.Domain.Models;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class DependencyConfirmationWindow : FluentWindow
    {
        public DependencyConfirmationWindow(DependencyConfirmationViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            viewModel.CloseRequested += () => Close();
        }

        public bool? ShowDialogWithResult()
        {
            ShowDialog();
            return (DataContext as DependencyConfirmationViewModel)?.Result;
        }
    }


    public class DependencyTypeToAppearanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DependencyType type)
            {
                return type switch
                {
                    DependencyType.Required => ControlAppearance.Primary,
                    DependencyType.Optional => ControlAppearance.Secondary,
                    DependencyType.Incompatible => ControlAppearance.Danger,
                    DependencyType.Embedded => ControlAppearance.Info,
                    _ => ControlAppearance.Secondary
                };
            }
            return ControlAppearance.Secondary;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

