using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    /// <summary>
    /// Represents a single log line with colorization.
    /// </summary>
    public class LogLine
    {
        public string Text { get; set; } = string.Empty;
        public Brush TextColor { get; set; } = Brushes.LightGray;
    }

    /// <summary>
    /// Dedicated console page for viewing server output and sending commands.
    /// Uses DispatcherTimer batching for high-performance rendering.
    /// </summary>
    public partial class ServerConsolePage : Page, INotifyPropertyChanged
    {
        private readonly InstanceMetadata _metadata;
        private readonly ServerProcess _serverProcess;
        private readonly ConcurrentQueue<LogLine> _pendingLines = new();
        private readonly DispatcherTimer _flushTimer;
        private const int MAX_LOG_LINES = 5000;

        public ObservableCollection<LogLine> Logs { get; } = new();

        public string ServerName => _metadata.Name;
        public string StatusText => _serverProcess.State switch
        {
            ServerState.Online => "● Online",
            ServerState.Starting => "◉ Starting...",
            ServerState.Stopping => "◉ Stopping...",
            ServerState.Crashed => "✖ Crashed",
            _ => "● Stopped"
        };
        public Brush StatusColor => _serverProcess.State switch
        {
            ServerState.Online => Brushes.LimeGreen,
            ServerState.Starting or ServerState.Stopping => Brushes.Orange,
            ServerState.Crashed => Brushes.Red,
            _ => Brushes.Gray
        };

        public ServerConsolePage(InstanceMetadata metadata, ServerProcess serverProcess)
        {
            _metadata = metadata;
            _serverProcess = serverProcess;

            InitializeComponent();
            DataContext = this;

            // Subscribe to output events
            _serverProcess.OnOutputLine += OnOutputReceived;
            _serverProcess.OnErrorLine += OnErrorReceived;
            _serverProcess.OnStateChanged += OnStateChanged;
            _serverProcess.OnServerCrashed += OnServerCrashed;

            // Flush timer: 100ms interval for batched UI updates
            _flushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _flushTimer.Tick += FlushPendingLines;
            _flushTimer.Start();

            // Drain any existing buffered lines
            while (_serverProcess.OutputBuffer.TryDequeue(out var existingLine))
            {
                _pendingLines.Enqueue(ColorizeLogLine(existingLine));
            }
        }

        private void OnOutputReceived(string line)
        {
            _pendingLines.Enqueue(ColorizeLogLine(line));
        }

        private void OnErrorReceived(string line)
        {
            _pendingLines.Enqueue(new LogLine { Text = line, TextColor = Brushes.Red });
        }

        private void OnStateChanged(ServerState state)
        {
            Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));

                if (state == ServerState.Starting)
                {
                    CrashBanner.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void OnServerCrashed(string crashContext)
        {
            Dispatcher.Invoke(() =>
            {
                TxtCrashLog.Text = crashContext;
                CrashBanner.Visibility = Visibility.Visible;
            });
        }

        private void FlushPendingLines(object? sender, EventArgs e)
        {
            int count = 0;
            while (_pendingLines.TryDequeue(out var line) && count < 200)
            {
                Logs.Add(line);
                count++;
            }

            // Trim old lines to prevent unbounded memory growth
            while (Logs.Count > MAX_LOG_LINES)
                Logs.RemoveAt(0);

            // Auto-scroll to bottom
            if (count > 0 && LogScroller != null)
                LogScroller.ScrollToEnd();
        }

        /// <summary>
        /// Applies regex colorization based on Minecraft log severity tags.
        /// </summary>
        private static LogLine ColorizeLogLine(string text)
        {
            Brush color;
            if (text.Contains("/WARN]") || text.Contains("[WARN]"))
                color = Brushes.Yellow;
            else if (text.Contains("/ERROR]") || text.Contains("[ERROR]") || text.Contains("Exception"))
                color = Brushes.OrangeRed;
            else if (text.Contains("/INFO]") || text.Contains("[INFO]"))
                color = Brushes.LightGray;
            else if (text.Contains("Done ("))
                color = Brushes.LimeGreen;
            else
                color = Brushes.WhiteSmoke;

            return new LogLine { Text = text, TextColor = color };
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await SendCommand();
        }

        private async void TxtCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendCommand();
                e.Handled = true;
            }
        }

        private async System.Threading.Tasks.Task SendCommand()
        {
            string command = TxtCommand.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            // Echo the command in the log
            Logs.Add(new LogLine { Text = $"> {command}", TextColor = Brushes.CornflowerBlue });
            TxtCommand.Text = string.Empty;

            await _serverProcess.WriteInputAsync(command);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Unsubscribe to prevent memory leaks
            _flushTimer.Stop();
            _serverProcess.OnOutputLine -= OnOutputReceived;
            _serverProcess.OnErrorLine -= OnErrorReceived;
            _serverProcess.OnStateChanged -= OnStateChanged;
            _serverProcess.OnServerCrashed -= OnServerCrashed;

            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _serverProcess.StopAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogLine { Text = $"[ERROR] Stop failed: {ex.Message}", TextColor = Brushes.Red });
            }
        }

        private async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            Logs.Add(new LogLine { Text = "[PocketMC] Restart is not yet implemented. Stop and start manually.", TextColor = Brushes.Orange });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
