using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PlayitSetupWizardDialog : FluentWindow
    {
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitPartnerProvisioningClient _partnerProvisioningClient;
        private readonly ILogger<PlayitSetupWizardDialog> _logger;

        private int _currentStep = 1;
        private int _closeRequested;
        private bool _isConnecting;

        private readonly Border[] _stepDots;
        private readonly StackPanel[] _stepPanels;

        public bool SetupCompleted { get; private set; }

        public PlayitSetupWizardDialog(
            PlayitAgentService playitAgentService,
            PlayitPartnerProvisioningClient partnerProvisioningClient,
            ILogger<PlayitSetupWizardDialog> logger)
        {
            InitializeComponent();
            _playitAgentService = playitAgentService;
            _partnerProvisioningClient = partnerProvisioningClient;
            _logger = logger;

            _stepDots = new[] { StepDot1, StepDot2 };
            _stepPanels = new[] { Step1Panel, Step2Panel };

            _playitAgentService.OnStateChanged += OnAgentStateChanged;

            Unloaded += OnUnloaded;
            UpdateStepVisuals();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _playitAgentService.OnStateChanged -= OnAgentStateChanged;
        }

        private void OnAgentStateChanged(object? sender, PlayitAgentState state)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (state == PlayitAgentState.Connected)
                {
                    ShowConnectSuccess();
                }
            }));
        }

        // --- Step Navigation ---

        private void GoToStep(int step)
        {
            _currentStep = Math.Clamp(step, 1, 2);
            UpdateStepVisuals();
        }

        private void UpdateStepVisuals()
        {
            for (int i = 0; i < _stepPanels.Length; i++)
            {
                _stepPanels[i].Visibility = i == _currentStep - 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            for (int i = 0; i < _stepDots.Length; i++)
            {
                bool isActive = i == _currentStep - 1;
                bool isCompleted = i < _currentStep - 1;
                _stepDots[i].Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA))
                    : isCompleted
                        ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
                        : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

                if (_stepDots[i].Child is TextBlock label)
                {
                    label.Foreground = (isActive || isCompleted) ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                    label.Text = isCompleted ? "✓" : (i + 1).ToString();
                }
            }

            // Update title
            TxtStepTitle.Text = _currentStep switch
            {
                1 => "Get your Setup Code",
                2 => "Paste Code & Connect",
                _ => "Setup"
            };

            // Show/hide nav buttons
            BtnPrevStep.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
            BtnNextStep.Visibility = _currentStep < 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnNextStep_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 2)
            {
                GoToStep(_currentStep + 1);
            }
        }

        private void BtnPrevStep_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                GoToStep(_currentStep - 1);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            RequestClose();
        }

        // --- Step 1: Open Setup Page ---

        private void BtnOpenSetupPage_Click(object sender, RoutedEventArgs e)
        {
            Uri? setupUri = _partnerProvisioningClient.GetSetupPageUri();
            if (setupUri == null) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = setupUri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open the Playit setup page.");
            }

            // Auto-advance to step 2 after opening
            GoToStep(2);
        }

        // --- Step 2: Connect ---

        private async void BtnWizardConnect_Click(object sender, RoutedEventArgs e)
        {
            string code = TxtWizardSetupCode.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                ShowConnectError("Please paste the setup code from Playit.");
                return;
            }

            await ConnectAsync(code);
        }

        private async Task ConnectAsync(string setupCode)
        {
            if (_isConnecting) return;
            _isConnecting = true;

            BtnWizardConnect.IsEnabled = false;
            TxtWizardSetupCode.IsEnabled = false;
            TxtConnectStatus.Visibility = Visibility.Collapsed;
            ConnectingPanel.Visibility = Visibility.Visible;
            TxtConnectingStatus.Text = "Provisioning agent...";

            try
            {
                PlayitPartnerCreateAgentResult result =
                    await _playitAgentService.ConnectWithSetupCodeAsync(setupCode);

                if (result.Success)
                {
                    TxtConnectingStatus.Text = "Agent connected! Closing...";
                    // Give the agent state change event a moment to fire
                    await Task.Delay(1500);
                    ShowConnectSuccess();
                }
                else
                {
                    ShowConnectError(result.ErrorMessage ?? "Could not provision the Playit agent.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Playit setup wizard connect failed.");
                ShowConnectError(ex.Message);
            }
            finally
            {
                _isConnecting = false;
                BtnWizardConnect.IsEnabled = true;
                TxtWizardSetupCode.IsEnabled = true;
            }
        }

        private void ShowConnectError(string message)
        {
            ConnectingPanel.Visibility = Visibility.Collapsed;
            TxtConnectStatus.Text = message;
            TxtConnectStatus.Foreground = Brushes.Orange;
            TxtConnectStatus.Visibility = Visibility.Visible;
        }

        private void ShowConnectSuccess()
        {
            if (SetupCompleted) return; // Prevent multiple triggers
            SetupCompleted = true;

            ConnectingPanel.Visibility = Visibility.Collapsed;
            TxtConnectStatus.Text = "✓ Agent connected successfully!";
            TxtConnectStatus.Foreground = Brushes.LimeGreen;
            TxtConnectStatus.Visibility = Visibility.Visible;

            // Auto-close after a short delay
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                RequestClose();
            };
            timer.Start();
        }

        private void RequestClose()
        {
            if (Interlocked.Exchange(ref _closeRequested, 1) != 0) return;
            DialogResult = true;
            Close();
        }
    }
}
