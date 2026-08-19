using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Application.Services.Networking;
using PocketMC.Application.Services.Instances;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Networking;

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
        private readonly PortCheckRequest? _request;
        private readonly int? _suggestedPort;
        private readonly PortProbeService _probeService;
        private readonly InstanceRegistry? _instanceRegistry;
        private readonly PortLeaseRegistry? _leaseRegistry;

        /// <summary>
        /// Gets the validated new port chosen by the user. Null if the user cancelled.
        /// </summary>
        public int? NewPort { get; private set; }

        /// <summary>
        /// Gets whether the user clicked "Change Port &amp; Start".
        /// </summary>
        public bool UserConfirmed { get; private set; }

        /// <summary>
        /// Creates a new port conflict dialog (legacy overload).
        /// </summary>
        public PortConflictWindow(string title, string message, int currentPort, PortProbeService probeService)
            : this(title, message, new PortCheckRequest(currentPort, PortProtocol.Tcp), null, probeService, null, null)
        {
        }

        /// <summary>
        /// Creates a new port conflict dialog with full context and suggestions.
        /// </summary>
        public PortConflictWindow(
            string title,
            string message,
            PortCheckRequest request,
            int? suggestedPort,
            PortProbeService probeService,
            InstanceRegistry? instanceRegistry = null,
            PortLeaseRegistry? leaseRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(probeService);

            _request = request;
            _currentPort = request.Port;
            _suggestedPort = suggestedPort;
            _probeService = probeService;
            _instanceRegistry = instanceRegistry;
            _leaseRegistry = leaseRegistry;

            InitializeComponent();

            try
            {
                var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
                visualService.ApplyThemeToDialog(this);

                if (System.Windows.Application.Current is App app)
                {
                    var accentService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                        .GetService<PocketMC.Desktop.Features.Shell.AccentColorService>(app.Services);
                    accentService?.ReassertAccent();
                }
            }
            catch
            {
                // Non-critical theme application.
            }

            TxtTitle.Text = title;
            TxtMessage.Text = message;

            if (_suggestedPort.HasValue && _suggestedPort.Value != _currentPort)
            {
                BorderSuggestedPort.Visibility = Visibility.Visible;
                string protoText = _request.Protocol == PortProtocol.Udp ? "UDP" : "TCP";
                TxtSuggestedPortDescription.Text = $"Port {_suggestedPort.Value} ({protoText}) is verified available";
                TxtPortInput.Text = _suggestedPort.Value.ToString();
            }
            else
            {
                TxtPortInput.Text = string.Empty;
            }

            TxtPortInput.Focus();
            TxtPortInput.SelectAll();
        }

        private void BtnApplySuggested_Click(object sender, RoutedEventArgs e)
        {
            if (_suggestedPort.HasValue)
            {
                TxtPortInput.Text = _suggestedPort.Value.ToString();
                TxtPortInput.Focus();
                TxtPortInput.SelectAll();
            }
        }

        private void TxtPortInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidatePort();
        }

        private void ValidatePort()
        {
            string input = TxtPortInput.Text.Trim();

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

            PortProtocol protocol = _request?.Protocol ?? PortProtocol.Tcp;
            PortIpMode ipMode = _request?.IpMode ?? PortIpMode.IPv4;
            string? bindAddress = _request?.BindAddress;

            // Check against active leases
            if (_leaseRegistry != null)
            {
                var holder = _leaseRegistry.FindHolder(port, protocol, ipMode, bindAddress);
                if (holder != null && holder.InstanceId != _request?.InstanceId)
                {
                    ShowValidationError($"Port {port} is currently leased by running instance '{holder.InstanceName}'.");
                    return;
                }
            }

            // Check against configured PocketMC instances
            if (_instanceRegistry != null)
            {
                foreach (var instance in _instanceRegistry.GetAll())
                {
                    if (_request?.InstanceId.HasValue == true && instance.Id == _request.InstanceId.Value)
                    {
                        continue;
                    }

                    bool isBedrock = instance.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true ||
                                     instance.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;
                    PortProtocol mainProto = isBedrock ? PortProtocol.Udp : PortProtocol.Tcp;

                    if (instance.ServerPort == port && ProtocolsOverlap(protocol, mainProto))
                    {
                        ShowValidationError($"Port {port} is already assigned to instance '{instance.Name}'.");
                        return;
                    }

                    if (instance.GeyserBedrockPort.HasValue && instance.GeyserBedrockPort.Value == port && ProtocolsOverlap(protocol, PortProtocol.Udp))
                    {
                        ShowValidationError($"Port {port} is assigned to Geyser on instance '{instance.Name}'.");
                        return;
                    }

                    if (instance.SimpleVoiceChatPort.HasValue && instance.SimpleVoiceChatPort.Value == port && ProtocolsOverlap(protocol, PortProtocol.Udp))
                    {
                        ShowValidationError($"Port {port} is assigned to Simple Voice Chat on instance '{instance.Name}'.");
                        return;
                    }
                }
            }

            // Probe OS socket availability
            try
            {
                var checkRequest = new PortCheckRequest(
                    port,
                    protocol,
                    ipMode,
                    bindAddress,
                    _request?.InstanceId,
                    _request?.InstanceName,
                    _request?.InstancePath,
                    bindingRole: _request?.BindingRole ?? PortBindingRole.PrimaryServer,
                    engine: _request?.Engine ?? PortEngine.Java,
                    displayName: _request?.DisplayName);

                var result = _probeService.Probe(checkRequest);
                if (!result.IsSuccessful)
                {
                    ShowValidationError($"Port {port} is not available on Windows — it may already be in use by another program.");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowValidationError($"Could not check port {port}: {ex.Message}");
                return;
            }

            string protoLabel = protocol == PortProtocol.Udp ? "UDP" : "TCP";
            TxtSuccess.Text = $"✓ Port {port} ({protoLabel}) is available.";
            TxtSuccess.Visibility = Visibility.Visible;
            BtnConfirm.IsEnabled = true;
        }

        private static bool ProtocolsOverlap(PortProtocol left, PortProtocol right)
        {
            if (left == PortProtocol.TcpAndUdp || right == PortProtocol.TcpAndUdp)
            {
                return true;
            }

            return left == right;
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
