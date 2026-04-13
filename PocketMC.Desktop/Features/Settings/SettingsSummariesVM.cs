using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Settings;

/// <summary>
/// Sub-ViewModel for the AI Summaries tab in Server Settings.
/// Manages listing, viewing, and deleting session summaries.
/// </summary>
public class SettingsSummariesVM : ViewModelBase
{
    private readonly string _serverDir;
    private readonly SummaryStorageService _storageService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<SessionSummaryItem> Summaries { get; } = new();

    public bool HasSummaries => Summaries.Count > 0;

    private bool _isFeatureAvailable;
    public bool IsFeatureAvailable { get => _isFeatureAvailable; set => SetProperty(ref _isFeatureAvailable, value); }

    private SessionSummaryItem? _selectedSummary;
    public SessionSummaryItem? SelectedSummary
    {
        get => _selectedSummary;
        set
        {
            if (SetProperty(ref _selectedSummary, value))
                OnPropertyChanged(nameof(HasSelectedSummary));
        }
    }

    public bool HasSelectedSummary => _selectedSummary != null;

    private string _summaryContent = string.Empty;
    public string SummaryContent { get => _summaryContent; set => SetProperty(ref _summaryContent, value); }

    private bool _isViewingSummary;
    public bool IsViewingSummary { get => _isViewingSummary; set => SetProperty(ref _isViewingSummary, value); }

    public ICommand ViewSummaryCommand { get; }
    public ICommand DeleteSummaryCommand { get; }
    public ICommand CloseSummaryCommand { get; }

    public SettingsSummariesVM(string serverDir, SummaryStorageService storageService, IDialogService dialogService)
    {
        _serverDir = serverDir;
        _storageService = storageService;
        _dialogService = dialogService;

        ViewSummaryCommand = new RelayCommand(ViewSummary);
        DeleteSummaryCommand = new AsyncRelayCommand(DeleteSummaryAsync);
        CloseSummaryCommand = new RelayCommand(_ => CloseSummary());
    }

    public void Load(bool hasApiKey)
    {
        IsFeatureAvailable = hasApiKey;

        Summaries.Clear();
        if (!hasApiKey)
        {
            OnPropertyChanged(nameof(HasSummaries));
            return;
        }

        var summaries = _storageService.ListSummaries(_serverDir);
        foreach (var s in summaries)
        {
            Summaries.Add(new SessionSummaryItem
            {
                FileName = s.FileName,
                SessionDate = s.SessionEnd.ToLocalTime().ToString("MMM dd, yyyy  HH:mm"),
                Duration = FormatDuration(s.Duration),
                Provider = s.AiProvider,
                Preview = TruncatePreview(s.Content)
            });
        }

        OnPropertyChanged(nameof(HasSummaries));
    }

    private void ViewSummary(object? param)
    {
        var item = param as SessionSummaryItem ?? SelectedSummary;
        if (item == null) return;

        var summary = _storageService.Read(_serverDir, item.FileName);
        if (summary == null)
        {
            _dialogService.ShowMessage("Error", "Could not load this summary. The file may have been deleted.", DialogType.Error);
            return;
        }

        SummaryContent = summary.Content;
        IsViewingSummary = true;
    }

    private async System.Threading.Tasks.Task DeleteSummaryAsync(object? param)
    {
        var item = param as SessionSummaryItem ?? SelectedSummary;
        if (item == null) return;

        var result = await _dialogService.ShowDialogAsync("Delete Summary",
            $"Are you sure you want to delete this session summary from {item.SessionDate}?",
            DialogType.Warning, false);

        if (result == DialogResult.Yes)
        {
            _storageService.Delete(_serverDir, item.FileName);
            Summaries.Remove(item);
            OnPropertyChanged(nameof(HasSummaries));
            if (IsViewingSummary) CloseSummary();
        }
    }

    private void CloseSummary()
    {
        IsViewingSummary = false;
        SummaryContent = string.Empty;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalSeconds}s";
    }

    private static string TruncatePreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "(empty)";
        // Get first meaningful line
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('#', ' ', '-', '*');
            if (trimmed.Length > 10)
                return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
        }
        return content.Length > 120 ? content[..120] + "..." : content;
    }
}

/// <summary>
/// Lightweight display item for the summaries list.
/// </summary>
public class SessionSummaryItem
{
    public string FileName { get; set; } = string.Empty;
    public string SessionDate { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
}
