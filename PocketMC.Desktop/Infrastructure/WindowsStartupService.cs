using System;
using Microsoft.Win32;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Infrastructure;

public interface IWindowsStartupRegistry
{
    string? GetValue(string name);
    void SetValue(string name, string value);
    void DeleteValue(string name);
}

public sealed class WindowsStartupService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "PocketMC";
    public const string WindowsStartupArgument = "--windows-startup";
    public const string MinimizedArgument = "--minimized";
    public const string BootTaskName = "PocketMC_Boot_Startup";

    private readonly IWindowsStartupRegistry _registry;
    private readonly string _executablePath;

    public WindowsStartupService()
        : this(new CurrentUserRunRegistry(), ResolveExecutablePath())
    {
    }

    public WindowsStartupService(IWindowsStartupRegistry registry, string executablePath)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? throw new ArgumentException("Executable path is required.", nameof(executablePath))
            : executablePath;
    }

    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.StartWithWindows)
        {
            _registry.SetValue(RunValueName, BuildStartupCommand(settings.StartMinimizedToTray));
            return;
        }

        _registry.DeleteValue(RunValueName);
    }

    public string BuildStartupCommand(bool minimized)
    {
        string command = $"{QuoteArgument(_executablePath)} {WindowsStartupArgument}";
        return minimized ? $"{command} {MinimizedArgument}" : command;
    }

    public bool IsRegistered()
    {
        string? value = _registry.GetValue(RunValueName);
        return !string.IsNullOrWhiteSpace(value);
    }

    public void ApplyBootTask(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.StartOnSystemBoot)
        {
            RegisterBootTask(settings.StartMinimizedToTray);
            return;
        }

        UnregisterBootTask();
    }

    private void RegisterBootTask(bool minimized)
    {
        string commandArgs = $"/create /tn \"{BootTaskName}\" /tr \"{QuoteArgument(_executablePath)} {WindowsStartupArgument} {(minimized ? MinimizedArgument : string.Empty)}\" /sc onstart /ru \"SYSTEM\" /rl HIGHEST /f";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = commandArgs,
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
            if (process != null && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"schtasks failed with exit code {process.ExitCode}.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new UnauthorizedAccessException("The startup task creation was cancelled by the user (UAC prompt declined).", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to register the Windows Task Scheduler startup task. Make sure you run as Administrator.", ex);
        }
    }

    private void UnregisterBootTask()
    {
        string commandArgs = $"/delete /tn \"{BootTaskName}\" /f";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = commandArgs,
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new UnauthorizedAccessException("The startup task deletion was cancelled by the user (UAC prompt declined).", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to delete the Windows Task Scheduler startup task. Make sure you run as Administrator.", ex);
        }
    }

    public bool IsBootTaskRegistered()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/query /tn \"{BootTaskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
            return process != null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.MainModule?.FileName
            ?? throw new InvalidOperationException("Could not resolve the PocketMC executable path.");
    }

    private sealed class CurrentUserRunRegistry : IWindowsStartupRegistry
    {
        public string? GetValue(string name)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(name) as string;
        }

        public void SetValue(string name, string value)
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                throw new InvalidOperationException("Could not open the current-user Windows startup registry key.");
            }

            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void DeleteValue(string name)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
