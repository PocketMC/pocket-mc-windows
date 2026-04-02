using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO.Compression;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Views
{
    public partial class ServerSettingsPage : Page
    {
        private InstanceMetadata _metadata;
        private string _appRoot;
        private string _serverDir;
        private readonly WorldManager _worldManager = new();
        private readonly BackupService _backupService = new();

        public ServerSettingsPage(InstanceMetadata metadata, string appRoot)
        {
            InitializeComponent();
            _metadata = metadata;
            _appRoot = appRoot;
            
            _serverDir = Path.Combine(_appRoot, "servers");
            foreach (var dir in Directory.GetDirectories(_serverDir))
            {
                var metaFile = Path.Combine(dir, ".pocket-mc.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        var content = File.ReadAllText(metaFile);
                        var meta = System.Text.Json.JsonSerializer.Deserialize<InstanceMetadata>(content);
                        if (meta != null && meta.Id == _metadata.Id)
                        {
                            _serverDir = dir;
                            break;
                        }
                    }
                    catch { }
                }
            }

            LoadSettings();
            LoadWorldTab();
            LoadPluginTab();
            LoadModTab();
            LoadBackupTab();
            LoadCrashRestartTab();

            // Tab change handler to refresh lock states
            MainTabControl.SelectionChanged += (s, e) =>
            {
                if (e.Source is TabControl)
                {
                    RefreshLockStates();
                }
            };
        }

        // ════════════════════════════════════════════════
        //  LOCK STATE (running server protection)
        // ════════════════════════════════════════════════

        private void RefreshLockStates()
        {
            bool isRunning = ServerProcessManager.IsRunning(_metadata.Id);

            // Worlds tab
            TxtWorldLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnUploadWorld.IsEnabled = !isRunning;
            BtnDeleteWorld.IsEnabled = !isRunning;

            // Plugins tab
            TxtPluginLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnAddPlugin.IsEnabled = !isRunning;

            // Mods tab
            TxtModLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnAddMod.IsEnabled = !isRunning;

            // Vanilla warning for plugins
            bool isVanilla = _metadata.ServerType?.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) == true;
            TxtVanillaWarning.Visibility = isVanilla ? Visibility.Visible : Visibility.Collapsed;
            if (isVanilla) BtnAddPlugin.IsEnabled = false;
        }

        // ════════════════════════════════════════════════
        //  TAB 1: PROPERTIES (existing logic preserved)
        // ════════════════════════════════════════════════

        private void LoadSettings()
        {
            SldMinRam.Value = _metadata.MinRamMb > 0 ? _metadata.MinRamMb : 1024;
            SldMaxRam.Value = _metadata.MaxRamMb > 0 ? _metadata.MaxRamMb : 4096;

            var propsFile = Path.Combine(_serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            TxtMotd.Text = props.TryGetValue("motd", out var motd) ? motd : "A Minecraft Server";
            TxtSeed.Text = props.TryGetValue("level-seed", out var seed) ? seed : "";
            TxtSpawnProtection.Text = props.TryGetValue("spawn-protection", out var prot) ? prot : "16";
            TxtMaxPlayers.Text = props.TryGetValue("max-players", out var mp) ? mp : "20";
            TxtServerPort.Text = props.TryGetValue("server-port", out var port) ? port : "25565";
            TxtServerIp.Text = props.TryGetValue("server-ip", out var ip) ? ip : "";

            if (props.TryGetValue("level-type", out var lt))
            {
                foreach (ComboBoxItem item in CmbLevelType.Items)
                {
                    if (item.Content.ToString() == lt)
                        CmbLevelType.SelectedItem = item;
                }
            }

            if (props.TryGetValue("online-mode", out var om)) ChkOnlineMode.IsChecked = om == "true";
            if (props.TryGetValue("pvp", out var pvp)) ChkPvp.IsChecked = pvp == "true";
            if (props.TryGetValue("white-list", out var wl)) ChkWhiteList.IsChecked = wl == "true";

            if (props.TryGetValue("gamemode", out var gm))
            {
                foreach (ComboBoxItem item in CmbGamemode.Items)
                {
                    if (item.Content.ToString() == gm)
                        CmbGamemode.SelectedItem = item;
                }
            }

            if (props.TryGetValue("difficulty", out var dif))
            {
                foreach (ComboBoxItem item in CmbDifficulty.Items)
                {
                    if (item.Content.ToString() == dif)
                        CmbDifficulty.SelectedItem = item;
                }
            }

            if (props.TryGetValue("enable-command-block", out var cb)) ChkAllowBlock.IsChecked = cb == "true";
            if (props.TryGetValue("allow-flight", out var af)) ChkAllowFlight.IsChecked = af == "true";
            if (props.TryGetValue("allow-nether", out var an)) ChkAllowNether.IsChecked = an == "true";

            var iconPath = Path.Combine(_serverDir, "server-icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ImgIconPreview.Source = bmp;
                }
                catch { }
            }
        }

        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRamWarning == null) return;
            ulong totalMb = MemoryHelper.GetTotalPhysicalMemoryMb();
            if (totalMb > 0)
            {
                double totalRequested = SldMaxRam.Value;
                TxtRamWarning.Visibility = totalRequested > (totalMb * 0.8) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TxtMotd_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtMotdPreview.Text = TxtMotd.Text;
        }

        private void BtnBrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PNG Files (*.png)|*.png",
                Title = "Select Server Icon (Must be 64x64)"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dlg.FileName);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    if (bmp.PixelWidth != 64 || bmp.PixelHeight != 64)
                    {
                        MessageBox.Show("Icon must be exactly 64x64 pixels.", "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    ImgIconPreview.Source = bmp;
                    var dest = Path.Combine(_serverDir, "server-icon.png");
                    File.Copy(dlg.FileName, dest, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading image: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _metadata.MinRamMb = (int)SldMinRam.Value;
            _metadata.MaxRamMb = (int)SldMaxRam.Value;
            
            _metadata.EnableAutoRestart = ChkEnableAutoRestart.IsChecked == true;
            if (int.TryParse(TxtMaxAutoRestarts.Text, out int m)) _metadata.MaxAutoRestarts = m;
            if (int.TryParse(TxtAutoRestartDelay.Text, out int d)) _metadata.AutoRestartDelaySeconds = d;

            var metaFile = Path.Combine(_serverDir, ".pocket-mc.json");
            File.WriteAllText(metaFile, System.Text.Json.JsonSerializer.Serialize(_metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            var propsFile = Path.Combine(_serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            props["motd"] = TxtMotd.Text;
            if (!string.IsNullOrWhiteSpace(TxtSeed.Text)) props["level-seed"] = TxtSeed.Text;
            props["spawn-protection"] = TxtSpawnProtection.Text;
            props["max-players"] = TxtMaxPlayers.Text;
            props["server-port"] = TxtServerPort.Text;
            if (!string.IsNullOrWhiteSpace(TxtServerIp.Text)) props["server-ip"] = TxtServerIp.Text;

            props["level-type"] = ((ComboBoxItem)CmbLevelType.SelectedItem).Content.ToString();
            props["online-mode"] = ChkOnlineMode.IsChecked == true ? "true" : "false";
            props["pvp"] = ChkPvp.IsChecked == true ? "true" : "false";
            props["white-list"] = ChkWhiteList.IsChecked == true ? "true" : "false";
            props["gamemode"] = ((ComboBoxItem)CmbGamemode.SelectedItem).Content.ToString();
            props["difficulty"] = ((ComboBoxItem)CmbDifficulty.SelectedItem).Content.ToString();

            props["enable-command-block"] = ChkAllowBlock.IsChecked == true ? "true" : "false";
            props["allow-flight"] = ChkAllowFlight.IsChecked == true ? "true" : "false";
            props["allow-nether"] = ChkAllowNether.IsChecked == true ? "true" : "false";

            ServerPropertiesParser.Write(propsFile, props);

            MessageBox.Show("Settings configuration saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ════════════════════════════════════════════════
        //  TAB 2: WORLDS
        // ════════════════════════════════════════════════

        private void LoadWorldTab()
        {
            var worldDir = Path.Combine(_serverDir, "world");
            if (Directory.Exists(worldDir))
            {
                TxtWorldStatus.Text = "✅ World folder exists";
                double sizeMb = FileUtils.GetDirectorySizeMb(worldDir);
                TxtWorldSize.Text = $"Size: {sizeMb} MB";
            }
            else
            {
                TxtWorldStatus.Text = "No world folder found (will be generated on first start)";
                TxtWorldSize.Text = "";
            }
            RefreshLockStates();
        }

        private async void BtnUploadWorld_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "ZIP Files (*.zip)|*.zip",
                Title = "Select World ZIP"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    BtnUploadWorld.IsEnabled = false;
                    BtnDeleteWorld.IsEnabled = false;
                    TxtWorldProgress.Visibility = Visibility.Visible;

                    var targetWorldPath = Path.Combine(_serverDir, "world");
                    await _worldManager.ImportWorldZipAsync(dlg.FileName, targetWorldPath, progress =>
                    {
                        Dispatcher.Invoke(() => TxtWorldProgress.Text = progress);
                    });

                    LoadWorldTab();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"World import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    TxtWorldProgress.Visibility = Visibility.Collapsed;
                    RefreshLockStates();
                }
            }
        }

        private async void BtnDeleteWorld_Click(object sender, RoutedEventArgs e)
        {
            var worldDir = Path.Combine(_serverDir, "world");
            if (!Directory.Exists(worldDir))
            {
                MessageBox.Show("No world folder found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to delete the current world? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await FileUtils.CleanDirectoryAsync(worldDir);
                    LoadWorldTab();
                    MessageBox.Show("World deleted successfully.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete world: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ════════════════════════════════════════════════
        //  TAB 3: PLUGINS
        // ════════════════════════════════════════════════

        private void LoadPluginTab()
        {
            var pluginsDir = Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
            }

            PluginList.Items.Clear();
            var jars = Directory.GetFiles(pluginsDir, "*.jar");

            foreach (var jar in jars)
            {
                var fi = new FileInfo(jar);
                string apiVersion = PluginScanner.TryGetApiVersion(jar) ?? "Unknown";
                string pluginName = PluginScanner.TryGetPluginName(jar) ?? fi.Name;
                
                // Only flag as incompatible if the plugin requires a NEWER API
                // than the server provides. Backward compat is guaranteed by Spigot/Paper:
                // e.g. api-version 1.14 on server 1.20.4 = FINE (backward compatible)
                // e.g. api-version 1.21 on server 1.20.4 = MISMATCH (too new)
                bool mismatch = PluginScanner.IsIncompatible(apiVersion == "Unknown" ? null : apiVersion, _metadata.MinecraftVersion);

                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoPanel = new StackPanel();
                var nameText = new TextBlock
                {
                    Text = pluginName + (mismatch ? "  ⚠ Version Mismatch" : ""),
                    Foreground = mismatch ? Brushes.Orange : Brushes.White,
                    FontWeight = FontWeights.SemiBold
                };
                var detailText = new TextBlock
                {
                    Text = $"Size: {Math.Round(fi.Length / 1024.0, 1)} KB  |  API: {apiVersion}  |  Modified: {fi.LastWriteTime:yyyy-MM-dd}",
                    Foreground = Brushes.Silver,
                    FontSize = 11
                };
                infoPanel.Children.Add(nameText);
                infoPanel.Children.Add(detailText);
                Grid.SetColumn(infoPanel, 0);
                grid.Children.Add(infoPanel);

                var deleteBtn = new Button
                {
                    Content = "🗑",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = jar,
                    IsEnabled = !ServerProcessManager.IsRunning(_metadata.Id)
                };
                deleteBtn.Click += DeletePlugin_Click;
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);

                row.Child = grid;
                PluginList.Items.Add(row);
            }

            if (jars.Length == 0)
            {
                PluginList.Items.Add(new TextBlock
                {
                    Text = "No plugins installed.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
            }

            RefreshLockStates();
        }

        private void BtnAddPlugin_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JAR Files (*.jar)|*.jar",
                Title = "Select Plugin JAR",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                var pluginsDir = Path.Combine(_serverDir, "plugins");
                foreach (var file in dlg.FileNames)
                {
                    var dest = Path.Combine(pluginsDir, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
                LoadPluginTab();
            }
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var result = MessageBox.Show(
                    $"Delete plugin '{Path.GetFileName(path)}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(path);
                        LoadPluginTab();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ════════════════════════════════════════════════
        //  TAB 4: MODS
        // ════════════════════════════════════════════════

        private void LoadModTab()
        {
            var modsDir = Path.Combine(_serverDir, "mods");
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
            }

            ModList.Items.Clear();
            var jars = Directory.GetFiles(modsDir, "*.jar");

            foreach (var jar in jars)
            {
                var fi = new FileInfo(jar);

                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoPanel = new StackPanel();
                infoPanel.Children.Add(new TextBlock
                {
                    Text = fi.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold
                });
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"Size: {Math.Round(fi.Length / 1024.0, 1)} KB  |  Modified: {fi.LastWriteTime:yyyy-MM-dd}",
                    Foreground = Brushes.Silver,
                    FontSize = 11
                });
                Grid.SetColumn(infoPanel, 0);
                grid.Children.Add(infoPanel);

                var deleteBtn = new Button
                {
                    Content = "🗑",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = jar,
                    IsEnabled = !ServerProcessManager.IsRunning(_metadata.Id)
                };
                deleteBtn.Click += DeleteMod_Click;
                Grid.SetColumn(deleteBtn, 1);
                grid.Children.Add(deleteBtn);

                row.Child = grid;
                ModList.Items.Add(row);
            }

            if (jars.Length == 0)
            {
                ModList.Items.Add(new TextBlock
                {
                    Text = "No mods installed.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
            }

            RefreshLockStates();
        }

        private void BtnAddMod_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JAR Files (*.jar)|*.jar",
                Title = "Select Mod JAR",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                var modsDir = Path.Combine(_serverDir, "mods");
                foreach (var file in dlg.FileNames)
                {
                    var dest = Path.Combine(modsDir, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
                LoadModTab();
            }
        }

        private void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var result = MessageBox.Show(
                    $"Delete mod '{Path.GetFileName(path)}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(path);
                        LoadModTab();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ════════════════════════════════════════════════
        //  TAB 5: BACKUPS
        // ════════════════════════════════════════════════

        private void LoadBackupTab()
        {
            // Set schedule dropdown to current value
            foreach (ComboBoxItem item in CmbBackupInterval.Items)
            {
                if (item.Tag?.ToString() == _metadata.BackupIntervalHours.ToString())
                {
                    CmbBackupInterval.SelectedItem = item;
                    break;
                }
            }

            // Set max backups dropdown to current value
            foreach (ComboBoxItem item in CmbMaxBackups.Items)
            {
                if (item.Tag?.ToString() == _metadata.MaxBackupsToKeep.ToString())
                {
                    CmbMaxBackups.SelectedItem = item;
                    break;
                }
            }

            // Show restore lock warning if running
            bool isRunning = ServerProcessManager.IsRunning(_metadata.Id);
            TxtBackupLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

            // Load backup list
            RefreshBackupList();
        }

        private void RefreshBackupList()
        {
            BackupList.Items.Clear();
            var backupDir = Path.Combine(_serverDir, "backups");
            if (!Directory.Exists(backupDir))
            {
                BackupList.Items.Add(new TextBlock
                {
                    Text = "No backups yet.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            var files = new DirectoryInfo(backupDir)
                .GetFiles("world-*.zip")
                .OrderByDescending(f => f.CreationTime)
                .ToArray();

            if (files.Length == 0)
            {
                BackupList.Items.Add(new TextBlock
                {
                    Text = "No backups yet.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            bool isRunning = ServerProcessManager.IsRunning(_metadata.Id);

            foreach (var file in files)
            {
                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = file.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"Size: {Math.Round(file.Length / (1024.0 * 1024.0), 1)} MB  |  Created: {file.CreationTime:yyyy-MM-dd HH:mm}",
                    Foreground = Brushes.Silver,
                    FontSize = 11
                });
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var restoreBtn = new Button
                {
                    Content = "🔄 Restore",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = file.FullName,
                    IsEnabled = !isRunning
                };
                restoreBtn.Click += RestoreBackup_Click;
                Grid.SetColumn(restoreBtn, 1);
                grid.Children.Add(restoreBtn);

                var deleteBtn = new Button
                {
                    Content = "🗑",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = file.FullName
                };
                deleteBtn.Click += DeleteBackup_Click;
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);

                row.Child = grid;
                BackupList.Items.Add(row);
            }
        }

        private async void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnCreateBackup.IsEnabled = false;
                TxtBackupProgress.Visibility = Visibility.Visible;

                await _backupService.RunBackupAsync(_metadata, _serverDir, progress =>
                {
                    Dispatcher.Invoke(() => TxtBackupProgress.Text = progress);
                });

                RefreshBackupList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Backup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCreateBackup.IsEnabled = true;
                TxtBackupProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (ServerProcessManager.IsRunning(_metadata.Id))
            {
                MessageBox.Show("Stop the server before restoring a backup.", "Server Running", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is Button btn && btn.Tag is string zipPath)
            {
                var result = MessageBox.Show(
                    $"Restore '{Path.GetFileName(zipPath)}'? This will REPLACE the current world.",
                    "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        TxtBackupProgress.Visibility = Visibility.Visible;
                        await _backupService.RestoreBackupAsync(zipPath, _serverDir, progress =>
                        {
                            Dispatcher.Invoke(() => TxtBackupProgress.Text = progress);
                        });
                        MessageBox.Show("World restored successfully!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Restore failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        TxtBackupProgress.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var result = MessageBox.Show(
                    $"Delete backup '{Path.GetFileName(path)}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(path);
                        RefreshBackupList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void CmbBackupInterval_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbBackupInterval.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                if (int.TryParse(item.Tag.ToString(), out int hours))
                {
                    _metadata.BackupIntervalHours = hours;
                    SaveMetadata();
                }
            }
        }

        private void CmbMaxBackups_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbMaxBackups.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                if (int.TryParse(item.Tag.ToString(), out int max))
                {
                    _metadata.MaxBackupsToKeep = max;
                    SaveMetadata();
                }
            }
        }

        private void SaveMetadata()
        {
            var metaFile = Path.Combine(_serverDir, ".pocket-mc.json");
            File.WriteAllText(metaFile, System.Text.Json.JsonSerializer.Serialize(_metadata,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        // ════════════════════════════════════════════════
        //  TAB 6: CRASH & RESTART
        // ════════════════════════════════════════════════

        private void LoadCrashRestartTab()
        {
            ChkEnableAutoRestart.IsChecked = _metadata.EnableAutoRestart;
            TxtMaxAutoRestarts.Text = _metadata.MaxAutoRestarts.ToString();
            TxtAutoRestartDelay.Text = _metadata.AutoRestartDelaySeconds.ToString();
        }
    }
}
