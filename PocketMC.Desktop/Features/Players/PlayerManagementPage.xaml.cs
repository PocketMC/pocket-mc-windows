using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Players;

public partial class PlayerManagementPage : Page, IDisposable, ITitleBarContextSource
{
    public PlayerManagementPage(PlayerManagementViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public PlayerManagementViewModel ViewModel { get; }
    public string? TitleBarContextTitle => ViewModel.InstanceName;
    public string? TitleBarContextStatusText => ViewModel.ServerStatusText;
    public Brush? TitleBarContextStatusBrush => ViewModel.ServerStatusBrush;
    public event Action? TitleBarContextChanged;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerManagementViewModel.ServerStatusText) or nameof(PlayerManagementViewModel.ServerStatusBrush))
        {
            TitleBarContextChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
    }
}
