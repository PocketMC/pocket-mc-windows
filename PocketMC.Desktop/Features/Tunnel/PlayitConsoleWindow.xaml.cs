using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PocketMC.Application.Services;
using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure;
using PocketMC.Infrastructure.Tunnel;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PlayitConsoleWindow : FluentWindow
    {
        private readonly PlayitAgentService _playitAgentService;
        private readonly ApplicationState _applicationState;
        private readonly ConcurrentQueue<string> _pendingLines = new();
        private readonly List<string> _allLogs = new();
        private readonly DispatcherTimer _flushTimer;
        private string _searchTerm = string.Empty;

        private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(243, 139, 168));
        private static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(249, 226, 175));
        private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(166, 227, 161));
        private static readonly SolidColorBrush DebugBrush = new(Color.FromRgb(140, 145, 160));
        private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(205, 214, 244));

        static PlayitConsoleWindow()
        {
            ErrorBrush.Freeze();
            WarnBrush.Freeze();
            SuccessBrush.Freeze();
            DebugBrush.Freeze();
            InfoBrush.Freeze();
        }

        public PlayitConsoleWindow(PlayitAgentService playitAgentService, ApplicationState applicationState)
        {
            InitializeComponent();
            _playitAgentService = playitAgentService;
            _applicationState = applicationState;

            _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _flushTimer.Tick += OnFlushTimerTick;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TxtLogFilePath.Text = _playitAgentService.GetLogFilePath();
            UpdateStatusDisplay();

            // Load existing history
            var recent = _playitAgentService.GetRecentLogs();
            foreach (var line in recent)
            {
                _allLogs.Add(line);
                AppendRun(line);
            }

            UpdateLineCount();
            if (BtnAutoScroll.IsChecked == true)
            {
                LogRichTextBox.ScrollToEnd();
            }

            // Subscribe to live events
            _playitAgentService.OnLogReceived += OnLogReceived;
            _playitAgentService.OnStateChanged += OnAgentStateChanged;
            _playitAgentService.OnTunnelRunning += OnTunnelRunning;

            _flushTimer.Start();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _flushTimer.Stop();
            _playitAgentService.OnLogReceived -= OnLogReceived;
            _playitAgentService.OnStateChanged -= OnAgentStateChanged;
            _playitAgentService.OnTunnelRunning -= OnTunnelRunning;
        }

        private void OnLogReceived(string line)
        {
            _pendingLines.Enqueue(line);
        }

        private void OnAgentStateChanged(object? sender, PlayitAgentState state)
        {
            Dispatcher.InvokeAsync(UpdateStatusDisplay);
        }

        private void OnTunnelRunning(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(UpdateStatusDisplay);
        }

        private void UpdateStatusDisplay()
        {
            switch (_playitAgentService.State)
            {
                case PlayitAgentState.Connected:
                    StatusDot.Fill = Brushes.LimeGreen;
                    TxtStatus.Text = "Connected";
                    break;
                case PlayitAgentState.Starting:
                case PlayitAgentState.ProvisioningAgent:
                    StatusDot.Fill = Brushes.Gold;
                    TxtStatus.Text = "Starting...";
                    break;
                case PlayitAgentState.AwaitingSetupCode:
                    StatusDot.Fill = Brushes.Orange;
                    TxtStatus.Text = "Awaiting Setup";
                    break;
                case PlayitAgentState.Error:
                case PlayitAgentState.ReauthRequired:
                    StatusDot.Fill = Brushes.OrangeRed;
                    TxtStatus.Text = "Error";
                    break;
                default:
                    StatusDot.Fill = Brushes.Gray;
                    TxtStatus.Text = "Stopped";
                    break;
            }
        }

        private void OnFlushTimerTick(object? sender, EventArgs e)
        {
            if (_pendingLines.IsEmpty) return;

            bool appendedAny = false;
            while (_pendingLines.TryDequeue(out string? line))
            {
                if (line == null) continue;
                _allLogs.Add(line);

                if (string.IsNullOrEmpty(_searchTerm) || line.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    AppendRun(line);
                    appendedAny = true;
                }
            }

            if (appendedAny)
            {
                UpdateLineCount();
                if (BtnAutoScroll.IsChecked == true)
                {
                    LogRichTextBox.ScrollToEnd();
                }
            }
        }

        private void AppendRun(string line)
        {
            SolidColorBrush brush = ClassifyLogColor(line);
            var run = new Run(line + Environment.NewLine)
            {
                Foreground = brush
            };
            LogParagraph.Inlines.Add(run);

            // Limit flow document inlines to prevent excessive memory usage
            if (LogParagraph.Inlines.Count > 3000)
            {
                while (LogParagraph.Inlines.Count > 2500)
                {
                    LogParagraph.Inlines.Remove(LogParagraph.Inlines.FirstInline);
                }
            }
        }

        private static SolidColorBrush ClassifyLogColor(string line)
        {
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error=", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorBrush;
            }

            if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("warn", StringComparison.OrdinalIgnoreCase))
            {
                return WarnBrush;
            }

            if (line.Contains("playit connected", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("tunnels loaded", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                return SuccessBrush;
            }

            if (line.Contains("DEBUG", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("TRACE", StringComparison.OrdinalIgnoreCase))
            {
                return DebugBrush;
            }

            return InfoBrush;
        }

        private void UpdateLineCount()
        {
            int total = _allLogs.Count;
            int displayed = LogParagraph.Inlines.Count;
            TxtLogCount.Text = string.IsNullOrEmpty(_searchTerm)
                ? $"{total:N0} lines"
                : $"{displayed:N0} of {total:N0} lines matching \"{_searchTerm}\"";
        }

        private async void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<string> source = string.IsNullOrEmpty(_searchTerm)
                ? _allLogs
                : _allLogs.Where(l => l.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase));

            string text = string.Join(Environment.NewLine, source);
            if (!string.IsNullOrEmpty(text))
            {
                bool copied = await ClipboardHelper.TrySetTextAsync(text);
                if (copied && sender is Wpf.Ui.Controls.Button btn)
                {
                    btn.Icon = new SymbolIcon(SymbolRegular.Checkmark24);
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, args) =>
                    {
                        btn.Icon = new SymbolIcon(SymbolRegular.Copy24);
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogParagraph.Inlines.Clear();
            _allLogs.Clear();
            UpdateLineCount();
        }

        private void TxtLogSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = TxtLogSearch.Text?.Trim() ?? string.Empty;
            RebuildDisplay();
        }

        private void TxtLogSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(TxtLogSearch.Text))
                {
                    TxtLogSearch.Text = string.Empty;
                }
                else
                {
                    Keyboard.ClearFocus();
                }
                e.Handled = true;
            }
        }

        private void RebuildDisplay()
        {
            LogParagraph.Inlines.Clear();
            IEnumerable<string> filtered = string.IsNullOrEmpty(_searchTerm)
                ? _allLogs
                : _allLogs.Where(l => l.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase));

            foreach (var line in filtered)
            {
                AppendRun(line);
            }

            UpdateLineCount();
            if (BtnAutoScroll.IsChecked == true)
            {
                LogRichTextBox.ScrollToEnd();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !TxtLogSearch.IsFocused)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
