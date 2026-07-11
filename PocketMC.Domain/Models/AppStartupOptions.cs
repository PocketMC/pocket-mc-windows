namespace PocketMC.Domain.Models;

public sealed record AppStartupOptions(bool IsWindowsStartup, bool IsMinimized, string? ActivatedUri = null)
{
    public const string WindowsStartupArgument = "--windows-startup";
    public const string MinimizedArgument = "--minimized";

    public static AppStartupOptions NormalLaunch { get; } = new(false, false);

    public bool ShouldStartMinimizedToTray => IsWindowsStartup && IsMinimized;

    public static AppStartupOptions Parse(IEnumerable<string>? args)
    {
        string[] normalizedArgs = args?.ToArray() ?? Array.Empty<string>();

        bool isWindowsStartup = normalizedArgs.Any(arg =>
            string.Equals(arg, WindowsStartupArgument, StringComparison.OrdinalIgnoreCase));
        bool isMinimized = normalizedArgs.Any(arg =>
            string.Equals(arg, MinimizedArgument, StringComparison.OrdinalIgnoreCase));

        string? activatedUri = normalizedArgs.FirstOrDefault(arg => arg.StartsWith("pocketmc://", StringComparison.OrdinalIgnoreCase));

        return new AppStartupOptions(isWindowsStartup, isMinimized, activatedUri);
    }
}
