using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PocketMC.Desktop.Features.Tunnel
{
    /// <summary>
    /// Dialog for manually creating a new PlayIt.gg tunnel.
    /// Returns <see cref="TunnelCreated"/> = true when a tunnel was successfully created.
    /// </summary>
    public partial class CreateTunnelDialog : Wpf.Ui.Controls.FluentWindow
    {
        private static readonly Regex AsciiOnlyRegex = new(@"^[\x20-\x7E]+$", RegexOptions.Compiled);

        private readonly PlayitApiClient _playitApiClient;

        /// <summary>True when the dialog created a tunnel successfully.</summary>
        public bool TunnelCreated { get; private set; }

        public CreateTunnelDialog(PlayitApiClient playitApiClient)
        {
            _playitApiClient = playitApiClient;
            InitializeComponent();
        }

        // ─── Validation helpers ──────────────────────────────────────────

        private bool ValidateForm()
        {
            bool valid = true;

            // Tunnel name
            string name = TxtTunnelName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowFieldError(TxtNameError, "Tunnel name is required.");
                valid = false;
            }
            else if (!AsciiOnlyRegex.IsMatch(name))
            {
                ShowFieldError(TxtNameError, "Tunnel name must contain ASCII characters only.");
                valid = false;
            }
            else
            {
                HideFieldError(TxtNameError);
            }

            // Port
            string portText = TxtLocalPort.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
            {
                ShowFieldError(TxtPortError, "Enter a valid port number (1–65535).");
                valid = false;
            }
            else
            {
                HideFieldError(TxtPortError);
            }

            return valid;
        }

        private static void ShowFieldError(TextBlock block, string message)
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }

        private static void HideFieldError(TextBlock block)
        {
            block.Text = string.Empty;
            block.Visibility = Visibility.Collapsed;
        }

        /// <summary>Only allow digits in the local port field.</summary>
        private void TxtLocalPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        // ─── Error mapping ───────────────────────────────────────────────

        /// <summary>
        /// Maps a TunnelCreateErrorV1 code to a user-friendly message.
        /// </summary>
        private static string MapErrorCode(string? errorCode)
        {
            return errorCode switch
            {
                "RequiresVerifiedAccount" => "Your PlayIt.gg account must be verified to create tunnels.",
                "RegionRequiresPlayitPremium" => "The selected region requires a PlayIt.gg Premium account.",
                "RequiresPlayitPremium" => "This tunnel type requires PlayIt.gg Premium.",
                "TunnelNameIsNotAscii" => "Tunnel name must contain ASCII characters only.",
                "TunnelNameTooLong" => "Tunnel name is too long.",
                "RegionNotSupported" => "The selected region is not supported for this tunnel type.",
                _ => $"Tunnel creation failed: {errorCode ?? "unknown error"}."
            };
        }

        // ─── Actions ─────────────────────────────────────────────────────

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            TunnelCreated = false;
            Close();
        }

        private async void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            // Hide any previous API error
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (!ValidateForm())
            {
                return;
            }

            // Gather values
            string tunnelName = TxtTunnelName.Text.Trim();
            string tunnelType = (CmbTunnelType.SelectedItem as ComboBoxItem)?.Tag as string ?? "minecraft-java";
            int localPort = int.Parse(TxtLocalPort.Text.Trim());
            string region = (CmbRegion.SelectedItem as ComboBoxItem)?.Tag as string ?? "global";
            bool enabled = ToggleEnabled.IsChecked == true;

            // Disable controls during the request
            SetFormEnabled(false);

            try
            {
                TunnelCreateResult result = await _playitApiClient.CreateTunnelAsync(
                    tunnelName, tunnelType, localPort, region, enabled);

                if (result.Success)
                {
                    TunnelCreated = true;
                    Close();
                    return;
                }

                // Map known error codes to inline field errors where appropriate
                if (result.ErrorCode is "TunnelNameIsNotAscii" or "TunnelNameTooLong")
                {
                    ShowFieldError(TxtNameError, MapErrorCode(result.ErrorCode));
                }
                else
                {
                    // Show in the general error area
                    TxtApiError.Text = MapErrorCode(result.ErrorCode ?? result.ErrorMessage);
                    ErrorBorder.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                TxtApiError.Text = $"Tunnel creation failed: {ex.Message}";
                ErrorBorder.Visibility = Visibility.Visible;
            }
            finally
            {
                SetFormEnabled(true);
            }
        }

        private void SetFormEnabled(bool enabled)
        {
            TxtTunnelName.IsEnabled = enabled;
            CmbTunnelType.IsEnabled = enabled;
            TxtLocalPort.IsEnabled = enabled;
            CmbRegion.IsEnabled = enabled;
            ToggleEnabled.IsEnabled = enabled;
            BtnCreate.IsEnabled = enabled;
            BtnCancel.IsEnabled = enabled;
            SpinnerRing.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
