using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public partial class InstanceImportPage : Page, ISupportsKeyboardBackNavigation
{
    private readonly IAppNavigationService _navigationService;
    private readonly IServerDetectionService _serverDetectionService;
    private readonly MouseWheelEventHandler _previewMouseWheelHandler;
    private bool _isForwardingMouseWheel;

    public InstanceImportPage(
        IAppNavigationService navigationService,
        InstanceImportViewModel viewModel,
        IServerDetectionService serverDetectionService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        ViewModel = viewModel;
        DataContext = ViewModel;
        _serverDetectionService = serverDetectionService;
        _previewMouseWheelHandler = OnPagePreviewMouseWheel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public InstanceImportViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AddHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler, true);
        DisableParentScrollViewer(this);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Pocket MC Instance Export",
            Filter = "Pocket MC Instance Export (*.zip)|*.zip|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ViewModel.ZipPath = dialog.FileName;
        if (string.IsNullOrWhiteSpace(ViewModel.RequestedName))
        {
            ViewModel.RequestedName = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private async void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Existing Minecraft Server Folder"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ViewModel.FolderPath = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(ViewModel.RequestedName))
        {
            ViewModel.RequestedName = Path.GetFileName(dialog.FolderName);
        }

        var (detectedType, detectedVersion) = await _serverDetectionService.DetectServerTypeAndVersionAsync(dialog.FolderName);
        ViewModel.SelectedServerType = detectedType;
        ViewModel.MinecraftVersion = detectedVersion;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsImporting)
        {
            var dialogResult = PocketMC.Desktop.Infrastructure.AppDialog.ShowResult(
                "Operation In Progress",
                "An import/export operation is currently running. Cancelling now may leave the instance incomplete and all current progress will be lost. Are you sure you want to cancel?",
                Infrastructure.AppDialogType.Warning,
                Infrastructure.AppDialogButtons.YesNo,
                primaryButtonText: "Continue Operation",
                secondaryButtonText: "Cancel Operation"
            );

            if (dialogResult == PocketMC.Desktop.Core.Interfaces.DialogResult.No) // Cancel Operation
            {
                if (ViewModel.CancelImportCommand.CanExecute(null))
                {
                    ViewModel.CancelImportCommand.Execute(null);
                }

                var controller = new System.Windows.Threading.DispatcherFrame();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (ViewModel.IsImporting)
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                    }
                    controller.Continue = false;
                });

                var originalCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    System.Windows.Threading.Dispatcher.PushFrame(controller);
                }
                finally
                {
                    Mouse.OverrideCursor = originalCursor;
                }

                if (!_navigationService.NavigateBack())
                {
                    _navigationService.NavigateToDashboard();
                }
            }
            return;
        }

        if (!_navigationService.NavigateBack())
        {
            _navigationService.NavigateToDashboard();
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsImporting)
        {
            var dialogResult = PocketMC.Desktop.Infrastructure.AppDialog.ShowResult(
                "Operation In Progress",
                "An import/export operation is currently running. Cancelling now may leave the instance incomplete and all current progress will be lost. Are you sure you want to cancel?",
                Infrastructure.AppDialogType.Warning,
                Infrastructure.AppDialogButtons.YesNo,
                primaryButtonText: "Continue Operation",
                secondaryButtonText: "Cancel Operation"
            );

            if (dialogResult == PocketMC.Desktop.Core.Interfaces.DialogResult.No) // Cancel Operation
            {
                if (ViewModel.CancelImportCommand.CanExecute(null))
                {
                    ViewModel.CancelImportCommand.Execute(null);
                }
            }
        }
    }

    private void BtnDashboard_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateToDashboard();
    }

    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isForwardingMouseWheel || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<ScrollBar>(source) != null ||
            FindAncestor<ComboBox>(source)?.IsDropDownOpen == true ||
            FindAncestor<Popup>(source) != null ||
            PageScroller.ScrollableHeight <= 0)
        {
            return;
        }

        e.Handled = true;

        try
        {
            _isForwardingMouseWheel = true;
            int steps = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine) * 3;
            for (int i = 0; i < steps; i++)
            {
                if (e.Delta > 0)
                {
                    PageScroller.LineUp();
                }
                else
                {
                    PageScroller.LineDown();
                }
            }
        }
        finally
        {
            _isForwardingMouseWheel = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            DependencyObject? visualParent = null;
            try { visualParent = VisualTreeHelper.GetParent(current); } catch { }
            current = visualParent ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    public bool HandleBackNavigation()
    {
        var focused = FocusManager.GetFocusedElement(Window.GetWindow(this));
        if (focused is System.Windows.Controls.Primitives.TextBoxBase ||
            focused is System.Windows.Controls.PasswordBox)
        {
            return false;
        }

        BtnBack_Click(BtnCancel, new RoutedEventArgs());
        return true;
    }

    private static void DisableParentScrollViewer(DependencyObject obj)
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(obj);
        while (parent != null)
        {
            if (parent is ScrollViewer scrollViewer)
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }
    }
}
