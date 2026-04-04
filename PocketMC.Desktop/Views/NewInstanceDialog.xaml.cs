using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    public partial class NewInstanceDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly InstanceManager _instanceManager;
        private readonly string _appRootPath;

        public bool WasCreated { get; private set; }

        public NewInstanceDialog(InstanceManager instanceManager, string appRootPath)
        {
            InitializeComponent();
            _instanceManager = instanceManager;
            _appRootPath = appRootPath;
            
            Loaded += async (s, e) => await LoadVersionsAsync("Vanilla");
        }

        private async void CmbServerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbVersion == null) return;
            var item = CmbServerType.SelectedItem as ComboBoxItem;
            string type = item?.Content?.ToString() ?? "Vanilla";
            await LoadVersionsAsync(type);
        }

        private async void ChkShowSnapshots_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbServerType == null) return;
            var item = CmbServerType.SelectedItem as ComboBoxItem;
            string type = item?.Content?.ToString() ?? "Vanilla";
            await LoadVersionsAsync(type);
        }

        private async Task LoadVersionsAsync(string serverType)
        {
            try
            {
                CmbVersion.ItemsSource = null;
                IServerJarProvider provider = serverType == "Paper" 
                    ? new PaperProvider() 
                    : new VanillaProvider(_appRootPath);

                var versions = await provider.GetAvailableVersionsAsync();

                if (ChkShowSnapshots.IsChecked != true)
                {
                    versions = versions.Where(v => v.Type == "release").ToList();
                }

                CmbVersion.ItemsSource = versions;
                if (versions.Count > 0)
                    CmbVersion.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                TxtError.Text = $"Failed to load versions: {ex.Message}";
                TxtError.Visibility = Visibility.Visible;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                TxtError.Text = "Name is required.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            var selectedVersion = CmbVersion.SelectedItem as MinecraftVersion;
            if (selectedVersion == null)
            {
                TxtError.Text = "Please select a version.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            string srvType = (CmbServerType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";
            
            // Disable inputs
            InputsPanel.Visibility = Visibility.Collapsed;
            BtnCreate.IsEnabled = false;
            ProgressOverlay.Visibility = Visibility.Visible;
            PrgDownload.IsIndeterminate = true;
            TxtError.Visibility = Visibility.Collapsed;

            try
            {
                // 1. Create Instance Metadata + Directory
                var metadata = _instanceManager.CreateInstance(TxtName.Text, TxtDescription.Text, srvType, selectedVersion.Id);
                
                string? instancePath = _instanceManager.GetInstancePath(metadata.Id);
                if (instancePath == null) throw new Exception("Instance directory could not be resolved.");

                string jarPath = Path.Combine(instancePath, "server.jar");

                // 2. Download Jar
                IServerJarProvider provider = srvType == "Paper" 
                    ? new PaperProvider() 
                    : new VanillaProvider(_appRootPath);

                var progress = new Progress<DownloadProgress>(p =>
                {
                    Dispatcher.Invoke(() => 
                    {
                        PrgDownload.IsIndeterminate = p.TotalBytes <= 0;
                        PrgDownload.Value = p.Percentage;
                        TxtProgress.Text = $"{p.BytesRead / 1024 / 1024} MB / {p.TotalBytes / 1024 / 1024} MB";
                    });
                });

                await provider.DownloadJarAsync(selectedVersion.Id, jarPath, progress);

                // 3. Handle EULA Acceptance (NET-12)
                if (ChkAcceptEula.IsChecked == true)
                {
                    string folderName = Path.GetFileName(instancePath);
                    _instanceManager.AcceptEula(folderName);
                }

                WasCreated = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                InputsPanel.IsEnabled = true;
                BtnCreate.IsEnabled = true;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                TxtError.Text = $"Error: {ex.Message}";
                TxtError.Visibility = Visibility.Visible;
            }
        }
    }
}
