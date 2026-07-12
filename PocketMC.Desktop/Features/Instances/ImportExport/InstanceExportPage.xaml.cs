using PocketMC.Desktop.Core.Interfaces;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PocketMC.Application.Interfaces;
using PocketMC.Infrastructure.Instances;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public partial class InstanceExportPage : Page, ISupportsKeyboardBackNavigation
{
    private readonly IAppNavigationService _navigationService;
    private readonly MouseWheelEventHandler _previewMouseWheelHandler;
    private bool _isForwardingMouseWheel;

    public InstanceExportPage(
        IAppNavigationService navigationService,
        InstanceExportViewModel viewModel)
    {
        InitializeComponent();
        _navigationService = navigationService;
        ViewModel = viewModel;
        DataContext = ViewModel;
        _previewMouseWheelHandler = OnPagePreviewMouseWheel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public InstanceExportViewModel ViewModel { get; }

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
        var dialog = new SaveFileDialog
        {
            Title = "Save Pocket MC Instance Export",
            Filter = "Pocket MC Instance Export (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(ViewModel.DestinationZipPath),
            InitialDirectory = Path.GetDirectoryName(ViewModel.DestinationZipPath)
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.DestinationZipPath = dialog.FileName;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsExporting)
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
                if (ViewModel.CancelExportCommand.CanExecute(null))
                {
                    ViewModel.CancelExportCommand.Execute(null);
                }

                var controller = new System.Windows.Threading.DispatcherFrame();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (ViewModel.IsExporting)
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
        if (ViewModel.IsExporting)
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
                if (ViewModel.CancelExportCommand.CanExecute(null))
                {
                    ViewModel.CancelExportCommand.Execute(null);
                }
            }
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string? exportPath = ViewModel.ExportResult?.ZipPath;
        string? folder = string.IsNullOrWhiteSpace(exportPath)
            ? null
            : Path.GetDirectoryName(exportPath);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
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
