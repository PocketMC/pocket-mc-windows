using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Domain.Models;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Setup
{
    public enum SettingsBackupDialogMode
    {
        Backup,
        Restore
    }

    public class SettingsBackupCategoryItemViewModel : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Title { get; }
        public string Description { get; }
        public SymbolRegular Symbol { get; }
        public bool IsSensitive { get; }
        public bool IsAvailable { get; }
        public Visibility UnavailableBadgeVisibility => !IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        public double CardOpacity => IsAvailable ? 1.0 : 0.45;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public SettingsBackupCategoryItemViewModel(
            string key,
            string title,
            string description,
            SymbolRegular symbol,
            bool isSensitive = false,
            bool isAvailable = true,
            bool isSelected = true)
        {
            Key = key;
            Title = title;
            Description = description;
            Symbol = symbol;
            IsSensitive = isSensitive;
            IsAvailable = isAvailable;
            _isSelected = isAvailable && isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class SettingsBackupDialogWindow : FluentWindow
    {
        public SettingsBackupDialogMode Mode { get; }
        public ObservableCollection<SettingsBackupCategoryItemViewModel> Items { get; } = new();
        public bool Confirmed { get; private set; }

        public SettingsBackupDialogWindow(
            SettingsBackupDialogMode mode,
            SettingsBackupCategories? availableInPackage = null)
        {
            InitializeComponent();
            Mode = mode;

            try
            {
                if (System.Windows.Application.Current is App app)
                {
                    var visualService = app.Services.GetService<IShellVisualService>();
                    visualService?.ApplyThemeToDialog(this);

                    var accentService = app.Services.GetService<AccentColorService>();
                    accentService?.ReassertAccent();
                }
            }
            catch
            {
                // Non-critical theme fallback
            }

            ConfigureDialog(availableInPackage);
        }

        private void ConfigureDialog(SettingsBackupCategories? available)
        {
            bool isBackup = Mode == SettingsBackupDialogMode.Backup;

            if (isBackup)
            {
                TxtTitle.Text = "Backup Settings";
                TxtSubtitle.Text = "Choose the settings categories and credentials you wish to include in your backup file.";
                HeaderIcon.Symbol = SymbolRegular.ArrowDownload24;
                BtnAction.Content = "Export Backup...";
            }
            else
            {
                TxtTitle.Text = "Restore Settings";
                TxtSubtitle.Text = "Select the categories you want to restore. Unchecked items in your current setup will not be changed.";
                HeaderIcon.Symbol = SymbolRegular.ArrowUpload24;
                BtnAction.Content = "Restore Selected";
            }

            Items.Clear();

            AddCategory(
                "behavior",
                "App Behavior & Startup",
                "Start with Windows, tray minimization, sleep prevention, console buffer, and telemetry.",
                SymbolRegular.Settings24,
                isSensitive: false,
                isAvailable: isBackup || (available?.IncludeAppBehavior ?? true));

            AddCategory(
                "appearance",
                "Appearance & Themes",
                "Window backdrop effect, accent color mode & custom hex, and custom wallpaper path.",
                SymbolRegular.Color24,
                isSensitive: false,
                isAvailable: isBackup || (available?.IncludeAppearance ?? true));

            AddCategory(
                "paths",
                "Storage & Root Paths",
                "PocketMC root data folder path, Playit directory, and Disaster Recovery sync directory.",
                SymbolRegular.Folder24,
                isSensitive: false,
                isAvailable: isBackup || (available?.IncludeStoragePaths ?? true));

            AddCategory(
                "notifications",
                "Desktop Notifications",
                "Server online alerts, agent connect alerts, remote control notifications, and AI summary alerts.",
                SymbolRegular.Alert24,
                isSensitive: false,
                isAvailable: isBackup || (available?.IncludeNotifications ?? true));

            AddCategory(
                "ai_config",
                "AI Summarization Configuration",
                "Active AI provider, selected model names, custom endpoint URLs, and auto-summary preferences.",
                SymbolRegular.BrainCircuit24,
                isSensitive: false,
                isAvailable: isBackup || (available?.IncludeAiConfiguration ?? true));

            AddCategory(
                "ai_keys",
                "AI Provider API Keys",
                "Stored API keys for Google Gemini, OpenAI, Anthropic Claude, Groq, Mistral, and Ollama.",
                SymbolRegular.Key24,
                isSensitive: true,
                isAvailable: isBackup || (available?.IncludeAiApiKeys ?? true));

            AddCategory(
                "curseforge",
                "CurseForge Marketplace Key",
                "CurseForge addon marketplace API token.",
                SymbolRegular.Cube24,
                isSensitive: true,
                isAvailable: isBackup || (available?.IncludeCurseForgeApiKey ?? true));

            AddCategory(
                "discord",
                "Discord Integration & Bot",
                "Discord Rich Presence state and linked Discord bot user credentials / webhook API keys.",
                SymbolRegular.Chat24,
                isSensitive: true,
                isAvailable: isBackup || (available?.IncludeDiscord ?? true));

            AddCategory(
                "playit",
                "Playit.gg Tunnel Connection",
                "Playit partner agent credentials, account ID, and agent secret key.",
                SymbolRegular.Globe24,
                isSensitive: true,
                isAvailable: isBackup || (available?.IncludePlayitTunnel ?? true));

            AddCategory(
                "cloud",
                "Cloud Backups & Tokens",
                "Cloud backup targets, sync preferences, and Google Drive / OneDrive / Dropbox OAuth tokens.",
                SymbolRegular.Cloud24,
                isSensitive: true,
                isAvailable: isBackup || (available?.IncludeCloudBackups ?? true));

            AddCategory(
                "remote_control",
                "Remote Control Configuration",
                "Remote control port, access mode, tunnel provider settings, and authorized user accounts.",
                SymbolRegular.ShieldKeyhole24,
                isSensitive: true,
                isAvailable: isBackup || (available?.IncludeRemoteControl ?? true));

            CategoriesList.ItemsSource = Items;
        }

        private void AddCategory(
            string key,
            string title,
            string description,
            SymbolRegular symbol,
            bool isSensitive,
            bool isAvailable)
        {
            Items.Add(new SettingsBackupCategoryItemViewModel(
                key,
                title,
                description,
                symbol,
                isSensitive: isSensitive,
                isAvailable: isAvailable,
                isSelected: isAvailable));
        }

        public SettingsBackupCategories GetSelectedCategories()
        {
            bool IsChecked(string key) =>
                Items.FirstOrDefault(i => i.Key == key && i.IsAvailable)?.IsSelected == true;

            return new SettingsBackupCategories
            {
                IncludeAppBehavior = IsChecked("behavior"),
                IncludeAppearance = IsChecked("appearance"),
                IncludeStoragePaths = IsChecked("paths"),
                IncludeNotifications = IsChecked("notifications"),
                IncludeAiConfiguration = IsChecked("ai_config"),
                IncludeAiApiKeys = IsChecked("ai_keys"),
                IncludeCurseForgeApiKey = IsChecked("curseforge"),
                IncludeDiscord = IsChecked("discord"),
                IncludePlayitTunnel = IsChecked("playit"),
                IncludeCloudBackups = IsChecked("cloud"),
                IncludeRemoteControl = IsChecked("remote_control")
            };
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items)
            {
                if (item.IsAvailable)
                {
                    item.IsSelected = true;
                }
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items)
            {
                item.IsSelected = false;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedCategories();
            bool anySelected = selected.IncludeAppBehavior ||
                               selected.IncludeAppearance ||
                               selected.IncludeStoragePaths ||
                               selected.IncludeNotifications ||
                               selected.IncludeAiConfiguration ||
                               selected.IncludeAiApiKeys ||
                               selected.IncludeCurseForgeApiKey ||
                               selected.IncludeDiscord ||
                               selected.IncludePlayitTunnel ||
                               selected.IncludeCloudBackups ||
                               selected.IncludeRemoteControl;

            if (!anySelected)
            {
                TxtStatusWarning.Text = "Please select at least one category to proceed.";
                TxtStatusWarning.Visibility = Visibility.Visible;
                return;
            }

            Confirmed = true;
            Close();
        }
    }
}
