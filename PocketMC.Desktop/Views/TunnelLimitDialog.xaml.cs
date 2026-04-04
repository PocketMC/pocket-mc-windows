using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    /// <summary>
    /// Dialog shown when the Playit.gg tunnel limit (4) is reached
    /// and the server's port is not among the existing tunnels.
    /// Implements NET-10.
    /// </summary>
    public partial class TunnelLimitDialog : Wpf.Ui.Controls.FluentWindow
    {
        /// <summary>
        /// True if the user chose "Change Port" — the caller should navigate
        /// to the server configuration screen.
        /// </summary>
        public bool UserChoseChangePort { get; private set; }

        public TunnelLimitDialog(List<TunnelData> existingTunnels)
        {
            InitializeComponent();
            TunnelListControl.ItemsSource = existingTunnels;
        }

        private void OpenDashboardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://playit.gg/account/setup/new-tunnel",
                    UseShellExecute = true
                });
            }
            catch { }

            Close();
        }

        private void ChangePortButton_Click(object sender, RoutedEventArgs e)
        {
            UserChoseChangePort = true;
            Close();
        }
    }
}
