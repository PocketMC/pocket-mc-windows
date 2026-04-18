using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Shell;


namespace PocketMC.Desktop.Features.Console
{
    /// <summary>
    /// Represents a single log line with colorization.
    /// </summary>
    public enum LogLevel
    {
        Chat,
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        System
    }

    public class LogLine
    {
        public string Text { get; set; } = string.Empty;
        public Brush TextColor { get; set; } = Brushes.LightGray;
        public LogLevel Level { get; set; } = LogLevel.Info;
    }

    /// <summary>
    /// Dedicated console page for viewing server output and sending commands.
    /// Uses DispatcherTimer batching for high-performance rendering.
    /// </summary>
    public partial class ServerConsolePage : Page, INotifyPropertyChanged, ITitleBarContextSource
    {
        private readonly IAppNavigationService _navigationService;
        private readonly InstanceMetadata _metadata;
        private readonly ServerProcess _serverProcess;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly ApplicationState _applicationState;
        private readonly ILogger<ServerConsolePage> _logger;
        private readonly ConcurrentQueue<LogLine> _pendingLines = new();
        private readonly DispatcherTimer _flushTimer;
        private int _maxLogLines = 5000;
        private ScrollViewer? _shellScrollViewer;
        private ScrollViewer? _logScrollViewer;
        private ScrollBarVisibility _originalShellVerticalScrollBarVisibility;
        private ScrollBarVisibility _originalShellHorizontalScrollBarVisibility;
        private bool _isShellScrollLocked;

        public ObservableCollection<LogLine> Logs { get; } = new();
        public ObservableCollection<LogLine> FilteredLogs { get; } = new();
        public ObservableCollection<string> CommandSuggestions { get; } = new();
        private readonly System.Collections.Generic.HashSet<string> _knownCommands = new();
        private readonly System.Collections.Generic.List<string> _commandHistory = new();
        private int _historyIndex = -1;
        private string _pendingCommandText = string.Empty;

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
        public string? TitleBarContextTitle => ServerName;
        public string? TitleBarContextStatusText => StatusText;
        public Brush? TitleBarContextStatusBrush => StatusColor;

        // --- Modern Filtering State ---
        private bool _isFilterChat = true;
        private bool _isFilterInfo = true;
        private bool _isFilterWarn = true;
        private bool _isFilterError = true;
        private bool _isFilterSystem = true;
        private bool _isRegexEnabled = false;

        public bool IsFilterChat { get => _isFilterChat; set { if (SetProperty(ref _isFilterChat, value)) ApplyFilters(); } }
        public bool IsFilterInfo { get => _isFilterInfo; set { if (SetProperty(ref _isFilterInfo, value)) ApplyFilters(); } }
        public bool IsFilterWarn { get => _isFilterWarn; set { if (SetProperty(ref _isFilterWarn, value)) ApplyFilters(); } }
        public bool IsFilterError { get => _isFilterError; set { if (SetProperty(ref _isFilterError, value)) ApplyFilters(); } }
        public bool IsFilterSystem { get => _isFilterSystem; set { if (SetProperty(ref _isFilterSystem, value)) ApplyFilters(); } }
        public bool IsRegexEnabled { get => _isRegexEnabled; set { if (SetProperty(ref _isRegexEnabled, value)) ApplyFilters(); } }

        public bool CanStopServer => _serverProcess.State == ServerState.Online || _serverProcess.State == ServerState.Starting;
        public event Action? TitleBarContextChanged;

        public ServerConsolePage(
            IAppNavigationService navigationService,
            IServerLifecycleService lifecycleService,
            InstanceMetadata metadata,
            ServerProcess serverProcess,
            ApplicationState applicationState,
            ILogger<ServerConsolePage> logger)
        {
            _navigationService = navigationService;
            _lifecycleService = lifecycleService;
            _metadata = metadata;
            _serverProcess = serverProcess;
            _applicationState = applicationState;
            _logger = logger;

            _maxLogLines = _applicationState.Settings.ConsoleBufferSize;
            if (_maxLogLines <= 0) _maxLogLines = 5000;

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
            Loaded += ServerConsolePage_Loaded;
            Unloaded += ServerConsolePage_Unloaded;

            // 1. Load full session history from the log file (NET-15)
            LoadSessionLogHistory();

            // 2. Drain and CLEAR the transient buffer
            // We clear it because the log file already contains these lines (autoflush is on)
            while (_serverProcess.OutputBuffer.TryDequeue(out _)) { }

            // 3. If in crashed state, show the crash banner immediately (NET-10)
            if (_serverProcess.State == ServerState.Crashed && !string.IsNullOrEmpty(_serverProcess.CrashContext))
            {
                TxtCrashLog.Text = _serverProcess.CrashContext;
                CrashBanner.Visibility = Visibility.Visible;
            }

            // 4. Initialize command suggestions
            InitializeDefaultCommands();

            // 5. Connect GotFocus for immediate suggestions
            TxtCommand.GotFocus += TxtCommand_GotFocus;
        }

