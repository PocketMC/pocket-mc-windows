using System;
using System.Windows;

namespace PocketMC.Desktop.Features.Networking
{
    /// <summary>
    /// Interactive dialog shown when a port conflict blocks server startup.
    /// Allows the user to enter a new port, validates it in real-time, and
    /// returns the chosen port for the caller to apply and retry.
    /// </summary>
    public partial class PortConflictWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly int _currentPort;
        private readonly PortProbeService _probeService;

        /// <summary>
        /// Gets the validated new port chosen by the user. Null if the user cancelled.
        /// </summary>
        public int? NewPort { get; private set; }

        /// <summary>
        /// Gets whether the user clicked "Change Port &amp; Start".
        /// </summary>
        public bool UserConfirmed { get; private set; }

        /// <summary>
        /// Creates a new port conflict dialog.
        /// </summary>
        /// <param name="title">The dialog title (e.g. "Port Already In Use").</param>
        /// <param name="message">The detailed error message from PortFailureMessageService.</param>
        /// <param name="currentPort">The port that caused the conflict.</param>
        /// <param name="probeService">The port probe service for real-time availability checks.</param>
        public PortConflictWindow(string title, string message, int currentPort, PortProbeService probeService)
        {
            _currentPort = currentPort;
            _probeService = probeService;

            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            // Re-assert accent color to prevent FluentWindow from reverting to system accent.
            try
            {
                if (System.Windows.Application.Current is App app)
                {
                    var accentService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                        .GetService<PocketMC.Desktop.Features.Shell.AccentColorService>(app.Services);
                    accentService?.ReassertAccent();
                }
            }
            catch
            {
                // Non-critical.
            }

            TxtTitle.Text = title;
            TxtMessage.Text = message;
            TxtPortInput.Text = currentPort.ToString();
            TxtPortInput.Focus();
            TxtPortInput.SelectAll();
        }

        private void TxtPortInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ValidatePort();
        }

        private void ValidatePort()
        {
            string input = TxtPortInput.Text.Trim();

            // Reset UI state
            TxtValidation.Visibility = Visibility.Collapsed;
            TxtSuccess.Visibility = Visibility.Collapsed;
            BtnConfirm.IsEnabled = false;

            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            if (!int.TryParse(input, out int port))
            {
                ShowValidationError("Please enter a valid number.");
                return;
            }

            if (port < 1 || port > 65535)
            {
                ShowValidationError("Port must be between 1 and 65535.");
                return;
            }

            if (port == _currentPort)
            {
                ShowValidationError($"Port {port} is the current conflicting port. Choose a different one.");
                return;
            }

            // Probe the port for availability using the existing infrastructure
            try
            {
                var request = new PortCheckRequest(port, PortProtocol.Tcp);
                var result = _probeService.Probe(request);

                if (!result.IsSuccessful)
                {
                    ShowValidationError($"Port {port} is not available â€” it may already be in use.");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowValidationError($"Could not check port {port}: {ex.Message}");
                return;
            }

            // Port is valid and available
            TxtSuccess.Text = $"âœ“ Port {port} is available.";
            TxtSuccess.Visibility = Visibility.Visible;
            BtnConfirm.IsEnabled = true;
        }

        private void ShowValidationError(string message)
        {
            TxtValidation.Text = message;
            TxtValidation.Visibility = Visibility.Visible;
            TxtSuccess.Visibility = Visibility.Collapsed;
            BtnConfirm.IsEnabled = false;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtPortInput.Text.Trim(), out int port) && port >= 1 && port <= 65535)
            {
                NewPort = port;
                UserConfirmed = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}


