using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Tunnel;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PlayitConsoleWindow : FluentWindow
    {
        private readonly PlayitAgentService _playitAgentService;
        private readonly ApplicationState _applicationState;
        private readonly ConcurrentQueue<string> _pendingLines = new();
        private readonly List<PlayitLogEntry> _allEntries = new();
        private readonly DispatcherTimer _flushTimer;
        private string _searchTerm = string.Empty;

        private Run? _lastRepeatRun;
        private PlayitLogEntry? _lastAppendedEntry;

        private static readonly SolidColorBrush TimestampBrush = new(Color.FromRgb(127, 132, 156));
        private static readonly SolidColorBrush ModuleBrush = new(Color.FromRgb(180, 190, 254));
        private static readonly SolidColorBrush RetryBrush = new(Color.FromRgb(250, 179, 135));
        private static readonly SolidColorBrush RepeatBrush = new(Color.FromRgb(148, 226, 213));
        private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(243, 139, 168));
        private static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(249, 226, 175));
        private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(166, 227, 161));
        private static readonly SolidColorBrush DebugBrush = new(Color.FromRgb(140, 145, 160));
        private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(205, 214, 244));
        private static readonly SolidColorBrush MessageTextBrush = new(Color.FromRgb(205, 214, 244));

        static PlayitConsoleWindow()
        {
            TimestampBrush.Freeze();
            ModuleBrush.Freeze();
            RetryBrush.Freeze();
            RepeatBrush.Freeze();
            ErrorBrush.Freeze();
            WarnBrush.Freeze();
            SuccessBrush.Freeze();
            DebugBrush.Freeze();
            InfoBrush.Freeze();
            MessageTextBrush.Freeze();
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

            // Load existing history and group consecutive transient duplicates
            var recent = _playitAgentService.GetRecentLogs();
            foreach (var line in recent)
            {
                var entry = PlayitLogFormatter.Parse(line);
                if (_allEntries.Count > 0 && PlayitLogFormatter.CanMerge(_allEntries[^1], entry))
                {
                    _allEntries[^1].RepeatCount++;
                }
                else
                {
                    _allEntries.Add(entry);
                }
            }

            RebuildDisplay();

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

                var entry = PlayitLogFormatter.Parse(line);

                // Check if this entry can be collapsed with the last one
                if (_allEntries.Count > 0 && PlayitLogFormatter.CanMerge(_allEntries[^1], entry))
                {
                    _allEntries[^1].RepeatCount++;

                    // If currently displaying this entry, update repeat indicator in-place
                    if (_lastRepeatRun != null && _lastAppendedEntry == _allEntries[^1])
                    {
                        _lastRepeatRun.Text = $" [x{_allEntries[^1].RepeatCount}]";
                        _lastRepeatRun.Foreground = RepeatBrush;
                        _lastRepeatRun.FontWeight = FontWeights.SemiBold;
                    }
                    appendedAny = true;
                    continue;
                }

                _allEntries.Add(entry);

                if (PlayitLogFormatter.MatchesSearch(entry, _searchTerm))
                {
                    AppendEntryToDocument(entry);
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

        private void AppendEntryToDocument(PlayitLogEntry entry)
        {
            var span = new Span();

            // 1. Timestamp: [HH:mm:ss]
            span.Inlines.Add(new Run($"[{entry.TimeText}] ")
            {
                Foreground = TimestampBrush
            });

            // 2. Level Badge: [INFO ], [WARN ], [RETRY], [READY], [ERROR]
            SolidColorBrush badgeBrush = GetBadgeBrush(entry);
            span.Inlines.Add(new Run($"{entry.LevelBadge.PadRight(5)} ")
            {
                Foreground = badgeBrush,
                FontWeight = FontWeights.SemiBold
            });

            // 3. Module Tag: [Control], [Daemon], etc.
            span.Inlines.Add(new Run($"[{entry.Module}] ")
            {
                Foreground = ModuleBrush
            });

            // 4. Message Content
            SolidColorBrush msgBrush = GetMessageBrush(entry);
            span.Inlines.Add(new Run(entry.Message)
            {
                Foreground = msgBrush
            });

            // 5. Repeat count badge if collapsed
            string repeatText = entry.RepeatCount > 1 ? $" [x{entry.RepeatCount}]" : string.Empty;
            var repeatRun = new Run(repeatText)
            {
                Foreground = RepeatBrush,
                FontWeight = FontWeights.SemiBold
            };
            span.Inlines.Add(repeatRun);

            // 6. Trailing newline
            span.Inlines.Add(new Run(Environment.NewLine));

            LogParagraph.Inlines.Add(span);

            _lastAppendedEntry = entry;
            _lastRepeatRun = repeatRun;

            // Prune excess lines to preserve memory and rendering performance
            if (LogParagraph.Inlines.Count > 3000)
            {
                while (LogParagraph.Inlines.Count > 2500)
                {
                    LogParagraph.Inlines.Remove(LogParagraph.Inlines.FirstInline);
                }
            }
        }

        private static SolidColorBrush GetBadgeBrush(PlayitLogEntry entry)
        {
            if (entry.IsTransientRetry) return RetryBrush;

            return entry.Level switch
            {
                PlayitLogLevel.Error => ErrorBrush,
                PlayitLogLevel.Warn => WarnBrush,
                PlayitLogLevel.Success => SuccessBrush,
                PlayitLogLevel.Debug => DebugBrush,
                PlayitLogLevel.Trace => DebugBrush,
                _ => InfoBrush
            };
        }

        private static SolidColorBrush GetMessageBrush(PlayitLogEntry entry)
        {
            if (entry.IsTransientRetry) return RetryBrush;

            return entry.Level switch
            {
                PlayitLogLevel.Error => ErrorBrush,
                PlayitLogLevel.Warn => WarnBrush,
                PlayitLogLevel.Success => SuccessBrush,
                PlayitLogLevel.Debug => DebugBrush,
                _ => MessageTextBrush
            };
        }

        private void UpdateLineCount()
        {
            int total = _allEntries.Count;
            int displayed = LogParagraph.Inlines.Count;
            TxtLogCount.Text = string.IsNullOrEmpty(_searchTerm)
                ? $"{total:N0} events"
                : $"{displayed:N0} of {total:N0} events matching \"{_searchTerm}\"";
        }

        private async void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<PlayitLogEntry> source = string.IsNullOrEmpty(_searchTerm)
                ? _allEntries
                : _allEntries.Where(entry => PlayitLogFormatter.MatchesSearch(entry, _searchTerm));

            var sb = new StringBuilder();
            foreach (var entry in source)
            {
                string repeat = entry.RepeatCount > 1 ? $" [x{entry.RepeatCount}]" : string.Empty;
                sb.AppendLine($"[{entry.TimeText}] [{entry.LevelBadge}] [{entry.Module}] {entry.Message}{repeat}");
            }

            string text = sb.ToString();
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
            _allEntries.Clear();
            _lastAppendedEntry = null;
            _lastRepeatRun = null;
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
            _lastAppendedEntry = null;
            _lastRepeatRun = null;

            IEnumerable<PlayitLogEntry> filtered = string.IsNullOrEmpty(_searchTerm)
                ? _allEntries
                : _allEntries.Where(entry => PlayitLogFormatter.MatchesSearch(entry, _searchTerm));

            foreach (var entry in filtered)
            {
                AppendEntryToDocument(entry);
            }

            UpdateLineCount();
            if (BtnAutoScroll.IsChecked == true)
            {
                LogRichTextBox.ScrollToEnd();
            }
        }

        private void TxtLogFilePath_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            string logPath = _playitAgentService.GetLogFilePath();
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{logPath}\"",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore explorer launch issues
                }
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