        private void ServerConsolePage_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                LockShellScrollHost();
                EnsureLogScrollViewer();
            }));
        }

        private void ServerConsolePage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnlockShellScrollHost();
            DetachHandlers();
        }

        private void TxtCommand_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtCommand.Text))
            {
                // Show common commands
                var common = new[] { "list", "stop", "help", "save-all", "op", "whitelist" };
                TxtCommand.ItemsSource = common;
            }

            // Try to force dropdown open if any items
            if (TxtCommand.ItemsSource != null)
            {
                // In WPF-UI 3.x, TxtCommand might have a property to show suggestions manually
                // or it might just show them automatically if they are present.
            }
        }

        private void InitializeDefaultCommands()
        {
            var defaults = new[]
            {
                "advancement", "attribute", "ban", "ban-ip", "banlist", "bossbar", "clear", "clone", "data", "datapack",
                "debug", "defaultgamemode", "deop", "difficulty", "effect", "enchant", "execute", "expr", "fill",
                "forceload", "function", "gamemode", "gamerule", "give", "help", "item", "jfr", "kick", "kill", "list",
                "locate", "loot", "me", "msg", "op", "pardon", "pardon-ip", "particle", "perf", "place", "recipe",
                "reload", "ride", "save-all", "save-off", "save-on", "say", "schedule", "scoreboard", "seed", "setblock",
                "setidletimeout", "setworldspawn", "spawnpoint", "spectate", "spreadplayers", "stop", "stopsound",
                "summon", "tag", "team", "teammsg", "teleport", "tell", "tellraw", "tick", "time", "title", "tp",
                "trigger", "weather", "whitelist", "worldborder"
            };

            foreach (var cmd in defaults)
            {
                _knownCommands.Add(cmd);
                CommandSuggestions.Add(cmd);
            }
        }

        private void OnOutputReceived(string line)
        {
            _pendingLines.Enqueue(ColorizeLogLine(line));
            ParseHelpOutput(line);
        }

        private readonly Regex _helpRegex = new(@"^\/?([a-zA-Z0-9\-_]+)", RegexOptions.Compiled);

        private void ParseHelpOutput(string line)
        {
            // Extract the part after the standard Minecraft log template [timestamp INFO]: /...
            // or [timestamp INFO]: command ... (no slash)
            int infoIdx = line.IndexOf(" INFO]: ");
            if (infoIdx == -1) return;

            string content = line.Substring(infoIdx + 8).Trim();
            if (content.StartsWith('/')) content = content.Substring(1);

            int spaceIdx = content.IndexOf(' ');
            string cmd = spaceIdx != -1 ? content.Substring(0, spaceIdx) : content;

            // Clean up common help output chars
            cmd = cmd.Trim('<', '>', '[', ']', '(', ')', ' ', ':', '-');

            if (!string.IsNullOrEmpty(cmd) && cmd.Length > 1 && _knownCommands.Add(cmd))
            {
                Dispatcher.Invoke(() =>
                {
                    CommandSuggestions.Add(cmd);
                    _logger.LogTrace("Added new command from console output: {Command}", cmd);
                });
            }
        }

        private void OnErrorReceived(string line)
        {
            _pendingLines.Enqueue(new LogLine { Text = line, TextColor = Brushes.Red, Level = LogLevel.Error });
        }

        private void OnStateChanged(ServerState state)
        {
            Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(CanStopServer));
                TitleBarContextChanged?.Invoke();

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
            bool addedToFiltered = false;

            while (_pendingLines.TryDequeue(out var line) && count < 500) // Increased batch size for high-perf
            {
                Logs.Add(line);
                if (PassesFilter(line))
                {
                    FilteredLogs.Add(line);
                    addedToFiltered = true;
                }
                count++;
            }

            // Trim old lines
            int excess = Logs.Count - _maxLogLines;
            for (int i = 0; i < excess; i++)
            {
                var removed = Logs[0];
                Logs.RemoveAt(0);
                if (FilteredLogs.Count > 0 && FilteredLogs[0] == removed)
                    FilteredLogs.RemoveAt(0);
            }

            // Auto-scroll to bottom of the ListView
            if (addedToFiltered && LogView != null && (BtnAutoScroll?.IsChecked ?? true))
            {
                if (FilteredLogs.Count > 0)
                    LogView.ScrollIntoView(FilteredLogs[^1]);
            }
        }

        private bool PassesFilter(LogLine line)
        {
            // 1. Severity/Level Toggles
            bool passesToggle = line.Level switch
            {
                LogLevel.Chat => IsFilterChat,
                LogLevel.Info => IsFilterInfo,
                LogLevel.Warn => IsFilterWarn,
                LogLevel.Error => IsFilterError,
                LogLevel.System => IsFilterSystem,
                _ => true
            };
            if (!passesToggle) return false;

            // 2. Search Logic
            if (string.IsNullOrWhiteSpace(TxtLogSearch?.Text)) return true;
            string query = TxtLogSearch.Text;

            if (IsRegexEnabled)
            {
                try
                {
                    return Regex.IsMatch(line.Text, query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch
                {
                    // Invalid regex - fail open or closed? Let's fail closed to avoid noise
                    return false;
                }
            }

            // 3. Traditional Multi-keyword search
            var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var kw in keywords)
            {
                if (kw.StartsWith('-') && kw.Length > 1)
                {
                    if (line.Text.Contains(kw.Substring(1), StringComparison.OrdinalIgnoreCase)) return false;
                }
                else
                {
                    if (!line.Text.Contains(kw, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }

            return true;
        }

        private void TxtLogSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CmbLogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (FilteredLogs == null) return;

            FilteredLogs.Clear();
            foreach (var line in Logs)
            {
                if (PassesFilter(line))
                    FilteredLogs.Add(line);
            }

            if (FilteredLogs.Count > 0 && (BtnAutoScroll?.IsChecked ?? true))
                LogView.ScrollIntoView(FilteredLogs[^1]);
        }

        private void LoadSessionLogHistory()
        {
            try
            {
                string logFile = System.IO.Path.Combine(_serverProcess.WorkingDirectory, "logs", "pocketmc-session.log");
                if (System.IO.File.Exists(logFile))
                {
                    // Read the session log with shared access to avoid locking errors
                    using var stream = new System.IO.FileStream(logFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var reader = new System.IO.StreamReader(stream);

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        _pendingLines.Enqueue(ColorizeLogLine(line));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load the session log history for {ServerName}.", _metadata.Name);
            }
        }

        private LogLevel _lastLevel = LogLevel.Info;

        /// <summary>
        /// Applies regex colorization and severity level based on log tags.
        /// </summary>
        private LogLine ColorizeLogLine(string text)
        {
            Brush color;
            LogLevel level;

            // Detection for severity
            if (text.Contains("/ERROR]") || text.Contains("[ERROR]") || text.Contains("Exception") || text.Contains("Fatal") || text.Contains("FATAL") || text.Contains("Error:") || text.Contains(" SEVERE") || text.Contains(" CRITICAL"))
            {
                color = Brushes.OrangeRed;
                level = LogLevel.Error;
            }
            else if (text.Contains("/WARN]") || text.Contains("[WARN]") || text.Contains(" WARN") || text.Contains("Warning") || text.Contains("WARNING") || text.Contains("****") || text.Contains("***"))
            {
                color = Brushes.Yellow;
                level = LogLevel.Warn;
            }
            else if (text.Contains("/DEBUG]") || text.Contains("[DEBUG]"))
            {
                color = Brushes.Cyan;
                level = LogLevel.Debug;
            }
            else if (text.Contains("/TRACE]") || text.Contains("[TRACE]"))
            {
                color = Brushes.Gray;
                level = LogLevel.Trace;
            }
            else if (text.Contains("Done (") || text.Contains("Server started"))
            {
                color = Brushes.LimeGreen;
                level = LogLevel.Info;
            }
            else if (Regex.IsMatch(text, @"^\[\d{2}:\d{2}:\d{2}\sINFO\]:\s<[^>]+>\s.*$") || // Vanilla <Player> Msg
                     Regex.IsMatch(text, @"^\[\d{2}:\d{2}:\d{2}\sINFO\]:\s[^\s:]+\s(joined|left) the game$") || // Join/Leave
                     Regex.IsMatch(text, @"^\[\d{2}:\d{2}:\d{2}\sINFO\]:\s\[[^\]]+\]\s.*$")) // Plugin messages often [Plugin] Msg
            {
                color = Brushes.White;
                level = LogLevel.Chat;
            }
            else if (text.TrimStart().StartsWith("at ") || text.TrimStart().StartsWith("...") || text.Contains("Caused by:"))
            {
                // Stack trace line sticky severity
                color = _lastLevel switch
                {
                    LogLevel.Error => Brushes.OrangeRed,
                    LogLevel.Warn => Brushes.Yellow,
                    _ => Brushes.WhiteSmoke
                };
                level = _lastLevel;
            }
            else if (text.StartsWith("> "))
            {
                color = Brushes.CornflowerBlue;
                level = LogLevel.System;
            }
            else
            {
                color = text.Contains("/INFO]") || text.Contains("[INFO]") ? Brushes.LightGray : Brushes.WhiteSmoke;
                level = LogLevel.Info;
            }

            _lastLevel = level;
            return new LogLine { Text = text, TextColor = color, Level = level };
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await SendCommand();
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Intercept '/' key (Oem2 or Divide) to focus command box
            if ((e.Key == Key.Oem2 || e.Key == Key.Divide) && !TxtCommand.IsFocused)
            {
                TxtCommand.Focus();
                e.Handled = true; // Don't print the '/' in the box - it's a focus shortcut
            }
        }

        private async void TxtCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendCommand();
                e.Handled = true;
                return;
            }

            // Command history navigation
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                if (_commandHistory.Count == 0) return;

                if (_historyIndex == -1) // Moving from current text to history
                    _pendingCommandText = TxtCommand.Text;

                if (e.Key == Key.Up)
                {
                    _historyIndex++;
                    if (_historyIndex >= _commandHistory.Count)
                        _historyIndex = _commandHistory.Count - 1;
                }
                else // Key.Down
                {
                    _historyIndex--;
                    if (_historyIndex < -1)
                        _historyIndex = -1;
                }

                if (_historyIndex == -1)
                    TxtCommand.Text = _pendingCommandText;
                else
                    TxtCommand.Text = _commandHistory[_commandHistory.Count - 1 - _historyIndex];

                // Set caret to end
                if (TxtCommand.Text != null)
                {
                    // ui:AutoSuggestBox doesn't always have a CaretIndex directly depending on the version
                    // but often its internal TextBox is accessible or it behaves like a text box.
                    // If TxtCommand.Text isn't null, WPF usually moves caret to start/end on programmatic change.
                }

                e.Handled = true;
            }
        }

        private void TxtCommand_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                string query = TxtCommand.Text;
                if (string.IsNullOrEmpty(query))
                {
                    // If empty, show first few commands or null
                    TxtCommand_GotFocus(sender, new RoutedEventArgs());
                    return;
                }

                // If command has spaces, we're likely in arguments mode, don't show command suggestions
                if (query.Contains(' '))
                {
                    TxtCommand.ItemsSource = null;
                    return;
                }

                // Strip leading slash for filtering since the command list doesn't have them
                string filterQuery = query.StartsWith('/') ? query.Substring(1) : query;

                var filtered = CommandSuggestions
                    .Where(c => c.StartsWith(filterQuery, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Length)
                    .ToList();

                if (filtered.Count == 0)
                {
                    filtered = CommandSuggestions
                        .Where(c => c.Contains(filterQuery, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Length)
                        .ToList();
                }

                TxtCommand.ItemsSource = filtered;
            }
        }

        private void TxtCommand_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs e)
        {
            if (e.SelectedItem is string cmd)
            {
                TxtCommand.Text = cmd;
            }
        }

        private async void TxtCommand_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
        {
            await SendCommand();
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var allText = string.Join(Environment.NewLine, Logs.Select(l => l.Text));
                if (!string.IsNullOrEmpty(allText))
                {
                    System.Windows.Clipboard.SetText(allText);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to copy logs: {ex.Message}", "Clipboard Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void BtnCopyCrash_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtCrashLog.Text))
            {
                System.Windows.Clipboard.SetText(TxtCrashLog.Text);
            }
        }

        private async System.Threading.Tasks.Task SendCommand()
        {
            string command = TxtCommand.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            // Strip leading '/' before sending to the server console (implicit)
            if (command.StartsWith('/')) command = command.Substring(1);
            if (string.IsNullOrEmpty(command)) return;

            // Update history
            if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
                _commandHistory.Add(command);

            _historyIndex = -1;

            // Echo the command in the log
            Logs.Add(new LogLine { Text = $"> {command}", TextColor = Brushes.CornflowerBlue });
            TxtCommand.Text = string.Empty;

            await _serverProcess.WriteInputAsync(command);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (!_navigationService.NavigateBack())
            {
                _navigationService.NavigateToDashboard();
            }
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
            try
            {
                Logs.Add(new LogLine { Text = "[PocketMC] Initiating manual restart...", TextColor = Brushes.Cyan });
                await _lifecycleService.RestartAsync(_metadata.Id);
            }
            catch (Exception ex)
            {
                Logs.Add(new LogLine { Text = $"[ERROR] Restart failed: {ex.Message}", TextColor = Brushes.Red });
            }
        }

        private void DetachHandlers()
        {
            _flushTimer.Stop();
            _serverProcess.OnOutputLine -= OnOutputReceived;
            _serverProcess.OnErrorLine -= OnErrorReceived;
            _serverProcess.OnStateChanged -= OnStateChanged;
            _serverProcess.OnServerCrashed -= OnServerCrashed;
        }

        private void LockShellScrollHost()
        {
            if (_isShellScrollLocked)
            {
                UpdatePageViewportHeight();
                return;
            }

            _shellScrollViewer = FindAncestor<ScrollViewer>(this);
            if (_shellScrollViewer == null)
            {
                return;
            }

            _originalShellVerticalScrollBarVisibility = _shellScrollViewer.VerticalScrollBarVisibility;
            _originalShellHorizontalScrollBarVisibility = _shellScrollViewer.HorizontalScrollBarVisibility;
            _shellScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _shellScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _shellScrollViewer.SizeChanged += ShellScrollViewer_SizeChanged;
            _isShellScrollLocked = true;

            UpdatePageViewportHeight();
        }

        private void UnlockShellScrollHost()
        {
            if (!_isShellScrollLocked || _shellScrollViewer == null)
            {
                return;
            }

            _shellScrollViewer.SizeChanged -= ShellScrollViewer_SizeChanged;
            _shellScrollViewer.VerticalScrollBarVisibility = _originalShellVerticalScrollBarVisibility;
            _shellScrollViewer.HorizontalScrollBarVisibility = _originalShellHorizontalScrollBarVisibility;
            _shellScrollViewer = null;
            _isShellScrollLocked = false;
            PageRoot.Height = double.NaN;
        }

        private void ShellScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdatePageViewportHeight();
        }

        private void UpdatePageViewportHeight()
        {
            if (_shellScrollViewer == null)
            {
                return;
            }

            double hostHeight = _shellScrollViewer.ViewportHeight > 0
                ? _shellScrollViewer.ViewportHeight
                : _shellScrollViewer.ActualHeight;

            if (hostHeight <= 0)
            {
                return;
            }

            double verticalMargin = PageRoot.Margin.Top + PageRoot.Margin.Bottom;
            PageRoot.Height = Math.Max(0, hostHeight - verticalMargin - 1);
        }

        private void EnsureLogScrollViewer()
        {
            _logScrollViewer ??= FindDescendant<ScrollViewer>(LogView);
        }

        private void LogView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            EnsureLogScrollViewer();
            if (_logScrollViewer == null)
            {
                return;
            }

            double newOffset = _logScrollViewer.VerticalOffset - (e.Delta / 3.0);
            _logScrollViewer.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is T ancestor)
                {
                    return ancestor;
                }
            }

            return null;
        }

        private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
        {
            if (current == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(current, i);
                if (child is T match)
                {
                    return match;
                }

                T? nestedMatch = FindDescendant<T>(child);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            return true;
        }
    }
}
