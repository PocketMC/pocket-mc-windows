using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Infrastructure
{
    /// <summary>
    /// Static helper that checks whether a required runtime (Java or PHP) is missing
    /// for a given server instance and, if so, shows a modal
    /// <see cref="RuntimeDownloadDialog"/> with real-time download progress.
    /// 
    /// Call <see cref="EnsureRuntimeAsync"/> before starting a server to replace
    /// the previous silent background download with a visible, blocking UI flow.
    /// </summary>
    public static class RuntimeProvisioningGate
    {
        /// <summary>
        /// Ensures the required runtime is present. If it is missing, a modal
        /// progress dialog is displayed while the runtime is downloaded.
        /// Returns <c>true</c> if the runtime is ready; <c>false</c> on failure.
        /// </summary>
        public static async Task<bool> EnsureRuntimeAsync(
            InstanceMetadata meta,
            JavaProvisioningService javaProvisioning,
            PhpProvisioningService phpProvisioning)
        {
            bool isPocketmine = meta.ServerType != null &&
                                meta.ServerType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase);

            bool isBedrock = meta.ServerType != null &&
                             meta.ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase);

            // Bedrock servers don't need a managed runtime.
            if (isBedrock)
                return true;

            if (isPocketmine)
            {
                if (phpProvisioning.IsPhpPresent())
                    return true;

                return await ShowAndProvisionAsync(
                    "Downloading PHP 8.2 Runtime",
                    progress => phpProvisioning.EnsurePhpAsync(progress));
            }
            else
            {
                // Java-based server
                int requiredVersion = JavaRuntimeResolver.GetRequiredJavaVersion(meta.MinecraftVersion);
                if (javaProvisioning.IsJavaVersionPresent(requiredVersion))
                    return true;

                return await ShowAndProvisionAsync(
                    $"Downloading Java {requiredVersion} Runtime",
                    progress => javaProvisioning.EnsureJavaAsync(requiredVersion, progress: progress));
            }
        }

        private static async Task<bool> ShowAndProvisionAsync(
            string title,
            Func<IProgress<DownloadProgress>, Task> provisionAsync)
        {
            var vm = new RuntimeDownloadDialogViewModel { Title = title };
            var dialog = new RuntimeDownloadDialog(vm);

            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow is { IsLoaded: true, IsVisible: true })
                    dialog.Owner = mainWindow;
            }
            catch { /* Owner assignment can fail during shutdown */ }

            // Start the provisioning task, then show the dialog modally.
            // ShowDialog blocks the UI thread, so we need to kick off the
            // async work first and let the dialog close itself on completion.
            Task provisioningTask = Task.Run(async () =>
            {
                try
                {
                    await provisionAsync(vm);
                    vm.Complete();
                }
                catch
                {
                    // On failure the dialog closes and the exception propagates
                    vm.Complete();
                    throw;
                }
            });

            dialog.ShowDialog();

            try
            {
                await provisioningTask;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
