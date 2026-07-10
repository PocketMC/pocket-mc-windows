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
    private readonly MouseWheelEventHandler _previewMouseWheelHandler;
    private bool _isForwardingMouseWheel;

    public InstanceImportPage(
        IAppNavigationService navigationService,
        InstanceImportViewModel viewModel)
    {
        InitializeComponent();
        _navigationService = navigationService;
        ViewModel = viewModel;
        DataContext = ViewModel;
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

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
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

        AutoDetectServerTypeAndVersion(dialog.FolderName);
    }

    private void AutoDetectServerTypeAndVersion(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        // 1. Detect server type
        string detectedType = "Vanilla";
        if (File.Exists(Path.Combine(folderPath, "PocketMine-MP.phar")))
        {
            detectedType = "Pocketmine";
        }
        else if (File.Exists(Path.Combine(folderPath, "bedrock_server.exe")) || File.Exists(Path.Combine(folderPath, "bedrock_server")))
        {
            detectedType = "Bedrock";
        }
        else if (Directory.Exists(Path.Combine(folderPath, ".fabric")) || 
                 (Directory.Exists(Path.Combine(folderPath, "mods")) && 
                  Directory.GetFiles(Path.Combine(folderPath, "mods"), "*fabric*.jar", SearchOption.AllDirectories).Any()))
        {
            detectedType = "Fabric";
        }
        else if (File.Exists(Path.Combine(folderPath, "user_jvm_args.txt")) && Directory.Exists(Path.Combine(folderPath, "libraries/net/neoforged")))
        {
            detectedType = "NeoForge";
        }
        else if (File.Exists(Path.Combine(folderPath, "user_jvm_args.txt")) || Directory.Exists(Path.Combine(folderPath, "libraries/net/minecraftforge")))
        {
            detectedType = "Forge";
        }
        else if (Directory.GetFiles(folderPath, "*paper*.jar").Any() || Directory.Exists(Path.Combine(folderPath, ".paper-jar")))
        {
            detectedType = "Paper";
        }

        ViewModel.SelectedServerType = detectedType;

        // 2. Try to detect Minecraft version
        try
        {
            var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                
                // Skip common non-version executable or launcher files
                if (name.Equals("bedrock_server.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bedrock_server", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("PocketMine-MP.phar", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                // A. Try to find version immediately following "mc." or "mc-" or "mc_" (e.g. fabric-server-mc.26.2)
                var mcMatch = System.Text.RegularExpressions.Regex.Match(name, @"mc[._-](\d+(?:\.\d+)+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mcMatch.Success)
                {
                    ViewModel.MinecraftVersion = mcMatch.Groups[1].Value;
                    break;
                }

                // B. Try to find version following a known brand suffix (e.g. paper-26.1.2, bedrock-server-1.26.33.1.zip)
                var brandMatch = System.Text.RegularExpressions.Regex.Match(name, @"(?:paper|forge|neoforge|spigot|purpur|vanilla|bds|bedrock|pocketmine)[._-](\d+(?:\.\d+)+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (brandMatch.Success)
                {
                    ViewModel.MinecraftVersion = brandMatch.Groups[1].Value;
                    break;
                }

                // C. Fallback: Find the first substring that matches a general version pattern but is not a loader version
                var matches = System.Text.RegularExpressions.Regex.Matches(name, @"\d+(?:\.\d+)+");
                string? fallbackVersion = null;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (!match.Value.StartsWith("0."))
                    {
                        fallbackVersion = match.Value;
                        break;
                    }
                }
                if (fallbackVersion != null)
                {
                    ViewModel.MinecraftVersion = fallbackVersion;
                    break;
                }

                // D. Try to inspect inside the jar for version.json (e.g. Vanilla server.jar)
                if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (var archive = System.IO.Compression.ZipFile.OpenRead(file))
                        {
                            var entry = archive.GetEntry("version.json");
                            if (entry != null)
                            {
                                using (var stream = entry.Open())
                                using (var reader = new StreamReader(stream))
                                {
                                    var content = reader.ReadToEnd();
                                    var idMatch = System.Text.RegularExpressions.Regex.Match(content, @"""id""\s*:\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (idMatch.Success)
                                    {
                                        ViewModel.MinecraftVersion = idMatch.Groups[1].Value;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 3. Fallback: Try scanning the "versions" subfolder
        if (string.IsNullOrWhiteSpace(ViewModel.MinecraftVersion))
        {
            try
            {
                string versionsPath = Path.Combine(folderPath, "versions");
                if (Directory.Exists(versionsPath))
                {
                    var subDirs = Directory.GetDirectories(versionsPath);
                    foreach (var dir in subDirs)
                    {
                        string dirName = Path.GetFileName(dir);
                        var match = System.Text.RegularExpressions.Regex.Match(dirName, @"\d+(?:\.\d+)+");
                        if (match.Success)
                        {
                            ViewModel.MinecraftVersion = match.Value;
                            break;
                        }
                    }
                }
            }
            catch { }
        }
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
