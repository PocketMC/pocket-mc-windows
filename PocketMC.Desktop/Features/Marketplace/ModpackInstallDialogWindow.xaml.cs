using PocketMC.Desktop.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Application.Services.Mods;
using PocketMC.Infrastructure.Mods;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class ModpackInstallDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<ModDownloadTaskViewModel> _items = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private InstanceMetadata? _createdInstanceMetadata;
        private string? _instancePath;

        private readonly string _modpackFilePath;
        private readonly ModpackService _modpackService;
        private readonly InstanceManager _instanceManager;
        private readonly InstanceRegistry _registry;
        private readonly IAppNavigationService _nav;
        private readonly ILogger<ModpackInstallDialogWindow> _logger;

        public ModpackInstallDialogWindow(
            string modpackFilePath,
            ModpackService modpackService,
            InstanceManager instanceManager,
            InstanceRegistry registry,
            IAppNavigationService nav,
            ILogger<ModpackInstallDialogWindow> logger)
        {
            InitializeComponent();
            var visualService = ((App)System.Windows.Application.Current).Services.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>();
            visualService.ApplyThemeToDialog(this);
            ItemsList.ItemsSource = _items;

            _modpackFilePath = modpackFilePath;
            _modpackService = modpackService;
            _instanceManager = instanceManager;
            _registry = registry;
            _nav = nav;
            _logger = logger;
        }

        private async void FluentWindow_ContentRendered(object? sender, EventArgs e)
        {
            await Task.Delay(300);

            _isRunning = true;
            _cts = new CancellationTokenSource();
            BtnCancel.Content = "Cancel";

            try
            {
                await RunInstallProcessAsync();
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowAlert("Installation was cancelled.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to install modpack");
                ShowAlert($"Failed to install modpack: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
                if (!string.IsNullOrEmpty(TxtAlert.Text) || BtnCancel.Content.ToString() == "Cancel")
                {
                    BtnCancel.Content = "Close";
                }
            }
        }

        private async Task RunInstallProcessAsync()
        {
            // 1. Parse Modpack
            UpdateOverallStatus("Parsing modpack archive...", 5);
            var parsedPack = await _modpackService.ParseModpackZipAsync(_modpackFilePath);

            // 2. Resolve Mod URLs (This takes some time, so let's show it)
            UpdateOverallStatus("Resolving mod URLs...", 10);
            await _modpackService.ResolveModUrlsAsync(parsedPack);

            // Filter out mods that weren't resolved or are .zip files
            parsedPack.Mods = parsedPack.Mods.Where(m => 
                !string.IsNullOrEmpty(m.DownloadUrl) && 
                !m.DownloadUrl.StartsWith("CURSEFORGE:") &&
                !m.DestinationPath.Trim().EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !m.Name.Trim().EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // 3. Create Instance
            UpdateOverallStatus("Creating instance...", 20);
            var metadata = _instanceManager.CreateInstance(parsedPack.Name ?? "Imported Modpack", "Imported from Modpack", parsedPack.Loader, parsedPack.MinecraftVersion);
            metadata.IsModpack = true;
            _createdInstanceMetadata = metadata;
            string? instancePath = _registry.GetPath(metadata.Id);
            if (instancePath == null) throw new InvalidOperationException("Failed to determine instance path.");
            _instancePath = instancePath;
            _instanceManager.SaveMetadata(metadata, instancePath);

            // 4. Populate UI Tasks
            var coreTask = new ModDownloadTaskViewModel
            {
                ProjectTitle = $"{parsedPack.Loader} Server Software",
                FileName = $"Minecraft {parsedPack.MinecraftVersion} ({parsedPack.LoaderVersion})",
                IsCoreItem = true
            };
            _items.Add(coreTask);

            foreach (var mod in parsedPack.Mods)
            {
                _items.Add(new ModDownloadTaskViewModel
                {
                    Mod = mod,
                    ProjectTitle = mod.Name,
                    FileName = mod.DestinationPath,
                    IsCoreItem = false
                });
            }

            // 5. Execute Import via ModpackService
            UpdateOverallStatus($"Downloading {parsedPack.Mods.Count} mods...", 30);
            
            var importProgress = new Progress<PocketMC.Application.Interfaces.Instances.InstanceTransferProgress>(p =>
            {
                Dispatcher.Invoke(() => UpdateOverallStatus(p.CurrentStep, p.OverallProgress));
            });

            var report = await _modpackService.ExecuteImportAsync(parsedPack, metadata, instancePath, _modpackFilePath, _items, importProgress, _cts?.Token ?? CancellationToken.None);

            // 6. Complete
            if (!report.Success)
            {
                ShowAlert("Some components failed to download or install correctly.");
                return;
            }

            if (report.SkippedOverrides.Any())
            {
                ShowAlert($"Skipped {report.SkippedOverrides.Count} unsafe overrides (e.g. paths outside instance directory).");
            }

            UpdateOverallStatus("Installation Complete!", 100);
            TxtOverallStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            
            await Task.Delay(1000); // Give user a moment to see success
            
            ShowEulaDialog();
        }

        private void ShowEulaDialog()
        {
            Dispatcher.Invoke(() =>
            {
                Close();

                var result = PocketMC.Desktop.Infrastructure.AppDialog.ShowResult(
                    "Minecraft EULA",
                    "By using this modpack, you agree to the Minecraft End User License Agreement. PocketMC will write eula.txt automatically. Review ",
                    PocketMC.Desktop.Infrastructure.AppDialogType.Confirm,
                    PocketMC.Desktop.Infrastructure.AppDialogButtons.Ok,
                    "Accept",
                    null,
                    null,
                    "Mojang's EULA before accepting.",
                    "https://aka.ms/MinecraftEULA"
                );

                if (result == PocketMC.Desktop.Core.Interfaces.DialogResult.Yes)
                {
                    if (_createdInstanceMetadata != null && _instancePath != null)
                    {
                        string folderName = System.IO.Path.GetFileName(_instancePath);
                        _instanceManager.AcceptEula(folderName);
                    }
                }

                _nav.NavigateToDashboard();
            });
        }

        private void UpdateOverallStatus(string text, double percent)
        {
            TxtOverallStatus.Text = text;
            OverallProgressBar.Value = percent;
        }

        private void ShowAlert(string text)
        {
            AlertBorder.Visibility = Visibility.Visible;
            TxtAlert.Text = text;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
            }
            else
            {
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Dispose();
            base.OnClosed(e);
        }
    }
}
